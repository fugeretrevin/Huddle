using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using MuseDesk.Phase2.Contracts;
using MuseDesk.Spatial;

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

        [Header("Column headings (wired by AgentScripts/BuildBoardV3; all optional)")]
        [Tooltip("Heading labels in lane order: Backlog, Working, Needs me, Completed. Each shows its " +
                 "card count after the name (\"Backlog 3\"); an empty lane shows the name only.")]
        public TMP_Text[] laneHeadings;
        [Tooltip("Header block renderers in the same order. An empty lane's header is dimmed a little " +
                 "through a property block, so an empty column reads as quiet rather than broken.")]
        public Renderer[] laneHeaders;
        [Tooltip("Brightness of an empty lane's header block (1 = no dimming).")]
        [Range(0.3f, 1f)] public float emptyLaneBrightness = 0.62f;

        static readonly string[] LaneNames = { "Backlog", "Working", "Needs me", "Completed" };
        readonly int[] _laneCounts = { -1, -1, -1, -1 };
        readonly Color[] _laneBase = new Color[4];
        readonly bool[] _laneBaseRead = new bool[4];
        MaterialPropertyBlock _laneBlock;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public IPhase2Gateway Gateway { get; private set; }
        public Phase2Reducer Reducer { get; private set; }
        public bool Ready { get; private set; }

        /// <summary>The card views currently on the board (one per live task). Read-only view for
        /// helpers such as the desk card-action console; do not mutate.</summary>
        public Dictionary<string, TaskCardView>.ValueCollection ActiveViews => _views.Values;

        /// <summary>The card view for a task id, or null. Lets the task dock send the same assign
        /// command a hand drop sends, with the same pending / confirmed / refused feedback.</summary>
        public TaskCardView FindView(string taskId)
        {
            if (string.IsNullOrEmpty(taskId)) return null;
            return _views.TryGetValue(taskId, out var v) ? v : null;
        }

        /// <summary>The card a hand is holding right now, or null. Computed at most once per frame
        /// and shared by the helpers that react to a held card (toolbar, assignment pads, dock).</summary>
        public TaskCardView HeldCard
        {
            get
            {
                int frame = Time.frameCount;
                if (frame == _heldFrame) return _heldCard;
                _heldFrame = frame;
                _heldCard = null;
                foreach (var v in _views.Values)
                {
                    if (v != null && v.IsHeld) { _heldCard = v; break; }
                }
                return _heldCard;
            }
        }

        TaskCardView _heldCard;
        int _heldFrame = -1;

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
            // Polish S9a: diff by task id. Only tasks that really left are hidden, only tasks that were not on the board get
            // the entrance reveal (Upsert), and cards already on it keep their transform and glide to any new slot.
            _curIds.Clear();
            foreach (var kv in _views) _curIds.Add(kv.Key);
            MuseDesk.Motion.PresenceDiff.Compute(_curIds, Reducer.Tasks.Keys, _diffAdded, _diffRemoved, _diffKept);
            for (int i = 0; i < _diffRemoved.Count; i++)
            {
                string id = _diffRemoved[i];
                if (_views.TryGetValue(id, out var gone)) { _views.Remove(id); Recycle(id, gone); }
            }
            foreach (var kv in Reducer.Tasks) Upsert(kv.Value);
            LayoutAllRegions();
        }

        // Reused by RebuildAll (a resync is rare, but it should not allocate lists every time).
        readonly List<string> _curIds = new List<string>();
        readonly List<string> _diffAdded = new List<string>(), _diffRemoved = new List<string>(), _diffKept = new List<string>();
        // Cards easing out (panelHide) after their task left. Not in _views, not in _pool until the hide finishes.
        readonly Dictionary<string, TaskCardView> _leaving = new Dictionary<string, TaskCardView>();

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
                bool revived = false;
                if (_leaving.TryGetValue(card.TaskId, out view) && view != null && view.CancelHide())
                {
                    // The task came back while its card was still leaving: grow it back from where it is.
                    _leaving.Remove(card.TaskId);
                    view.HideFinished -= OnHideFinished;
                    revived = true;
                }
                else view = Rent();
                _views[card.TaskId] = view;
                if (!revived) view.BeginReveal();   // grows in with panelAppear; its slot and data are final (no full-size pop)
                var drag = view.GetComponent<TaskDragController>();
                if (drag != null) drag.Bind(this, view);
                view.AssignRequested -= OnPokeAssign;   // avoid double subscription on reuse
                view.AssignRequested += OnPokeAssign;
                view.PauseResumeRequested -= OnPokePauseResume;
                view.PauseResumeRequested += OnPokePauseResume;
                view.PriorityCycleRequested -= OnPokeCyclePriority;
                view.PriorityCycleRequested += OnPokeCyclePriority;
            }
            view.SetData(card);
            var region = RegionFor(card.TaskState);
            var parent = RegionTransform(region);
            // A fresh card takes the lane's frame; a card a hand has moved keeps its place in the
            // world so the layout can ease it to its new slot instead of jumping.
            if (parent != null && view.transform.parent != parent)
                view.MoveToRegion(parent);
        }

        TaskCardView Rent()
        {
            TaskCardView v = _pool.Count > 0 ? _pool.Pop() : Instantiate(cardPrefab);
            v.gameObject.SetActive(true);
            return v;
        }

        /// <summary>Send a card away: it eases out on panelHide (colliders off), then OnHideFinished deactivates and pools it.</summary>
        void Recycle(string taskId, TaskCardView v)
        {
            if (v == null) return;
            v.AssignRequested -= OnPokeAssign;
            v.PauseResumeRequested -= OnPokePauseResume;
            v.PriorityCycleRequested -= OnPokeCyclePriority;
            v.SetExpanded(false);
            if (_leaving.TryGetValue(taskId, out var stale) && stale != null && stale != v)
                FinishLeaving(taskId, stale);   // two cards for one id cannot both leave: pool the older one now
            _leaving[taskId] = v;
            v.HideFinished -= OnHideFinished;
            v.HideFinished += OnHideFinished;
            v.BeginHide();                       // may finish at once (inactive card); the handler copes
        }

        void OnHideFinished(TaskCardView v)
        {
            if (v == null) return;
            v.HideFinished -= OnHideFinished;
            string key = null;
            foreach (var kv in _leaving) if (kv.Value == v) { key = kv.Key; break; }
            if (key == null) return;             // revived or already pooled: a stale completion does nothing
            FinishLeaving(key, v);
        }

        void FinishLeaving(string key, TaskCardView v)
        {
            _leaving.Remove(key);
            v.HideFinished -= OnHideFinished;
            v.gameObject.SetActive(false);       // OnDisable resets scale, colliders, edge
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
            SetLaneCount(0, LayoutRegion(backlogRegion));
            SetLaneCount(1, LayoutRegion(workingRegion));
            SetLaneCount(2, LayoutRegion(needsMeRegion));
            SetLaneCount(3, LayoutRegion(completedRegion));
        }

        /// <summary>Heading text ("Backlog 3") and the dimming of an empty lane's header. Touched
        /// only when a lane's count changes, so nothing is rebuilt per layout.</summary>
        void SetLaneCount(int lane, int count)
        {
            if (_laneCounts[lane] == count) return;
            _laneCounts[lane] = count;

            TMP_Text heading = laneHeadings != null && lane < laneHeadings.Length ? laneHeadings[lane] : null;
            if (heading != null) heading.text = count > 0 ? LaneNames[lane] + " " + count : LaneNames[lane];

            Renderer header = laneHeaders != null && lane < laneHeaders.Length ? laneHeaders[lane] : null;
            if (header == null) return;
            if (!_laneBaseRead[lane])
            {
                _laneBaseRead[lane] = true;
                Material m = header.sharedMaterial;
                _laneBase[lane] = m == null ? Color.white
                    : m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId)
                    : m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white;
            }
            Color c = _laneBase[lane];
            if (count == 0)
            {
                float k = Mathf.Clamp(emptyLaneBrightness, 0.3f, 1f);
                c = new Color(c.r * k, c.g * k, c.b * k, c.a);
            }
            if (_laneBlock == null) _laneBlock = new MaterialPropertyBlock();
            header.GetPropertyBlock(_laneBlock);
            _laneBlock.SetColor(BaseColorId, c);
            _laneBlock.SetColor(ColorId, c);
            header.SetPropertyBlock(_laneBlock);
        }

        /// <summary>Lay a lane's cards into their slots. Returns the number of cards in the lane.</summary>
        int LayoutRegion(Transform region)
        {
            if (region == null) return 0;
            int i = 0;
            int cols = Mathf.Max(1, columns);
            for (int c = 0; c < region.childCount; c++)
            {
                var child = region.GetChild(c);
                if (!child.gameObject.activeSelf) continue;
                var leaver = child.GetComponent<TaskCardView>();
                if (leaver != null && leaver.IsLeaving) continue;   // easing out: no slot, no moving
                int row = i / cols, col = i % cols;
                var card = child.GetComponent<TaskCardView>();
                var pos = new Vector3(col * cardSpacing.x, -row * cardSpacing.y, 0f);
                // Record the stable resting slot (parent + local pose) so ReturnHome snaps back
                // correctly even while the hand is moving the card.
                if (card != null) card.SetHome(region, pos, Quaternion.identity);
                // Don't fight a card the user is actively holding — it owns its transform then.
                if (card != null && card.IsHeld) { i++; continue; }
                if (card != null)
                {
                    // Eases there if a hand moved the card (a drop), otherwise places it at once.
                    card.PlaceAtHome();
                }
                else
                {
                    child.localPosition = pos;
                    child.localRotation = Quaternion.identity;
                }
                i++;
            }
            return i;
        }

        // ─────────────────────────── commands ───────────────────────────

        // ── Polish S2: one command in flight per task card, one stable idempotency key per logical action ──
        // The lock is taken inside RequestAssignAsync / RunCardCommandAsync, so every entry point (pad and
        // chip pokes, the drag, the dock Start key) shares it. A second poke while one is in flight is
        // ignored with an "Already sending" note. The key for an action (task + kind + parameter) is
        // minted once and reused by a retry; it is dropped only when the server has answered.
        readonly CommandLedger _ledger = new CommandLedger();
        readonly Dictionary<string, float> _acceptedAt = new Dictionary<string, float>();

        /// <summary>The stable key for one logical card action (same action, same key until answered).</summary>
        public string StableKey(string taskId, string action, string param, string prefix)
            => _ledger.KeyFor(taskId + "|" + action + "|" + param, () => NextIdemKey(prefix));

        /// <summary>Take the one-in-flight lock for a card command that does not go through
        /// RequestAssignAsync (a card with no view). Pair with <see cref="ReleaseCommand"/>.</summary>
        public bool TryBeginCommand(string lockId) => _ledger.TryLock(lockId);

        /// <summary>Give the lock back; a definitive answer (ok or refused) forgets the key, a transport
        /// failure or cancel keeps it so a retry reuses it.</summary>
        public void ReleaseCommand(string lockId, string key, Phase2CommandResult<TaskCardV2> result)
            => _ledger.Unlock(lockId, key, IsDefinitive(result));

        static bool IsDefinitive(Phase2CommandResult<TaskCardV2> r)
        {
            if (r == null) return false;
            if (r.Ok) return true;
            string code = r.Error?.Code;
            return code != "CANCELLED" && code != "TRANSPORT_ERROR" && code != "CLIENT_NOT_READY" && code != "BUSY";
        }

        static Phase2CommandResult<TaskCardV2> Busy() =>
            Phase2CommandResult<TaskCardV2>.Conflict(new CommandErrorBody
            { Code = "BUSY", Message = "a command for this card is already in flight" });

        void OnPokeAssign(TaskCardView card, string targetAgentId)
        {
            if (card?.Data == null) return;
            _ = RequestAssignAsync(card, targetAgentId, StableKey(card.Data.TaskId, "assign", targetAgentId, "assign"));
        }

        void OnPokePauseResume(TaskCardView card)
        {
            if (card?.Data == null) return;
            // Decided from local state; with the in-flight lock a second poke cannot send a duplicate.
            bool paused = card.Data.TaskState == TaskStates.Paused ||
                          card.Data.TaskState == TaskStates.PauseRequested;
            string id = card.Data.TaskId;
            _ = paused ? RequestResumeAsync(card, StableKey(id, "resume", "", "resume"))
                       : RequestPauseAsync(card, StableKey(id, "pause", "", "pause"));
        }

        void OnPokeCyclePriority(TaskCardView card)
        {
            if (card?.Data == null) return;
            int next = (Mathf.Clamp(card.Data.Priority, 0, 3) + 1) % 4;
            _ = RequestPriorityAsync(card, next, StableKey(card.Data.TaskId, "priority", next.ToString(), "priority"));
        }

        /// <summary>Set a task's priority (0..3), reconciling to canonical truth; same idempotency +
        /// 409 contract as assign. Reuse the SAME key on retry.</summary>
        public Task<Phase2CommandResult<TaskCardV2>> RequestPriorityAsync(TaskCardView card, int priority, string idempotencyKey)
        {
            if (card?.Data == null || Gateway == null) return Task.FromResult(NotReady());
            var cmd = new PriorityCommand
            {
                Priority = Mathf.Clamp(priority, 0, 3),
                ExpectedRevision = card.Data.Revision,
                IdempotencyKey = idempotencyKey,
            };
            return RunCardCommandAsync(card, ct => Gateway.SetPriorityAsync(card.Data.TaskId, cmd, ct), "priority", idempotencyKey);
        }

        /// <summary>Pause a running task (canonical-reconciled).</summary>
        public Task<Phase2CommandResult<TaskCardV2>> RequestPauseAsync(TaskCardView card, string idempotencyKey)
        {
            if (card?.Data == null || Gateway == null) return Task.FromResult(NotReady());
            var cmd = new PauseCommand { ExpectedRevision = card.Data.Revision, IdempotencyKey = idempotencyKey };
            return RunCardCommandAsync(card, ct => Gateway.PauseAsync(card.Data.TaskId, cmd, ct), "pause", idempotencyKey);
        }

        /// <summary>Resume a paused task (canonical-reconciled).</summary>
        public Task<Phase2CommandResult<TaskCardV2>> RequestResumeAsync(TaskCardView card, string idempotencyKey)
        {
            if (card?.Data == null || Gateway == null) return Task.FromResult(NotReady());
            var cmd = new ResumeCommand { ExpectedRevision = card.Data.Revision, IdempotencyKey = idempotencyKey };
            return RunCardCommandAsync(card, ct => Gateway.ResumeAsync(card.Data.TaskId, cmd, ct), "resume", idempotencyKey);
        }

        /// <summary>Create a new backlog task. The server's task.created event adds the card to the
        /// board, so we don't synthesize it locally. Returns the canonical card (or null on error).</summary>
        public async Task<TaskCardV2> RequestCreateTaskAsync(string title, string agentId, int priority, string idempotencyKey)
        {
            if (Gateway == null) return null;
            var cmd = new TaskCreateV2
            {
                Title = string.IsNullOrEmpty(title) ? "New task" : title,
                AgentId = string.IsNullOrEmpty(agentId) ? null : agentId,
                Priority = Mathf.Clamp(priority, 0, 3),
                IdempotencyKey = idempotencyKey,
            };
            // One create per title in flight (a double poke / repeated phrase must not add two cards).
            string lockId = "create|" + cmd.Title;
            if (!_ledger.TryLock(lockId)) return null;
            TaskCardV2 created = null;
            bool answered = false;
            try { created = await Gateway.CreateTaskAsync(cmd, _cts.Token); answered = created != null; return created; }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex) { Debug.LogWarning($"[MuseDesk.P2] create task failed: {ex.Message}"); return null; }
            finally { _ledger.Unlock(lockId, idempotencyKey, answered); }
        }

        /// <summary>The stable key for creating a task with this title: a retry of the same create reuses it
        /// until the server has answered.</summary>
        public string StableCreateKey(string title, string prefix = "create")
            => StableKey("board", "create", string.IsNullOrEmpty(title) ? "New task" : title, prefix);

        // Short, plain-language outcome lines for the card (never a claim the backend didn't make).
        static string Confirmation(string label, TaskCardV2 c) => label switch
        {
            "assign" => AssignLine(c),
            "priority" => c != null ? "Priority: " + TaskCardView.PriorityWord(c.Priority) : "Priority set",
            "pause" => "Paused",
            "resume" => "Back in the queue",
            _ => "Done",
        };

        /// <summary>Words for a confirmed command, from the canonical card the gateway returned.
        /// <paramref name="label"/> is "assign", "priority", "pause" or "resume".</summary>
        public static string DescribeConfirmation(string label, TaskCardV2 canonical) => Confirmation(label, canonical);

        /// <summary>Words for a refused command (a 409 body, a transport failure or a client-side
        /// "not ready"). Same wording the card itself shows.</summary>
        public static string DescribeRefusal(CommandErrorBody error) => Reason(error);

        // An executable card that the backend has started says so. A planned card says plainly that
        // it was only assigned: no agent runs it (never imply work began when it did not).
        static string AssignLine(TaskCardV2 c)
        {
            if (c == null || string.IsNullOrEmpty(c.AgentId)) return "Assigned";
            string who = TaskCardView.RoleWord(c.AgentId);
            if (c.Executable && c.TaskState == TaskStates.Running) return who + " started it";
            // Short enough for the card's state line; "only" keeps it honest: nothing runs it.
            return c.Executable ? "Assigned to " + who : "Planned only: " + who;
        }

        static string Reason(CommandErrorBody e)
        {
            if (e == null) return "Not accepted";
            return e.Code switch
            {
                CommandErrorCodes.TaskRunning => "Already running",
                CommandErrorCodes.TaskNotAssignable => "Finished tasks can't move",
                CommandErrorCodes.UnsafeTransfer => "Can't move it right now",
                CommandErrorCodes.TaskNotPausable => "Can't pause this now",
                CommandErrorCodes.RoleIncompatible => "Starts with the Researcher",
                CommandErrorCodes.RunInProgress => "Another task is running",
                CommandErrorCodes.TaskPaused => "Paused: resume it first",
                CommandErrorCodes.DependenciesNotReady => "Waiting on another task",
                CommandErrorCodes.ExecutorUnavailable => "Nothing can run it right now",
                CommandErrorCodes.TaskNotPaused => "It isn't paused",
                CommandErrorCodes.RevisionConflict => "It just changed. Try again",
                "CLIENT_NOT_READY" => "Board is still loading",
                "TRANSPORT_ERROR" => "Couldn't reach the desktop",
                "BUSY" => "Already sending",
                "CANCELLED" => "Cancelled before it was sent",
                _ => string.IsNullOrEmpty(e.Message) ? "Not accepted" : e.Message,
            };
        }

        static Phase2CommandResult<TaskCardV2> TaskGone() =>
            Phase2CommandResult<TaskCardV2>.Conflict(new CommandErrorBody
            { Code = "TASK_GONE", Message = "the task left the board" });

        static Phase2CommandResult<TaskCardV2> NotReady() =>
            Phase2CommandResult<TaskCardV2>.Conflict(new CommandErrorBody
            { Code = "CLIENT_NOT_READY", Message = "board not initialized" });

        /// <summary>Shared command runner: issue, reconcile the view to canonical truth (success card
        /// or the 409's canonical_task), keep the reducer coherent, and surface the result — never
        /// fabricating success. Mirrors RequestAssignAsync's contract for the other task commands.</summary>
        async Task<Phase2CommandResult<TaskCardV2>> RunCardCommandAsync(
            TaskCardView card, Func<CancellationToken, Task<Phase2CommandResult<TaskCardV2>>> invoke, string label,
            string idempotencyKey)
        {
            if (card.TaskGone) return TaskGone();   // Polish S9b: nothing is sent for a task that left the board
            // One in flight per card (S2): a second command on the same card is ignored with a note.
            string lockId = card.Data.TaskId;
            if (!_ledger.TryLock(lockId)) { card.ShowPending("Already sending"); return Busy(); }
            Phase2CommandResult<TaskCardV2> outcome = null;
            try { outcome = await RunCardCommandCoreAsync(card, invoke, label); return outcome; }
            finally { _ledger.Unlock(lockId, idempotencyKey, IsDefinitive(outcome)); }
        }

        /// <summary>Records an accepted command, and tells whether a refusal is a late stale 409 that
        /// must not replace a recent success line for the same card.</summary>
        bool ShieldedByRecentSuccess(string taskId, float startedAt, Phase2CommandResult<TaskCardV2> result)
        {
            if (taskId == null) return false;
            if (result.Ok) { _acceptedAt[taskId] = Time.unscaledTime; return false; }
            // Only a command that was accepted WHILE this one was in flight can make this refusal stale
            // (the lock normally prevents that; this covers an event-driven change or a lock-less path).
            return result.Error?.Code == CommandErrorCodes.RevisionConflict
                && _acceptedAt.TryGetValue(taskId, out float t) && t >= startedAt;
        }

        async Task<Phase2CommandResult<TaskCardV2>> RunCardCommandCoreAsync(
            TaskCardView card, Func<CancellationToken, Task<Phase2CommandResult<TaskCardV2>>> invoke, string label)
        {
            string taskId = card.Data?.TaskId;
            float startedAt = Time.unscaledTime;
            Phase2CommandResult<TaskCardV2> result;
            card.ShowPending();
            try
            {
                result = await invoke(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                return Phase2CommandResult<TaskCardV2>.Conflict(new CommandErrorBody
                { Code = "CANCELLED", Message = "command cancelled" });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MuseDesk.P2] {label} transport error: {ex.Message}");
                card.ShowResult(false, "Couldn't reach the desktop");
                _ = ResyncAsync();
                return Phase2CommandResult<TaskCardV2>.Conflict(new CommandErrorBody
                { Code = "TRANSPORT_ERROR", Message = ex.Message });
            }

            var canonical = result.Ok ? result.Value : result.Error?.CanonicalTask;
            if (canonical != null)
            {
                card.SetData(canonical);
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
                Debug.Log($"[MuseDesk.P2] {label} rejected: {result.Error?.Code} — {result.Error?.Message}");
            if (!ShieldedByRecentSuccess(taskId, startedAt, result))
                card.ShowResult(result.Ok, result.Ok ? Confirmation(label, canonical) : Reason(result.Error));

            AssignResolved?.Invoke(card, result);
            return result;
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

            if (card.TaskGone) return TaskGone();   // Polish S9b: nothing is sent for a task that left the board
            // One in flight per card (S2), shared with the pad / chip pokes, the drag and the dock Start key.
            string lockId = card.Data.TaskId;
            if (!_ledger.TryLock(lockId)) { card.ShowPending("Already sending"); return Busy(); }
            Phase2CommandResult<TaskCardV2> outcome = null;
            try { outcome = await RequestAssignCoreAsync(card, targetAgentId, idempotencyKey); return outcome; }
            finally { _ledger.Unlock(lockId, idempotencyKey, IsDefinitive(outcome)); }
        }

        async Task<Phase2CommandResult<TaskCardV2>> RequestAssignCoreAsync(
            TaskCardView card, string targetAgentId, string idempotencyKey)
        {
            string taskId = card.Data.TaskId;
            float startedAt = Time.unscaledTime;

            var cmd = new AssignCommand
            {
                TargetAgentId = targetAgentId,
                ExpectedRevision = card.Data.Revision,
                IdempotencyKey = idempotencyKey,
            };

            Phase2CommandResult<TaskCardV2> result;
            card.ShowPending();
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
                card.ShowResult(false, "Couldn't reach the desktop");
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
            if (!ShieldedByRecentSuccess(taskId, startedAt, result))
                card.ShowResult(result.Ok, result.Ok ? Confirmation("assign", canonical) : Reason(result.Error));

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
