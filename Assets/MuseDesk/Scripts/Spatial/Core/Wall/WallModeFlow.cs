using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>Phase 3 switches. ALL false by default: with them off nothing of the wall habitat runs or shows.
    /// Names follow 03_INTEGRATION_CONTRACT.json "feature_flags".</summary>
    [Serializable]
    public sealed class WallFeatureFlags
    {
        public bool wallHabitatEnabled;
        public bool wallManagerEnabled;
        public bool agentSelectEnabled;
        public bool agentBtwEnabled;
        public bool largeAgentPortalEnabled;
        public bool wallElevatorVfxEnabled;
    }

    public enum WallModeState { Off = 0, Previewing = 1, Active = 2, Relocating = 3 }

    /// <summary>
    /// Wall mode placement, pure logic: Off -> Previewing (ghost on the best wall spot; Try another, layout
    /// switch, point to move) -> Place Here (explicit, never on release of a drag, never while a hand holds
    /// something) -> Active. Relocating shows a new ghost while the habitat stays put; Cancel keeps the old
    /// spot. Without walls in the scan a manual spot is offered amber with the words "nothing was checked".
    /// A room change that invalidates the placed spot SUSPENDS (says so, moves nothing); it never jumps.
    /// </summary>
    public sealed class WallModeFlow
    {
        readonly Func<IRoomGeometry> _room;
        readonly Func<IWallGeometry> _walls;
        readonly Func<SeatedUser> _user;
        List<WallCandidate> _candidates = new List<WallCandidate>();
        int _index;

        public WallPlacementSettings settings;
        public WallFeatureFlags flags;
        public WallModeState State { get; private set; }
        public WallLayoutKind Layout { get; private set; }
        public WallCandidate Shown { get; private set; }
        public bool HasShown { get; private set; }
        public WallCandidate Placed { get; private set; }
        public bool HasPlaced { get; private set; }
        /// <summary>The placed spot no longer passes after a room update: no movement until the user re-places.</summary>
        public bool Suspended { get; private set; }
        public string Status { get; private set; }
        public int CandidateCount => _candidates.Count;
        public bool ManualSpot => HasShown && !Shown.roomChecked;
        public event Action Changed;
        /// <summary>Raised when Place Here moved the habitat (new pose in Placed).</summary>
        public event Action PlacedChanged;

        public WallModeFlow(Func<IRoomGeometry> room, Func<IWallGeometry> walls, Func<SeatedUser> user,
                            WallPlacementSettings settings, WallFeatureFlags flags)
        {
            _room = room; _walls = walls; _user = user;
            this.settings = settings ?? new WallPlacementSettings();
            this.flags = flags ?? new WallFeatureFlags();
            Layout = WallLayoutKind.Full;
        }

        /// <summary>Open the preview. <paramref name="canLeaveCurrentMode"/>: the director is idle at the desk (or the habitat is already active).</summary>
        public bool RequestEnter(bool canLeaveCurrentMode, bool handsBusy)
        {
            if (!flags.wallHabitatEnabled) { Say("Wall mode is switched off in this build"); return false; }
            if (State == WallModeState.Previewing || State == WallModeState.Relocating) return true;
            if (!canLeaveCurrentMode) { Say("Finish the current move first"); return false; }
            if (handsBusy) { Say("Put down what you are holding first"); return false; }
            State = State == WallModeState.Active ? WallModeState.Relocating : WallModeState.Previewing;
            Refresh();
            return true;
        }

        public bool Relocate(bool handsBusy) { return State == WallModeState.Active && RequestEnter(true, handsBusy); }

        void Refresh()
        {
            SeatedUser user = _user();
            IWallGeometry walls = _walls();
            _candidates = WallPlacementEvaluator.FindCandidates(_room(), walls, user, settings, Layout);
            _index = 0;
            _hasLastValid = false;
            if (_candidates.Count > 0) { Show(_candidates[0]); return; }
            if (walls == null || walls.WallAvailability != RoomAvailability.Available)
            {
                WallCandidate m = WallPlacementEvaluator.Manual(user, 65f, 1.5f, 0.15f, Layout, settings);
                if (!m.valid) m = WallPlacementEvaluator.Manual(user, -65f, 1.5f, 0.15f, Layout, settings);
                Show(m);
                Status = (walls == null ? "No room scan" : SpatialGeometry.ReasonText(walls.WallAvailability)) + ". " + m.reason;
                Raise();
                return;
            }
            HasShown = false;
            Say(Layout == WallLayoutKind.Full ? "No wall spot fits the full habitat: try Compact, or point at a wall" : "No wall spot fits: point at a wall to see why");
        }

        WallCandidate _lastValid;
        bool _hasLastValid;

        void Show(WallCandidate c)
        {
            Shown = c;
            HasShown = true;
            if (c.valid) { _lastValid = c; _hasLastValid = true; }
            Status = c.valid ? c.reason : WallPlacementEvaluator.Words(c.problem) + ": " + c.reason;
            Raise();
        }

        public void TryAnother()
        {
            if (State != WallModeState.Previewing && State != WallModeState.Relocating) return;
            if (_candidates.Count == 0) { Refresh(); return; }
            _index = (_index + 1) % _candidates.Count;
            Show(_candidates[_index]);
        }

        public void ToggleLayout()
        {
            if (State != WallModeState.Previewing && State != WallModeState.Relocating) return;
            Layout = Layout == WallLayoutKind.Full ? WallLayoutKind.Compact : WallLayoutKind.Full;
            Refresh();
        }

        /// <summary>Point with a hand ray: the ghost jumps to the spot under the ray (red with a reason when it does not fit).</summary>
        public bool Aim(Vector3 origin, Vector3 dir)
        {
            if (State != WallModeState.Previewing && State != WallModeState.Relocating) return false;
            WallCandidate c;
            if (!WallPlacementEvaluator.FromRay(_room(), _walls(), origin, dir, Layout, _user(), settings, out c)) return false;
            Show(c);
            return true;
        }

        /// <summary>
        /// A drag ended (the pinch opened). On a spot that fits nothing changes. On one that does not, the ghost goes to the
        /// nearest spot on the same wall that fits (within <see cref="WallPlacementSettings.snapRadius"/>), else back to the
        /// last spot that fitted, and the status says which. Nothing is placed: Place is still needed.
        /// </summary>
        public bool ReleaseAim()
        {
            if ((State != WallModeState.Previewing && State != WallModeState.Relocating) || !HasShown || Shown.valid) return false;
            string why = WallPlacementEvaluator.Words(Shown.problem);
            WallCandidate near;
            if (Shown.wallIndex >= 0 && WallPlacementEvaluator.NearestValid(_room(), _walls(), Shown.wallIndex, Shown.u, Shown.centerY, Layout, _user(), settings, out near))
            {
                Show(near);
                Status = why + " there: moved to the nearest spot that fits";
                Raise();
                return true;
            }
            if (_hasLastValid && _lastValid.layout == Layout)
            {
                Show(_lastValid);
                Status = why + " there: back to the last spot that fits";
                Raise();
                return true;
            }
            return false;
        }

        /// <summary>Slide the shown spot along its wall / up and down (metres).</summary>
        public void Nudge(float du, float dy)
        {
            if ((State != WallModeState.Previewing && State != WallModeState.Relocating) || !HasShown || Shown.wallIndex < 0) return;
            Show(WallPlacementEvaluator.Evaluate(_room(), _walls(), Shown.wallIndex, Shown.u + du, Shown.centerY + dy, Layout, _user(), settings));
        }

        /// <summary>The explicit confirmation. Refused on an invalid spot or while a hand holds something.</summary>
        public bool PlaceHere(bool handsBusy)
        {
            if (State != WallModeState.Previewing && State != WallModeState.Relocating) return false;
            if (handsBusy) { Say("Put down what you are holding first"); return false; }
            if (!HasShown || !Shown.valid) { Say(HasShown ? "Can't place here: " + Shown.reason : "Choose a spot first"); return false; }
            Placed = Shown;
            HasPlaced = true;
            Suspended = false;
            State = WallModeState.Active;
            HasShown = false;
            Status = Placed.roomChecked ? "Habitat placed" : "Habitat placed (room not checked)";
            var h = PlacedChanged; if (h != null) h();
            Raise();
            return true;
        }

        /// <summary>Leave the preview. Entering: back to the previous mode. Relocating: the habitat stays where it was.</summary>
        public void Cancel()
        {
            if (State == WallModeState.Previewing) { State = WallModeState.Off; HasShown = false; Say("Wall mode cancelled"); return; }
            if (State == WallModeState.Relocating) { State = WallModeState.Active; HasShown = false; Say("Habitat stays where it was"); }
        }

        public void Exit()
        {
            State = WallModeState.Off;
            HasShown = false;
            Suspended = false;
            Say("");
        }

        /// <summary>Restore a saved placement (start-up, re-entry). Re-runs every check; on failure the caller previews instead.</summary>
        public bool TryRestore(SavedWallPlacement saved, out string reason)
        {
            reason = null;
            if (!flags.wallHabitatEnabled) { reason = "Wall mode is off"; return false; }
            WallCandidate c;
            if (!WallPlacementCodec.TryRestore(saved, _room(), _walls(), _user(), settings, out c, out reason)) return false;
            Layout = c.layout;
            Placed = c;
            HasPlaced = true;
            Suspended = false;
            State = WallModeState.Active;
            Status = "Habitat restored";
            var h = PlacedChanged; if (h != null) h();
            Raise();
            return true;
        }

        /// <summary>The room scan changed (or a recenter moved the anchors). Re-check the placed spot without moving it.</summary>
        public void OnRoomChanged()
        {
            if (State != WallModeState.Active || !HasPlaced || !Placed.roomChecked) return;
            IWallGeometry walls = _walls();
            bool ok = false;
            if (walls != null && walls.WallAvailability == RoomAvailability.Available)
                for (int i = 0; i < walls.WallCount; i++)
                {
                    if (walls.GetWall(i).id != Placed.wallId) continue;
                    WallCandidate c = WallPlacementEvaluator.Evaluate(_room(), walls, i, Placed.u, Placed.centerY, Placed.layout, _user(), settings);
                    ok = c.valid || c.problem == WallProblem.TooCloseToUser || c.problem == WallProblem.OverMonitor || c.problem == WallProblem.TooFar;   // user moved: not a room problem
                    break;
                }
            bool was = Suspended;
            Suspended = !ok;
            if (Suspended != was) Say(Suspended ? "The room scan changed: the habitat is paused. Re-place it when ready." : "Room scan confirmed");
        }

        void Say(string s) { Status = s; Raise(); }
        void Raise() { var h = Changed; if (h != null) h(); }
    }
}
