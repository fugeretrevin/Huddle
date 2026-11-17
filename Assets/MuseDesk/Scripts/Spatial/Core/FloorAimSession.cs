using System;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>Tunable numbers of pointing at the floor. Distances in metres, times in seconds.
    /// Pinch timing (debounce, release, tracking grace) is in <see cref="HandOwnershipSettings"/>.</summary>
    [Serializable]
    public sealed class FloorAimSettings
    {
        [Header("Grab")]
        [Tooltip("A pinch grabs the footprint when the aim point is on it or this far outside it. Generous: the hand jitters by centimetres at 2 to 3 m.")]
        public float grabMargin = 0.18f;
        [Tooltip("The aim point may wander this far on the floor while the pinch is pressed. Further, and the pinch is a drag.")]
        public float holdTolerance = 0.10f;

        [Header("Retired in rc6 (kept so older builders compile; ignored)")]
        [Tooltip("RETIRED: letting go never places the office now. Release leaves the preview; the Place here key moves it.")]
        public bool releaseToPlace = false;
        [Tooltip("RETIRED: see releaseToPlace.")]
        public float placeMinSeconds = 0.25f;

        [Header("Smoothing of the aim point")]
        [Tooltip("The point does not move at all for changes smaller than this.")]
        public float deadZone = 0.02f;
        [Tooltip("Time constant for small movements (jitter).")]
        public float slowTau = 0.20f;
        [Tooltip("Time constant for large movements (a deliberate sweep).")]
        public float fastTau = 0.05f;
        [Tooltip("A change this large, beyond the dead zone, counts as a deliberate sweep.")]
        public float fastDistance = 0.35f;
    }

    /// <summary>One hand for one frame: is it tracked, where its pointer ray is, is it pinching, and what
    /// its Interaction SDK interactors are doing (read by HandInteractionProbe; all false in a plain sample).</summary>
    public struct AimHandSample
    {
        public bool tracked;
        public Vector3 origin;
        public Vector3 direction;
        public bool pinching;
        /// <summary>A grab interactor of this hand has something selected (a paper, a stamp, a card, a grab bar).</summary>
        public bool holding;
        /// <summary>A grab interactor of this hand (near or distance) is hovering something.</summary>
        public bool nearHover;
        /// <summary>A poke of this hand, or its ray, is hovering or selecting a UI control.</summary>
        public bool uiHover;

        public static AimHandSample None { get { return default(AimHandSample); } }

        public static AimHandSample Ray(Vector3 origin, Vector3 direction, bool pinching)
        {
            AimHandSample s = default(AimHandSample);
            s.tracked = true; s.origin = origin; s.direction = direction; s.pinching = pinching;
            return s;
        }

        /// <summary>This sample with what the hand's interactors are doing.</summary>
        public AimHandSample With(bool holding, bool nearHover, bool uiHover)
        {
            AimHandSample s = this;
            s.holding = holding; s.nearHover = nearHover; s.uiHover = uiHover;
            return s;
        }
    }

    /// <summary>Plain geometry of aiming at a floor plane. No scene access, no allocation.</summary>
    public static class FloorAimMath
    {
        /// <summary>A ray that points up by more than this (the y of its unit direction) aims at nothing.</summary>
        public const float MaxUpward = 0.10f;
        /// <summary>A ray flatter than this never reaches the floor in the room: it aims "as far as allowed".</summary>
        public const float MinDownward = 0.03f;
        const float Far = 50f;

        /// <summary>
        /// Where a pointer ray meets the horizontal plane at <paramref name="floorY"/>. A nearly level
        /// ray gives a point far away in its direction (the caller clamps it to the allowed
        /// distance); a ray that points up, or starts at or under the floor, gives nothing.
        /// </summary>
        public static bool RayToFloor(Vector3 origin, Vector3 direction, float floorY, out Vector3 point)
        {
            point = new Vector3(origin.x, floorY, origin.z);
            float len = direction.magnitude;
            if (len < 1e-5f) return false;
            Vector3 d = direction / len;
            if (origin.y <= floorY + 0.05f) return false;
            if (d.y > MaxUpward) return false;
            if (d.y < -MinDownward)
            {
                float t = (floorY - origin.y) / d.y;
                if (t > Far) t = Far;
                point = origin + d * t;
            }
            else
            {
                Vector3 flat = new Vector3(d.x, 0f, d.z);
                if (flat.sqrMagnitude < 1e-6f) return false;
                point = origin + flat.normalized * Far;
            }
            point.y = floorY;
            return true;
        }

        /// <summary>Keep a floor point between <paramref name="min"/> and <paramref name="max"/> metres
        /// (horizontally) from the point under <paramref name="head"/>. The height is left alone.</summary>
        public static Vector3 ClampToRing(Vector3 point, Vector3 head, float min, float max)
        {
            float dx = point.x - head.x, dz = point.z - head.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 1e-4f) { dx = 1f; dz = 0f; len = 1f; }
            float want = Mathf.Clamp(len, min, Mathf.Max(min, max));
            if (Mathf.Abs(want - len) < 1e-6f) return point;
            float k = want / len;
            return new Vector3(head.x + dx * k, point.y, head.z + dz * k);
        }
    }

    /// <summary>
    /// Low-pass filter with a dead zone for a point on the floor. Changes smaller than the dead
    /// zone are ignored outright (a resting hand gives a resting point); larger ones are followed
    /// with a time constant that shortens as the change grows, so jitter is smoothed and a sweep
    /// is not laggy. The output stops one dead zone short of the input.
    /// </summary>
    public struct AimFilter
    {
        Vector3 _value;
        bool _has;

        public bool HasValue { get { return _has; } }
        public Vector3 Value { get { return _value; } }

        public void Clear() { _has = false; }

        public Vector3 Step(Vector3 raw, float dt, FloorAimSettings s)
        {
            if (!_has) { _has = true; _value = raw; return _value; }
            Vector3 d = raw - _value;
            float dist = d.magnitude;
            float dead = Mathf.Max(0f, s.deadZone);
            if (dist <= dead || dist < 1e-6f) return _value;
            float beyond = dist - dead;
            float tau = Mathf.Lerp(s.slowTau, s.fastTau, Mathf.Clamp01(beyond / Mathf.Max(0.01f, s.fastDistance)));
            float a = tau <= 1e-4f ? 1f : 1f - Mathf.Exp(-Mathf.Max(0f, dt) / tau);
            _value += d * (beyond / dist * a);
            return _value;
        }
    }

    public enum FloorAimState
    {
        /// <summary>No floor preview is open: nothing is read, nothing is shown.</summary>
        Off = 0,
        /// <summary>The preview is open and the hands may point at it.</summary>
        Aiming = 1,
        /// <summary>A pinch armed on the footprint and has not moved yet.</summary>
        Pressing = 2,
        /// <summary>The footprint follows the hand.</summary>
        Dragging = 3,
    }

    /// <summary>
    /// Pointing at the floor preview with either hand, free of scene access so it can be tested.
    /// Fed one <see cref="AimHandSample"/> per hand per frame; it edits only the PREVIEW of the
    /// <see cref="SpatialDirectorCore"/> (BeginAimDrag / AimDragTo / EndAimDrag / CancelAimDrag). It
    /// never asks for the office to move: since rc6 only the Place here key (or the console's Confirm)
    /// does, through Confirm and its swept route.
    ///
    ///  - A pinch arms only through <see cref="HandOwnershipArbiter"/> (claim Placement): fresh, held
    ///    minPinchSeconds, nothing held in either hand, and this hand's grab, poke and UI ray clear of
    ///    everything for nearClearSeconds. It must also BEGIN with the aim point on the footprint (plus
    ///    a margin) and still be on it when it arms. A pinch that was already closed, began elsewhere,
    ///    or belonged to a paper, a stamp, a card or a key is ignored until the hand opens again.
    ///  - Move the hand (more than holdTolerance) and the footprint follows (Dragging).
    ///  - Let go (the pinch SEEN to open on a tracked hand): the footprint stays where it is
    ///    (EndAimDrag). Nothing moves; the Place here key appears when the spot and its route are clear.
    ///  - Tracking lost longer than the grace time, or back with the pinch open, or anything picked up:
    ///    the drag is called off (CancelAimDrag, the preview goes back to where it was). Never a release.
    ///  - Holding a pinch still does nothing (the pinch-and-hold confirm of rc-feedback-1 is gone: an
    ///    accidental gesture must never move the office).
    ///  - While the fingers are opening the footprint is frozen, so the twitch of letting go does not move it.
    /// </summary>
    public sealed class FloorAimSession
    {
        struct Hand
        {
            public AimFilter filter;
            public bool tracked, hasCursor, over, overAtStart, rawPinch;
            public Vector3 cursor, origin, direction;
        }

        readonly SpatialDirectorCore _core;
        readonly HandOwnershipArbiter _arbiter;
        Hand _left, _right;
        int _active = -1;
        float _heldTime;
        Vector3 _pressCursor, _grabOffset;

        public FloorAimSettings settings = new FloorAimSettings();

        public FloorAimState State { get; private set; }
        /// <summary>0 = left, 1 = right, -1 = neither (the hand whose pinch is in progress).</summary>
        public int ActiveHand { get { return _active; } }
        /// <summary>Who owns which hand, and the pinch timing (settings live on it).</summary>
        public HandOwnershipArbiter Arbiter => _arbiter;
        /// <summary>Always 0 since rc6 (no pinch-and-hold confirm). Kept for drivers.</summary>
        public float HoldProgress => 0f;
        /// <summary>How many pinches took the footprint (Pressing).</summary>
        public int Grabs { get; private set; }
        /// <summary>How many drags ended by letting go (the footprint left where it was let go; nothing moved).</summary>
        public int Drops { get; private set; }
        /// <summary>How many presses or drags were called off (tracking lost, something held, preempted).</summary>
        public int Cancels { get; private set; }
        /// <summary>Seconds the current (or last) pinch on the footprint has been held.</summary>
        public float HeldSeconds => _heldTime;

        public FloorAimSession(SpatialDirectorCore core, HandClaimRegistry registry = null)
        {
            _core = core;
            _arbiter = new HandOwnershipArbiter(registry);
        }

        public bool HasCursor(int hand) { return hand == 0 ? _left.hasCursor : _right.hasCursor; }
        public Vector3 Cursor(int hand) { return hand == 0 ? _left.cursor : _right.cursor; }
        public Vector3 RayOrigin(int hand) { return hand == 0 ? _left.origin : _right.origin; }
        public Vector3 RayDirection(int hand) { return hand == 0 ? _left.direction : _right.direction; }
        /// <summary>True while that hand's aim point is on the footprint (plus the grab margin).</summary>
        public bool OverFootprint(int hand) { return hand == 0 ? _left.over : _right.over; }
        public bool AnyOverFootprint { get { return _left.over || _right.over; } }

        /// <summary>Forget everything. A drag in progress is called off (the preview goes back to where it was).</summary>
        public void Reset()
        {
            if (_core != null && _core.AimDragging) _core.CancelAimDrag();
            State = FloorAimState.Off;
            _active = -1;
            _left = default(Hand);
            _right = default(Hand);
            _arbiter.Reset();
        }

        public void Tick(float dt, AimHandSample left, AimHandSample right)
        {
            if (_core == null) return;
            if (!_core.CanAim)
            {
                if (State != FloorAimState.Off) Reset();
                return;
            }
            if (State == FloorAimState.Off)
            {
                State = FloorAimState.Aiming;
                _active = -1;
                _left = default(Hand);
                _right = default(Hand);
                _arbiter.Reset();                  // a pinch that is already closed must be opened before it can count
            }
            if (dt < 0f) dt = 0f;

            float floorY;
            bool haveFloor = _core.TryGetAimFloorHeight(out floorY);
            Read(ref _left, left, dt, haveFloor, floorY);
            Read(ref _right, right, dt, haveFloor, floorY);
            _arbiter.Tick(dt, left, right);
            if (_arbiter.Began(0)) _left.overAtStart = _left.over;
            if (_arbiter.Began(1)) _right.overAtStart = _right.over;

            switch (State)
            {
                case FloorAimState.Aiming:
                {
                    int pick = Wants(1, _right) ? 1 : Wants(0, _left) ? 0 : -1;
                    if (pick >= 0 && _arbiter.TryClaim(pick, HandClaim.Placement)) Press(pick, pick == 0 ? _left.cursor : _right.cursor);
                    break;
                }

                case FloorAimState.Pressing:
                {
                    Hand h = _active == 0 ? _left : _right;
                    HandGateSignal sig = _arbiter.Signal(_active);
                    if (sig == HandGateSignal.Cancelled) { Cancels++; ToAiming(); break; }
                    if (sig == HandGateSignal.Released) { ToAiming(); break; }            // a tap: nothing
                    if (sig != HandGateSignal.Holding || !h.hasCursor) break;             // frozen, or the ray is off the floor
                    if (h.rawPinch) _heldTime += dt;
                    Vector3 moved = h.cursor - _pressCursor; moved.y = 0f;
                    if (moved.magnitude > settings.holdTolerance)
                    {
                        if (_core.BeginAimDrag()) { State = FloorAimState.Dragging; _core.AimDragTo(h.cursor + _grabOffset); }
                        else { _arbiter.Relinquish(_active); ToAiming(); }
                    }
                    break;
                }

                case FloorAimState.Dragging:
                {
                    if (!_core.AimDragging) { _arbiter.Relinquish(_active); ToAiming(); break; }     // ended by a key (Next, Cancel, ...)
                    Hand h = _active == 0 ? _left : _right;
                    HandGateSignal sig = _arbiter.Signal(_active);
                    if (sig == HandGateSignal.Cancelled)
                    {
                        // Tracking lost, something picked up, preempted: call the drag off. Never a release.
                        _core.CancelAimDrag();
                        Cancels++;
                        ToAiming();
                        break;
                    }
                    if (sig == HandGateSignal.Released)
                    {
                        // Let go: the footprint stays where it is. Nothing moves; Place here decides.
                        _core.EndAimDrag();
                        Drops++;
                        ToAiming();
                        break;
                    }
                    if (sig != HandGateSignal.Holding || !h.hasCursor) break;   // frozen, or the ray is off the floor
                    // While the fingers are opening (inside the release debounce) the footprint is frozen.
                    if (!h.rawPinch) break;
                    _heldTime += dt;
                    _core.AimDragTo(h.cursor + _grabOffset);
                    break;
                }
            }
        }

        bool Wants(int hand, Hand h)
        {
            return _arbiter.Signal(hand) == HandGateSignal.Ready && h.overAtStart && h.over && h.hasCursor;
        }

        void Press(int hand, Vector3 cursor)
        {
            _active = hand;
            State = FloorAimState.Pressing;
            _pressCursor = cursor;
            _heldTime = 0f;
            Grabs++;
            Vector3 off = _core.PreviewPose.position - cursor;
            off.y = 0f;
            _grabOffset = off;                   // the footprint does not jump to the aim point when grabbed
        }

        void ToAiming()
        {
            State = FloorAimState.Aiming;
            _active = -1;
        }

        /// <summary>Take one hand's sample: the aim point on the floor and whether it is on the footprint.</summary>
        void Read(ref Hand h, AimHandSample s, float dt, bool haveFloor, float floorY)
        {
            h.tracked = s.tracked;
            h.origin = s.origin;
            h.direction = s.direction;
            h.rawPinch = s.tracked && s.pinching;

            Vector3 raw;
            if (s.tracked && haveFloor && FloorAimMath.RayToFloor(s.origin, s.direction, floorY, out raw))
            {
                raw = _core.ClampAimPoint(raw);
                h.cursor = h.filter.Step(raw, dt, settings);
                h.hasCursor = true;
            }
            else
            {
                h.hasCursor = false;
                h.filter.Clear();
            }
            h.over = h.hasCursor && _core.FootprintContains(h.cursor, settings.grabMargin);
        }
    }
}
