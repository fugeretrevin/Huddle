using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Meta.XR.MRUtilityKit;
using MuseDesk.Config;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// TASK-01 deliverable. Places the desk-relative WorkspaceRoot:
    ///  1. Wait for MRUK scene data (if spatial permission granted).
    ///  2. If a TABLE surface is found, anchor the tray slightly above it, oriented toward the user.
    ///  3. If permission is denied, no table is found, or scene load times out, fall back to a
    ///     hand-placed floating tray ~0.5 m in front of the user.
    /// Provides Move/Reset controls and the coordination hooks other tracks consume
    /// (WorkspaceRootTransform, GetStationAnchor(role)).
    /// </summary>
    public class WorkspacePlacement : MonoBehaviour
    {
        public enum PlacementState { WaitingForScene, PlacedFromTable, AwaitingManualConfirm, PlacedManual }

        [Header("Wiring")]
        public RuntimeSettings settings;
        [Tooltip("The movable tray root. Stations/agents/UI live under this.")]
        public Transform workspaceRoot;
        [Tooltip("Optional ghost shown while the user positions a manual tray.")]
        public GameObject placementGuide;

        [Header("Behaviour")]
        [Tooltip("Seconds to wait for MRUK scene data before falling back to manual placement.")]
        [Range(1f, 10f)] public float sceneLoadTimeout = 6f;
        [Tooltip("Vertical offset above a detected table surface, metres.")]
        [Range(0f, 0.08f)] public float tableClearance = 0.02f;

        [Header("Off-desk mode (MAIN): agents sit to the side/low and walk up to the user")]
        public bool offDeskMode = true;
        [Tooltip("Metres to the user's side (+ = right) for the off-desk station cluster.")]
        public float offDeskSide = 0.75f;
        [Tooltip("Metres in front of the user for the station cluster.")]
        public float offDeskDepth = 0.1f;
        [Tooltip("Metres below eye level (approximates desk-side / floor-adjacent).")]
        public float offDeskDrop = 0.55f;
        [Tooltip("Longest to wait for the headset to be worn and steady before placing anyway, seconds.")]
        [Range(2f, 20f)] public float settleTimeout = 10f;
        [Tooltip("Re-anchor the workspace automatically when HMDMounted / RecenteredPose fires. " +
                 "Off by default so the diorama stays put once placed.")]
        public bool autoReplaceOnRecenter = false;

        bool _placing;
        bool _hooksSubscribed;

        public PlacementState State { get; private set; } = PlacementState.WaitingForScene;
        public Transform WorkspaceRootTransform => workspaceRoot;

        /// <summary>Fired once the workspace is placed (either from table or manually confirmed).</summary>
        public event Action<PlacementState> Placed;

        readonly Dictionary<string, WorkstationAnchor> _anchors = new Dictionary<string, WorkstationAnchor>();
        Transform _head;
        bool _sceneHandled;

        void Awake()
        {
            CacheAnchors();
            var cam = Camera.main;
            _head = cam != null ? cam.transform : null;
        }

        void OnEnable()
        {
            if (offDeskMode)
            {
                StartCoroutine(PlaceOffDeskRoutine());
                return;
            }
            if (MRUK.Instance != null)
                MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
            StartCoroutine(SceneLoadWatchdog());
        }

        /// <summary>
        /// Off-desk main mode: park the station cluster to the user's side and a bit low
        /// (desk-side / floor-adjacent), facing the user, once tracking has settled. Agents
        /// then walk up to the user to deliver. No table dependency.
        /// </summary>
        IEnumerator PlaceOffDeskRoutine()
        {
            if (_placing) yield break;
            _placing = true;

            // Place only once the headset is actually WORN and the head is level and steady. The old
            // version sampled the head ~1 s after launch and locked ~2.5 s later; if the headset was
            // still being put on (or the user was looking around) the diorama locked to a stale pose,
            // so the user ended up beside or behind it instead of in front of it.
            float waited = 0f, steady = 0f;
            Quaternion lastRot = Quaternion.identity;
            bool hasLast = false;
            float maxWait = Mathf.Min(settleTimeout, 4.0f);
            while (waited < maxWait)
            {
                waited += Time.deltaTime;
                EnsureHead();
                if (_head != null && _head.position.sqrMagnitude > 0.0004f && UserPresent() && HeadLevel(_head))
                {
                    float degPerSec = hasLast
                        ? Quaternion.Angle(lastRot, _head.rotation) / Mathf.Max(Time.deltaTime, 1e-4f)
                        : float.MaxValue;
                    lastRot = _head.rotation;
                    hasLast = true;
                    steady = degPerSec < 45f ? steady + Time.deltaTime : 0f;
                    if (steady >= 0.35f) break;
                }
                else { steady = 0f; hasLast = false; }
                yield return null;
            }
            EnsureHead();
            if (workspaceRoot == null) { _placing = false; yield break; }
            // The office is on the floor or ceiling (or travelling): the spatial director owns its pose.
            if (MuseDesk.Spatial.SpatialWorkspaceState.Mode != MuseDesk.Spatial.WorkspaceMode.Desk || MuseDesk.Spatial.SpatialWorkspaceState.IsTransitioning) { _placing = false; yield break; }

            Vector3 pos = OffDeskPosition();
            workspaceRoot.position = pos;
            workspaceRoot.rotation = FacingUserYaw(pos);
            workspaceRoot.gameObject.SetActive(true);
            if (placementGuide != null) placementGuide.SetActive(false);
            _sceneHandled = true;
            State = PlacementState.PlacedManual;
            Placed?.Invoke(State);
            Debug.Log($"[MuseDesk] Workspace placed in front of user (settled after {waited:F1}s, " +
                      $"steady={steady:F1}s, head={(_head != null ? _head.position : Vector3.zero)}, root={pos}).");
            SubscribeReplaceHooks();
            _placing = false;
        }

        /// <summary>Head pitched no more than ~40° up or down (not staring at the floor or ceiling).</summary>
        static bool HeadLevel(Transform head)
        {
            Vector3 flat = head.forward; flat.y = 0f;
            return flat.sqrMagnitude > 1e-4f && Vector3.Angle(head.forward, flat) < 40f;
        }

        /// <summary>True when the proximity sensor says the headset is on a head. In the Editor (no
        /// device) this is false, so placement simply falls through after settleTimeout.</summary>
        static bool UserPresent()
        {
            try { return OVRPlugin.userPresent; }
            catch { return true; }   // API unavailable on this platform: don't block placement
        }

        // Bring the diorama back in front of the user whenever they put the headset back on or use
        // the system recenter gesture — the "it isn't in front of me" recovery path.
        void SubscribeReplaceHooks()
        {
            if (_hooksSubscribed) return;
            OVRManager.HMDMounted += OnReplaceRequested;
            if (OVRManager.display != null) OVRManager.display.RecenteredPose += OnReplaceRequested;
            _hooksSubscribed = true;
        }

        void UnsubscribeReplaceHooks()
        {
            if (!_hooksSubscribed) return;
            OVRManager.HMDMounted -= OnReplaceRequested;
            if (OVRManager.display != null) OVRManager.display.RecenteredPose -= OnReplaceRequested;
            _hooksSubscribed = false;
        }

        void OnReplaceRequested()
        {
            if (!offDeskMode || !isActiveAndEnabled) return;
            if (!autoReplaceOnRecenter && _sceneHandled) return;
            StartCoroutine(PlaceOffDeskRoutine());
        }

        Vector3 OffDeskPosition()
        {
            EnsureHead();
            if (_head == null) return new Vector3(offDeskSide, 0.5f, offDeskDepth);
            Vector3 fwd = _head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            return _head.position + right * offDeskSide + fwd * offDeskDepth + Vector3.down * offDeskDrop;
        }

        void EnsureHead()
        {
            if (_head != null) return;
            var cam = Camera.main;
            _head = cam != null ? cam.transform : null;
        }

        void OnDisable()
        {
            UnsubscribeReplaceHooks();
            _placing = false;   // a disabled component's coroutines are stopped; don't stay latched
            if (MRUK.Instance != null)
                MRUK.Instance.SceneLoadedEvent.RemoveListener(OnSceneLoaded);
        }

        void CacheAnchors()
        {
            _anchors.Clear();
            if (workspaceRoot == null) return;
            foreach (var a in workspaceRoot.GetComponentsInChildren<WorkstationAnchor>(true))
                _anchors[a.role] = a;
        }

        public WorkstationAnchor GetStationAnchor(string role)
            => _anchors.TryGetValue(role, out var a) ? a : null;

        // ─────────────────────── MRUK path ───────────────────────
        void OnSceneLoaded()
        {
            if (_sceneHandled) return;
            MRUKAnchor table = null;
            try
            {
                var room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
                if (room != null)
                    table = room.FindLargestSurface(MRUKAnchor.SceneLabels.TABLE);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MuseDesk] MRUK table query failed: {e.Message}");
            }

            if (table != null)
            {
                _sceneHandled = true;
                PlaceOnTable(table.transform.position);
            }
            else
            {
                BeginManualPlacement("No table found in scene data.");
            }
        }

        IEnumerator SceneLoadWatchdog()
        {
            float t = 0f;
            while (t < sceneLoadTimeout && !_sceneHandled)
            {
                t += Time.deltaTime;
                yield return null;
            }
            if (!_sceneHandled)
                BeginManualPlacement("Scene data unavailable or permission denied.");
        }

        void PlaceOnTable(Vector3 surfacePos)
        {
            if (workspaceRoot == null) return;
            Vector3 pos = surfacePos + Vector3.up * tableClearance;
            workspaceRoot.position = pos;
            workspaceRoot.rotation = FacingUserYaw(pos);
            workspaceRoot.gameObject.SetActive(true);
            if (placementGuide != null) placementGuide.SetActive(false);
            State = PlacementState.PlacedFromTable;
            Placed?.Invoke(State);
            StartCoroutine(RefaceUser());
            Debug.Log("[MuseDesk] Workspace placed on detected table.");
        }

        /// <summary>
        /// Re-orients the workspace toward the user for a couple of seconds after placement.
        /// Head tracking is often not settled at placement time, so the first FacingUserYaw can
        /// point the diorama the wrong way; this converges it once tracking is live, then locks.
        /// </summary>
        IEnumerator RefaceUser()
        {
            float t = 0f;
            while (t < 2.5f)
            {
                t += Time.deltaTime;
                // One writer per mode: once the office is on the floor / ceiling, or a mode transition is moving
                // it, the spatial director owns the root and this loop must stop (same guard as the other
                // placement writers above).
                if (MuseDesk.Spatial.SpatialWorkspaceState.Mode != MuseDesk.Spatial.WorkspaceMode.Desk
                    || MuseDesk.Spatial.SpatialWorkspaceState.IsTransitioning) yield break;
                if (workspaceRoot != null)
                {
                    var target = FacingUserYaw(workspaceRoot.position);
                    workspaceRoot.rotation = Quaternion.Slerp(workspaceRoot.rotation, target,
                        Time.deltaTime * 8f);
                }
                yield return null;
            }
        }

        // ─────────────────────── manual fallback ───────────────────────
        void BeginManualPlacement(string reason)
        {
            if (_sceneHandled && State == PlacementState.PlacedFromTable) return;
            _sceneHandled = true;
            Debug.Log($"[MuseDesk] Manual placement: {reason}");
            State = PlacementState.AwaitingManualConfirm;
            float dist = settings != null ? settings.manualTrayDistance : 0.5f;
            Vector3 pos = InFrontOfUser(dist);
            if (placementGuide != null)
            {
                placementGuide.transform.SetPositionAndRotation(pos, FacingUserYaw(pos));
                placementGuide.SetActive(true);
            }
            if (workspaceRoot != null)
            {
                workspaceRoot.SetPositionAndRotation(pos, FacingUserYaw(pos));
                workspaceRoot.gameObject.SetActive(true);
            }
        }

        /// <summary>Call from a hand-driven world-space button to lock in the manual tray.</summary>
        public void ConfirmManualPlacement()
        {
            if (MuseDesk.Spatial.SpatialWorkspaceState.Mode != MuseDesk.Spatial.WorkspaceMode.Desk || MuseDesk.Spatial.SpatialWorkspaceState.IsTransitioning) return;
            if (placementGuide != null) placementGuide.SetActive(false);
            State = PlacementState.PlacedManual;
            Placed?.Invoke(State);
            StartCoroutine(RefaceUser());
            Debug.Log("[MuseDesk] Manual workspace placement confirmed.");
        }

        /// <summary>Re-centre the tray in front of the user (Move/Reset control).</summary>
        public void ResetWorkspaceInFront()
        {
            if (workspaceRoot == null) return;
            if (MuseDesk.Spatial.SpatialWorkspaceState.Mode != MuseDesk.Spatial.WorkspaceMode.Desk || MuseDesk.Spatial.SpatialWorkspaceState.IsTransitioning) return;   // the spatial director owns the pose away from the desk
            float dist = settings != null ? settings.manualTrayDistance : 0.5f;
            Vector3 pos = InFrontOfUser(dist);
            workspaceRoot.SetPositionAndRotation(pos, FacingUserYaw(pos));
        }

        // ─────────────────────── helpers ───────────────────────
        Vector3 InFrontOfUser(float distance)
        {
            if (_head == null)
            {
                var cam = Camera.main;
                _head = cam != null ? cam.transform : null;
            }
            if (_head == null) return new Vector3(0f, 1.0f, distance);
            Vector3 fwd = _head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();
            return _head.position + fwd * distance + Vector3.down * 0.25f;
        }

        Quaternion FacingUserYaw(Vector3 fromPos)
        {
            if (_head == null) return Quaternion.identity;
            Vector3 toUser = _head.position - fromPos;
            toUser.y = 0f;
            if (toUser.sqrMagnitude < 0.0001f) return Quaternion.identity;
            // The diorama's FRONT is local +Z: the agents face +Z across their desks, and the factory,
            // trays and controls sit on the +Z side. So WorkspaceRoot's +Z must point AT the user.
            // (Verified in Scene View 2026-11-12: from +Z you see faces, from -Z you see backs. A brief
            // experiment that pointed -Z at the user was wrong and was reverted.)
            return Quaternion.LookRotation(toUser.normalized, Vector3.up);
        }
    }
}
