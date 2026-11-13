using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Entry point of the optional room-aware workspace: modes Desk / Floor / Ceiling.
    ///
    /// Flow for Floor: RequestMode(Floor) -> candidates from the detected floor -> a ghost outline on
    /// the first one -> NextCandidate / Nudge / Rotate -> Confirm moves the office (and only Confirm
    /// does) or Cancel leaves everything as it was. Without usable room data the user is offered
    /// manual placement and told so in words. Ceiling works the same way but only on real ceiling
    /// geometry; once confirmed the office docks under the ceiling and BringPodDown / SendPodUp move
    /// it. RequestMode(Desk) returns the office to exactly the pose it had when Desk was left.
    ///
    /// Both feature flags are false by default. With both false this component publishes Desk
    /// state once and then does nothing: no room subscription, no per-frame work, no keys shown.
    ///
    /// It is the only writer of <see cref="SpatialWorkspaceState"/>. Presentation only: it holds no
    /// reference to the session, gateways or reducers, so a mode change cannot start, cancel or
    /// reset work. The logic lives in <see cref="SpatialDirectorCore"/> (testable without a scene).
    /// </summary>
    public class SpatialWorkspaceDirector : MonoBehaviour
    {
        [Header("Feature flags (both off by default)")]
        public SpatialFeatureFlags flags = new SpatialFeatureFlags();

        [Header("Wiring")]
        public Transform workspaceRoot;
        [Tooltip("The existing desk placement. It keeps owning the root in Desk mode and is told to stand down otherwise.")]
        public WorkspacePlacement deskPlacement;
        public RoomPlacementService roomService;
        public WorkspaceTransitionController transition;
        public CeilingPodController pod;
        public WorkspaceGhostPreview ghost;
        public WorkspacePlacementPersistence persistence;
        public SpatialModeStrip strip;
        [Tooltip("The user's head. Camera.main is used when empty.")]
        public Transform head;

        [Header("Placement")]
        public FloorPlacementSettings floorSettings = new FloorPlacementSettings();
        public CeilingPlacementSettings ceilingSettings = new CeilingPlacementSettings();
        [Tooltip("Office footprint (width, depth) in metres at scale 1.")]
        public Vector2 footprint = new Vector2(1.0f, 0.7f);
        [Tooltip("Off: the office always keeps scale 1. On: the floor search may also try floorSettings.scaleOptions.")]
        public bool allowScaleChange;
        public float nudgeStep = 0.10f;
        public float rotateStep = 15f;

        [Header("Near-user pose (where a remote paper comes to rest)")]
        public float nearAhead = 0.45f;
        public float nearBelowEye = 0.25f;

        [Header("Saved placement")]
        public bool restoreOnStart = true;
        [Tooltip("Give up restoring when the room scan has not arrived after this many seconds.")]
        public float restoreTimeoutSeconds = 15f;

        static readonly float[] UnitScale = { 1f };
        const string ParkedStatus = "Office is at your desk. Send back returns it to the floor.";

        SpatialDirectorCore _core;
        float[] _authoredScales;
        bool _hooks;
        bool _mountHook;
        bool _appliedFloor, _appliedCeiling;
        bool _standingDown;
        bool _savedAutoReplace;
        bool _restoreDone;
        float _restoreDeadline;
        int _recenterFrames;
        PlacementMode _publishedMode;
        bool _wasTransitioning;
        SavedPlacement _parked;          // the floor spot the office was brought back from
        bool _parkedValid;

        /// <summary>The scene-free logic (null until Awake, or when the wiring is incomplete).</summary>
        public SpatialDirectorCore Core => _core;
        public WorkspaceMode Mode => _core != null ? (WorkspaceMode)(int)_core.Mode : WorkspaceMode.Desk;
        public bool IsPreviewing => _core != null && _core.Phase == DirectorPhase.Previewing;
        /// <summary>What is being offered or why something was refused, in words; empty when there is nothing to say.</summary>
        public string StatusText => _core != null ? _core.StatusText : "";

        void Awake()
        {
            if (transition == null || pod == null)
            {
                Debug.LogWarning("[MuseDesk] SpatialWorkspaceDirector is missing its transition or pod controller; spatial modes are unavailable.");
                return;
            }
            if (transition.workspaceRoot == null) transition.workspaceRoot = workspaceRoot;
            if (pod.transition == null) pod.transition = transition;
            transition.tickedByDirector = true;
            pod.tickedByDirector = true;

            _authoredScales = floorSettings.scaleOptions;
            _core = new SpatialDirectorCore(flags, transition.Machine, pod.Machine, ReadUser, IsHeld, DeskReady);
            _core.floorSettings = floorSettings;
            _core.ceilingSettings = ceilingSettings;
            _core.footprint = footprint;
            _core.Changed += OnCoreChanged;
        }

        void OnEnable()
        {
            // Desk state is always published, flags or not: readers of the contract get a valid root.
            SpatialWorkspaceState.PublishRoot(workspaceRoot);
            SpatialWorkspaceState.Publish(Mode, _core != null && _core.IsRemote, _core != null && _core.IsTransitioning);
            // Requests raised by the reach side (a near-user key, a hand ray, a voice phrase).
            SummonRequests.BringOfficeHere += OnBringOfficeHere;
            SummonRequests.SendOfficeBack += OnSendOfficeBack;
        }

        void Start()
        {
            ApplyFlags();
        }

        void OnDisable()
        {
            SummonRequests.BringOfficeHere -= OnBringOfficeHere;
            SummonRequests.SendOfficeBack -= OnSendOfficeBack;
            Unhook();
            ReleaseDeskPlacement();
        }

        void OnDestroy()
        {
            if (_core != null) _core.Changed -= OnCoreChanged;
        }

        // ───────────────────────── flags ─────────────────────────

        /// <summary>Switch the modes on or off at run time (a settings screen, a Play Mode driver).
        /// Switching the active mode off returns the office to Desk.</summary>
        public void SetFlags(bool floorEnabled, bool ceilingEnabled)
        {
            flags.floorModeEnabled = floorEnabled;
            flags.ceilingModeEnabled = ceilingEnabled;
            ApplyFlags();
        }

        void ApplyFlags()
        {
            if (_core == null) return;
            _appliedFloor = flags.floorModeEnabled;
            _appliedCeiling = flags.ceilingModeEnabled;
            floorSettings.scaleOptions = allowScaleChange && _authoredScales != null && _authoredScales.Length > 0 ? _authoredScales : UnitScale;
            if (roomService != null)
            {
                roomService.SetActive(flags.Any);
                _core.Room = flags.Any ? roomService.Room : null;
            }
            if (flags.Any && !_restoreDone && _restoreDeadline <= 0f) _restoreDeadline = Time.unscaledTime + restoreTimeoutSeconds;

            // A mode whose flag was switched off is left at once (Desk is always allowed).
            if (_core.Phase == DirectorPhase.Previewing
                && ((_core.PendingMode == PlacementMode.Floor && !flags.floorModeEnabled)
                    || (_core.PendingMode == PlacementMode.Ceiling && !flags.ceilingModeEnabled)))
                _core.Cancel();
            if ((_core.Mode == PlacementMode.Floor && !flags.floorModeEnabled)
                || (_core.Mode == PlacementMode.Ceiling && !flags.ceilingModeEnabled))
                _core.RequestMode(PlacementMode.Desk);
            RefreshViews();
        }

        // ───────────────────────── public API (keys call the void methods) ─────────────────────────

        /// <summary>Ask for a mode. Floor and Ceiling open a preview; nothing moves until Confirm.</summary>
        public bool RequestMode(WorkspaceMode mode)
        {
            if (_core == null) return false;
            if (flags.Any) SyncRoom();
            return _core.RequestMode((PlacementMode)(int)mode);
        }

        public void RequestFloor()
        {
            // A second press while the floor preview is already open must not restart it.
            if (_core != null && _core.Phase == DirectorPhase.Previewing && _core.PendingMode == PlacementMode.Floor) return;
            // After "Bring it here" from Floor the office is parked at the desk and the Floor key
            // reads "Send back": it returns the office to the spot it came from (or opens the floor
            // preview if that spot no longer passes the room checks).
            if (_core != null && _parkedValid && _core.Mode == PlacementMode.Desk && _core.Phase == DirectorPhase.Idle)
            {
                SendOfficeBack();
                return;
            }
            RequestMode(WorkspaceMode.Floor);
        }

        /// <summary>True while a Floor office that was brought to the desk can be sent back to its floor spot.</summary>
        public bool HasParkedFloorSpot => _parkedValid;

        public void RequestCeiling()
        {
            if (_core != null && _core.Phase == DirectorPhase.Previewing && _core.PendingMode == PlacementMode.Ceiling) return;
            RequestMode(WorkspaceMode.Ceiling);
        }

        public void RequestDesk() { RequestMode(WorkspaceMode.Desk); }

        public bool Confirm() { return _core != null && _core.Confirm(); }
        public void ConfirmPlacement() { Confirm(); }
        public void Cancel() { if (_core != null) _core.Cancel(); }
        public void CancelPlacement() { Cancel(); }
        public void NextCandidate() { if (_core != null) _core.NextCandidate(); }
        public void ResetPlacement() { if (_core != null) _core.ResetPlacement(); }

        /// <summary>Move the previewed spot (metres, relative to the user's view). Up applies to manual placement only.</summary>
        public void Nudge(float right, float forward, float up) { if (_core != null) _core.Nudge(right, forward, up); }
        /// <summary>Turn the previewed spot about the vertical axis (degrees).</summary>
        public void Rotate(float degrees) { if (_core != null) _core.Rotate(degrees); }

        public void NudgeRight() { Nudge(nudgeStep, 0f, 0f); }
        public void NudgeLeft() { Nudge(-nudgeStep, 0f, 0f); }
        public void NudgeAway() { Nudge(0f, nudgeStep, 0f); }
        public void NudgeCloser() { Nudge(0f, -nudgeStep, 0f); }
        public void NudgeUp() { Nudge(0f, 0f, nudgeStep * 0.5f); }
        public void NudgeDown() { Nudge(0f, 0f, -nudgeStep * 0.5f); }
        public void RotateLeft() { Rotate(-rotateStep); }
        public void RotateRight() { Rotate(rotateStep); }

        /// <summary>Ceiling mode: bring the office down beside the user.</summary>
        public void BringPodDown() { if (_core != null) _core.RequestPodDescend(); }
        /// <summary>Ceiling mode: send the office back up (waits while the user holds a paper).</summary>
        public void SendPodUp() { if (_core != null) _core.RequestPodRetract(); }
        /// <summary>Ceiling mode: down when docked, up when engaged.</summary>
        public void TogglePod()
        {
            if (_core == null || _core.Mode != PlacementMode.Ceiling) return;
            if (_core.Pod.State == CeilingPodState.Docked) _core.RequestPodDescend();
            else if (_core.Pod.State == CeilingPodState.Engaged) _core.RequestPodRetract();
        }

        /// <summary>
        /// "Bring the office within reach" (SummonRequests.BringOfficeHere). Ceiling: lower the pod.
        /// Floor: return to the desk pose, remembering the floor spot so it can be sent back.
        /// A request, not a command: it is refused while a move runs or a paper is held.
        /// </summary>
        public void BringOfficeHere()
        {
            if (_core == null || !flags.Any) return;
            if (_core.Mode == PlacementMode.Ceiling) { _core.RequestPodDescend(); return; }
            if (_core.Mode != PlacementMode.Floor) return;
            SavedPlacement parked = _core.BuildSave(Application.buildGUID);   // null for a hand-placed spot
            if (_core.RequestMode(PlacementMode.Desk))
            {
                _parked = parked; _parkedValid = true;
                RefreshViews();          // the Floor key now reads "Send back"
            }
        }

        /// <summary>
        /// "Send the office back" (SummonRequests.SendOfficeBack). Ceiling: raise the pod (waits while a
        /// paper is held). After a Floor office was brought to the desk: back to the same floor spot if
        /// it still passes the room checks, otherwise the floor preview opens so the user chooses again.
        /// </summary>
        public void SendOfficeBack()
        {
            if (_core == null || !flags.Any) return;
            if (_core.Mode == PlacementMode.Ceiling) { _core.RequestPodRetract(); return; }
            if (_core.Mode != PlacementMode.Desk || !_parkedValid) return;
            string reason;
            if (_parked != null && _core.TryRestore(_parked, out reason)) { _parkedValid = false; _parked = null; RefreshViews(); return; }
            if (_core.IsTransitioning || SpatialWorkspaceState.TransitionHeld) return;   // try again later; the spot is kept
            _parkedValid = false; _parked = null;
            RequestFloor();
            RefreshViews();              // the key reads "Floor" again even if the preview was refused
        }

        void OnBringOfficeHere() { BringOfficeHere(); }
        void OnSendOfficeBack() { SendOfficeBack(); }

        /// <summary>Editor / Play Mode: use a hand-built room instead of the headset's scan (null = back to the scan).</summary>
        public void UseSyntheticRoom(SyntheticRoomGeometry room)
        {
            if (roomService == null || _core == null) return;
            roomService.UseSyntheticRoom(room);
            SyncRoom();
        }

        /// <summary>Editor / Play Mode: a 4 x 4 m synthetic room around the current head pose.</summary>
        public void UseSyntheticTestRoom()
        {
            if (roomService == null || _core == null) return;
            SeatedUser user = ReadUser();
            roomService.UseSyntheticTestRoom(user.head, user.forward);
            SyncRoom();
        }

        void SyncRoom()
        {
            if (roomService != null && _core != null) _core.Room = flags.Any ? roomService.Room : null;
        }

        // ───────────────────────── inputs to the core ─────────────────────────

        SeatedUser ReadUser()
        {
            EnsureHead();
            if (head == null) return new SeatedUser(new Vector3(0f, 1.2f, 0f), Vector3.forward);
            return new SeatedUser(head.position, head.forward);
        }

        void EnsureHead()
        {
            if (head != null) return;
            Camera cam = Camera.main;                 // only on a request, never per frame
            if (cam != null) head = cam.transform;
        }

        static bool IsHeld() { return SpatialWorkspaceState.TransitionHeld; }

        /// <summary>The desk placement must have finished before the office may leave Desk mode.</summary>
        bool DeskReady()
        {
            if (deskPlacement == null) return true;
            WorkspacePlacement.PlacementState s = deskPlacement.State;
            return s == WorkspacePlacement.PlacementState.PlacedManual || s == WorkspacePlacement.PlacementState.PlacedFromTable;
        }

        // ───────────────────────── frame loop ─────────────────────────

        void Update()
        {
            if (_core == null) return;
            // A flag ticked in the Inspector at run time is picked up here (two bool compares).
            if (flags.floorModeEnabled != _appliedFloor || flags.ceilingModeEnabled != _appliedCeiling) ApplyFlags();
            // Flags off and at rest in Desk: nothing to do, nothing done.
            if (!flags.Any && _core.Mode == PlacementMode.Desk && _core.Phase == DirectorPhase.Idle && !_core.IsTransitioning) return;

            if (!_hooks) Hook();

            if (_recenterFrames > 0 && --_recenterFrames == 0)
            {
                // Two frames after the recenter, so the room anchors have their new poses.
                _core.OnRecenter();
                PublishNearUserPose();
            }

            _core.Tick();

            if (!_restoreDone && restoreOnStart && flags.Any) TryRestore();
        }

        void TryRestore()
        {
            if (persistence == null) { _restoreDone = true; return; }
            bool ready = DeskReady() && _core.Room != null && _core.Room.Availability == RoomAvailability.Available;
            if (!ready)
            {
                if (Time.unscaledTime > _restoreDeadline) _restoreDone = true;   // stay in Desk
                return;
            }
            _restoreDone = true;
            SavedPlacement saved; string reason;
            if (!persistence.TryLoad(out saved, out reason)) return;            // nothing saved, or refused: Desk
            if (_core.TryRestore(saved, out reason))
            {
                Debug.Log("[MuseDesk] Restored the saved workspace placement (" + (PlacementMode)saved.mode + ").");
                PublishNearUserPose();
            }
            else
            {
                Debug.Log("[MuseDesk] Saved workspace placement not used: " + reason + ". Staying in Desk mode.");
                persistence.Clear();
            }
        }

        // ───────────────────────── reacting to the core ─────────────────────────

        void OnCoreChanged()
        {
            PlacementMode mode = _core.Mode;
            bool transitioning = _core.IsTransitioning;

            // Desk placement stands down while the office is away; it gets the root back in Desk.
            if (mode != PlacementMode.Desk) StandDownDeskPlacement();
            else if (!transitioning) ReleaseDeskPlacement();

            if (mode != _publishedMode)
            {
                _publishedMode = mode;
                if (mode != PlacementMode.Desk) { _parkedValid = false; _parked = null; }   // a new placement replaces a parked one
                PublishNearUserPose();              // a mode was just confirmed
                if (mode == PlacementMode.Ceiling && pod != null && _core.LastCeiling.valid)
                    pod.SetCeilingHeight(_core.LastCeiling.ceilingHeight);
                Save();
            }
            else if (_wasTransitioning && !transitioning) Save();   // arrived
            _wasTransitioning = transitioning;

            if (_core.PlacementCleared)
            {
                _core.PlacementCleared = false;
                if (persistence != null) persistence.Clear();
            }

            SpatialWorkspaceState.Publish((WorkspaceMode)(int)mode, _core.IsRemote, transitioning);
            RefreshViews();
        }

        void RefreshViews()
        {
            if (_core == null) return;
            if (ghost != null)
            {
                if (_core.HasPreview)
                    ghost.Show(_core.PreviewPose, !_core.PreviewIsDetected || _core.PreviewValid,
                               _core.PendingMode == PlacementMode.Ceiling, _core.PreviewIsDetected);
                else ghost.Hide();
            }
            if (strip != null)
            {
                bool previewing = _core.Phase == DirectorPhase.Previewing;
                bool canCycle = previewing && (_core.PendingMode == PlacementMode.Ceiling || _core.CandidateCount > 1);
                bool transitioning = _core.IsTransitioning;
                // Beside the Focus key while the office is at the desk or down beside the user;
                // within reach at the near-user pose while it is away or travelling.
                bool followRoot = (_core.Mode == PlacementMode.Desk && !transitioning)
                                  || (_core.Mode == PlacementMode.Ceiling && _core.Pod.State == CeilingPodState.Engaged);
                strip.SetFollowRoot(followRoot);
                string status = _core.StatusText;
                if (string.IsNullOrEmpty(status) && _core.Pod.Active) status = _core.Pod.StatusText;
                // A Floor office parked at the desk: the Floor key is the way back, and says so.
                bool parked = _parkedValid && _core.Mode == PlacementMode.Desk && !previewing;
                strip.SetFloorKeySendsBack(parked);
                if (parked && !transitioning && string.IsNullOrEmpty(status)) status = ParkedStatus;
                strip.Apply(flags.floorModeEnabled, flags.ceilingModeEnabled, _core.Mode, previewing, canCycle,
                            transitioning, _core.Pod.State, _core.Pod.RetractPending, status);
            }
        }

        void StandDownDeskPlacement()
        {
            if (_standingDown || deskPlacement == null) return;
            // The least invasive hook: WorkspacePlacement writes the root after start-up only from
            // its recenter / re-mount handler, and only when this flag is set (or before the first
            // placement, which DeskReady rules out). No edit to its file is needed.
            _standingDown = true;
            _savedAutoReplace = deskPlacement.autoReplaceOnRecenter;
            deskPlacement.autoReplaceOnRecenter = false;
        }

        void ReleaseDeskPlacement()
        {
            if (!_standingDown || deskPlacement == null) return;
            deskPlacement.autoReplaceOnRecenter = _savedAutoReplace;
            _standingDown = false;
        }

        void Save()
        {
            if (persistence == null || _core == null || !flags.Any) return;
            if (_core.Mode == PlacementMode.Desk) { persistence.Save(null); return; }
            SavedPlacement saved = _core.BuildSave(Application.buildGUID);
            // A hand-placed spot has no room reference: it is not saved, and an older record is dropped.
            persistence.Save(saved);
        }

        /// <summary>About 0.45 m ahead of and 0.25 m below the eyes, +Z toward the user (the project's
        /// facing convention). Recomputed when a mode is confirmed and on recenter, never per frame.</summary>
        void PublishNearUserPose()
        {
            EnsureHead();
            if (head == null) { SpatialWorkspaceState.PublishNearUserPose(Pose.identity, false); return; }
            SeatedUser user = new SeatedUser(head.position, head.forward);
            Vector3 p = user.head + user.forward * nearAhead + Vector3.down * nearBelowEye;
            SpatialWorkspaceState.PublishNearUserPose(new Pose(p, SpatialGeometry.FaceUserYaw(p, user.head)), true);
        }

        // ───────────────────────── recenter, pause ─────────────────────────

        void Hook()
        {
            if (_hooks) return;
            if (!_mountHook) { OVRManager.HMDMounted += OnHeadsetMounted; _mountHook = true; }
            if (OVRManager.display == null) return;         // not up yet (Editor, early frames): try again next frame
            OVRManager.display.RecenteredPose += OnRecentered;
            _hooks = true;
        }

        void Unhook()
        {
            if (_mountHook) { OVRManager.HMDMounted -= OnHeadsetMounted; _mountHook = false; }
            if (_hooks && OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
            _hooks = false;
        }

        void OnRecentered()
        {
            if (_core == null) return;
            _recenterFrames = 2;
        }

        void OnHeadsetMounted()
        {
            // The user may sit differently now: only the near-user pose is refreshed. The office
            // stays where it is in the room.
            if (_core == null || !flags.Any) return;
            PublishNearUserPose();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused || _core == null) return;
            if (!flags.Any && _core.Mode == PlacementMode.Desk) return;
            _core.OnPause();        // nothing is left mid-air
            Save();
        }
    }
}
