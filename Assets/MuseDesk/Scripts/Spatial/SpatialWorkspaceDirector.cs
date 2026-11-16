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
        [Tooltip("Phase 3 Wall Habitat host (implements IWallModeHost). Empty, or its flag off: no change to Desk/Floor/Ceiling.")]
        public MonoBehaviour wallHost;

        IWallModeHost Wall => wallHost as IWallModeHost;
        bool WallActive { get { var w = Wall; return w != null && w.WallEnabled && w.WallActive; } }

        [Header("Placement")]
        public FloorPlacementSettings floorSettings = new FloorPlacementSettings();
        public CeilingPlacementSettings ceilingSettings = new CeilingPlacementSettings();
        [Tooltip("Office footprint (width, depth) in metres at scale 1.")]
        public Vector2 footprint = new Vector2(1.0f, 0.7f);
        [Tooltip("Off: the office always keeps scale 1. On: the floor search may also try floorSettings.scaleOptions.")]
        public bool allowScaleChange;
        [Tooltip("Off (default): with no room data the way back to the desk cannot be checked and is refused, in words. " +
                 "On: it is then travelled with the user rules only. An escape hatch for a headset whose room scan comes and goes.")]
        public bool returnToDeskWithoutRoom;
        public float nudgeStep = 0.10f;
        public float rotateStep = 15f;
        [Tooltip("Pointing at the floor (FloorAimController): the spot is kept between these distances from the user, in metres.")]
        public float aimMinDistance = 0.8f;
        public float aimMaxDistance = 3.5f;
        [Tooltip("With no room scan, start hand placement on the tracked floor (floor-level tracking origin) instead of a guessed eye height. " +
                 "Used only when the head is a plausible seated or standing height above it.")]
        public bool useTrackedFloorWithoutRoom = true;

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
        public WorkspaceMode Mode => WallActive ? WorkspaceMode.Wall : (_core != null ? (WorkspaceMode)(int)_core.Mode : WorkspaceMode.Desk);
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
            _core.aimMinDistance = aimMinDistance;
            _core.aimMaxDistance = aimMaxDistance;
            // rc6: no spatial move starts while a hand holds anything through the Interaction SDK.
            _core.handsBusy = HandsHoldSomething;
            _core.Changed += OnCoreChanged;
            _core.PreviewMoved += OnPreviewMoved;
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
            BindWallDock();
            ApplyFlags();
        }

        /// <summary>
        /// UnifiedModeDockV1: when the mode console carries the Wall place key it is the ONE Spaces dock, and the wall host
        /// is told to build no floating dock of its own. Runs in Start (every Awake has run, nothing has been drawn yet).
        /// </summary>
        void BindWallDock()
        {
            IWallModeHost wall = Wall;
            ModeConsole console = strip != null ? strip.console : null;
            if (wall == null || console == null || !console.HasWallKey) return;
            if (console.wallHostBehaviour == null) console.wallHostBehaviour = wallHost;
            wall.ExternalDock = true;
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
            if (_core != null) { _core.Changed -= OnCoreChanged; _core.PreviewMoved -= OnPreviewMoved; }
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
            _core.returnToDeskWithoutRoom = returnToDeskWithoutRoom;
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
            // Phase 3 Wall Habitat: a request for Wall goes to its host (only from Desk, nothing moving);
            // any other request first leaves Wall (visits return, the desk office shows again).
            IWallModeHost wall = Wall;
            if (mode == WorkspaceMode.Wall)
            {
                if (wall == null || !wall.WallEnabled) return false;
                bool deskIdle = _core.Mode == PlacementMode.Desk && _core.Phase == DirectorPhase.Idle && !_core.IsTransitioning;
                return wall.RequestWall(deskIdle);
            }
            if (wall != null && (wall.WallActive || wall.WallPreviewing))
            {
                wall.ExitWall("mode " + mode);
                if (mode == WorkspaceMode.Desk) return true;
            }
            if (flags.Any) SyncRoom();
            if (mode == WorkspaceMode.Floor) UpdateTrackedFloor();
            return _core.RequestMode((PlacementMode)(int)mode);
        }

        /// <summary>
        /// Tell the core where the tracked floor is, for hand placement without a room scan. The
        /// tracking space's origin is on the floor only with a floor-level (or stage) tracking
        /// origin; the value is used only when the head is 0.5 to 2.2 m above it, so an Editor run
        /// with the camera at the origin falls back to the stated guess.
        /// </summary>
        void UpdateTrackedFloor()
        {
            _core.hasTrackedFloor = false;
            if (!useTrackedFloorWithoutRoom) return;
            EnsureHead();
            if (head == null || OVRManager.instance == null) return;
            OVRManager.TrackingOrigin origin = OVRManager.instance.trackingOriginType;
            if (origin != OVRManager.TrackingOrigin.FloorLevel && origin != OVRManager.TrackingOrigin.Stage) return;
            Transform space = head.parent;          // CenterEyeAnchor sits directly under TrackingSpace
            if (space == null) return;
            float above = head.position.y - space.position.y;
            if (above < 0.5f || above > 2.2f) return;
            _core.hasTrackedFloor = true;
            _core.trackedFloorHeight = space.position.y;
        }

        // ───────────────────────── pointing at the floor (preview only) ─────────────────────────

        /// <summary>True while the floor preview is open and can be pointed at (FloorAimController reads this).</summary>
        public bool CanAim => _core != null && _core.CanAim;
        /// <summary>The seated user as the placement logic sees them (head position, flattened forward).</summary>
        public SeatedUser CurrentUser => ReadUser();

        void OnPreviewMoved()
        {
            // A drag frame: only the ghost moves. Mode, keys, words and saved state are untouched.
            if (ghost != null && _core.HasPreview)
                ghost.Show(_core.ShownPose, _core.ShownValid, _core.PendingMode == PlacementMode.Ceiling, _core.ShownRoomChecked);
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

        /// <summary>Floor preview, the Place here key: move the office to the previewed spot. The route is
        /// planned and swept again now; refused, nothing moves and the footprint says why.</summary>
        public bool PlaceHere() { return _core != null && _core.PlaceHere(); }
        /// <summary>The "Choose another spot" key: the next detected spot, or how to choose one by pointing.</summary>
        public void ChooseAnotherSpot() { if (_core != null) _core.ChooseAnotherSpot(); }
        /// <summary>Previewing: leave the preview. The office is moving: stop the move (see <see cref="StopMove"/>).</summary>
        public void Cancel() { if (_core != null) _core.Cancel(); }
        /// <summary>The Cancel key's target. The same key reads "Stop" while the office moves, and this stops it.</summary>
        public void CancelPlacement() { Cancel(); }
        /// <summary>
        /// Stop whatever is moving the office: a mode change goes back along the path it travelled and
        /// the mode goes back with it; a descending pod goes back up to the dock; a pod going up is left
        /// to finish. While a paper is held the office only halts, and goes back when it is put down.
        /// Does nothing when nothing moves. Safe to call at any time.
        /// </summary>
        public void StopMove() { if (_core != null) _core.StopMove(); }
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

        /// <summary>A grab interactor of either hand has something selected (read-only probe of the rig).</summary>
        static bool HandsHoldSomething() { return HandInteractionProbe.AnyHeld; }

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
            if (WallActive) { _restoreDone = true; return; }          // the wall habitat was restored first: keep it
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
                Save();
            }
            else if (_wasTransitioning && !transitioning) Save();   // arrived
            _wasTransitioning = transitioning;
            // The dock can change without the mode changing (a new ceiling spot, a recenter).
            if (mode == PlacementMode.Ceiling && pod != null && _core.LastCeiling.valid)
                pod.SetCeilingHeight(_core.LastCeiling.ceilingHeight);

            if (_core.PlacementCleared)
            {
                _core.PlacementCleared = false;
                if (persistence != null) persistence.Clear();
            }

            if (WallActive) SpatialWorkspaceState.Publish(WorkspaceMode.Wall, true, transitioning);
            else SpatialWorkspaceState.Publish((WorkspaceMode)(int)mode, _core.IsRemote, transitioning);
            RefreshViews();
        }

        /// <summary>Phase 3: the wall host entered or left Wall. The director publishes it (it stays the only
        /// writer of SpatialWorkspaceState). Wall is remote: papers are brought to the user as in Floor mode.</summary>
        public void PublishWallChanged()
        {
            if (_core == null) return;
            if (WallActive) { StandDownDeskPlacement(); SpatialWorkspaceState.Publish(WorkspaceMode.Wall, true, _core.IsTransitioning); }
            else
            {
                if (_core.Mode == PlacementMode.Desk && !_core.IsTransitioning) ReleaseDeskPlacement();
                SpatialWorkspaceState.Publish((WorkspaceMode)(int)_core.Mode, _core.IsRemote, _core.IsTransitioning);
            }
            PublishNearUserPose();
            RefreshViews();
        }

        /// <summary>True once the saved Desk/Floor/Ceiling placement was restored or given up (the wall host waits for it).</summary>
        public bool RestoreSettled => _restoreDone || !flags.Any || !restoreOnStart;

        void RefreshViews()
        {
            if (_core == null) return;
            if (ghost != null)
            {
                // During a drag by pointing the ghost shows the spot under the hand (teal or coral);
                // otherwise ShownPose / ShownValid are the preview exactly as before.
                if (_core.HasPreview)
                    ghost.Show(_core.ShownPose, _core.ShownValid,
                               _core.PendingMode == PlacementMode.Ceiling, _core.ShownRoomChecked);
                else ghost.Hide();
            }
            if (strip != null)
            {
                bool previewing = _core.Phase == DirectorPhase.Previewing;
                bool canCycle = _core.CanCycle;     // also true after the footprint was dragged off its only candidate
                bool transitioning = _core.IsTransitioning;
                // Beside the Focus key while the office is at the desk or down beside the user;
                // within reach at the near-user pose while it is away or travelling.
                // Never on the root while it moves: the Stop key has to stay still and within reach.
                bool followRoot = !transitioning
                                  && (_core.Mode == PlacementMode.Desk
                                      || (_core.Mode == PlacementMode.Ceiling && _core.Pod.State == CeilingPodState.Engaged));
                strip.SetFollowRoot(followRoot);
                string status = _core.StatusText;
                if (string.IsNullOrEmpty(status) && _core.Pod.Active) status = _core.Pod.StatusText;
                // A Floor office parked at the desk: the Floor key is the way back, and says so.
                bool parked = _parkedValid && _core.Mode == PlacementMode.Desk && !previewing;
                strip.SetFloorKeySendsBack(parked);
                // Floor preview: the console's Confirm reads Place here and shows only while it applies.
                if (strip.console != null)
                    strip.console.SetFloorPlacement(previewing && _core.PendingMode == PlacementMode.Floor, _core.CanPlaceHere);
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
            // Persisted only after success: a move in flight may still be stopped and go back. The record
            // is written when it arrives (OnCoreChanged sees the transition end).
            if (_core.IsTransitioning) return;
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
