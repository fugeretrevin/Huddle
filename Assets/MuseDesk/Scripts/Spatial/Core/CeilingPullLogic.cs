using UnityEngine;

namespace MuseDesk.Spatial
{
    // Pure logic of "reach up and pull it down" (CeilingPullGesture draws and reads the hands; this
    // decides). It lives beside the other scene-free logic so EditMode tests can reach it. No scene
    // access, no allocation. It never moves the pod: the gesture calls the director on Fired.

    public enum CeilingPullState { Idle = 0, Armed = 1, Pulling = 2, Fired = 3, Cancelled = 4 }

    /// <summary>Down: pull the docked pod down to the user. Up: push the engaged pod back up.</summary>
    public enum CeilingPullDirection { Down = 0, Up = 1 }

    public enum CeilingPullCancel { None = 0, Released = 1, Sideways = 2, WrongWay = 3, Timeout = 4, Blocked = 5, Lost = 6 }

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
    /// sideways, went the wrong way, timed out, was blocked or lost). Fired and Cancelled are shown for
    /// one Tick; the next Tick returns to Idle. A pinch that was already closed cannot arm: it has to
    /// open and start again while pointing.
    /// </summary>
    public sealed class CeilingPullMachine
    {
        public CeilingPullSettings settings = new CeilingPullSettings();

        Vector3 _start;
        bool _prevPinch, _reached;
        float _cooldown, _last;

        public CeilingPullState State { get; private set; }
        public CeilingPullDirection Direction { get; private set; }
        /// <summary>0..1 share of the distance covered now (0 outside Armed and Pulling).</summary>
        public float Progress { get; private set; }
        public float Elapsed { get; private set; }
        public CeilingPullCancel LastCancel { get; private set; }
        public bool Active => State == CeilingPullState.Armed || State == CeilingPullState.Pulling;
        public int Fires { get; private set; }
        public int Cancels { get; private set; }

        public void Reset()
        {
            State = CeilingPullState.Idle; Progress = 0f; Elapsed = 0f; _reached = false; _last = 0f; _prevPinch = false;
        }

        /// <summary>One frame. <paramref name="targeted"/> is only read on the frame the pinch starts;
        /// <paramref name="allowed"/> is read every frame (false cancels a gesture in progress).</summary>
        public CeilingPullState Tick(float dt, bool tracked, bool pinching, Vector3 hand, bool targeted, bool allowed, CeilingPullDirection direction)
        {
            if (State == CeilingPullState.Fired || State == CeilingPullState.Cancelled)
            {
                State = CeilingPullState.Idle; Progress = 0f; Elapsed = 0f;
            }
            if (_cooldown > 0f) _cooldown -= dt;
            bool pinch = tracked && pinching;
            bool rising = pinch && !_prevPinch;
            _prevPinch = pinch;

            if (State == CeilingPullState.Idle)
            {
                if (rising && targeted && allowed && _cooldown <= 0f)
                {
                    State = CeilingPullState.Armed; Direction = direction; _start = hand;
                    _reached = false; _last = 0f; Progress = 0f; Elapsed = 0f; LastCancel = CeilingPullCancel.None;
                }
                return State;
            }

            // Armed or Pulling.
            if (!tracked) return Finish(false, CeilingPullCancel.Lost);
            if (!pinching)
                return _reached && _last >= settings.releaseShare ? Finish(true, CeilingPullCancel.None)
                                                                  : Finish(false, CeilingPullCancel.Released);
            if (!allowed) return Finish(false, CeilingPullCancel.Blocked);

            Elapsed += dt;
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
            return State;
        }
    }
}
