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
            // wait for valid head tracking
            float t = 0f;
            while (t < 1.0f)
            {
                t += Time.deltaTime;
                EnsureHead();
                if (_head != null && _head.position.sqrMagnitude > 0.0004f) break;
                yield return null;
            }
            EnsureHead();
            if (workspaceRoot == null) yield break;

            Vector3 pos = OffDeskPosition();
            workspaceRoot.position = pos;
            workspaceRoot.rotation = FacingUserYaw(pos);
            workspaceRoot.gameObject.SetActive(true);
            if (placementGuide != null) placementGuide.SetActive(false);
            _sceneHandled = true;
            State = PlacementState.PlacedManual;
            Placed?.Invoke(State);
            StartCoroutine(RefaceUser());
            Debug.Log("[MuseDesk] Workspace placed off-desk (side/low).");
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
            // The content under WorkspaceRoot was authored with its FRONTS on local -Z (at author time
            // the user sat on the -Z side of the content). So point WorkspaceRoot's +Z AWAY from the
            // user; its -Z — the agents' faces, prop fronts, readable text — then turns toward the user.
            // (Billboarded FaceUser objects override their own rotation and are unaffected.)
            return Quaternion.LookRotation(-toUser.normalized, Vector3.up);
        }
    }
}
