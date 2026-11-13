using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MuseDesk.Spatial
{
    public enum DirectorPhase { Idle = 0, Previewing = 1 }

    /// <summary>
    /// The decision logic of the room-aware workspace, free of scene access so it can be tested:
    /// modes Desk / Floor / Ceiling, the preview-then-confirm flow, flag gating, the hand-over to the
    /// transition machine and the ceiling pod, recenter and pause handling, and what is saved.
    ///
    /// Rules it enforces:
    ///  - with a mode's flag off, a request for that mode does nothing at all;
    ///  - the office moves only on Confirm, never on a request or a preview;
    ///  - a position is called "detected floor" only when it came from room data and still passes
    ///    the checks; without room data the user is offered manual placement, in words;
    ///  - Ceiling mode exists only on real ceiling geometry;
    ///  - returning to Desk puts the root back at exactly the pose it had when Desk was left;
    ///  - nothing here knows about tasks, gateways or reducers: a mode change cannot start, cancel
    ///    or reset work.
    /// </summary>
    public sealed class SpatialDirectorCore
    {
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
        PlacementMode _modeBeforeMove;
        bool _awaitingMove;

        int _index;
        RootPose _previewOrigin;
        CeilingPlacement _ceiling;
        int _ceilingSide = 1;

        // The confirmed placement relative to the room reference (recenter, save).
        bool _hasLocal;
        Vector3 _localPos, _dockLocalPos, _inspectLocalPos;
        Quaternion _localRot = Quaternion.identity, _dockLocalRot = Quaternion.identity, _inspectLocalRot = Quaternion.identity;
        float _localScale = 1f;

        PlacementMode _lastMode;
        bool _lastRemote, _lastTransitioning;

        public FloorPlacementSettings floorSettings = new FloorPlacementSettings();
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

        public PlacementMode Mode { get; private set; }
        public DirectorPhase Phase { get; private set; }
        /// <summary>The mode being previewed (valid while <see cref="Phase"/> is Previewing).</summary>
        public PlacementMode PendingMode { get; private set; }
        public bool HasPreview => Phase == DirectorPhase.Previewing;
        public RootPose PreviewPose { get; private set; }
        /// <summary>True when the previewed pose comes from room data; false for manual placement.</summary>
        public bool PreviewIsDetected { get; private set; }
        /// <summary>False when a detected pose was nudged somewhere that fails the room checks.</summary>
        public bool PreviewValid { get; private set; }
        public int CandidateCount => _candidates.Count;
        public int CandidateIndex => _index;
        public string StatusText { get; private set; }
        /// <summary>Set when the user reset the placement: the host deletes the saved record.</summary>
        public bool PlacementCleared { get; set; }

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
        public CeilingPlacement LastCeiling => _ceiling;
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

        bool Enabled(PlacementMode m)
        {
            if (m == PlacementMode.Floor) return _flags.floorModeEnabled;
            if (m == PlacementMode.Ceiling) return _flags.ceilingModeEnabled;
            return true;
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
            if (IsTransitioning) return Refuse("The office is moving; try again when it has stopped");
            if (Phase == DirectorPhase.Previewing) EndPreview();

            if (mode == PlacementMode.Desk) return ReturnToDesk();
            if (mode == PlacementMode.Floor) return BeginFloorPreview();
            return BeginCeilingPreview();
        }

        bool ReturnToDesk()
        {
            if (!_hasDeskPose) return Refuse("No desk position is known");
            if (Held) return Refuse("Put the paper down first, then the office can move");
            var r = _mover.TryMove(_deskPose, MoveSeconds(_mover.CurrentPose.position, _deskPose.position));
            if (r == TransitionStartResult.Held || r == TransitionStartResult.AlreadyRunning) return Refuse(_mover.LastRefusal);
            _pod.CancelPendingRetract();        // the pod must not start its own move under this one
            _modeBeforeMove = Mode;
            _awaitingMove = r == TransitionStartResult.Started;
            Mode = PlacementMode.Desk;
            _hasLocal = false;
            StatusText = "";
            if (r == TransitionStartResult.AlreadyThere && _pod.Active) _pod.Deactivate();
            Touch();
            return true;
        }

        bool BeginFloorPreview()
        {
            SeatedUser user = _user();
            _candidates.Clear();
            if (_room != null) FloorPlacementEvaluator.Evaluate(_room, user, footprint, floorSettings, _candidates, _report);
            else { _report.Clear(); _report.availability = RoomAvailability.NotLoadedYet; }
            _roomDirty = false;

            PendingMode = PlacementMode.Floor;
            Phase = DirectorPhase.Previewing;
            _index = 0;
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
                StatusText = _report.availability != RoomAvailability.Available
                    ? "Room scan not available: place it by hand. " + SpatialGeometry.ReasonText(_report.availability) + "."
                    : "No clear floor spot found: place it by hand. This spot is not checked against your room.";
            }
            Touch();
            return true;
        }

        bool BeginCeilingPreview()
        {
            SeatedUser user = _user();
            int keep = ceilingSettings.preferredSide;
            ceilingSettings.preferredSide = _ceilingSide;
            CeilingPlacement fresh;
            bool ok = CeilingPlacementEvaluator.Evaluate(_room, user, footprint, ceilingSettings, out fresh);
            ceilingSettings.preferredSide = keep;
            _roomDirty = false;
            if (!ok)
            {
                // Ceiling mode exists only on real ceiling geometry: no manual fallback.
                return Refuse("Ceiling mode is not available: " + fresh.explanation + ".");
            }
            _ceiling = fresh;
            PendingMode = PlacementMode.Ceiling;
            Phase = DirectorPhase.Previewing;
            _candidates.Clear();
            _index = 0;
            PreviewIsDetected = true;
            PreviewValid = true;
            _previewOrigin = _ceiling.InspectionPose;
            PreviewPose = _previewOrigin;       // the outline shows where the office will come down
            StatusText = _ceiling.explanation + ". The outline shows where it comes down. Confirm or Cancel.";
            Touch();
            return true;
        }

        RootPose ManualPose(SeatedUser user)
        {
            int side = floorSettings.preferredSide < 0 ? -1 : 1;
            Vector3 p = user.head + user.Right * (side * manualSideDistance) + user.forward * 0.2f;
            p.y = user.head.y - manualAssumedEyeHeight;
            return new RootPose(p, SpatialGeometry.FaceUserYaw(p, user.head), 1f);
        }

        string DetectedStatus()
        {
            return string.Format(CultureInfo.InvariantCulture, "Detected floor spot {0} of {1}. Next, Confirm or Cancel.",
                                 _index + 1, _candidates.Count);
        }

        /// <summary>Show the next detected candidate (Floor), or the dock on the other side (Ceiling).</summary>
        public void NextCandidate()
        {
            if (Phase != DirectorPhase.Previewing) return;
            if (PendingMode == PlacementMode.Ceiling)
            {
                _ceilingSide = -_ceilingSide;
                if (!BeginCeilingPreview())
                {
                    // Nothing on the other side: keep the dock already shown.
                    _ceilingSide = -_ceilingSide;
                    StatusText = "No other ceiling dock found. Confirm or Cancel.";
                    Touch();
                }
                return;
            }
            if (!PreviewIsDetected || _candidates.Count < 2) return;
            _index = (_index + 1) % _candidates.Count;
            _previewOrigin = _candidates[_index].Pose;
            PreviewPose = _previewOrigin;
            PreviewValid = true;
            StatusText = DetectedStatus();
            Touch();
        }

        /// <summary>Move the previewed spot, in metres, relative to the user's view (right, forward).
        /// <paramref name="up"/> applies to manual placement only: a detected spot stays on its floor.</summary>
        public void Nudge(float right, float forward, float up = 0f)
        {
            if (Phase != DirectorPhase.Previewing || PendingMode != PlacementMode.Floor) return;
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
            RootPose p = PreviewPose;
            p.rotation = Quaternion.AngleAxis(degrees, Vector3.up) * p.rotation;
            ApplyPreviewEdit(p, _user());
        }

        void ApplyPreviewEdit(RootPose p, SeatedUser user)
        {
            if (PreviewIsDetected)
            {
                string reason;
                PreviewValid = FloorPlacementEvaluator.ValidatePose(_room, user, p, footprint, floorSettings, true, out reason);
                StatusText = PreviewValid ? "Detected floor, adjusted by hand. Confirm or Cancel."
                                          : reason + ". Move it back onto clear floor.";
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
                PreviewPose = _previewOrigin;
                PreviewValid = true;
                if (PendingMode == PlacementMode.Floor && PreviewIsDetected) StatusText = DetectedStatus();
                Touch();
                return;
            }
            if (Mode != PlacementMode.Desk && RequestMode(PlacementMode.Desk)) PlacementCleared = true;
        }

        /// <summary>Leave the preview without moving anything.</summary>
        public void Cancel()
        {
            if (Phase != DirectorPhase.Previewing) return;
            EndPreview();
            StatusText = "";
            Touch();
        }

        void EndPreview()
        {
            Phase = DirectorPhase.Idle;
            _candidates.Clear();
            _index = 0;
        }

        /// <summary>Accept the previewed spot: only now does the office move.</summary>
        public bool Confirm()
        {
            if (Phase != DirectorPhase.Previewing) return false;
            if (!Enabled(PendingMode)) { Cancel(); return false; }
            if (PreviewIsDetected && !PreviewValid) return Refuse("That spot is not clear floor. Move it or choose Next.");
            if (Held) return Refuse("Put the paper down first, then the office can move");
            if (IsTransitioning) return Refuse("The office is moving; try again when it has stopped");

            RootPose current = _mover.CurrentPose;
            if (Mode == PlacementMode.Desk) { _deskPose = current; _hasDeskPose = true; }

            if (PendingMode == PlacementMode.Floor)
            {
                RootPose target = PreviewPose;
                var r = _mover.TryMove(target, MoveSeconds(current.position, target.position));
                if (r == TransitionStartResult.Held || r == TransitionStartResult.AlreadyRunning) return Refuse(_mover.LastRefusal);
                _pod.CancelPendingRetract();
                _modeBeforeMove = Mode;
                _awaitingMove = r == TransitionStartResult.Started;
                if (r == TransitionStartResult.AlreadyThere && _pod.Active) _pod.Deactivate();
                Mode = PlacementMode.Floor;
                RememberLocal(target, false, target, target);
                StatusText = PreviewIsDetected ? "" : "Placed by hand (not checked against your room)";
            }
            else
            {
                _modeBeforeMove = Mode;
                _awaitingMove = false;
                Mode = PlacementMode.Ceiling;
                _pod.Configure(_ceiling.DockPose, _ceiling.InspectionPose);
                _pod.RequestRetract();                      // travel up to the dock through the pod's own states
                RememberLocal(_ceiling.DockPose, true, _ceiling.DockPose, _ceiling.InspectionPose);
                StatusText = "";
            }
            EndPreview();
            Touch();
            return true;
        }

        /// <summary>Ceiling mode: bring the office down beside the user.</summary>
        public bool RequestPodDescend()
        {
            if (Mode != PlacementMode.Ceiling || !_flags.ceilingModeEnabled) return false;
            bool ok = _pod.RequestDescend();
            Touch();
            return ok;
        }

        /// <summary>Ceiling mode: send the office back up (waits while a transition hold is set).</summary>
        public bool RequestPodRetract()
        {
            if (Mode != PlacementMode.Ceiling || !_flags.ceilingModeEnabled) return false;
            bool ok = _pod.RequestRetract();
            Touch();
            return ok;
        }

        float MoveSeconds(Vector3 from, Vector3 to)
        {
            return Mathf.Clamp((to - from).magnitude / Mathf.Max(0.05f, moveMetresPerSecond), minMoveSeconds, maxMoveSeconds);
        }

        bool Refuse(string why)
        {
            StatusText = why ?? "";
            Touch();
            return false;
        }

        // ───────────────────────── machine events ─────────────────────────

        void OnMoveFinished(bool reached)
        {
            if (reached)
            {
                if (Mode != PlacementMode.Ceiling && _pod.Active) _pod.Deactivate();
                _awaitingMove = false;
                return;
            }
            if (!_awaitingMove) return;
            // The move was given up before the root moved (agents stayed busy): stay in the old mode.
            _awaitingMove = false;
            Mode = _modeBeforeMove;
            if (Mode == PlacementMode.Desk) _hasLocal = false;
            StatusText = _mover.LastRefusal ?? "";
            Touch();
        }

        void OnRoomChanged()
        {
            _roomDirty = true;
            // A preview built on the old room data is stale: rebuild it rather than show an unchecked spot.
            if (Phase == DirectorPhase.Previewing)
            {
                if (PendingMode == PlacementMode.Floor) BeginFloorPreview();
                else if (!BeginCeilingPreview()) EndPreview();
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

        /// <summary>
        /// The tracking origin moved (system recenter, headset put back on). Any travel is ended. In
        /// Floor and Ceiling mode the office is put back at its place relative to the room reference,
        /// if the room still provides one; a preview is dropped because its spot is stale.
        /// </summary>
        public void OnRecenter()
        {
            if (Phase == DirectorPhase.Previewing)
            {
                EndPreview();
                StatusText = "The view was recentered: choose the spot again";
            }
            Pose reference = Pose.identity;
            bool haveRef = _hasLocal && _room != null && _room.TryGetReferencePose(out reference);

            if (Mode == PlacementMode.Floor)
            {
                if (haveRef) _mover.Snap(WorkspacePlacementCodec.ToWorld(reference, _localPos, _localRot, _localScale));
                else _mover.FinishNow();
            }
            else if (Mode == PlacementMode.Ceiling)
            {
                if (haveRef)
                {
                    RootPose dock = WorkspacePlacementCodec.ToWorld(reference, _dockLocalPos, _dockLocalRot, 1f);
                    RootPose inspect = WorkspacePlacementCodec.ToWorld(reference, _inspectLocalPos, _inspectLocalRot, 1f);
                    CeilingPlacement fresh;
                    if (CeilingPlacementEvaluator.ValidateDock(_room, _user(), dock, footprint, ceilingSettings, out fresh))
                        inspect = fresh.InspectionPose;      // beside where the user sits now
                    _pod.OnRecenter(dock, inspect);
                }
                else _pod.OnPause();
            }
            else _mover.FinishNow();
            Touch();
        }

        /// <summary>The app is being paused: end any travel at its destination.</summary>
        public void OnPause()
        {
            if (_pod.Active) _pod.OnPause(); else _mover.FinishNow();
            Touch();
        }

        // ───────────────────────── persistence ─────────────────────────

        /// <summary>The record to save for the current placement, or null (Desk, moving, or no room reference).</summary>
        public SavedPlacement BuildSave(string build)
        {
            if (Mode == PlacementMode.Desk || !_hasLocal || _room == null) return null;
            Pose reference;
            if (!_room.TryGetReferencePose(out reference)) return null;
            RootPose pose = WorkspacePlacementCodec.ToWorld(reference, _localPos, _localRot, _localScale);
            RootPose dock = WorkspacePlacementCodec.ToWorld(reference, _dockLocalPos, _dockLocalRot, 1f);
            return WorkspacePlacementCodec.Capture(Mode, _room, pose, Mode == PlacementMode.Ceiling, dock, build);
        }

        /// <summary>
        /// Restore a saved placement after validating it against the CURRENT room and user. On any
        /// doubt it refuses with a reason and the office stays in Desk mode.
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

            if (mode == PlacementMode.Floor)
            {
                if (!FloorPlacementEvaluator.ValidatePose(_room, user, pose, footprint, floorSettings, false, out reason)) return false;
                OrientedRect rect = SpatialGeometry.Footprint(pose.position, pose.rotation, pose.scale, footprint, floorSettings.footprintCenterOffset);
                if (rect.DistanceToPoint(new Vector2(user.head.x, user.head.z)) < floorSettings.userClearance)
                { reason = "The saved spot is now too close to your chair"; return false; }

                RootPose current = _mover.CurrentPose;
                var r = _mover.TryMove(pose, MoveSeconds(current.position, pose.position));
                if (r == TransitionStartResult.Held || r == TransitionStartResult.AlreadyRunning) { reason = _mover.LastRefusal; return false; }
                _deskPose = current; _hasDeskPose = true;
                _modeBeforeMove = PlacementMode.Desk;
                _awaitingMove = r == TransitionStartResult.Started;
                Mode = PlacementMode.Floor;
                RememberLocal(pose, false, pose, pose);
            }
            else
            {
                if (!saved.hasDock) { reason = "Saved placement has no ceiling dock"; return false; }
                CeilingPlacement placement;
                if (!CeilingPlacementEvaluator.ValidateDock(_room, user, dock, footprint, ceilingSettings, out placement))
                { reason = placement.explanation; return false; }
                _ceiling = placement;
                _deskPose = _mover.CurrentPose; _hasDeskPose = true;
                _modeBeforeMove = PlacementMode.Desk;
                _awaitingMove = false;
                Mode = PlacementMode.Ceiling;
                _pod.Configure(placement.DockPose, placement.InspectionPose);
                _pod.RequestRetract();
                RememberLocal(placement.DockPose, true, placement.DockPose, placement.InspectionPose);
            }
            StatusText = "";
            Touch();
            return true;
        }
    }
}
