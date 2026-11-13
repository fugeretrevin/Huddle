using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MuseDesk.Spatial
{
    public enum DirectorPhase { Idle = 0, Previewing = 1 }

    /// <summary>What the floor footprint shows (deliberate placement, rc6).</summary>
    public enum FloorPlaceStage
    {
        /// <summary>No floor placement on show.</summary>
        None = 0,
        /// <summary>The office may not stand there (wall, furniture, too close to the user, off the scan).</summary>
        SpotBlocked = 1,
        /// <summary>The office may stand there, but no clear way there was found.</summary>
        RouteBlocked = 2,
        /// <summary>Dragging over a spot whose floor AND way are clear: let go to choose it.</summary>
        RouteValid = 3,
        /// <summary>At rest on a clear spot: waiting for Place here (nothing moves without it).</summary>
        NeedsConfirmation = 4,
        /// <summary>Place here was accepted: the office is travelling the swept route.</summary>
        Moving = 5,
        /// <summary>Stop was pressed (or the way became unsafe): the office is going back.</summary>
        Returning = 6,
        /// <summary>The office arrived at the spot.</summary>
        Placed = 7,
    }

    /// <summary>
    /// The decision logic of the room-aware workspace, free of scene access so it can be tested:
    /// modes Desk / Floor / Ceiling, the preview-then-confirm flow, flag gating, the hand-over to the
    /// transition machine and the ceiling pod, recenter and pause handling, and what is saved.
    ///
    /// Rules it enforces:
    ///  - with a mode's flag off, a request for that mode does nothing at all;
    ///  - the office moves only on Confirm, never on a request or a preview;
    ///  - EVERY move is along a route whose swept volume was just shown clear of recognised
    ///    furniture, the user and the walls (CeilingPlacementEvaluator.PlanRoute / SweepRoute /
    ///    ValidateDock; moves between the desk and floor spots also try the side-step shapes of
    ///    FloorRoutePlanner, swept the same way). When no route is clear the move is refused with
    ///    words that say what to do next. Nothing is teleported and no margin is relaxed;
    ///  - a running move can be stopped at any moment (<see cref="StopMove"/>): it goes back along
    ///    the poses it already travelled, and the mode goes back with it;
    ///  - after a recenter the office is only ever put at a pose that was validated against the
    ///    room as it is now; otherwise it stays where it is and the placement must be chosen again;
    ///  - a position is called "detected floor" only when it came from room data and still passes
    ///    the checks; without room data the user is offered manual placement, in words;
    ///  - Ceiling mode exists only on real ceiling geometry;
    ///  - returning to Desk puts the root back at exactly the pose it had when Desk was left;
    ///  - nothing here knows about tasks, gateways or reducers: a mode change cannot start, cancel
    ///    or reset work.
    /// </summary>
    public sealed class SpatialDirectorCore
    {
        /// <summary>Everything needed to put the bookkeeping back when a move is called off.</summary>
        struct Resting
        {
            public PlacementMode mode;
            public bool hasLocal, handPlaced, podEngaged;
            public Vector3 localPos, dockLocalPos, inspectLocalPos;
            public Quaternion localRot, dockLocalRot, inspectLocalRot;
            public float localScale;
            public RootPose dock, inspection;
            public CeilingPlacement ceiling;
        }

        readonly SpatialFeatureFlags _flags;
        readonly WorkspaceTransitionMachine _mover;
        readonly CeilingPodMachine _pod;
        readonly Func<SeatedUser> _user;
        readonly Func<bool> _isHeld;
        readonly Func<bool> _deskReady;

        readonly List<FloorCandidate> _candidates = new List<FloorCandidate>(8);
        readonly FloorEvaluationReport _report = new FloorEvaluationReport();

        IRoomGeometry _room;
        bool _roomDirty = true;

        RootPose _deskPose;
        bool _hasDeskPose;
        bool _hasDeskLocal;
        Pose _deskReference;
        Vector3 _deskLocalPos;
        Quaternion _deskLocalRot = Quaternion.identity;
        Resting _before;
        bool _awaitingMove;
        bool _needsConfirm;
        bool _handPlaced;

        int _index;
        RootPose _previewOrigin;
        string _previewProblem;
        CeilingPlacement _ceiling;          // the confirmed dock (Ceiling mode)
        CeilingPlacement _previewCeiling;   // the dock being previewed; becomes _ceiling on Confirm
        int _ceilingSide = 1;

        // The confirmed placement relative to the room reference (recenter, save).
        bool _hasLocal;
        Vector3 _localPos, _dockLocalPos, _inspectLocalPos;
        Quaternion _localRot = Quaternion.identity, _dockLocalRot = Quaternion.identity, _inspectLocalRot = Quaternion.identity;
        float _localScale = 1f;

        PlacementMode _lastMode;
        bool _lastRemote, _lastTransitioning;

        public FloorPlacementSettings floorSettings = new FloorPlacementSettings();
        /// <summary>Ceiling search numbers, and the swept-path numbers every move uses (Floor moves too).</summary>
        public CeilingPlacementSettings ceilingSettings = new CeilingPlacementSettings();
        /// <summary>Office footprint (width, depth) in metres at scale 1.</summary>
        public Vector2 footprint = new Vector2(1.0f, 0.7f);
        public float moveMetresPerSecond = 0.9f;
        public float minMoveSeconds = 0.9f;
        public float maxMoveSeconds = 2.5f;
        [Tooltip("Manual placement (no room data): start this far to the user's side.")]
        public float manualSideDistance = 1.2f;
        [Tooltip("Manual placement only: assumed eye height above the floor. A guess, and labelled as one.")]
        public float manualAssumedEyeHeight = 1.15f;
        [Tooltip("Off (default): with no room data the way back to the desk cannot be checked and is refused. " +
                 "On: it is then travelled with the user rules only (an escape hatch, said in words).")]
        public bool returnToDeskWithoutRoom;
        [Tooltip("Pointing at the floor: the spot is kept at least this far (horizontally) from the user.")]
        public float aimMinDistance = 0.8f;
        [Tooltip("Pointing at the floor: the spot is kept at most this far (horizontally) from the user.")]
        public float aimMaxDistance = 3.5f;
        /// <summary>Set by the host when the headset reports a floor-level tracking origin: manual
        /// placement (no room scan) then starts on that floor instead of a guessed eye height.</summary>
        public bool hasTrackedFloor;
        public float trackedFloorHeight;

        public PlacementMode Mode { get; private set; }
        public DirectorPhase Phase { get; private set; }
        /// <summary>The mode being previewed (valid while <see cref="Phase"/> is Previewing).</summary>
        public PlacementMode PendingMode { get; private set; }
        public bool HasPreview => Phase == DirectorPhase.Previewing;
        public RootPose PreviewPose { get; private set; }
        /// <summary>True when the previewed pose comes from room data; false for manual placement.</summary>
        public bool PreviewIsDetected { get; private set; }
        /// <summary>False when a detected pose was nudged somewhere that fails the room checks, or
        /// somewhere the office cannot be taken to along a clear route.</summary>
        public bool PreviewValid { get; private set; }
        public int CandidateCount => _candidates.Count;
        public int CandidateIndex => _index;
        public string StatusText { get; private set; }
        /// <summary>Set when the user reset the placement: the host deletes the saved record.</summary>
        public bool PlacementCleared { get; set; }
        /// <summary>
        /// True when the office is somewhere that could not be validated (a recenter the room data did
        /// not confirm, or a halt off its path). It stays where it is; the pod will not move it;
        /// the way out is Desk (itself a swept route) and placing it again.
        /// </summary>
        public bool PlacementNeedsConfirmation => _needsConfirm || _pod.Stranded;
        /// <summary>The route of the last move that was planned (accepted or refused), and its check.</summary>
        public WorkspaceRoute LastRoute { get; private set; }
        public CeilingPathCheck LastRouteCheck { get; private set; }

        public bool IsRemote
        {
            get
            {
                if (Mode == PlacementMode.Floor) return true;
                if (Mode == PlacementMode.Ceiling) return _pod.IsRemote;
                return false;
            }
        }

        public bool IsTransitioning
        {
            get
            {
                if (_mover.IsRunning) return true;
                return _pod.Active && _pod.State != CeilingPodState.Docked && _pod.State != CeilingPodState.Engaged;
            }
        }

        public WorkspaceTransitionMachine Mover => _mover;
        public CeilingPodMachine Pod => _pod;
        public FloorEvaluationReport LastFloorReport => _report;
        /// <summary>The ceiling placement on show: the previewed one during a ceiling preview, otherwise the confirmed one.</summary>
        public CeilingPlacement LastCeiling => Phase == DirectorPhase.Previewing && PendingMode == PlacementMode.Ceiling ? _previewCeiling : _ceiling;
        public RootPose DeskPose => _deskPose;
        public bool HasDeskPose => _hasDeskPose;

        /// <summary>Raised when mode, phase, preview, status or the remote / transitioning flags change.</summary>
        public event Action Changed;

        public SpatialDirectorCore(SpatialFeatureFlags flags, WorkspaceTransitionMachine mover, CeilingPodMachine pod,
                                   Func<SeatedUser> user, Func<bool> isHeld, Func<bool> deskReady)
        {
            _flags = flags ?? new SpatialFeatureFlags();
            _mover = mover;
            _pod = pod;
            _user = user;
            _isHeld = isHeld;
            _deskReady = deskReady;
            StatusText = "";
            _mover.MoveFinished += OnMoveFinished;
        }

        public IRoomGeometry Room
        {
            get { return _room; }
            set
            {
                if (ReferenceEquals(_room, value)) return;
                if (_room != null) _room.Changed -= OnRoomChanged;
                _room = value;
                if (_room != null) _room.Changed += OnRoomChanged;
                OnRoomChanged();
            }
        }

        public FloorCandidate GetCandidate(int i) { return _candidates[i]; }

        bool Held => _isHeld != null && _isHeld();

        /// <summary>
        /// Optional (rc6): true while a hand holds anything through the Interaction SDK (a stamp, a card, a
        /// panel grab bar; a paper also sets the transition hold). Spatial movement never STARTS while it
        /// is true: Confirm / Place here, Desk, Lower and Raise are refused with words. Stop is never
        /// blocked. A paper keeps its own transition-hold behaviour (checked first), so this adds a
        /// refusal only where nothing else applied. Null (tests, no rig): ignored.
        /// </summary>
        public Func<bool> handsBusy;

        const string HandsBusyWords = "Put down what you are holding first, then the office can move";

        bool GrabBusy => !Held && handsBusy != null && handsBusy();

        bool Enabled(PlacementMode m)
        {
            if (m == PlacementMode.Floor) return _flags.floorModeEnabled;
            if (m == PlacementMode.Ceiling) return _flags.ceilingModeEnabled;
            return true;
        }

        // ───────────────────────── routes ─────────────────────────

        /// <summary>
        /// Plan the route of a mode change and sweep it. <paramref name="fromDesk"/> / <paramref name="toDesk"/>
        /// mark the end that is the desk pose (see CeilingPlacementSettings.deskStartRegion);
        /// <paramref name="handPlaced"/> marks a far end the scan does not vouch for.
        /// </summary>
        bool PlanMove(SeatedUser user, RootPose from, RootPose to, bool fromDesk, bool toDesk, bool allowStraight,
                      bool handPlaced, bool mast, Vector3 mastTop, out WorkspaceRoute route, out CeilingPathCheck check)
        {
            PathSweepOptions o = default(PathSweepOptions);
            o.fromDesk = fromDesk;
            o.toDesk = toDesk;
            o.handPlaced = handPlaced;
            o.mast = mast;
            o.mastTop = mastTop;
            // To or from the dock: the usual shapes first, then the dock side step (worn pass 1), so a dock
            // beside or behind the shoulder can be reached without turning the office over the head.
            bool ok = mast
                ? FloorRoutePlanner.Plan(_room, user, from, to, footprint, ceilingSettings, o, allowStraight, out route, out check)
                : CeilingPlacementEvaluator.PlanRoute(_room, user, from, to, footprint, ceilingSettings, o, allowStraight, out route, out check);
            LastRoute = route;
            LastRouteCheck = check;
            o.absoluteHigh = true;              // as PlanRoute and FloorRoutePlanner sweep every shape
            _lastPlanOptions = o;
            return ok;
        }

        /// <summary>The route from where the office is now to a floor spot (straight if clear, else up
        /// and over, else one of the side-step shapes of <see cref="FloorRoutePlanner"/>).</summary>
        bool PlanToFloor(SeatedUser user, RootPose target, bool handPlaced, out WorkspaceRoute route, out CeilingPathCheck check)
        {
            bool leavingCeiling = Mode == PlacementMode.Ceiling && _ceiling.valid;
            if (leavingCeiling)
                return PlanMove(user, _mover.CurrentPose, target, false, false, true, handPlaced, true, _ceiling.mastTop, out route, out check);
            return PlanLowMove(user, _mover.CurrentPose, target, Mode == PlacementMode.Desk, false, handPlaced, out route, out check);
        }

        /// <summary>
        /// A move between two low poses (desk and floor spots), planned by <see cref="FloorRoutePlanner"/>:
        /// the usual shapes first, then the side steps, every one swept by the unchanged SweepRoute
        /// with these options. Used for every move to, between and back from floor spots, so the way
        /// back to the desk can always be the way out reversed.
        /// </summary>
        bool PlanLowMove(SeatedUser user, RootPose from, RootPose to, bool fromDesk, bool toDesk, bool handPlaced,
                         out WorkspaceRoute route, out CeilingPathCheck check)
        {
            PathSweepOptions o = default(PathSweepOptions);
            o.fromDesk = fromDesk;
            o.toDesk = toDesk;
            o.handPlaced = handPlaced;
            bool ok = FloorRoutePlanner.Plan(_room, user, from, to, footprint, ceilingSettings, o, true, out route, out check);
            LastRoute = route;
            LastRouteCheck = check;
            o.absoluteHigh = true;
            _lastPlanOptions = o;
            return ok;
        }

        // The route the mover is travelling now and the options it was swept with, so a room change in
        // the middle of the move can sweep it again (see OnRoomChanged).
        PathSweepOptions _lastPlanOptions, _activeOptions;
        WorkspaceRoute _activeRoute;
        bool _activeRouteValid;

        void RememberActiveRoute(WorkspaceRoute route, bool started)
        {
            _activeRoute = route;
            _activeOptions = _lastPlanOptions;
            _activeRouteValid = started;
        }

        /// <summary>The words when a room change made the route of a running move unsafe and it went back.</summary>
        public const string RoomChangedMidMoveWords = "The room scan changed and the way is no longer clear: going back";

        float MoveSeconds(WorkspaceRoute route)
        {
            return Mathf.Clamp(route.Length / Mathf.Max(0.05f, moveMetresPerSecond), minMoveSeconds, maxMoveSeconds);
        }

        void Snapshot()
        {
            _before.mode = Mode;
            _before.hasLocal = _hasLocal;
            _before.handPlaced = _handPlaced;
            _before.localPos = _localPos; _before.localRot = _localRot; _before.localScale = _localScale;
            _before.dockLocalPos = _dockLocalPos; _before.dockLocalRot = _dockLocalRot;
            _before.inspectLocalPos = _inspectLocalPos; _before.inspectLocalRot = _inspectLocalRot;
            _before.podEngaged = _pod.Active && _pod.State == CeilingPodState.Engaged;
            _before.dock = _pod.DockPose;
            _before.inspection = _pod.InspectionPose;
            _before.ceiling = _ceiling;
        }

        /// <summary>A move was called off and the office is back where it rested: put the mode and
        /// the bookkeeping back too.</summary>
        void Revert(string status)
        {
            _awaitingMove = false;
            Mode = _before.mode;
            _hasLocal = _before.hasLocal;
            _handPlaced = _before.handPlaced;
            _localPos = _before.localPos; _localRot = _before.localRot; _localScale = _before.localScale;
            _dockLocalPos = _before.dockLocalPos; _dockLocalRot = _before.dockLocalRot;
            _inspectLocalPos = _before.inspectLocalPos; _inspectLocalRot = _before.inspectLocalRot;
            _ceiling = _before.ceiling;
            if (Mode == PlacementMode.Ceiling) _pod.Restore(_before.dock, _before.inspection, _before.podEngaged);
            else if (_pod.Active) _pod.Deactivate();
            StatusText = status ?? "";
            Touch();
        }

        void RememberDesk(RootPose current)
        {
            _deskPose = current;
            _hasDeskPose = true;
            _hasDeskLocal = false;
            Pose reference;
            if (_room == null || !_room.TryGetReferencePose(out reference)) return;
            _hasDeskLocal = true;
            _deskReference = reference;
            _deskLocalPos = WorkspacePlacementCodec.ToLocal(reference, current.position);
            _deskLocalRot = WorkspacePlacementCodec.ToLocal(reference, current.rotation);
        }

        // ───────────────────────── requests ─────────────────────────

        /// <summary>
        /// Ask for a mode. Floor and Ceiling open a preview (nothing moves until <see cref="Confirm"/>);
        /// Desk returns the office to its desk pose at once. Returns false, with the reason in
        /// <see cref="StatusText"/> where one helps the user, when the request is not acted on.
        /// </summary>
        public bool RequestMode(PlacementMode mode)
        {
            if (!Enabled(mode)) return false;                                   // flag off: do nothing at all
            if (mode == PlacementMode.Desk && Mode == PlacementMode.Desk)
            {
                if (Phase == DirectorPhase.Previewing) { Cancel(); return true; }
                return false;
            }
            if (_deskReady != null && !_deskReady()) return Refuse("The office is still being placed");
            if (IsTransitioning) return Refuse("The office is moving. Press Stop, or wait until it has stopped.");
            if (Phase == DirectorPhase.Previewing) EndPreview();

            if (mode == PlacementMode.Desk) return ReturnToDesk();
            if (mode == PlacementMode.Floor) return BeginFloorPreview();
            return BeginCeilingPreview();
        }

        bool ReturnToDesk()
        {
            if (!_hasDeskPose) return Refuse("No desk position is known");
            if (Held) return Refuse("Put the paper down first, then the office can move");
            if (GrabBusy) return Refuse(HandsBusyWords);

            RootPose current = _mover.CurrentPose;
            SeatedUser user = _user();
            bool fromCeiling = Mode == PlacementMode.Ceiling && _ceiling.valid;
            bool engaged = _pod.Active && _pod.State == CeilingPodState.Engaged;
            bool roomless = _room == null || !_room.HasRoom;
            // A straight line is tried from the floor and from beside the user; from the dock the
            // office comes across above head height and then straight down onto the desk pose.
            bool allowStraight = Mode == PlacementMode.Floor || engaged;
            bool unchecked_ = _handPlaced || (roomless && returnToDeskWithoutRoom);
            WorkspaceRoute route; CeilingPathCheck check;
            // From the floor: the same shapes as the way out (FloorRoutePlanner), so a spot the office
            // was taken to along a side step can be left along it again.
            bool planned = Mode == PlacementMode.Floor && !fromCeiling
                ? PlanLowMove(user, current, _deskPose, false, true, unchecked_, out route, out check)
                : PlanMove(user, current, _deskPose, false, true, allowStraight, unchecked_, fromCeiling, _ceiling.mastTop, out route, out check);
            if (!planned)
            {
                if (check.blockedBy == CeilingPathBlock.User)
                    return Refuse(check.Words("The way back to the desk") + ". Sit back, then press Desk again.");
                if (check.blockedBy == CeilingPathBlock.Unverifiable)
                    return Refuse(check.Words("The way back to the desk") + ". Press Desk again when the room scan is back.");
                return Refuse(check.Words("The way back to the desk") + ". Clear the way, then press Desk again.");
            }

            Snapshot();
            var r = _mover.TryMoveRoute(route, MoveSeconds(route));
            if (r != TransitionStartResult.Started && r != TransitionStartResult.AlreadyThere) return Refuse(_mover.LastRefusal);
            _pod.CancelPendingRetract();        // the pod must not start its own move under this one
            _awaitingMove = r == TransitionStartResult.Started;
            RememberActiveRoute(route, r == TransitionStartResult.Started);
            _placeOutcome = PlaceOutcome.None;
            Mode = PlacementMode.Desk;
            _hasLocal = false;
            _handPlaced = false;
            _needsConfirm = false;
            StatusText = roomless && !check.roomChecked ? "Returning to the desk (not checked against your room)" : "";
            if (r == TransitionStartResult.AlreadyThere && _pod.Active) _pod.Deactivate();
            Touch();
            return true;
        }

        bool BeginFloorPreview()
        {
            SeatedUser user = _user();
            _candidates.Clear();
            int unreachable = 0;
            if (_room != null)
            {
                FloorPlacementEvaluator.Evaluate(_room, user, footprint, floorSettings, _candidates, _report);
                // Only spots the office can be taken to along a clear route are offered.
                for (int i = _candidates.Count - 1; i >= 0; i--)
                {
                    WorkspaceRoute route; CeilingPathCheck check;
                    if (PlanToFloor(user, _candidates[i].Pose, false, out route, out check)) continue;
                    _candidates.RemoveAt(i);
                    unreachable++;
                }
            }
            else { _report.Clear(); _report.availability = RoomAvailability.NotLoadedYet; }
            _roomDirty = false;

            PendingMode = PlacementMode.Floor;
            Phase = DirectorPhase.Previewing;
            _index = 0;
            _previewProblem = null;
            _aimDragging = false;               // a drag on the old preview is over (the room changed under it)
            _aimMoved = false;
            _placeOutcome = PlaceOutcome.None;
            ClearAimWords();
            if (_candidates.Count > 0)
            {
                PreviewIsDetected = true;
                PreviewValid = true;
                _previewOrigin = _candidates[0].Pose;
                PreviewPose = _previewOrigin;
                StatusText = DetectedStatus();
            }
            else
            {
                // No detected spot. Offer a hand-placed position and say plainly that it is not
                // checked against the room: the height in particular is only a guess.
                PreviewIsDetected = false;
                PreviewValid = true;
                _previewOrigin = ManualPose(user);
                PreviewPose = _previewOrigin;
                if (_report.availability != RoomAvailability.Available)
                    StatusText = "Room scan not available: place it by hand. " + SpatialGeometry.ReasonText(_report.availability) + ".";
                else if (unreachable > 0)
                    StatusText = "No floor spot with a clear way to it: place it by hand. This spot is not checked against your room.";
                else
                    StatusText = "No clear floor spot found: place it by hand. This spot is not checked against your room.";
            }
            Touch();
            return true;
        }

        bool BeginCeilingPreview()
        {
            SeatedUser user = CeilingUser();
            int keep = ceilingSettings.preferredSide;
            ceilingSettings.preferredSide = _ceilingSide;
            CeilingPlacement fresh;
            // Only a dock the office can be taken up to from where it is now (the check Confirm makes
            // again), as the floor preview only offers spots with a clear way to them.
            RootPose current = _mover.CurrentPose;
            bool fromDesk = Mode == PlacementMode.Desk;
            Predicate<CeilingPlacement> reachable = p =>
            {
                var o = default(PathSweepOptions);
                o.fromDesk = fromDesk;
                o.mast = true;
                o.mastTop = p.mastTop;
                WorkspaceRoute r; CeilingPathCheck c;
                return FloorRoutePlanner.Plan(_room, user, current, p.DockPose, footprint, ceilingSettings, o, false, out r, out c);
            };
            bool ok = CeilingPlacementEvaluator.Evaluate(_room, user, footprint, ceilingSettings, reachable, out fresh);
            ceilingSettings.preferredSide = keep;
            _roomDirty = false;
            if (!ok)
            {
                // Ceiling mode exists only on real ceiling geometry: no manual fallback.
                return Refuse("Ceiling mode is not available: " + fresh.explanation + ". Use Floor or stay at the desk.");
            }
            _previewCeiling = fresh;
            _placeOutcome = PlaceOutcome.None;
            PendingMode = PlacementMode.Ceiling;
            Phase = DirectorPhase.Previewing;
            _candidates.Clear();
            _index = 0;
            _previewProblem = null;
            ClearAimWords();
            PreviewIsDetected = true;
            PreviewValid = true;
            _previewOrigin = _previewCeiling.InspectionPose;
            PreviewPose = _previewOrigin;       // the outline shows where the office will come down
            StatusText = _previewCeiling.explanation + ". The outline shows where it comes down. Confirm or Cancel.";
            Touch();
            return true;
        }

        RootPose ManualPose(SeatedUser user)
        {
            int side = floorSettings.preferredSide < 0 ? -1 : 1;
            Vector3 p = user.head + user.Right * (side * manualSideDistance) + user.forward * 0.2f;
            p.y = hasTrackedFloor ? trackedFloorHeight : user.head.y - manualAssumedEyeHeight;
            return new RootPose(p, SpatialGeometry.FaceUserYaw(p, user.head), 1f);
        }

        string DetectedStatus()
        {
            return string.Format(CultureInfo.InvariantCulture, "Detected floor spot {0} of {1}. Place here, Other spot, or Cancel.",
                                 _index + 1, _candidates.Count);
        }

        /// <summary>Show the next detected candidate (Floor), or the dock on the other side (Ceiling).</summary>
        public void NextCandidate()
        {
            if (Phase != DirectorPhase.Previewing) return;
            if (PendingMode == PlacementMode.Ceiling)
            {
                CeilingPlacement shown = _previewCeiling;
                _ceilingSide = -_ceilingSide;
                if (!BeginCeilingPreview())
                {
                    // Nothing on the other side: keep the dock already shown.
                    _ceilingSide = -_ceilingSide;
                    _previewCeiling = shown;
                    StatusText = "No other ceiling dock found. Confirm or Cancel.";
                    Touch();
                }
                return;
            }
            if (_candidates.Count == 0) return;
            // A spot that was dragged away by pointing can always go back to a scored one.
            if (!_aimMoved && !_aimDragging && (!PreviewIsDetected || _candidates.Count < 2)) return;
            _aimDragging = false;
            _aimMoved = false;
            ClearAimWords();
            _index = (_index + 1) % _candidates.Count;
            _previewOrigin = _candidates[_index].Pose;
            PreviewPose = _previewOrigin;
            PreviewIsDetected = true;
            PreviewValid = true;
            _previewProblem = null;
            StatusText = DetectedStatus();
            Touch();
        }

        /// <summary>Move the previewed spot, in metres, relative to the user's view (right, forward).
        /// <paramref name="up"/> applies to manual placement only: a detected spot stays on its floor.</summary>
        public void Nudge(float right, float forward, float up = 0f)
        {
            if (Phase != DirectorPhase.Previewing || PendingMode != PlacementMode.Floor) return;
            _aimDragging = false;               // a key takes over from a drag: the spot stays at its last clear place
            SeatedUser user = _user();
            RootPose p = PreviewPose;
            p.position += user.Right * right + user.forward * forward;
            if (!PreviewIsDetected) p.position.y += up;
            ApplyPreviewEdit(p, user);
        }

        /// <summary>Turn the previewed spot about the vertical axis, in degrees.</summary>
        public void Rotate(float degrees)
        {
            if (Phase != DirectorPhase.Previewing || PendingMode != PlacementMode.Floor) return;
            _aimDragging = false;
            RootPose p = PreviewPose;
            p.rotation = Quaternion.AngleAxis(degrees, Vector3.up) * p.rotation;
            ApplyPreviewEdit(p, _user());
        }

        void ApplyPreviewEdit(RootPose p, SeatedUser user)
        {
            ClearAimWords();
            if (PreviewIsDetected)
            {
                string reason;
                if (_aimMoved)
                {
                    // A spot the user pointed at is held to the rules it was accepted under
                    // (no forward-cone preference), so a nudge does not turn it invalid for a new reason.
                    FloorPoseProblem problem;
                    PreviewValid = FloorPlacementEvaluator.CheckPlacedPose(_room, user, SeatForward(user), p, footprint, floorSettings, out problem);
                    reason = FloorPlacementEvaluator.ShortReason(problem);
                }
                else PreviewValid = FloorPlacementEvaluator.ValidatePose(_room, user, p, footprint, floorSettings, true, out reason);
                if (!PreviewValid) { _previewProblem = reason + ". Move it back onto clear floor."; _previewShort = reason; }
                else
                {
                    WorkspaceRoute route; CeilingPathCheck check;
                    PreviewValid = PlanToFloor(user, p, false, out route, out check);
                    _previewProblem = PreviewValid ? null : check.Words("The way to that spot") + ". Move it or choose Next.";
                    _previewShort = PreviewValid ? null : check.Words("The way there");
                    _previewRouteBlocked = !PreviewValid;   // the spot is clear; only the way there is not
                }
                StatusText = PreviewValid ? "Detected floor, adjusted by hand. Place here or Cancel." : _previewProblem;
            }
            else
            {
                float dx = p.position.x - user.head.x, dz = p.position.z - user.head.z;
                if (Mathf.Sqrt(dx * dx + dz * dz) < floorSettings.userClearance) return;   // not onto the chair
                PreviewValid = true;
            }
            PreviewPose = p;
            Touch();
        }

        /// <summary>While previewing: back to the spot as first offered. Otherwise: back to Desk and
        /// forget the saved placement.</summary>
        public void ResetPlacement()
        {
            if (Phase == DirectorPhase.Previewing)
            {
                _aimDragging = false;
                _aimMoved = false;
                ClearAimWords();
                PreviewPose = _previewOrigin;
                PreviewValid = true;
                _previewProblem = null;
                // Pointing may have turned a hand-placed spot into a room-checked one or back: the
                // spot as first offered is detected exactly when the room gave candidates.
                if (PendingMode == PlacementMode.Floor) PreviewIsDetected = _candidates.Count > 0;
                if (PendingMode == PlacementMode.Floor && PreviewIsDetected) StatusText = DetectedStatus();
                Touch();
                return;
            }
            if (Mode != PlacementMode.Desk && RequestMode(PlacementMode.Desk)) PlacementCleared = true;
        }

        /// <summary>
        /// The one "get me out of this" entry. While previewing: leave the preview without moving
        /// anything. While the office is moving: stop the move (<see cref="StopMove"/>). Otherwise nothing.
        /// </summary>
        public void Cancel()
        {
            if (Phase == DirectorPhase.Previewing)
            {
                EndPreview();
                StatusText = "";
                Touch();
                return;
            }
            StopMove();
        }

        /// <summary>
        /// Stop whatever is moving the office, in every phase:
        ///  - a mode change in flight (Desk to Floor, Floor or Ceiling to Desk, ...): it halts and
        ///    goes back along the poses already travelled; the mode goes back to what it was;
        ///  - the first trip up to the ceiling dock: the same, and Ceiling mode is left again;
        ///  - the pod coming down: it goes back up to the dock along the same path, ending Docked;
        ///  - the pod going up: not stopped, it is moving away from the user;
        ///  - while a paper is held (transition hold): the office only HALTS, because going back would
        ///    take it away from the paper; it goes back when the paper is put down;
        ///  - nothing moving, already going back, previewing: nothing happens.
        /// Returns true when it stopped something. Safe to call at any time, any number of times.
        /// </summary>
        public bool StopMove()
        {
            if (Phase == DirectorPhase.Previewing) return false;
            bool stopped = false;
            if (_awaitingMove && _mover.IsRunning)
            {
                if (_mover.IsReversing) return false;               // already stopped
                stopped = _mover.Reverse();
                if (stopped && _mover.IsRunning)
                    StatusText = _mover.IsHalted ? "Stopped. Put the paper down and the office goes back."
                                                 : "Stopped: going back to where it was";
            }
            else if (_pod.Active)
            {
                stopped = _pod.RequestStop();
                if (stopped) StatusText = "";                       // the pod's own words are shown
                SyncPod();
            }
            if (stopped) Touch();
            return stopped;
        }

        void EndPreview()
        {
            Phase = DirectorPhase.Idle;
            _candidates.Clear();
            _index = 0;
            _previewProblem = null;
            _aimDragging = false;
            _aimMoved = false;
            ClearAimWords();
        }

        /// <summary>Accept the previewed spot: only now does the office move, and only along a route
        /// that was just swept and found clear.</summary>
        public bool Confirm()
        {
            if (Phase != DirectorPhase.Previewing) return false;
            if (!Enabled(PendingMode)) { EndPreview(); StatusText = ""; Touch(); return false; }
            // Pointing: while the footprint is held over a spot that is not clear, nothing can be
            // confirmed (an extra refusal only; every check below is unchanged and still decides).
            if (_aimDragging && !AimValid)
                return RefusePreview((string.IsNullOrEmpty(AimProblem) ? "That spot is not clear" : AimProblem) + ". Move it to a clear spot first.", AimProblem);
            // rc6: a spot is chosen by letting go of the outline, then confirmed. Never in the middle of a drag.
            if (_aimDragging)
                return RefusePreview("Let go of the outline first, then press Place here.", "Let go first, then press Place here");
            // A spot whose way there was refused is planned again below (the user may have moved);
            // any other spot that is not clear is refused here.
            if (!PreviewValid && !_previewRouteBlocked)
                return RefusePreview(string.IsNullOrEmpty(_previewProblem) ? "That spot is not clear floor. Move it or choose Next." : _previewProblem, _previewShort);
            if (Held) return RefusePreview("Put the paper down first, then the office can move", "Put the paper down first, then it can move");
            if (GrabBusy) return RefusePreview(HandsBusyWords, "Put down what you are holding first");
            if (IsTransitioning) return RefusePreview("The office is moving. Press Stop, or wait until it has stopped.", "The office is moving");

            RootPose current = _mover.CurrentPose;
            bool fromDesk = Mode == PlacementMode.Desk;
            SeatedUser user = _user();

            if (PendingMode == PlacementMode.Floor)
            {
                RootPose target = PreviewPose;
                WorkspaceRoute route; CeilingPathCheck check;
                if (!PlanToFloor(user, target, !PreviewIsDetected, out route, out check))
                {
                    // Nothing moves; the spot stays on show, drawn as not clear, and says why.
                    string next = PreviewIsDetected ? ". Choose Next, nudge the spot, or Cancel." : ". Nudge the spot, or Cancel.";
                    string shortWords = check.Words("The way there");
                    MarkRouteBlocked(shortWords);
                    return RefusePreview(check.Words("The way to that spot") + next, shortWords);
                }
                Snapshot();
                var r = _mover.TryMoveRoute(route, MoveSeconds(route));
                if (r != TransitionStartResult.Started && r != TransitionStartResult.AlreadyThere) return RefusePreview(_mover.LastRefusal, _mover.LastRefusal);
                if (fromDesk) RememberDesk(current);
                _pod.CancelPendingRetract();
                _awaitingMove = r == TransitionStartResult.Started;
                RememberActiveRoute(route, r == TransitionStartResult.Started);
                _placeOutcome = r == TransitionStartResult.Started ? PlaceOutcome.Moving : PlaceOutcome.Placed;
                _placeTarget = target;
                if (r == TransitionStartResult.AlreadyThere && _pod.Active) _pod.Deactivate();
                Mode = PlacementMode.Floor;
                _needsConfirm = false;
                _handPlaced = !PreviewIsDetected;
                RememberLocal(target, false, target, target);
                StatusText = PreviewIsDetected ? "" : "Placed by hand (not checked against your room)";
            }
            else
            {
                // The preview may be minutes old: check the dock and the way down again for where
                // the user sits now, then the way up to it from where the office is.
                CeilingPlacement fresh;
                if (!CeilingPlacementEvaluator.ValidateDock(_room, CeilingUser(), _previewCeiling.DockPose, footprint, ceilingSettings, out fresh))
                    return Refuse("That ceiling spot is no longer clear: " + fresh.explanation + ". Choose Next, or Cancel.");
                WorkspaceRoute route; CeilingPathCheck check;
                if (!PlanMove(user, current, fresh.DockPose, fromDesk, false, false, false, true, fresh.mastTop, out route, out check))
                {
                    if (check.blockedBy == CeilingPathBlock.User && !fromDesk)
                        return Refuse(check.Words("The way up to that ceiling spot") + ". Return to Desk first, then choose it again.");
                    return Refuse(check.Words("The way up to that ceiling spot") + ". Choose Next for the other side, or Cancel.");
                }
                Snapshot();
                _pod.Configure(fresh.DockPose, fresh.InspectionPose);
                if (!_pod.BeginArrival(route))
                {
                    string why = _pod.StatusText;
                    if (_before.mode == PlacementMode.Ceiling) _pod.Restore(_before.dock, _before.inspection, _before.podEngaged);
                    else _pod.Deactivate();
                    return Refuse(string.IsNullOrEmpty(why) ? "The office cannot go up right now; try again" : why);
                }
                if (fromDesk) RememberDesk(current);
                _ceiling = fresh;
                _awaitingMove = false;                      // the pod drives this trip and reports through ArrivalFailed
                Mode = PlacementMode.Ceiling;
                _needsConfirm = false;
                _handPlaced = false;
                RememberLocal(fresh.DockPose, true, fresh.DockPose, fresh.InspectionPose);
                StatusText = "";
            }
            EndPreview();
            Touch();
            return true;
        }

        /// <summary>
        /// Ceiling mode: bring the office down beside the user. The dock and the way down are checked
        /// again NOW, for where the user sits and what the room contains at this moment; the pod gets
        /// the fresh inspection pose, or the request is refused with the reason.
        /// </summary>
        public bool RequestPodDescend()
        {
            if (Mode != PlacementMode.Ceiling || !_flags.ceilingModeEnabled) return false;
            if (_pod.State != CeilingPodState.Docked || IsTransitioning) return false;      // only from rest at the dock
            if (PlacementNeedsConfirmation) return Refuse("The ceiling spot is not confirmed. Return to Desk, then place it again.");
            if (Held) { bool held = _pod.RequestDescend(); Touch(); return held; }          // the pod says why
            if (GrabBusy) return Refuse(HandsBusyWords);
            CeilingPlacement fresh;
            if (!CeilingPlacementEvaluator.ValidateDock(_room, CeilingUser(), _pod.DockPose, footprint, ceilingSettings, out fresh))
                return Refuse("Cannot come down now: " + fresh.explanation + ". Move, clear the way, or return to Desk and choose another spot.");
            _ceiling = fresh;
            _pod.Configure(_pod.DockPose, fresh.InspectionPose);
            RememberLocal(_pod.DockPose, true, _pod.DockPose, fresh.InspectionPose);
            StatusText = "";
            bool ok = _pod.RequestDescend();
            Touch();
            return ok;
        }

        /// <summary>
        /// Ceiling mode: send the office back up (waits while a transition hold is set). From beside
        /// the user the way up is swept again first. While the office is still coming down this is
        /// a Stop: it goes back up at once and nothing is queued.
        /// </summary>
        public bool RequestPodRetract()
        {
            if (Mode != PlacementMode.Ceiling || !_flags.ceilingModeEnabled) return false;
            if (_pod.State == CeilingPodState.Engaged)
            {
                if (PlacementNeedsConfirmation) return Refuse("The ceiling spot is not confirmed. Return to Desk, then place it again.");
                if (GrabBusy) return Refuse(HandsBusyWords);
                CeilingPathCheck check;
                if (!CeilingPlacementEvaluator.SweepPath(_room, _user(), _pod.InspectionPose, _pod.DockPose, footprint, ceilingSettings,
                                                         CeilingPlacementEvaluator.PodPathOptions(_ceiling.mastTop), out check))
                    return Refuse(check.Words("The way back up") + ". Clear the way or move back, then try again, or return to Desk.");
            }
            bool ok = _pod.RequestRetract();
            SyncPod();
            Touch();
            return ok;
        }

        // ───────────────────────── pointing at the floor (preview only) ─────────────────────────
        //
        // Deliberate placement (rc6). The user points at the floor, pinches the footprint, drags it and
        // lets go: the footprint STAYS where it was let go and nothing moves. When that spot and the
        // way there are clear, the Place here key (PlaceHere, the console's Confirm) appears; pressing
        // it calls Confirm, which plans and sweeps the route AGAIN for the user and the room as they
        // are at that moment, and only then moves the office. Everything else here edits the PREVIEW.
        //
        //  - During a drag the spot under the hand is checked against the floor rules for a chosen
        //    spot (FloorPlacementEvaluator.CheckPlacedPose with a room; the user zone and the distance
        //    limits without one) AND the way there is planned and swept (PlanToFloor, the same planner
        //    Confirm uses). AimSpotValid is the first, AimValid both. PreviewPose only ever follows to
        //    spots that passed both, so it is always the LAST CLEAR one.
        //  - The route result is reused while the spot and the head stay within 2 cm and 2 degrees of
        //    the pose it was planned for (a drag would otherwise plan on every frame). On release the
        //    route is planned again for the exact pose, and Confirm plans it once more.
        //  - A route refused at release or by Confirm leaves the footprint where it is, drawn as not
        //    clear, with the words (FootprintWords). It never jumps back silently.
        //  - PlaceStage says which of these the footprint shows (FloorAimController colours it).

        struct AimRestore
        {
            public RootPose pose;
            public bool detected, valid, moved, routeBlocked;
            public string problem, shortProblem, status;
        }

        enum PlaceOutcome { None = 0, Moving = 1, Placed = 2 }

        bool _aimDragging, _aimMoved, _aimDragMoved;
        AimRestore _aimStart;
        float _aimDisplacement;
        string _aimNote;                    // what the footprint says after a release or a refusal
        bool _previewRouteBlocked;          // PreviewValid is false only because the way there was refused
        string _previewShort;               // why the preview is not valid, in a few words
        PlaceOutcome _placeOutcome;         // the last floor placement this core started
        RootPose _placeTarget;

        // The last route planned for a spot under the hand.
        bool _aimRouteKnown, _aimRouteHand, _aimRouteOk;
        RootPose _aimRoutePose;
        Vector3 _aimRouteHead;
        string _aimRouteWords;
        const float AimRouteReuseDistance = 0.02f, AimRouteReuseDegrees = 2f;

        const string AimDragWords = "Move your hand to slide it across the floor. Let go on a clear spot, then press Place here.";
        const string AimConfirmHint = "Press Place here to move it, drag it again, or Cancel.";
        /// <summary>Footprint words during a drag over a spot whose floor and way are both clear.</summary>
        public const string ReadyWords = "Clear spot, clear way. Let go to choose it";
        public const string ReadyHandWords = "Let go to choose it. Not checked against your room";
        /// <summary>Footprint words at rest on a clear spot: the Place here key is shown.</summary>
        public const string ConfirmWords = "Press Place here to move it here";
        public const string ConfirmHandWords = "Press Place here. Not checked against your room";
        public const string IdleWords = "Press Place here, or pinch and drag it";
        public const string MovingWords = "Moving there. Stop brings it back";
        public const string ReturningWords = "Stopped: going back";
        public const string PlacedWords = "Placed";
        public const string AnotherSpotHint = "Point at the outline, pinch and drag it to another spot";

        /// <summary>The largest distance (metres, on the floor) the footprint under the hand has been from
        /// where it was grabbed, during the current or last drag.</summary>
        public float AimDragDisplacement => _aimDisplacement;

        /// <summary>True while a floor preview is open and can be pointed at.</summary>
        public bool CanAim => Phase == DirectorPhase.Previewing && PendingMode == PlacementMode.Floor && !IsTransitioning;
        /// <summary>True between <see cref="BeginAimDrag"/> and the drop.</summary>
        public bool AimDragging => _aimDragging;
        /// <summary>The spot under the hand during a drag (it may be one that is not clear).</summary>
        public RootPose AimPose { get; private set; }
        /// <summary>The spot under the hand passes the floor rules AND the way there is clear.</summary>
        public bool AimValid { get; private set; }
        /// <summary>The spot under the hand passes the floor rules (the way there may still be blocked).</summary>
        public bool AimSpotValid { get; private set; }
        /// <summary>Why the spot under the hand cannot be used, in a few words; null when it can.</summary>
        public string AimProblem { get; private set; }
        /// <summary>True when the spot under the hand was checked against room data.</summary>
        public bool AimRoomChecked { get; private set; }
        /// <summary>True when the previewed spot was put there by pointing (not a scored candidate as offered).</summary>
        public bool PlacedByAim => _aimMoved;

        /// <summary>What the ghost should show: the spot under the hand during a drag, else the preview.</summary>
        public RootPose ShownPose => _aimDragging ? AimPose : PreviewPose;
        public bool ShownValid => _aimDragging ? AimValid : PreviewValid;
        public bool ShownRoomChecked => _aimDragging ? AimRoomChecked : PreviewIsDetected;

        /// <summary>
        /// Which state the floor footprint is in (FloorAimController draws each one differently):
        /// a spot that may not be used, a spot whose way is blocked, a clear spot under the hand, a clear
        /// spot waiting for Place here, the office on its way, going back after Stop, or placed.
        /// </summary>
        public FloorPlaceStage PlaceStage
        {
            get
            {
                if (_placeOutcome == PlaceOutcome.Moving && _mover.IsRunning)
                    return _mover.IsReversing ? FloorPlaceStage.Returning : FloorPlaceStage.Moving;
                if (Phase == DirectorPhase.Previewing && PendingMode == PlacementMode.Floor)
                {
                    if (_aimDragging)
                    {
                        if (AimValid) return FloorPlaceStage.RouteValid;
                        return AimSpotValid ? FloorPlaceStage.RouteBlocked : FloorPlaceStage.SpotBlocked;
                    }
                    if (PreviewValid) return FloorPlaceStage.NeedsConfirmation;
                    return _previewRouteBlocked ? FloorPlaceStage.RouteBlocked : FloorPlaceStage.SpotBlocked;
                }
                if (_placeOutcome == PlaceOutcome.Placed && Mode == PlacementMode.Floor && !IsTransitioning) return FloorPlaceStage.Placed;
                return FloorPlaceStage.None;
            }
        }

        /// <summary>Where the office is going (Moving / Returning) or went (Placed).</summary>
        public RootPose PlaceTarget => _placeTarget;

        /// <summary>True when the Place here key applies: a floor preview at rest (no drag) on a spot shown
        /// clear, with nothing moving. Pressing it still re-plans the route (Confirm).</summary>
        public bool CanPlaceHere => Phase == DirectorPhase.Previewing && PendingMode == PlacementMode.Floor
                                    && !_aimDragging && PreviewValid && !IsTransitioning;

        /// <summary>
        /// The Place here key: move the office to the previewed floor spot. Only from a floor preview at
        /// rest; the route is planned and swept again by <see cref="Confirm"/> for the user and the room as
        /// they are now, and nothing moves when it is refused (the footprint then says why). Returns true
        /// when the office started to move (or was already there).
        /// </summary>
        public bool PlaceHere()
        {
            if (Phase != DirectorPhase.Previewing || PendingMode != PlacementMode.Floor) return false;
            return Confirm();
        }

        /// <summary>
        /// The "Choose another spot" key: the next detected spot (or, after a drag, back to a scored one);
        /// with nothing to cycle to, it says how to choose one by pointing. Never moves the office.
        /// </summary>
        public bool ChooseAnotherSpot()
        {
            if (Phase != DirectorPhase.Previewing) return false;
            if (PendingMode == PlacementMode.Ceiling || CanCycle) { NextCandidate(); return true; }
            if (_aimDragging) CancelAimDrag();
            _aimNote = AnotherSpotHint;
            StatusText = AnotherSpotHint + ".";
            Touch();
            return false;
        }

        /// <summary>
        /// What the footprint says, in a few words (empty when there is nothing to say): during a drag
        /// whether the spot and its way are clear, or why not; at rest why it cannot be used or that
        /// Place here moves it; then that it is moving, going back, or placed.
        /// </summary>
        public string FootprintWords
        {
            get
            {
                FloorPlaceStage stage = PlaceStage;
                if (stage == FloorPlaceStage.Moving) return MovingWords;
                if (stage == FloorPlaceStage.Returning) return ReturningWords;
                if (stage == FloorPlaceStage.Placed) return PlacedWords;
                if (Phase != DirectorPhase.Previewing || PendingMode != PlacementMode.Floor) return "";
                if (_aimDragging)
                {
                    if (!AimValid) return string.IsNullOrEmpty(AimProblem) ? "That spot is not clear" : AimProblem;
                    return AimRoomChecked ? ReadyWords : ReadyHandWords;
                }
                if (!string.IsNullOrEmpty(_aimNote)) return _aimNote;
                if (!PreviewValid) return string.IsNullOrEmpty(_previewShort) ? "That spot is not clear" : _previewShort;
                if (!PreviewIsDetected) return ConfirmHandWords;
                return _aimMoved ? ConfirmWords : IdleWords;
            }
        }

        /// <summary>True when Next has somewhere to go (another candidate, the other ceiling side, or
        /// back to a scored spot after the footprint was dragged away).</summary>
        public bool CanCycle => Phase == DirectorPhase.Previewing
                                && (PendingMode == PlacementMode.Ceiling || _candidates.Count > 1
                                    || (_candidates.Count > 0 && (_aimMoved || _aimDragging)));

        /// <summary>Raised when only the pose on show moved (a drag frame with no change of validity or
        /// words). <see cref="Changed"/> is raised instead when anything else changed too.</summary>
        public event Action PreviewMoved;

        bool RoomUsable => _room != null && _room.HasRoom && _room.Availability == RoomAvailability.Available && _room.FloorCount > 0;

        /// <summary>
        /// The height of the plane a pointer ray is intersected with: the detected floor under the
        /// user (or under the preview, or the first floor) when there is a room; otherwise the height
        /// the hand-placed preview already has (tracked floor level or the stated guess, plus any
        /// nudge up or down). False when no floor preview is open.
        /// </summary>
        public bool TryGetAimFloorHeight(out float height)
        {
            height = 0f;
            if (!CanAim) return false;
            height = PreviewPose.position.y;
            if (!RoomUsable) return true;
            SeatedUser user = _user();
            int floor = SpatialGeometry.FindFloor(_room, new Vector2(user.head.x, user.head.z));
            if (floor < 0) floor = SpatialGeometry.FindFloor(_room, new Vector2(PreviewPose.position.x, PreviewPose.position.z));
            if (floor < 0) floor = 0;
            height = _room.GetFloor(floor).height;
            return true;
        }

        /// <summary>Keep a floor point within the allowed distance from the user (height unchanged).</summary>
        public Vector3 ClampAimPoint(Vector3 point)
        {
            return FloorAimMath.ClampToRing(point, _user().head, aimMinDistance, aimMaxDistance);
        }

        /// <summary>True when the floor point lies on the footprint on show, or within <paramref name="margin"/> of it.</summary>
        public bool FootprintContains(Vector3 point, float margin)
        {
            if (Phase != DirectorPhase.Previewing) return false;
            RootPose p = ShownPose;
            OrientedRect rect = SpatialGeometry.Footprint(p.position, p.rotation, p.scale, footprint, floorSettings.footprintCenterOffset);
            return rect.Inflated(Mathf.Max(0f, margin)).Contains(new Vector2(point.x, point.z));
        }

        /// <summary>The footprint was grabbed. Nothing moves yet. False when no floor preview is open
        /// or a drag is already running.</summary>
        public bool BeginAimDrag()
        {
            if (!CanAim || _aimDragging) return false;
            _aimStart.pose = PreviewPose;
            _aimStart.detected = PreviewIsDetected;
            _aimStart.valid = PreviewValid;
            _aimStart.moved = _aimMoved;
            _aimStart.routeBlocked = _previewRouteBlocked;
            _aimStart.problem = _previewProblem;
            _aimStart.shortProblem = _previewShort;
            _aimStart.status = StatusText;
            _aimDragging = true;
            _aimDragMoved = false;
            _aimDisplacement = 0f;
            _aimNote = null;
            _aimRouteKnown = false;             // the user or the room may have moved since the last plan
            AimPose = PreviewPose;
            AimValid = PreviewValid;
            AimSpotValid = PreviewValid || _previewRouteBlocked;
            AimProblem = AimValid ? null : (string.IsNullOrEmpty(_previewShort) ? "That spot is not clear" : _previewShort);
            AimRoomChecked = PreviewIsDetected;
            StatusText = AimDragWords;
            Touch();
            return true;
        }

        /// <summary>
        /// The hand moved the footprint to this point on the floor. The office faces the user from
        /// there. The spot is checked at once (floor rules, then the way there); the preview
        /// follows only if both pass.
        /// </summary>
        public void AimDragTo(Vector3 floorPoint)
        {
            if (!_aimDragging) return;
            if (!CanAim) { _aimDragging = false; return; }
            SeatedUser user = _user();
            Vector3 p = FloorAimMath.ClampToRing(floorPoint, user.head, aimMinDistance, aimMaxDistance);
            bool roomChecked = RoomUsable;
            if (roomChecked)
            {
                int floor = SpatialGeometry.FindFloor(_room, new Vector2(p.x, p.z));
                if (floor >= 0) p.y = _room.GetFloor(floor).height;
            }
            RootPose pose = new RootPose(p, SpatialGeometry.FaceUserYaw(p, user.head), PreviewPose.scale);

            Vector3 shift = p - _aimStart.pose.position;
            shift.y = 0f;
            if (shift.magnitude > _aimDisplacement) _aimDisplacement = shift.magnitude;

            string problem;
            bool valid = CheckAimPose(user, pose, roomChecked, out problem);
            bool spotValid = valid;
            if (valid)
            {
                string routeWords;
                if (!AimRouteClear(user, pose, roomChecked, out routeWords)) { valid = false; problem = routeWords; }
            }
            bool changed = valid != AimValid || spotValid != AimSpotValid || problem != AimProblem || roomChecked != AimRoomChecked;
            AimPose = pose;
            AimValid = valid;
            AimSpotValid = spotValid;
            AimProblem = problem;
            AimRoomChecked = roomChecked;
            if (valid)
            {
                PreviewPose = pose;
                PreviewIsDetected = roomChecked;
                PreviewValid = true;
                _previewRouteBlocked = false;
                _previewShort = null;
                _previewProblem = null;
                _aimDragMoved = true;
                _aimMoved = true;
            }
            if (changed)
            {
                StatusText = valid ? AimDragWords : problem + ". Slide it to a clear spot.";
                Touch();
            }
            else
            {
                var h = PreviewMoved;
                if (h != null) h();
            }
        }

        /// <summary>The floor rules for a spot chosen by pointing. With a room: the evaluator's rules for
        /// a chosen spot. Without one: only the user zone (the distance limits are applied by the clamp).</summary>
        bool CheckAimPose(SeatedUser user, RootPose pose, bool roomChecked, out string problem)
        {
            problem = null;
            Vector3 legs = SeatForward(user);
            if (roomChecked)
            {
                FloorPoseProblem why;
                if (FloorPlacementEvaluator.CheckPlacedPose(_room, user, legs, pose, footprint, floorSettings, out why)) return true;
                problem = FloorPlacementEvaluator.ShortReason(why);
                return false;
            }
            if (!FloorPlacementEvaluator.ClearOfUser(pose, user, legs, footprint, floorSettings))
            {
                problem = FloorPlacementEvaluator.ShortReason(FloorPoseProblem.NearUser);
                return false;
            }
            return true;
        }

        /// <summary>
        /// The way from where the office is now to the spot under the hand: planned and swept by
        /// <see cref="PlanToFloor"/> (the planner Confirm uses), or the last result when the spot and
        /// the head are within 2 cm and 2 degrees of the pose it was planned for.
        /// </summary>
        bool AimRouteClear(SeatedUser user, RootPose pose, bool roomChecked, out string words)
        {
            bool hand = !roomChecked;
            float reuse2 = AimRouteReuseDistance * AimRouteReuseDistance;
            if (_aimRouteKnown && hand == _aimRouteHand
                && (pose.position - _aimRoutePose.position).sqrMagnitude <= reuse2
                && Quaternion.Angle(pose.rotation, _aimRoutePose.rotation) <= AimRouteReuseDegrees
                && (user.head - _aimRouteHead).sqrMagnitude <= reuse2)
            {
                words = _aimRouteWords;
                return _aimRouteOk;
            }
            WorkspaceRoute route; CeilingPathCheck check;
            bool ok = PlanToFloor(user, pose, hand, out route, out check);
            _aimRouteKnown = true;
            _aimRouteHand = hand;
            _aimRoutePose = pose;
            _aimRouteHead = user.head;
            _aimRouteOk = ok;
            _aimRouteWords = ok ? null : check.Words("The way there");
            words = _aimRouteWords;
            return ok;
        }

        /// <summary>
        /// The footprint was let go. It NEVER moves the office (rc6): it stays on the last clear spot it
        /// reached (which is where the hand is, unless the hand was over a spot that is not clear). The
        /// way there is planned again for the exact pose; if it is refused now the footprint stays
        /// there, drawn as not clear (RouteBlocked), with the words. Returns true when the footprint
        /// stayed exactly where it was released and that spot is clear: the Place here key then applies.
        /// </summary>
        public bool EndAimDrag()
        {
            if (!_aimDragging) return false;
            _aimDragging = false;
            bool overBadSpot = !AimValid;
            string badWords = AimProblem;
            if (!_aimDragMoved)
            {
                // It never reached a ready spot: nothing changed.
                RestoreAimStart();
                if (overBadSpot && !string.IsNullOrEmpty(badWords))
                {
                    StatusText = badWords + ". It stays where it was.";
                    _aimNote = badWords + ". It stays where it was";
                }
                Touch();
                return false;
            }
            WorkspaceRoute route; CeilingPathCheck check;
            if (!PlanToFloor(_user(), PreviewPose, !PreviewIsDetected, out route, out check))
            {
                string words = check.Words("The way there");
                MarkRouteBlocked(words);
                StatusText = words + ". Move it, choose Next, or Cancel.";
                _aimNote = words;
                Touch();
                return false;
            }
            if (overBadSpot)
            {
                // "Too close to the wall there" reads; "The way there is blocked by X there" does not.
                string there = !string.IsNullOrEmpty(badWords) && badWords.StartsWith("The way", StringComparison.Ordinal) ? "" : " there";
                StatusText = badWords + there + ": it is on the last clear spot. " + AimConfirmHint;
                _aimNote = badWords + there + ". It is on the last clear spot";
            }
            else
            {
                StatusText = (PreviewIsDetected ? "Dropped here by pointing. " : "Dropped here by pointing (not checked against your room). ") + AimConfirmHint;
                _aimNote = null;
            }
            Touch();
            return !overBadSpot;
        }

        /// <summary>
        /// For a placement guide that opens the floor preview where the user is looking: put the
        /// preview's footprint centre on <paramref name="floorPoint"/> (facing the user), under the
        /// same rules as a drag (floor rules for a chosen spot and the swept route). Nothing moves.
        /// True when the preview is now there and ready; false when no floor preview is open, a drag
        /// is running, or that spot cannot be used (the preview then stays where it was and the
        /// footprint says why).
        /// </summary>
        public bool CentrePreviewOn(Vector3 floorPoint, bool quietIfBlocked = false)
        {
            if (!CanAim || _aimDragging) return false;
            SeatedUser user = _user();
            float scale = PreviewPose.scale;
            Quaternion yaw = SpatialGeometry.FaceUserYaw(floorPoint, user.head);
            Vector2 c = floorSettings.footprintCenterOffset;
            Vector3 root = floorPoint - yaw * new Vector3(c.x * scale, 0f, c.y * scale);
            if (!BeginAimDrag()) return false;
            AimDragTo(root);
            // A guide's "where you looked" is a hint, not a request: when that spot cannot be used,
            // put the preview back exactly as it was (the automatic spot, its own words), say nothing.
            if (quietIfBlocked && !ShownValid) { CancelAimDrag(); return false; }
            return EndAimDrag();
        }

        /// <summary>Call a drag off: the preview is exactly as it was before the footprint was grabbed.</summary>
        public void CancelAimDrag()
        {
            if (!_aimDragging) return;
            _aimDragging = false;
            if (Phase == DirectorPhase.Previewing && PendingMode == PlacementMode.Floor) RestoreAimStart();
            Touch();
        }

        void RestoreAimStart()
        {
            PreviewPose = _aimStart.pose;
            PreviewIsDetected = _aimStart.detected;
            PreviewValid = _aimStart.valid;
            _aimMoved = _aimStart.moved;
            _previewRouteBlocked = _aimStart.routeBlocked;
            _previewProblem = _aimStart.problem;
            _previewShort = _aimStart.shortProblem;
            _aimNote = null;
            StatusText = _aimStart.status ?? "";
        }

        /// <summary>The previewed spot stays on show but is drawn as not clear: the way there was refused.</summary>
        void MarkRouteBlocked(string words)
        {
            PreviewValid = false;
            _previewRouteBlocked = true;
            _previewShort = words;
            _previewProblem = words + ". Move it, choose Next, or Cancel.";
        }

        /// <summary>Forget what the footprint said about the last release, refusal or blocked route.</summary>
        void ClearAimWords()
        {
            _aimNote = null;
            _previewRouteBlocked = false;
            _previewShort = null;
            _aimRouteKnown = false;
        }

        bool Refuse(string why)
        {
            StatusText = why ?? "";
            Touch();
            return false;
        }

        /// <summary>A refusal while previewing: the floor footprint says it too (in <paramref name="shortWords"/>).</summary>
        bool RefusePreview(string why, string shortWords)
        {
            if (Phase == DirectorPhase.Previewing && PendingMode == PlacementMode.Floor)
                _aimNote = string.IsNullOrEmpty(shortWords) ? why : shortWords;
            return Refuse(why);
        }

        // ───────────────────────── machine events ─────────────────────────

        void OnMoveFinished(bool reached)
        {
            _activeRouteValid = false;
            if (reached)
            {
                if (Mode != PlacementMode.Ceiling && _pod.Active) _pod.Deactivate();
                _awaitingMove = false;
                if (_placeOutcome == PlaceOutcome.Moving) _placeOutcome = PlaceOutcome.Placed;
                return;
            }
            if (_placeOutcome == PlaceOutcome.Moving) _placeOutcome = PlaceOutcome.None;
            if (!_awaitingMove) return;
            // The move was given up before the root moved (agents stayed busy), was stopped and went
            // back to its start, or was aborted where it was: the old mode applies again.
            Revert(_mover.LastRefusal);
        }

        /// <summary>The first trip up to the dock was stopped or could not be made: leave Ceiling mode again.</summary>
        void SyncPod()
        {
            if (!_pod.ConsumeArrivalFailed()) return;
            string why = _pod.StatusText;
            Revert(string.IsNullOrEmpty(why) || why == "Stopped" ? "Stopped: the office is back where it was" : why);
        }

        void OnRoomChanged()
        {
            _roomDirty = true;
            // A move in flight was swept against the old room data: sweep its route again against the
            // new data (and the user as they sit now). If it is no longer clear, it goes back along the
            // poses it already travelled (StopMove), which were validated before it started.
            if (_awaitingMove && _activeRouteValid && _mover.IsRunning && !_mover.IsReversing)
            {
                CeilingPathCheck again;
                if (!CeilingPlacementEvaluator.SweepRoute(_room, _user(), _activeRoute, footprint, ceilingSettings, _activeOptions, out again))
                {
                    LastRouteCheck = again;
                    if (StopMove()) StatusText = RoomChangedMidMoveWords + ". " + again.Words("The way") + ".";
                }
            }
            // A preview built on the old room data is stale: rebuild it rather than show an unchecked spot.
            if (Phase == DirectorPhase.Previewing)
            {
                if (PendingMode == PlacementMode.Floor) BeginFloorPreview();
                else if (!BeginCeilingPreview()) EndPreview();
            }
            else if (Mode == PlacementMode.Ceiling && _pod.Active && _pod.State == CeilingPodState.Docked
                     && !IsTransitioning && !PlacementNeedsConfirmation)
            {
                // Docked: look again at the way down, so the words are true before the user asks.
                // (A descent request checks again anyway.)
                CeilingPlacement fresh;
                if (CeilingPlacementEvaluator.ValidateDock(_room, CeilingUser(), _pod.DockPose, footprint, ceilingSettings, out fresh))
                {
                    _ceiling = fresh;
                    _pod.Configure(_pod.DockPose, fresh.InspectionPose);
                    RememberLocal(_pod.DockPose, true, _pod.DockPose, fresh.InspectionPose);
                    StatusText = "";
                }
                else StatusText = "The room changed: " + fresh.explanation + ". Clear the way, or return to Desk and choose another spot.";
            }
            Touch();
        }

        /// <summary>True when the room changed since candidates were last computed.</summary>
        public bool RoomDirty => _roomDirty;

        /// <summary>Advance the transition machine, the pod and this director, in that order.</summary>
        public void Tick()
        {
            _mover.Tick();
            _pod.Tick();
            SyncPod();
            if (Mode != _lastMode || IsRemote != _lastRemote || IsTransitioning != _lastTransitioning) Touch();
        }

        void Touch()
        {
            _lastMode = Mode; _lastRemote = IsRemote; _lastTransitioning = IsTransitioning;
            var h = Changed;
            if (h != null) h();
        }

        // ───────────────────────── recenter, pause ─────────────────────────

        void RememberLocal(RootPose pose, bool ceiling, RootPose dock, RootPose inspection)
        {
            _hasLocal = false;
            Pose reference;
            if (_room == null || !_room.TryGetReferencePose(out reference)) return;
            _hasLocal = true;
            _localPos = WorkspacePlacementCodec.ToLocal(reference, pose.position);
            _localRot = WorkspacePlacementCodec.ToLocal(reference, pose.rotation);
            _localScale = pose.scale;
            if (ceiling)
            {
                _dockLocalPos = WorkspacePlacementCodec.ToLocal(reference, dock.position);
                _dockLocalRot = WorkspacePlacementCodec.ToLocal(reference, dock.rotation);
                _inspectLocalPos = WorkspacePlacementCodec.ToLocal(reference, inspection.position);
                _inspectLocalRot = WorkspacePlacementCodec.ToLocal(reference, inspection.rotation);
            }
        }

        /// <summary>Stop everything where it is and require the placement to be chosen again.</summary>
        void HoldInPlace(string why)
        {
            if (_pod.Active) _pod.Hold(why);        // also ends a running move where it is
            else _mover.Abort(why);                 // a mode change in flight: the old mode applies again (Revert)
            _needsConfirm = true;
            StatusText = why;
        }

        /// <summary>A floor spot held to the rules a chosen spot is accepted under (CheckPlacedPose), so a
        /// spot the user pointed at is not refused after a recenter or on restore for a rule it never had
        /// to meet. Scored candidates meet these rules too (they are a superset of the search's).</summary>
        bool FloorSpotStillClear(RootPose pose, SeatedUser user, out string reason)
        {
            reason = null;
            FloorPoseProblem problem;
            if (FloorPlacementEvaluator.CheckPlacedPose(_room, user, SeatForward(user), pose, footprint, floorSettings, out problem)) return true;
            if (problem == FloorPoseProblem.NearUser) reason = "It is now too close to your chair";
            else if (problem == FloorPoseProblem.NoRoom)
                reason = SpatialGeometry.ReasonText(_room == null ? RoomAvailability.NotLoadedYet : _room.Availability);
            else reason = FloorPlacementEvaluator.SpotReason(problem);     // the words restore and recenter always used
            return false;
        }

        /// <summary>
        /// The direction from the seated user toward their desk (where their legs are), flattened.
        /// Taken from the desk pose of the office (its +Z points at the user), which does not turn
        /// when the user looks round at the floor; the view direction when no desk pose is known.
        /// </summary>
        Vector3 SeatForward(SeatedUser user)
        {
            bool known = Mode == PlacementMode.Desk || _hasDeskPose;
            if (known)
            {
                RootPose desk = Mode == PlacementMode.Desk ? _mover.CurrentPose : _deskPose;
                Vector3 v = desk.position - user.head;
                v.y = 0f;
                if (v.sqrMagnitude > 0.01f) return v.normalized;
            }
            return user.forward;
        }

        /// <summary>
        /// The user for the ceiling search and its re-checks: the head where it is, but "forward" is the
        /// seat's forward (<see cref="SeatForward"/>), not where the eyes point. Worn pass 1: the user
        /// looks up and to the side at the pod to bring it down (key, voice or pull gesture), and with
        /// the gaze as forward the poses "beside you" turned with the head, into the desk and the screen
        /// in front ("SCREEN in the way") or across the face ("too close to you"). Only head.position
        /// is used by the sweep itself, so no margin changes.
        /// </summary>
        SeatedUser CeilingUser()
        {
            SeatedUser u = _user();
            return new SeatedUser(u.head, SeatForward(u));
        }

        /// <summary>
        /// The tracking origin moved (system recenter, headset put back on). World coordinates no
        /// longer mean what they did, so nothing is taken on trust:
        ///  - a preview is dropped;
        ///  - Floor: the spot is recomputed from the room reference and checked against the room and
        ///    the user as they are now. Clear: the office is put there. Not clear, or no reference
        ///    while a move runs: any move is ended WHERE IT IS and the placement must be chosen again;
        ///  - Ceiling: the dock is recomputed and <see cref="CeilingPlacementEvaluator.ValidateDock"/>
        ///    run for the current user. Valid: the pod gets the dock and the fresh inspection pose.
        ///    Not valid: the same hold. The old poses are never used;
        ///  - Desk, with a move back to the desk in flight: finished at the desk pose recomputed from
        ///    the room reference, or held when there is none.
        /// The office is never written to a pose that was not validated after the recenter.
        /// </summary>
        public void OnRecenter()
        {
            if (Phase == DirectorPhase.Previewing)
            {
                EndPreview();
                StatusText = "The view was recentered: choose the spot again";
            }
            Pose reference = Pose.identity;
            bool roomRef = _room != null && _room.TryGetReferencePose(out reference);
            bool haveRef = _hasLocal && roomRef;

            // The desk pose follows the room too, so the way back still ends at the same real place.
            bool deskKnown = _hasDeskPose && _hasDeskLocal && roomRef;
            if (deskKnown && (!Near(reference.position, _deskReference.position) || Quaternion.Angle(reference.rotation, _deskReference.rotation) > 0.01f))
            {
                _deskPose = WorkspacePlacementCodec.ToWorld(reference, _deskLocalPos, _deskLocalRot, _deskPose.scale);
                _deskReference = reference;
            }

            const string again = " Return to Desk, then place it again.";
            if (Mode == PlacementMode.Floor)
            {
                if (haveRef)
                {
                    RootPose pose = WorkspacePlacementCodec.ToWorld(reference, _localPos, _localRot, _localScale);
                    string reason;
                    if (FloorSpotStillClear(pose, _user(), out reason)) { _mover.Snap(pose); _needsConfirm = false; }
                    else HoldInPlace("The view was recentered and the floor spot is no longer clear: " + reason + "." + again);
                }
                else if (_mover.IsRunning) HoldInPlace("The view was recentered during the move." + again);
                // A hand-placed spot at rest has no room reference: it stays where it is, unchecked as before.
            }
            else if (Mode == PlacementMode.Ceiling)
            {
                CeilingPlacement fresh = default(CeilingPlacement);
                bool valid = false;
                if (haveRef)
                {
                    RootPose dock = WorkspacePlacementCodec.ToWorld(reference, _dockLocalPos, _dockLocalRot, 1f);
                    valid = CeilingPlacementEvaluator.ValidateDock(_room, CeilingUser(), dock, footprint, ceilingSettings, out fresh);
                }
                if (valid)
                {
                    _ceiling = fresh;
                    _pod.OnRecenter(fresh.DockPose, fresh.InspectionPose);   // both validated just now
                    RememberLocal(fresh.DockPose, true, fresh.DockPose, fresh.InspectionPose);
                    _needsConfirm = false;
                }
                else
                {
                    string why = haveRef ? fresh.explanation : "the room scan gives no reference";
                    HoldInPlace("The view was recentered and the ceiling spot could not be confirmed: " + why + "." + again);
                }
            }
            else if (_mover.IsRunning)
            {
                // On the way back to the desk.
                if (deskKnown) _mover.Snap(_deskPose);
                else HoldInPlace("The view was recentered during the move. Press Desk again.");
            }
            Touch();
        }

        static bool Near(Vector3 a, Vector3 b) { return (a - b).sqrMagnitude <= 1e-8f; }

        /// <summary>The app is being paused: end any travel at its destination (or, if it had been
        /// stopped, where it started). Both ends of a route were validated before the move began.</summary>
        public void OnPause()
        {
            if (_pod.Active) _pod.OnPause(); else _mover.FinishNow();
            SyncPod();
            Touch();
        }

        // ───────────────────────── persistence ─────────────────────────

        /// <summary>The record to save for the current placement, or null (Desk, moving, or no room reference).</summary>
        public SavedPlacement BuildSave(string build)
        {
            // Persisted only after success: never while a move is in flight (it may still be stopped and go back).
            if (Mode == PlacementMode.Desk || !_hasLocal || _room == null || PlacementNeedsConfirmation) return null;
            if (_awaitingMove || IsTransitioning) return null;
            Pose reference;
            if (!_room.TryGetReferencePose(out reference)) return null;
            RootPose pose = WorkspacePlacementCodec.ToWorld(reference, _localPos, _localRot, _localScale);
            RootPose dock = WorkspacePlacementCodec.ToWorld(reference, _dockLocalPos, _dockLocalRot, 1f);
            return WorkspacePlacementCodec.Capture(Mode, _room, pose, Mode == PlacementMode.Ceiling, dock, build);
        }

        /// <summary>
        /// Restore a saved placement after validating it, AND THE WAY TO IT, against the CURRENT room
        /// and user. On any doubt it refuses with a reason and the office stays in Desk mode.
        /// </summary>
        public bool TryRestore(SavedPlacement saved, out string reason)
        {
            reason = null;
            if (saved == null) { reason = "No saved placement"; return false; }
            var mode = (PlacementMode)saved.mode;
            if (mode == PlacementMode.Desk) { reason = "Saved placement has no spatial mode"; return false; }
            if (!Enabled(mode)) { reason = "That mode is switched off"; return false; }
            if (Mode != PlacementMode.Desk || Phase != DirectorPhase.Idle || IsTransitioning) { reason = "The office is not at rest in Desk mode"; return false; }
            if (_deskReady != null && !_deskReady()) { reason = "The office is still being placed"; return false; }
            if (Held) { reason = "Something from the office is being held"; return false; }

            RootPose pose, dock;
            if (!WorkspacePlacementCodec.TryResolve(saved, _room, out pose, out dock, out reason)) return false;
            SeatedUser user = _user();
            RootPose current = _mover.CurrentPose;
            WorkspaceRoute route; CeilingPathCheck check;

            if (mode == PlacementMode.Floor)
            {
                if (!FloorSpotStillClear(pose, user, out reason))
                {
                    if (reason == "It is now too close to your chair") reason = "The saved spot is now too close to your chair";
                    return false;
                }
                if (!PlanLowMove(user, current, pose, true, false, false, out route, out check))
                { reason = check.Words("The way to the saved spot"); return false; }

                Snapshot();
                var r = _mover.TryMoveRoute(route, MoveSeconds(route));
                if (r != TransitionStartResult.Started && r != TransitionStartResult.AlreadyThere) { reason = _mover.LastRefusal; return false; }
                RememberDesk(current);
                _awaitingMove = r == TransitionStartResult.Started;
                RememberActiveRoute(route, r == TransitionStartResult.Started);
                _placeOutcome = PlaceOutcome.None;
                Mode = PlacementMode.Floor;
                _handPlaced = false;
                RememberLocal(pose, false, pose, pose);
            }
            else
            {
                if (!saved.hasDock) { reason = "Saved placement has no ceiling dock"; return false; }
                CeilingPlacement placement;
                if (!CeilingPlacementEvaluator.ValidateDock(_room, CeilingUser(), dock, footprint, ceilingSettings, out placement))
                { reason = placement.explanation; return false; }
                if (!PlanMove(user, current, placement.DockPose, true, false, false, false, true, placement.mastTop, out route, out check))
                { reason = check.Words("The way up to the saved dock"); return false; }

                Snapshot();
                _pod.Configure(placement.DockPose, placement.InspectionPose);
                if (!_pod.BeginArrival(route))
                {
                    reason = string.IsNullOrEmpty(_pod.StatusText) ? "The office cannot go up right now" : _pod.StatusText;
                    _pod.Deactivate();
                    return false;
                }
                _ceiling = placement;
                RememberDesk(current);
                _awaitingMove = false;
                Mode = PlacementMode.Ceiling;
                _handPlaced = false;
                RememberLocal(placement.DockPose, true, placement.DockPose, placement.InspectionPose);
            }
            _needsConfirm = false;
            StatusText = "";
            Touch();
            return true;
        }
    }
}
