using System;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>Tunable numbers of pointing at the floor. Distances in metres, times in seconds.</summary>
    [Serializable]
    public sealed class FloorAimSettings
    {
        [Header("Grab")]
        [Tooltip("A pinch grabs the footprint when the aim point is on it or this far outside it. Generous: the hand jitters by centimetres at 2 to 3 m.")]
        public float grabMargin = 0.18f;
        [Tooltip("A pinch that ended this recently is still held (hand tracking drops a pinch for a frame or two).")]
        public float releaseDebounce = 0.12f;
        [Tooltip("A drag or a hold survives this long without the hand being tracked.")]
        public float lostGraceSeconds = 0.4f;
        [Tooltip("After a drop, a confirm or a tap, a new pinch is ignored for this long.")]
        public float regrabCooldown = 0.45f;

        [Header("Drag and let go to place")]
        [Tooltip("Letting go of a drag on a spot shown ready places the office there (through the same Confirm as the key).")]
        public bool releaseToPlace = true;
        [Tooltip("...only when the pinch was held at least this long (seconds) from grab to release. The footprint must " +
                 "also have been dragged at least SpatialDirectorCore.aimPlaceMinDrag (5 cm), and a pinch only becomes a drag " +
                 "after holdTolerance of movement, so a tap or a twitch never places it.")]
        public float placeMinSeconds = 0.25f;

        [Header("Pinch and hold to confirm (still works: hold still on the footprint)")]
        [Tooltip("The aim point may wander this far on the floor during a hold. Further, and the pinch is a drag.")]
        public float holdTolerance = 0.10f;
        [Tooltip("Stillness required before the ring starts to fill (so the start of a drag does not flash it).")]
        public float holdArmSeconds = 0.25f;
        [Tooltip("Time the ring takes to fill. Confirm happens when it is full.")]
        public float holdSeconds = 0.8f;

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

    /// <summary>One hand for one frame: is it tracked, where its pointer ray is, is it pinching.</summary>
    public struct AimHandSample
    {
        public bool tracked;
        public Vector3 origin;
        public Vector3 direction;
        public bool pinching;

        public static AimHandSample None { get { return default(AimHandSample); } }

        public static AimHandSample Ray(Vector3 origin, Vector3 direction, bool pinching)
        {
            AimHandSample s;
            s.tracked = true; s.origin = origin; s.direction = direction; s.pinching = pinching;
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
        /// <summary>A pinch began on the footprint and has not moved yet: the hold ring may be filling.</summary>
        Pressing = 2,
        /// <summary>The footprint follows the hand.</summary>
        Dragging = 3,
    }

    /// <summary>
    /// Pointing at the floor preview with either hand, free of scene access so it can be tested.
    /// Fed one <see cref="AimHandSample"/> per hand per frame; it edits only the PREVIEW of the
    /// <see cref="SpatialDirectorCore"/> (BeginAimDrag / AimDragTo / ReleaseAimDrag / EndAimDrag)
    /// and asks for the office to be placed only through the director's Confirm, the same Confirm
    /// the key calls. It never touches the office.
    ///
    ///  - A pinch counts only when it BEGINS while that hand's aim point is on the footprint
    ///    (plus a margin). A pinch that was already closed when the preview opened, or that
    ///    began anywhere else, is ignored until it is released.
    ///  - Move the hand (more than holdTolerance) and the footprint follows (Dragging).
    ///  - Let go on a spot shown ready and the office is placed there: ReleaseAimDrag(true), which
    ///    drops the footprint and calls Confirm. Only when the pinch lasted placeMinSeconds and the
    ///    footprint was dragged at least the director's aimPlaceMinDrag; a tap, a twitch, or a hand
    ///    that was lost drops it without placing. Let go over a spot that is not clear and it is
    ///    dropped on the last ready spot, with the reason; nothing moves.
    ///  - Keep the pinch still on the footprint and, after a short pause, a ring fills; when it is
    ///    full the spot is confirmed (the alternative to dragging). Any movement beyond the
    ///    tolerance turns the pinch into a drag, and a drag never fills the ring.
    ///  - While the fingers are opening the footprint is frozen, so the twitch of letting go
    ///    does not move it.
    /// </summary>
    public sealed class FloorAimSession
    {
        struct Hand
        {
            public AimFilter filter;
            public bool tracked, hasCursor, over;
            public bool rawPinch, down, wasDown;
            public float offTime;
            public Vector3 cursor, origin, direction;
        }

        readonly SpatialDirectorCore _core;
        Hand _left, _right;
        int _active = -1;
        float _cooldown, _pressTime, _heldTime, _lost;
        bool _trackingLost;                 // the dragging hand lost tracking since its pinch was last seen closed
        Vector3 _pressCursor, _grabOffset;

        public FloorAimSettings settings = new FloorAimSettings();

        public FloorAimState State { get; private set; }
        /// <summary>0 = left, 1 = right, -1 = neither (the hand whose pinch is in progress).</summary>
        public int ActiveHand { get { return _active; } }
        /// <summary>0..1 while a pinch-and-hold is filling the ring; 0 otherwise.</summary>
        public float HoldProgress { get; private set; }
        /// <summary>How many times a full hold called Confirm, and how many of those were accepted.</summary>
        public int HoldConfirmAttempts { get; private set; }
        public int HoldConfirms { get; private set; }
        /// <summary>Every time a drag ended (released or the hand lost), placed or not.</summary>
        public int Drops { get; private set; }
        /// <summary>How many releases asked for the office to be placed, and how many moved it.</summary>
        public int ReleasePlaceAttempts { get; private set; }
        public int ReleasePlaces { get; private set; }
        /// <summary>Seconds the current (or last) pinch on the footprint has been held.</summary>
        public float HeldSeconds => _heldTime;

        public FloorAimSession(SpatialDirectorCore core) { _core = core; }

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
            HoldProgress = 0f;
            _cooldown = 0f;
            _left = default(Hand);
            _right = default(Hand);
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
                HoldProgress = 0f;
                _cooldown = 0f;
                _left = default(Hand);
                _right = default(Hand);
                // A pinch that is already closed must be opened before it can count.
                _left.wasDown = _left.down = left.tracked && left.pinching;
                _right.wasDown = _right.down = right.tracked && right.pinching;
            }
            if (dt < 0f) dt = 0f;

            float floorY;
            bool haveFloor = _core.TryGetAimFloorHeight(out floorY);
            bool leftPressed = Read(ref _left, left, dt, haveFloor, floorY);
            bool rightPressed = Read(ref _right, right, dt, haveFloor, floorY);
            if (_cooldown > 0f) _cooldown -= dt;

            switch (State)
            {
                case FloorAimState.Aiming:
                    if (_cooldown > 0f) break;
                    if (rightPressed && _right.over) Press(1, _right.cursor);
                    else if (leftPressed && _left.over) Press(0, _left.cursor);
                    break;

                case FloorAimState.Pressing:
                {
                    Hand h = _active == 0 ? _left : _right;
                    if (!h.down) { ToAiming(settings.regrabCooldown * 0.5f); break; }           // a tap: nothing
                    if (!h.hasCursor)
                    {
                        _lost += dt;
                        if (_lost > settings.lostGraceSeconds) ToAiming(settings.regrabCooldown);
                        break;
                    }
                    _lost = 0f;
                    if (h.rawPinch) _heldTime += dt;
                    Vector3 moved = h.cursor - _pressCursor; moved.y = 0f;
                    if (moved.magnitude > settings.holdTolerance)
                    {
                        HoldProgress = 0f;
                        if (_core.BeginAimDrag()) { State = FloorAimState.Dragging; _core.AimDragTo(h.cursor + _grabOffset); }
                        else ToAiming(settings.regrabCooldown);
                        break;
                    }
                    if (!h.rawPinch) break;                                 // fingers opening: do not count
                    _pressTime += dt;
                    HoldProgress = Mathf.Clamp01((_pressTime - settings.holdArmSeconds) / Mathf.Max(0.05f, settings.holdSeconds));
                    if (HoldProgress >= 1f)
                    {
                        HoldConfirmAttempts++;
                        if (_core.Confirm()) HoldConfirms++;
                        ToAiming(settings.regrabCooldown);                  // refused: the reason is on the status line
                    }
                    break;
                }

                case FloorAimState.Dragging:
                {
                    if (!_core.AimDragging) { ToAiming(settings.regrabCooldown); break; }     // ended by a key (Next, Cancel, ...)
                    Hand h = _active == 0 ? _left : _right;
                    // Tracking lost: the pinch reads as open, but nobody let go. Checked BEFORE the
                    // release, so a lost hand only ever drops the footprint (after the grace time).
                    if (!h.tracked)
                    {
                        _trackingLost = true;
                        _lost += dt;
                        if (_lost > settings.lostGraceSeconds) Drop();      // hand lost: drop, never place
                        break;
                    }
                    if (h.rawPinch) _trackingLost = false;                  // the pinch is seen closed again
                    // Let go: place it if this was a real drag, and the opening was seen (not a tracking gap).
                    if (!h.down) { Release(!_trackingLost); break; }
                    if (!h.hasCursor)
                    {
                        _lost += dt;
                        if (_lost > settings.lostGraceSeconds) Drop();      // ray off the floor: drop, never place
                        break;
                    }
                    _lost = 0f;
                    if (h.rawPinch) _heldTime += dt;
                    // While the fingers are opening the footprint is frozen where it was.
                    if (h.rawPinch) _core.AimDragTo(h.cursor + _grabOffset);
                    break;
                }
            }
        }

        void Press(int hand, Vector3 cursor)
        {
            _active = hand;
            State = FloorAimState.Pressing;
            _pressCursor = cursor;
            _pressTime = 0f;
            _heldTime = 0f;
            _trackingLost = false;
            _lost = 0f;
            HoldProgress = 0f;
            Vector3 off = _core.PreviewPose.position - cursor;
            off.y = 0f;
            _grabOffset = off;                   // the footprint does not jump to the aim point when grabbed
        }

        void Drop()
        {
            _core.EndAimDrag();
            Drops++;
            ToAiming(settings.regrabCooldown);
        }

        /// <summary>The pinch opened during a drag. A deliberate drag (held long enough) asks the director
        /// to place the office where the footprint is; the director decides (spot ready, dragged far
        /// enough, Confirm's swept route) and says why when it does not.</summary>
        void Release(bool seen)
        {
            bool place = seen && settings.releaseToPlace && _heldTime >= settings.placeMinSeconds;
            if (place) ReleasePlaceAttempts++;
            if (_core.ReleaseAimDrag(place)) ReleasePlaces++;
            Drops++;
            ToAiming(settings.regrabCooldown);
        }

        void ToAiming(float cooldown)
        {
            State = FloorAimState.Aiming;
            _active = -1;
            HoldProgress = 0f;
            _cooldown = cooldown;
            _lost = 0f;
        }

        /// <summary>Take one hand's sample; returns true on the frame its (debounced) pinch begins.</summary>
        bool Read(ref Hand h, AimHandSample s, float dt, bool haveFloor, float floorY)
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

            h.wasDown = h.down;
            if (h.rawPinch) { h.down = true; h.offTime = 0f; }
            else if (h.down)
            {
                h.offTime += dt;
                if (h.offTime >= settings.releaseDebounce) h.down = false;
            }
            return h.down && !h.wasDown;
        }
    }
}
