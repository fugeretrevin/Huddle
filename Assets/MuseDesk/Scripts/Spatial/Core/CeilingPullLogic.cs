using UnityEngine;

namespace MuseDesk.Spatial
{
    // Pure logic of "reach up and pull it down" (CeilingPullGesture draws and reads the hands; this
    // decides). It lives beside the other scene-free logic so EditMode tests can reach it. No scene
    // access, no allocation. It never moves the pod: the gesture calls the director on Fired.

    public enum CeilingPullState { Idle = 0, Armed = 1, Pulling = 2, Fired = 3, Cancelled = 4 }

    /// <summary>Down: pull the docked pod down to the user. Up: push the engaged pod back up.</summary>
    public enum CeilingPullDirection { Down = 0, Up = 1 }

    public enum CeilingPullCancel { None = 0, Released = 1, Sideways = 2, WrongWay = 3, Timeout = 4, Blocked = 5, Lost = 6, TargetLost = 7, Held = 8 }

    [System.Serializable]
    public class CeilingPullSettings
    {
        [Tooltip("A ray (hand or gaze) that passes this close to the mast counts as pointing at the pod (metres).")]
        public float targetRadius = 0.25f;
        [Tooltip("Radius of the sphere around the pod's centre that also counts (metres, office scale 1).")]
        public float podRadius = 0.30f;
        [Tooltip("How far the pinch must travel (down to pull, up to push), metres.")]
        public float distance = 0.12f;
        [Tooltip("The distance must be reached within this long of the pinch starting (seconds).")]
        public float windowSeconds = 1.2f;
        [Tooltip("Sideways drift from where the pinch started that cancels the pull (metres).")]
        public float maxSideways = 0.20f;
        [Tooltip("Travel the wrong way that cancels (metres).")]
        public float wrongWay = 0.08f;
        [Tooltip("Travel before the state reads Pulling rather than Armed (metres).")]
        public float armTravel = 0.02f;
        [Tooltip("At release the pull must still be at least this share of the distance (the hand did not go back).")]
        [Range(0.5f, 1f)] public float releaseShare = 0.75f;
        [Tooltip("Pause after a pull fires or is cancelled before another can start (seconds).")]
        public float cooldownSeconds = 0.8f;
        [Tooltip("While a pull is in progress the pod must stay under the hand ray or the gaze. Lost for longer than this " +
                 "(seconds) cancels it. The ray swings as the hand pulls down, so a short gap and a wider radius are allowed.")]
        public float targetLostSeconds = 0.25f;
        [Tooltip("During a pull the target radii are this many times the arming radii (hysteresis).")]
        public float targetKeepFactor = 1.5f;
    }

    public static class CeilingPullMath
    {
        const float RayFar = 30f;

