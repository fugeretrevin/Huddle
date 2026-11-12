using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// Owns the board's card views and turns reducer state into placed cards (P2-01, master §4).
    /// It subscribes to Phase2Reducer, recycles TaskCardView instances by task id (object pooling),
    /// lays them into the Backlog / Working / Needs me / Completed regions, and is the single place
    /// that issues task commands and reconciles their results. Status columns are VISUAL PROJECTIONS;
    /// the station drop zones do the real assignment.
    ///
    /// Nothing here runs until Phase2Flags.BoardActive is true. Wire-up is done by an integrator
    /// bootstrap that calls Initialize(gateway, reducer) once a v2 session exists — this keeps the
    /// board additive and decoupled from Phase 1 (no DI container in the project).
    ///
    /// All work happens on the Unity main thread: reducer events are raised on the main thread (the
    /// live channel marshals through its Update pump; the demo raises inline from awaited main-thread
    /// methods), and command callbacks are async/awaited back on the main thread.
    /// </summary>
    public class BoardPresenter : MonoBehaviour
    {
        public enum BoardRegion { Backlog, Working, NeedsMe, Completed }

        [Header("Prefab + regions")]
        public TaskCardView cardPrefab;
        public Transform backlogRegion;
        public Transform workingRegion;
        public Transform needsMeRegion;
        public Transform completedRegion;

        [Header("Layout")]
        [Tooltip("Local-space grid spacing between cards within a region.")]
        public Vector2 cardSpacing = new Vector2(0.14f, 0.10f);
        [Tooltip("Cards per row within a region before wrapping.")]
        public int columns = 3;

        public IPhase2Gateway Gateway { get; private set; }
        public Phase2Reducer Reducer { get; private set; }
        public bool Ready { get; private set; }

        readonly Dictionary<string, TaskCardView> _views = new Dictionary<string, TaskCardView>();
        readonly Stack<TaskCardView> _pool = new Stack<TaskCardView>();
        CancellationTokenSource _cts;
        int _idemCounter;

        /// <summary>Raised after an assign resolves (success or conflict). Lets a drag controller /
        /// HUD show a one-line reason. Args: (card, result).</summary>
        public event Action<TaskCardView, Phase2CommandResult<TaskCardV2>> AssignResolved;

        /// <summary>
        /// Wire the board to a live or demo gateway and its reducer. Call once; safe to call again to
        /// rebind. Does nothing (and logs) if the board feature flag is off.
        /// </summary>
        public void Initialize(IPhase2Gateway gateway, Phase2Reducer reducer)
        {
            if (!Phase2Flags.BoardActive)
            {
                Debug.Log("[MuseDesk.P2] BoardPresenter.Initialize ignored — Phase2Flags.BoardActive is false.");
                return;
            }
            Unsubscribe();
            Gateway = gateway;
            Reducer = reducer;
            _cts ??= new CancellationTokenSource();

            Reducer.SnapshotLoaded += RebuildAll;
            Reducer.TaskChanged += OnTaskChanged;
            Reducer.ResyncNeeded += OnResyncNeeded;

            Ready = true;
            RebuildAll();
        }

        void OnDestroy()
        {
            Unsubscribe();
            try { _cts?.Cancel(); } catch { }
        }

        void Unsubscribe()
        {
            if (Reducer == null) return;
            Reducer.SnapshotLoaded -= RebuildAll;
            Reducer.TaskChanged -= OnTaskChanged;
            Reducer.ResyncNeeded -= OnResyncNeeded;
        }

        // ─────────────────────────── view lifecycle ───────────────────────────

        void RebuildAll()
        {
            if (!Ready || Reducer == null) return;
            // Recycle everything, then upsert from the reducer's current projection.
            foreach (var kv in _views) Recycle(kv.Value);
            _views.Clear();
            foreach (var kv in Reducer.Tasks) Upsert(kv.Value);
            LayoutAllRegions();
        }

        void OnTaskChanged(TaskCardV2 card)
        {
            if (!Ready) return;
            Upsert(card);
            LayoutAllRegions();
        }

        void Upsert(TaskCardV2 card)
        {
            if (card?.TaskId == null) return;
            if (!_views.TryGetValue(card.TaskId, out var view))
            {
                view = Rent();
                _views[card.TaskId] = view;
                var drag = view.GetComponent<TaskDragController>();
                if (drag != null) drag.Bind(this, view);
                view.AssignRequested -= OnPokeAssign;   // avoid double subscription on reuse
                view.AssignRequested += OnPokeAssign;
            }
            view.SetData(card);
            var region = RegionFor(card.TaskState);
            var parent = RegionTransform(region);
            if (parent != null && view.transform.parent != parent)
                view.transform.SetParent(parent, worldPositionStays: false);
        }

        TaskCardView Rent()
        {
            TaskCardView v = _pool.Count > 0 ? _pool.Pop() : Instantiate(cardPrefab);
            v.gameObject.SetActive(true);
            return v;
        }

        void Recycle(TaskCardView v)
        {
            if (v == null) return;
            v.AssignRequested -= OnPokeAssign;
            v.SetExpanded(false);
            v.gameObject.SetActive(false);
            _pool.Push(v);
        }

        // ─────────────────────────── layout ───────────────────────────

        public static BoardRegion RegionFor(string taskState) => taskState switch
        {
            TaskStates.Running => BoardRegion.Working,
            TaskStates.AwaitingApproval => BoardRegion.NeedsMe,
            TaskStates.Completed or TaskStates.Failed or TaskStates.Cancelled => BoardRegion.Completed,
            _ => BoardRegion.Backlog,   // queued, blocked, paused, pause_requested
        };

        Transform RegionTransform(BoardRegion r) => r switch
        {
            BoardRegion.Working => workingRegion,
            BoardRegion.NeedsMe => needsMeRegion,
            BoardRegion.Completed => completedRegion,
            _ => backlogRegion,
        };

        void LayoutAllRegions()
        {
            LayoutRegion(backlogRegion);
            LayoutRegion(workingRegion);
            LayoutRegion(needsMeRegion);
            LayoutRegion(completedRegion);
        }

        void LayoutRegion(Transform region)
        {
            if (region == null) return;
            int i = 0;
            int cols = Mathf.Max(1, columns);
            for (int c = 0; c < region.childCount; c++)
            {
                var child = region.GetChild(c);
                if (!child.gameObject.activeSelf) continue;
                int row = i / cols, col = i % cols;
                var card = child.GetComponent<TaskCardView>();
                var pos = new Vector3(col * cardSpacing.x, -row * cardSpacing.y, 0f);
                // Record the stable resting slot (parent + local pose) so ReturnHome snaps back
                // correctly even while the hand is moving the card.
                if (card != null) card.SetHome(region, pos, Quaternion.identity);
                // Don't fight a card the user is actively holding — it owns its transform then.
                if (card != null && card.IsHeld) { i++; continue; }
                child.localPosition = pos;
                child.localRotation = Quaternion.identity;
                i++;
            }
        }

        // ─────────────────────────── commands ───────────────────────────

        void OnPokeAssign(TaskCardView card, string targetAgentId)
        {
            _ = RequestAssignAsync(card, targetAgentId, NextIdemKey("assign"));
        }

        /// <summary>
        /// Issue ONE idempotent assign for a drop or a poke. expected_revision is the card's current
        /// revision; on 409 we reconcile the view from canonical_task and surface the reason. The SAME
        /// idempotencyKey must be reused by the caller if it retries after a network timeout.
        /// Returns the result so the drag state machine can animate Confirmed vs Error.
        /// </summary>
        public async Task<Phase2CommandResult<TaskCardV2>> RequestAssignAsync(
            TaskCardView card, string targetAgentId, string idempotencyKey)
        {
            if (card?.Data == null || Gateway == null)
                return Phase2CommandResult<TaskCardV2>.Conflict(new CommandErrorBody
                { Code = "CLIENT_NOT_READY", Message = "board not initialized" });

            var cmd = new AssignCommand
            {
                TargetAgentId = targetAgentId,
                ExpectedRevision = card.Data.Revision,
                IdempotencyKey = idempotencyKey,
            };

            Phase2CommandResult<TaskCardV2> result;
            try
            {
                result = await Gateway.AssignAsync(card.Data.TaskId, cmd, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return Phase2CommandResult<TaskCardV2>.Conflict(new CommandErrorBody
                { Code = "CANCELLED", Message = "command cancelled" });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MuseDesk.P2] assign transport error: {ex.Message}");
                // Unknown canonical state: do NOT fabricate success. Reconcile from a fresh snapshot.
                _ = ResyncAsync();
                return Phase2CommandResult<TaskCardV2>.Conflict(new CommandErrorBody
                { Code = "TRANSPORT_ERROR", Message = ex.Message });
            }

            // Reconcile the view to canonical truth (success card, or the 409's canonical_task).
            var canonical = result.Ok ? result.Value : result.Error?.CanonicalTask;
            if (canonical != null)
            {
                card.SetData(canonical);
                // keep the reducer projection coherent for any later delta
                if (Reducer != null && Reducer.Tasks.TryGetValue(canonical.TaskId, out var existing))
                {
                    existing.AgentId = canonical.AgentId;
                    existing.Revision = canonical.Revision;
                    existing.TaskState = canonical.TaskState;
                    existing.Priority = canonical.Priority;
                }
                LayoutAllRegions();
            }

            if (!result.Ok)
                Debug.Log($"[MuseDesk.P2] assign rejected: {result.Error?.Code} — {result.Error?.Message}");

            AssignResolved?.Invoke(card, result);
            return result;
        }

        /// <summary>Re-fetch the authoritative snapshot and reload the reducer (gap / transport
        /// uncertainty). The live WS channel independently resumes from its own after_seq.</summary>
        public async Task ResyncAsync()
        {
            if (Gateway == null || Reducer == null) return;
            try
            {
                var snap = await Gateway.GetSnapshotAsync(_cts.Token);
                Reducer.LoadSnapshot(snap);   // raises SnapshotLoaded → RebuildAll
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.LogWarning($"[MuseDesk.P2] resync failed: {ex.Message}"); }
        }

        void OnResyncNeeded(long expected, long got)
        {
            Debug.Log($"[MuseDesk.P2] v2 seq gap (expected {expected}, got {got}); resyncing snapshot.");
            _ = ResyncAsync();
        }

        public string NextIdemKey(string prefix)
        {
            // >= 8 chars (schema min). Stable within a single attempt; a retry must reuse the SAME key,
            // so callers capture the returned key and pass it back on retry.
            string dev = SystemInfo.deviceUniqueIdentifier;
            string tail = dev != null && dev.Length >= 6 ? dev.Substring(0, 6) : "device";
            return $"{prefix}-{tail}-{DateTime.UtcNow.Ticks:x}-{++_idemCounter:D4}";
        }
    }
}
