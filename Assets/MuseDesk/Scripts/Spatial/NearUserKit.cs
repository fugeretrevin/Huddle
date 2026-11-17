using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Keeps the user's own controls where they are when the office goes away.
    ///
    /// Two kinds of things stay with the user, both through one rig (Spatial/NearUserRig) that
    /// stands at the office's DESK pose:
    ///
    ///  1. MOVABLE PANELS (<see cref="MovablePanel"/>: the task dock with its board, the mode plaque,
    ///     the mode console with the Focus key, the completion receipt) live under the rig
    ///     PERMANENTLY. While the office is at the desk and at rest this component copies the office
    ///     root's pose, scale and active state onto the rig every frame, so a panel the user never
    ///     moved is exactly where it was as a child of the root (a panel holder has identity local
    ///     pose), and it follows a new desk placement or a recenter. When the office leaves for the
    ///     floor or the ceiling the rig simply stops following: nothing is re-parented, so a panel
    ///     keeps whatever pose the user gave it, and its saved pose stays relative to the desk.
    ///
    ///  2. LOOSE ITEMS (the stamps and their pad, the artifact viewer) are still children of the
    ///     office root, because their own scripts reference it. As before, they are moved under the
    ///     rig when the office leaves Desk mode, keeping every local position unchanged, and moved
    ///     back when the office is at the desk again and at rest. The stamps are additionally shown
    ///     only while a decision waits (<see cref="decisionOnly"/>), by scale, exactly as before.
    ///
    /// START-UP REACH (UnifiedModeDockV1, rc8: "the Start/board dock was multiple steps away when I started the app").
    /// The desk pose comes from one head sample in the first seconds (WorkspacePlacement) and is never revisited, so a
    /// stale sample left every panel out of reach. During a short window after the first desk pose this component checks
    /// the panels against the live head (<see cref="StartupReachRule"/>) and, when the first pose was taken without a
    /// usable head or a panel is beyond 0.8 m, re-seats the RIG in front of the head (the office itself is not moved). The
    /// correction is kept as a pose relative to the office root, so the rig still follows the desk afterwards. It never
    /// acts on tracking loss, during a preview or a move, with something in hand, or after the user moved a panel.
    /// The window runs in EVERY mode (rc9): away from the desk the rig stands still, so re-seating it moves panels only;
    /// there the correction is kept relative to the root only when the root is at its desk pose (Desk, or Wall once the
    /// desk was placed), never relative to a Floor / Ceiling office.
    ///
    /// STRANDED PANELS (rc9 owner: "the panel for placing the wall or choosing floor or ceiling were entirely gone").
    /// The rig only learns the desk pose while the office rests at a PLACED desk. If the office leaves the desk before
    /// that ever happened (any mode, any path: a restore at launch, voice, a driver), the rig used to stay at the root's
    /// pre-placement pose, out of sight. Now it is seated in front of the head (<see cref="StartupReachRule.DecideAwaySeat"/>,
    /// <see cref="StartupReachRule.ReseatPose"/>) as soon as the head is usable and nothing is in hand, and the start-up
    /// reach window is armed from that moment. Panels only: the office never moves.
    ///
    /// Presentation only. It never touches session or task state.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class NearUserKit : MonoBehaviour
    {
        [Tooltip("Loose objects that stay with the user while the office is away. Direct or indirect children of the office root. Movable panels are NOT listed here: they live under the rig.")]
        public Transform[] items;
        [Tooltip("Rig that stands at the office's desk pose. Not a child of the office root. Movable panels are its children.")]
        public Transform rig;
        [Tooltip("The office root, used until the director has published it. Optional.")]
        public Transform workspaceRoot;
        [Tooltip("Keep the rig on the office root while the office is at the desk and at rest (needed for movable panels). " +
                 "Off: the rig is only placed when the office leaves, as before the panels existed.")]
        public bool followDesk = true;

        readonly List<Transform> _homes = new List<Transform>();
        readonly List<int> _siblingIndex = new List<int>();
        bool _detached;
        bool _subscribed;
        bool _hasDeskPose;

        /// <summary>True while the items are parked at the desk and the office is elsewhere.</summary>
        public bool Detached => _detached;
        /// <summary>True once the rig has taken the office's desk pose at least once.</summary>
        public bool HasDeskPose => _hasDeskPose;
        /// <summary>True once the rig has stood at a PLACED desk pose (the desk placement had finished) this session.</summary>
        public bool HasPlacedDeskPose => _hasPlacedDeskPose;
        /// <summary>Times the rig was seated in front of the head because the office left the desk before any placed desk pose.</summary>
        public int AwaySeats => _awaySeats;

        bool _hasPlacedDeskPose, _awaySeated;
        int _awaySeats;

        Transform Root()
        {
            Transform root = SpatialWorkspaceState.WorkspaceRoot;
            return root != null ? root : workspaceRoot;
        }

        [Header("Start-up reach (AgentScripts/UnifiedModeDockV1)")]
        [Tooltip("Re-seat the rig in front of the head at start-up when the panels are out of reach (see StartupReachRule).")]
        public bool startupReach = true;
        [Tooltip("rc9: when the office leaves the desk before the rig ever stood at a placed desk pose, seat the rig in front of the head (panels only; the office never moves).")]
        public bool seatWhenStranded = true;
        [Tooltip("The user's head. Camera.main when empty.")]
        public Transform head;
        [Tooltip("The desk placement: its offDeskSide / Depth / Drop give the pose the rig is re-seated at. Found when empty.")]
        public MuseDesk.Workspace.WorkspacePlacement deskPlacement;
        [Tooltip("Optional: the director (no re-seat while a preview is open).")]
        public SpatialWorkspaceDirector director;

        // The rig's pose relative to the office root (identity until a start-up re-seat).
        Vector3 _fixPos;
        Quaternion _fixRot = Quaternion.identity;
        bool _hasFix;
        // Start-up window bookkeeping.
        float _deskPoseAt = -1f, _lastReseatAt = -100f, _steady;
        bool _firstPoseWithoutHead, _userMoved, _reachDone, _hasLastRot;
        int _reseats;
        bool _directorLooked;
        Quaternion _lastHeadRot;
        readonly List<MovablePanel> _hooked = new List<MovablePanel>();
        string _reachNote = "";

        /// <summary>Times the rig was re-seated in front of the head this session.</summary>
        public int Reseats => _reseats;
        /// <summary>True once the start-up window is over (or the user took over the layout).</summary>
        public bool StartupReachDone => _reachDone;
        /// <summary>What the start-up reach did last, in words (drivers).</summary>
        public string ReachNote => _reachNote;
        public Transform Rig => rig;

        void OnEnable()
        {
            if (!_subscribed) { SpatialWorkspaceState.Changed += Sync; _subscribed = true; }
            FollowDesk();
            Sync();
        }

        void OnDisable()
        {
            if (_subscribed) { SpatialWorkspaceState.Changed -= Sync; _subscribed = false; }
            for (int i = 0; i < _hooked.Count; i++) if (_hooked[i] != null) _hooked[i].DragChanged -= OnPanelDrag;
            _hooked.Clear();
            // Never leave the controls stranded under a rig nobody manages.
            if (_detached) Reattach();
        }

        void OnPanelDrag(bool dragging) { if (dragging) _userMoved = true; }

        /// <summary>The desk pose the rig stands at: the office root's pose, with the start-up correction when there is one.</summary>
        void DeskPose(Transform root, out Vector3 position, out Quaternion rotation)
        {
            if (!_hasFix) { position = root.position; rotation = root.rotation; return; }
            StartupReachRule.FromRootLocal(root.position, root.rotation, _fixPos, _fixRot, out position, out rotation);
        }

        void Sync()
        {
            bool away = SpatialWorkspaceState.Mode != WorkspaceMode.Desk;
            if (away && !_detached) Detach();
            else if (!away && _detached && !SpatialWorkspaceState.IsTransitioning) Reattach();
        }

        /// <summary>
        /// While the office is at the desk and at rest: the rig takes the root's pose, scale and
        /// active state. Otherwise the rig keeps the last desk pose and stays active.
        /// </summary>
        void FollowDesk()
        {
            if (!followDesk || rig == null) return;
            Transform root = Root();
            if (root == null) return;
            bool atDesk = SpatialWorkspaceState.Mode == WorkspaceMode.Desk && !SpatialWorkspaceState.IsTransitioning && !_detached;
            if (!atDesk)
            {
                if (!rig.gameObject.activeSelf) rig.gameObject.SetActive(true);
                return;
            }
            Vector3 p; Quaternion q;
            DeskPose(root, out p, out q);
            if (rig.position != p || rig.rotation != q)
                rig.SetPositionAndRotation(p, q);
            Vector3 scale = root.lossyScale;
            if (rig.localScale != scale) rig.localScale = scale;
            // The office is hidden until the desk is placed; its controls are hidden with it.
            bool on = root.gameObject.activeInHierarchy;
            if (rig.gameObject.activeSelf != on) rig.gameObject.SetActive(on);
            _hasDeskPose = true;
            if (!_hasPlacedDeskPose && on && DeskPlaced(root)) _hasPlacedDeskPose = true;
        }

        void Detach()
        {
            Transform root = Root();
            if (root == null || rig == null) return;
            // The rig stands at the desk pose. If it has been following the root it already has it
            // (and the root may have started to move this frame); otherwise take the root's pose now.
            if (!(followDesk && _hasDeskPose))
            {
                Vector3 p; Quaternion q;
                DeskPose(root, out p, out q);
                rig.SetPositionAndRotation(p, q);
                rig.localScale = root.lossyScale;
            }
            if (!rig.gameObject.activeSelf) rig.gameObject.SetActive(true);
            _homes.Clear(); _siblingIndex.Clear(); _homeLocals.Clear();
            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    Transform t = items[i];
                    _homes.Add(t != null ? t.parent : null);
                    _siblingIndex.Add(t != null ? t.GetSiblingIndex() : 0);
                    _homeLocals.Add(t != null ? new Pose(t.localPosition, t.localRotation) : Pose.identity);
                    if (t == null) continue;
                    Vector3 lp = t.localPosition; Quaternion lr = t.localRotation; Vector3 ls = t.localScale;
                    // Only items directly under the root keep identical locals (the rig then stands exactly on the root);
                    // with a start-up reach correction the rig is elsewhere, so they keep their world pose (they stay at the desk).
                    if (t.parent == root && !_hasFix) { t.SetParent(rig, false); t.localPosition = lp; t.localRotation = lr; t.localScale = ls; }
                    else t.SetParent(rig, true);
                }
            }
            _detached = true;
            _awaySeated = false;
            // Left before the desk was ever placed: the pose above is the root's pre-placement pose, not a desk.
            TrySeatAway("the office left the desk before the first desk pose");
        }

        void Reattach()
        {
            if (items != null)
            {
                Transform root = Root();
                for (int i = 0; i < items.Length && i < _homes.Count; i++)
                {
                    Transform t = items[i]; Transform home = _homes[i];
                    if (t == null || home == null) continue;
                    Vector3 lp = t.localPosition; Quaternion lr = t.localRotation; Vector3 ls = t.localScale;
                    if (home == root)
                    {
                        t.SetParent(home, false);
                        // Back exactly where it was on the root (also when the rig was re-seated in the meantime).
                        if (_hasFix && i < _homeLocals.Count) { lp = _homeLocals[i].position; lr = _homeLocals[i].rotation; }
                        t.localPosition = lp; t.localRotation = lr; t.localScale = ls;
                    }
                    else t.SetParent(home, true);
                    t.SetSiblingIndex(Mathf.Min(_siblingIndex[i], home.childCount - 1));
                }
            }
            _homes.Clear(); _siblingIndex.Clear(); _homeLocals.Clear();
            _detached = false;
            _awaySeated = false;
        }

        // ───────────────────────── stranded panels (rc9) ─────────────────────────

        /// <summary>
        /// Away from the desk with no placed desk pose yet: seat the rig in front of the head once (StartupReachRule.
        /// DecideAwaySeat). Waits on tracking loss or with something in hand; called on leaving and every frame after.
        /// </summary>
        void TrySeatAway(string why)
        {
            if (!seatWhenStranded || rig == null || !_detached) return;
            if (_hasPlacedDeskPose || _awaySeated) return;                 // the common case: nothing to decide
            Transform root = Root();
            if (root == null) return;
            Transform h = Head();
            AwaySeatInput input = new AwaySeatInput
            {
                away = _detached,
                hasPlacedDeskPose = _hasPlacedDeskPose,
                seated = _awaySeated,
                headUsable = h != null && HeadUsableNow(h),
                held = HandInteractionProbe.AnyHeld || SpatialWorkspaceState.TransitionHeld,
                userMovedPanel = _userMoved,
            };
            if (StartupReachRule.DecideAwaySeat(input) != AwaySeatAction.Seat) return;
            if (deskPlacement == null) deskPlacement = FindAnyObjectByType<MuseDesk.Workspace.WorkspacePlacement>();
            float side = 0f, depth = 0.62f, drop = 0.39f;
            if (deskPlacement != null) { side = deskPlacement.offDeskSide; depth = deskPlacement.offDeskDepth; drop = deskPlacement.offDeskDrop; }
            Vector3 p; Quaternion q;
            StartupReachRule.ReseatPose(h.position, h.forward, side, depth, drop, out p, out q);
            rig.SetPositionAndRotation(p, q);
            Vector3 s = rig.localScale;
            if (s.x < 1e-3f || s.y < 1e-3f || s.z < 1e-3f) rig.localScale = Vector3.one;    // a hidden root's scale is no layout
            if (!rig.gameObject.activeSelf) rig.gameObject.SetActive(true);
            ResetStalePanels(h.position);
            _awaySeated = true;
            _awaySeats++;
            // Arm the start-up reach window from here (the rig never had a desk pose to start it).
            if (_deskPoseAt < 0f) { _deskPoseAt = Time.unscaledTime; _firstPoseWithoutHead = false; }
            _reachNote = "seated in front of the head (" + why + ")";
            Debug.Log("[MuseDesk] Near-user panels seated in front of the head: " + why + " (mode " + SpatialWorkspaceState.Mode + "). The office was not moved.");
        }

        /// <summary>A saved panel pose from an earlier session that is still out of reach is stale: back to its default.</summary>
        void ResetStalePanels(Vector3 headPos)
        {
            for (int i = 0; i < MovablePanel.All.Count; i++)
            {
                MovablePanel mp = MovablePanel.All[i];
                if (mp == null || mp.transform.parent != rig) continue;
                if (Vector3.Distance(mp.PivotWorld, headPos) > StartupReachRule.MaxReach && !string.IsNullOrEmpty(mp.SavedText()))
                    mp.ResetToDefault(false);
            }
        }

        /// <summary>
        /// Play Mode driver only: replay "the office left the desk before the first desk pose" from where it is now (the
        /// office must be away, e.g. Wall after Place). The items come home, the rig forgets its desk pose and is put at
        /// <paramref name="strandAt"/> (where the pre-placement root would leave it), then the office leaves again through
        /// the same Detach path the app uses. The office itself is not touched.
        /// </summary>
        public string DriverLeaveDeskWithoutDeskPose(Vector3 strandAt)
        {
            if (rig == null || Root() == null) return "no rig or office root";
            if (SpatialWorkspaceState.Mode == WorkspaceMode.Desk) return "the office is at the desk: put it away first (e.g. Wall -> Place)";
            if (_detached) Reattach();
            // rc9: the rig HAD followed the root (so _hasDeskPose stays true and Detach keeps the rig where it stands), but
            // the root was at its pre-placement pose: the rig never stood at a placed desk.
            _hasPlacedDeskPose = false;
            _awaySeated = false;
            rig.position = strandAt;
            Detach();
            return "stranded at " + strandAt.ToString("F2") + ", left again: " + (_awaySeated ? "seated in front of the head" : "waiting for a usable head / free hands")
                 + " | rig at " + rig.position.ToString("F2");
        }

        readonly List<Pose> _homeLocals = new List<Pose>();
        Vector3 _fixRootPos;
        Quaternion _fixRootRot = Quaternion.identity;

        // ───────────────────────── start-up reach ─────────────────────────

        Transform Head()
        {
            if (head == null) { Camera c = Camera.main; if (c != null) head = c.transform; }
            return head;
        }

        static bool HeadUsableNow(Transform h)
        {
            if (!StartupReachRule.HeadUsable(h.position, h.forward)) return false;
#if !UNITY_EDITOR
            try { if (!OVRPlugin.userPresent) return false; } catch { }     // headset not on a head: the pose is not the user's
#endif
            return true;
        }

        /// <summary>The desk placement has finished (from a table or in front of the user). Without one: the root is active.</summary>
        bool DeskPlaced(Transform root)
        {
            if (deskPlacement == null) deskPlacement = FindAnyObjectByType<MuseDesk.Workspace.WorkspacePlacement>();
            if (deskPlacement == null) return root.gameObject.activeInHierarchy;
            var s = deskPlacement.State;
            return s == MuseDesk.Workspace.WorkspacePlacement.PlacementState.PlacedManual
                || s == MuseDesk.Workspace.WorkspacePlacement.PlacementState.PlacedFromTable;
        }

        void HookPanels()
        {
            for (int i = 0; i < MovablePanel.All.Count; i++)
            {
                MovablePanel p = MovablePanel.All[i];
                if (p == null || _hooked.Contains(p) || rig == null || p.transform.parent != rig) continue;
                p.DragChanged += OnPanelDrag;
                _hooked.Add(p);
            }
        }

        /// <summary>Farthest and nearest pivot of the panels under the rig from the head; worst -1 when there are none.</summary>
        void MeasurePanels(Vector3 headPos, out float worst, out float nearest)
        {
            worst = -1f; nearest = float.MaxValue;
            for (int i = 0; i < MovablePanel.All.Count; i++)
            {
                MovablePanel p = MovablePanel.All[i];
                if (p == null || rig == null || p.transform.parent != rig || !p.isActiveAndEnabled) continue;
                float d = Vector3.Distance(p.PivotWorld, headPos);
                if (d > worst) worst = d;
                if (d < nearest) nearest = d;
            }
            if (worst < 0f) nearest = -1f;
        }

        void TickStartupReach()
        {
            if (!startupReach || _reachDone || rig == null) return;
            Transform root = Root();
            if (root == null) return;
            Transform h = Head();
            float now = Time.unscaledTime;

            // A new desk placement (WorkspacePlacement re-placed the office) replaces the one the correction was made for.
            bool restingAtDesk = SpatialWorkspaceState.Mode == WorkspaceMode.Desk && !SpatialWorkspaceState.IsTransitioning;
            if (_hasFix && restingAtDesk
                && ((root.position - _fixRootPos).sqrMagnitude > 1e-4f || Quaternion.Angle(root.rotation, _fixRootRot) > 1f))
            {
                _hasFix = false;
                _reachNote = "the office was placed again: start-up correction dropped";
            }

            // Head steadiness (as WorkspacePlacement: under 45 degrees per second).
            bool usable = h != null && HeadUsableNow(h);
            if (usable)
            {
                float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
                float rate = _hasLastRot ? Quaternion.Angle(_lastHeadRot, h.rotation) / dt : float.MaxValue;
                _lastHeadRot = h.rotation; _hasLastRot = true;
                _steady = rate < StartupReachRule.SteadyDegPerSec ? _steady + Time.unscaledDeltaTime : 0f;
            }
            else { _steady = 0f; _hasLastRot = false; }

            if (_deskPoseAt < 0f && _hasDeskPose && DeskPlaced(root) && root.gameObject.activeInHierarchy)
            {
                _deskPoseAt = now;
                _firstPoseWithoutHead = !usable;
            }
            HookPanels();

            float worst = -1f, nearest = -1f;
            if (h != null) MeasurePanels(h.position, out worst, out nearest);
            bool held = HandInteractionProbe.AnyHeld || SpatialWorkspaceState.TransitionHeld;
            if (director == null && !_directorLooked) { director = FindAnyObjectByType<SpatialWorkspaceDirector>(); _directorLooked = true; }
            bool previewing = director != null && director.IsPreviewing;
            IWallModeHost wall = director != null ? director.wallHost as IWallModeHost : null;
            bool wallPreviewing = wall != null && wall.WallPreviewing;
            // Any mode (rc9): away from the desk the rig stands still, so a re-seat moves panels only.
            bool placeable = StartupReachRule.Placeable(SpatialWorkspaceState.IsTransitioning, held, previewing, wallPreviewing);

            StartupReachInput input = new StartupReachInput
            {
                secondsSinceDeskPose = _deskPoseAt < 0f ? -1f : now - _deskPoseAt,
                headUsable = usable,
                headSteadySeconds = _steady,
                placeable = placeable,
                userMovedPanel = _userMoved,
                firstPoseWithoutHead = _firstPoseWithoutHead,
                reseats = _reseats,
                secondsSinceLastReseat = now - _lastReseatAt,
                worstDistance = worst,
                nearestDistance = nearest,
            };
            switch (StartupReachRule.Decide(input))
            {
                case StartupReachAction.Done:
                    _reachDone = true;
                    if (string.IsNullOrEmpty(_reachNote)) _reachNote = _userMoved ? "the user moved a panel: layout kept" : "start-up window over: panels were in reach";
                    break;
                case StartupReachAction.Reseat:
                    Reseat(root, h, input.firstPoseWithoutHead && _reseats == 0 ? "first desk pose was taken without a usable head" : "a panel was " + worst.ToString("F2") + " m from the head");
                    break;
            }
        }

        /// <summary>Stand the rig where the desk pose would be if the office were placed from the head now. The office does not move.</summary>
        void Reseat(Transform root, Transform h, string why)
        {
            float side = 0f, depth = 0.62f, drop = 0.39f;
            if (deskPlacement != null) { side = deskPlacement.offDeskSide; depth = deskPlacement.offDeskDepth; drop = deskPlacement.offDeskDrop; }
            Vector3 p; Quaternion q;
            StartupReachRule.ReseatPose(h.position, h.forward, side, depth, drop, out p, out q);
            WorkspaceMode mode = SpatialWorkspaceState.Mode;
            if (StartupReachRule.KeepAsDeskCorrection(mode == WorkspaceMode.Desk, mode == WorkspaceMode.Wall, _hasPlacedDeskPose))
            {
                // The root stands at its desk pose: keep the correction relative to it, so the rig follows the desk afterwards.
                StartupReachRule.ToRootLocal(root.position, root.rotation, p, q, out _fixPos, out _fixRot);
                _fixRootPos = root.position; _fixRootRot = root.rotation;
                _hasFix = true;
            }
            // Otherwise (Floor / Ceiling, or no desk yet) the rig is only stood in front of the head; back at the desk it
            // follows the root again and the window re-checks.
            rig.SetPositionAndRotation(p, q);
            ResetStalePanels(h.position);
            _reseats++;
            _lastReseatAt = Time.unscaledTime;
            _reachNote = "re-seated in front of the head (" + why + ")";
            Debug.Log("[MuseDesk] Near-user panels re-seated in front of the head at start-up: " + why + ". The office was not moved.");
        }

        /// <summary>Play Mode driver: re-seat now as the start-up rule would (head must be usable). False when it could not.</summary>
        public bool ForceStartupReseat()
        {
            Transform root = Root(); Transform h = Head();
            if (root == null || rig == null || h == null || !StartupReachRule.HeadUsable(h.position, h.forward)) return false;
            if (SpatialWorkspaceState.IsTransitioning) return false;
            if (deskPlacement == null) deskPlacement = FindAnyObjectByType<MuseDesk.Workspace.WorkspacePlacement>();
            Reseat(root, h, "driver");
            return true;
        }

        /// <summary>One line for drivers.</summary>
        public string DescribeReach()
        {
            Transform h = Head();
            float worst = -1f, nearest = -1f;
            if (h != null) MeasurePanels(h.position, out worst, out nearest);
            return "startupReach " + (startupReach ? "on" : "off") + " done=" + _reachDone + " reseats=" + _reseats + " fix=" + _hasFix
                 + " firstPoseWithoutHead=" + _firstPoseWithoutHead + " userMoved=" + _userMoved
                 + " placedDeskPose=" + _hasPlacedDeskPose + " awaySeats=" + _awaySeats + " detached=" + _detached
                 + " panels worst=" + worst.ToString("F2") + " nearest=" + nearest.ToString("F2") + " m"
                 + (h != null ? " headUsable=" + StartupReachRule.HeadUsable(h.position, h.forward) : " no head")
                 + " | " + _reachNote;
        }

        [Header("Only when needed (while the office is away)")]
        [Tooltip("Shown at the desk only while a decision is waiting (the stamps and their pad). In Desk mode they are always shown, as before.")]
        public Transform[] decisionOnly;
        [Tooltip("Seconds to grow in or shrink away.")]
        public float revealSeconds = 0.25f;

        Vector3[] _fullScale;
        float _reveal = 1f;      // 0 hidden .. 1 shown

        /// <summary>True while the decision-only items (stamps) are shown.</summary>
        public bool DecisionItemsShown => _reveal > 0.5f;

        /// <summary>Set by the Wall controller while the Manager's plan paper awaits the user's stamp (flagged feature; false
        /// otherwise, so with the Wall flags off nothing changes). Main thread only.</summary>
        public static bool ExternalStampDemand;

        void Update()
        {
            if (decisionOnly == null || decisionOnly.Length == 0) return;
            if (_fullScale == null || _fullScale.Length != decisionOnly.Length)
            {
                _fullScale = new Vector3[decisionOnly.Length];
                for (int i = 0; i < decisionOnly.Length; i++)
                    _fullScale[i] = decisionOnly[i] != null ? decisionOnly[i].localScale : Vector3.one;
            }
            // The user's desk stays clear while the office is away: the stamps come out only when
            // there is something to stamp. At the desk nothing changes.
            bool want = !_detached;
            if (_detached)
            {
                AttentionController a = AttentionController.Instance;
                want = (a != null && a.PendingDecisionCount > 0) || ExternalStampDemand;
            }
            float target = want ? 1f : 0f;
            if (Mathf.Approximately(_reveal, target)) return;
            _reveal = Mathf.MoveTowards(_reveal, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, revealSeconds));
            float e = 1f - (1f - _reveal) * (1f - _reveal);          // ease out
            float k = Mathf.Lerp(0.001f, 1f, e);
            for (int i = 0; i < decisionOnly.Length; i++)
                if (decisionOnly[i] != null) decisionOnly[i].localScale = _fullScale[i] * k;
        }

        void LateUpdate()
        {
            // Returning to the desk ends with a state change, but if that was missed (domain reload,
            // disabled at the time) pick it up here; cheap, and only while detached.
            if (_detached && SpatialWorkspaceState.Mode == WorkspaceMode.Desk && !SpatialWorkspaceState.IsTransitioning)
                Reattach();
            FollowDesk();
            if (_detached && !_hasPlacedDeskPose && !_awaySeated) TrySeatAway("the office left the desk before the first desk pose");
            TickStartupReach();
        }
    }
}