        /// <summary>Shortest distance between two segments (Ericson, Real-Time Collision Detection 5.1.9).</summary>
        public static float SegmentSegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            const float eps = 1e-8f;
            float s, t;
            if (a <= eps && e <= eps) return r.magnitude;
            if (a <= eps) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= eps) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > eps ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return ((p1 + d1 * s) - (p2 + d2 * t)).magnitude;
        }

        /// <summary>Distance from a ray (forward only, 30 m long) to a segment.</summary>
        public static float RayToSegment(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b)
        {
            if (direction.sqrMagnitude < 1e-10f) return float.MaxValue;
            return SegmentSegmentDistance(origin, origin + direction.normalized * RayFar, a, b);
        }

        /// <summary>
        /// Is this ray pointing at the pod? True when it passes within <paramref name="segmentRadius"/> of
        /// the mast segment, or within <paramref name="podRadius"/> of the pod's centre. Maths only: the
        /// pod visuals have no colliders.
        /// </summary>
        public static bool RayTargetsPod(Vector3 origin, Vector3 direction, Vector3 mastTop, Vector3 mastBottom,
                                         Vector3 podCentre, float segmentRadius, float podRadius)
        {
            if (RayToSegment(origin, direction, mastTop, mastBottom) <= segmentRadius) return true;
            return RayToSegment(origin, direction, podCentre, podCentre) <= podRadius;
        }
    }

    /// <summary>
    /// Idle -> Armed (a pinch STARTS while pointing at the pod) -> Pulling (the hand has travelled) ->
    /// Fired (released after the full distance within the window) or Cancelled (released early, drifted
    /// sideways, went the wrong way, timed out, was blocked, lost, or the pod left the hand ray and the
    /// gaze for longer than targetLostSeconds). Fired and Cancelled are shown for one Tick; the next Tick
    /// returns to Idle.
    ///
    /// Two ways to drive it:
    ///  - <see cref="Tick"/> with the raw pinch (it finds the rising edge itself; the EditMode tests of
    ///    the travel rules use this);
    ///  - <see cref="TryArm"/> + <see cref="TickOwned"/> with the signals of a <see cref="HandOwnershipArbiter"/>
    ///    (CeilingPullGesture since rc6): the arbiter decides when a pinch is fresh, debounced and clear of
    ///    any held or hovered object, when it is released, and when a lost hand cancels.
    /// </summary>
    public sealed class CeilingPullMachine
    {
        public CeilingPullSettings settings = new CeilingPullSettings();

        Vector3 _start;
        bool _prevPinch, _reached;
        float _cooldown, _last, _targetGone;

        public CeilingPullState State { get; private set; }
        public CeilingPullDirection Direction { get; private set; }
        /// <summary>0..1 share of the distance covered now (0 outside Armed and Pulling).</summary>
        public float Progress { get; private set; }
        public float Elapsed { get; private set; }
        public CeilingPullCancel LastCancel { get; private set; }
        public bool Active => State == CeilingPullState.Armed || State == CeilingPullState.Pulling;
        public int Fires { get; private set; }
        public int Cancels { get; private set; }
        /// <summary>True when a new pull may arm now (Idle, cooldown over).</summary>
        public bool CanArm => State == CeilingPullState.Idle && _cooldown <= 0f;

        public void Reset()
        {
            State = CeilingPullState.Idle; Progress = 0f; Elapsed = 0f; _reached = false; _last = 0f; _prevPinch = false; _targetGone = 0f;
        }

        /// <summary>One frame, raw pinch. <paramref name="targeted"/> arms on the frame the pinch starts, and while
        /// a pull is in progress its absence for targetLostSeconds cancels it; <paramref name="allowed"/> is read
        /// every frame (false cancels a gesture in progress).</summary>
        public CeilingPullState Tick(float dt, bool tracked, bool pinching, Vector3 hand, bool targeted, bool allowed, CeilingPullDirection direction)
        {
            Advance(dt);
            bool pinch = tracked && pinching;
            bool rising = pinch && !_prevPinch;
            _prevPinch = pinch;

            if (State == CeilingPullState.Idle)
            {
                if (rising && targeted && allowed) TryArm(hand, direction);
                return State;
            }

            // Armed or Pulling.
            if (!tracked) return Finish(false, CeilingPullCancel.Lost);
            if (!pinching) return Release();
            return Track(dt, hand, targeted, allowed);
        }

        /// <summary>Arm a pull from <paramref name="hand"/> (the caller has checked the target and claimed the
        /// hand). False while a pull runs or during the cooldown.</summary>
        public bool TryArm(Vector3 hand, CeilingPullDirection direction)
        {
            if (!CanArm) return false;
            State = CeilingPullState.Armed; Direction = direction; _start = hand;
            _reached = false; _last = 0f; Progress = 0f; Elapsed = 0f; _targetGone = 0f; LastCancel = CeilingPullCancel.None;
            return true;
        }

        /// <summary>
        /// One frame driven by the arbiter's signal for the hand that armed. Idle: only the cooldown runs
        /// (arm with <see cref="TryArm"/>). Armed / Pulling: Cancelled ends it with <paramref name="cancelReason"/>
        /// (never a fire); Frozen (tracking gap within grace) keeps it as it is while the window keeps running;
        /// Released fires when the full distance was reached and held; Holding tracks the travel.
        /// </summary>
        public CeilingPullState TickOwned(float dt, HandGateSignal signal, Vector3 hand, bool targetedNow, bool allowed,
                                          CeilingPullCancel cancelReason = CeilingPullCancel.Lost)
        {
            Advance(dt);
            if (!Active) return State;
            switch (signal)
            {
                case HandGateSignal.Cancelled:
                    return Finish(false, cancelReason == CeilingPullCancel.None ? CeilingPullCancel.Lost : cancelReason);
                case HandGateSignal.Released:
                    return Release();
                case HandGateSignal.Frozen:
                    Elapsed += dt;
                    if (!_reached && Elapsed > settings.windowSeconds) return Finish(false, CeilingPullCancel.Timeout);
                    return State;
                case HandGateSignal.Holding:
                    return Track(dt, hand, targetedNow, allowed);
                default:
                    // The arbiter no longer reports this hand as ours: treat as a cancel, never a fire.
                    return Finish(false, CeilingPullCancel.Lost);
            }
        }

        /// <summary>The gesture gave up the hand for its own reason (e.g. the mode changed): a cancel, never a fire.</summary>
        public CeilingPullState Abort(CeilingPullCancel why)
        {
            if (!Active) return State;
            return Finish(false, why);
        }

        void Advance(float dt)
        {
            if (State == CeilingPullState.Fired || State == CeilingPullState.Cancelled)
            {
                State = CeilingPullState.Idle; Progress = 0f; Elapsed = 0f;
            }
            if (_cooldown > 0f) _cooldown -= dt;
        }

        CeilingPullState Release()
        {
            return _reached && _last >= settings.releaseShare ? Finish(true, CeilingPullCancel.None)
                                                              : Finish(false, CeilingPullCancel.Released);
        }

        CeilingPullState Track(float dt, Vector3 hand, bool targeted, bool allowed)
        {
            if (!allowed) return Finish(false, CeilingPullCancel.Blocked);

            Elapsed += dt;
            // The pod must stay identified: under the hand ray or the gaze, with a short gap allowed.
            if (targeted) _targetGone = 0f;
            else
            {
                _targetGone += dt;
                if (_targetGone > settings.targetLostSeconds) return Finish(false, CeilingPullCancel.TargetLost);
            }

            float delta = Direction == CeilingPullDirection.Down ? _start.y - hand.y : hand.y - _start.y;
            float dx = hand.x - _start.x, dz = hand.z - _start.z;
            if (Mathf.Sqrt(dx * dx + dz * dz) > settings.maxSideways) return Finish(false, CeilingPullCancel.Sideways);
            if (delta < -settings.wrongWay) return Finish(false, CeilingPullCancel.WrongWay);

            _last = Mathf.Clamp01(delta / Mathf.Max(0.01f, settings.distance));
            Progress = _last;
            if (_last >= 1f) _reached = true;
            if (!_reached && Elapsed > settings.windowSeconds) return Finish(false, CeilingPullCancel.Timeout);
            State = delta >= settings.armTravel || _reached ? CeilingPullState.Pulling : CeilingPullState.Armed;
            return State;
        }

        CeilingPullState Finish(bool fired, CeilingPullCancel why)
        {
            State = fired ? CeilingPullState.Fired : CeilingPullState.Cancelled;
            LastCancel = why;
            if (fired) Fires++; else Cancels++;
            _cooldown = settings.cooldownSeconds;
            _reached = false;
            _targetGone = 0f;
            return State;
        }
    }

    /// <summary>
    /// Both hands of the ceiling pull, with hand ownership (rc6). Pure: the gesture feeds it the hand
    /// samples (with what their interactors do), whether the identified target (the docked pod / its
    /// mast, or the engaged pod) is under each hand's ray or the gaze at the arming radius
    /// (<c>targeted</c>) and at the wider keep radius (<c>keep</c>), and whether a move may start.
    ///
    /// Arms only on the arbiter's Ready signal (fresh, debounced, nothing held, hand clear), only when the
    /// target was under the hand ray or the gaze when the pinch BEGAN and still is, and only claim Summon
    /// (a placement drag on the same hand wins). In progress, the target must stay under the ray or gaze
    /// (keep radius, targetLostSeconds gap), tracking loss within the grace time freezes it and beyond
    /// cancels it, anything picked up cancels it, and only a pinch seen to open fires it. Any end gives
    /// the hand back; the next pull needs a fresh pinch.
    /// </summary>
    public sealed class CeilingPullSession
    {
        readonly HandOwnershipArbiter _arbiter;
        readonly CeilingPullMachine[] _machines = { new CeilingPullMachine(), new CeilingPullMachine() };
        readonly bool[] _targetAtStart = new bool[2];
        readonly bool[] _heldAtTarget = new bool[2];
        readonly CeilingPullState[] _before = new CeilingPullState[2];

        public CeilingPullSettings settings = new CeilingPullSettings();

        public CeilingPullSession(HandClaimRegistry registry = null)
        {
            _arbiter = new HandOwnershipArbiter(registry);
        }

        public HandOwnershipArbiter Arbiter => _arbiter;
        public CeilingPullMachine Machine(int hand) { return _machines[hand & 1]; }
        /// <summary>The hand whose pull is in progress, or -1.</summary>
        public int ActiveHand => _machines[1].Active ? 1 : _machines[0].Active ? 0 : -1;
        /// <summary>True on the tick a pinch began at the target while something was held (say why nothing happens).</summary>
        public bool PinchedAtTargetWhileHeld(int hand) { return _heldAtTarget[hand & 1]; }
        /// <summary>The state the machine had before this tick (to see Fired / Cancelled edges).</summary>
        public CeilingPullState StateBefore(int hand) { return _before[hand & 1]; }

        public void Reset()
        {
            _machines[0].Reset(); _machines[1].Reset();
            _arbiter.Reset();
            _targetAtStart[0] = _targetAtStart[1] = false;
            _heldAtTarget[0] = _heldAtTarget[1] = false;
        }

        public void Tick(float dt, AimHandSample left, AimHandSample right,
                         bool leftTargeted, bool rightTargeted, bool leftKeep, bool rightKeep,
                         bool allowed, CeilingPullDirection direction)
        {
            _arbiter.Tick(dt, left, right);
            Hand(0, dt, left, leftTargeted, leftKeep || leftTargeted, allowed, direction);
            Hand(1, dt, right, rightTargeted, rightKeep || rightTargeted, allowed, direction);
        }

        void Hand(int i, float dt, AimHandSample s, bool targeted, bool keep, bool allowed, CeilingPullDirection dir)
        {
            CeilingPullMachine m = _machines[i];
            m.settings = settings;
            _before[i] = m.State;
            _heldAtTarget[i] = false;
            if (_arbiter.Began(i))
            {
                _targetAtStart[i] = targeted;
                _heldAtTarget[i] = targeted && _arbiter.AnyHeld;
            }
            if (!m.Active)
            {
                m.TickOwned(dt, HandGateSignal.None, s.origin, false, allowed);       // cooldown, Fired/Cancelled -> Idle
                if (_arbiter.Signal(i) == HandGateSignal.Ready && _targetAtStart[i] && targeted && allowed && m.CanArm
                    && _arbiter.TryClaim(i, HandClaim.Summon))
                    m.TryArm(s.origin, dir);
            }
            else
            {
                m.TickOwned(dt, _arbiter.Signal(i), s.origin, keep, allowed, CeilingPullOwnership.CancelReason(_arbiter.LastBlock(i)));
            }
            // The machine ended it for its own reason (sideways, too slow, target lost, ...) while the pinch
            // is still closed: give the hand back; it must open and pinch again.
            if (!m.Active && _arbiter.IsClaimed(i)) _arbiter.Relinquish(i);
        }
    }

    /// <summary>Maps the arbiter's cancel reason to the pull's.</summary>
    public static class CeilingPullOwnership
    {
        public static CeilingPullCancel CancelReason(HandBlock block)
        {
            switch (block)
            {
                case HandBlock.Holding: return CeilingPullCancel.Held;
                case HandBlock.Preempted: return CeilingPullCancel.Blocked;
                default: return CeilingPullCancel.Lost;
            }
        }
    }
}
