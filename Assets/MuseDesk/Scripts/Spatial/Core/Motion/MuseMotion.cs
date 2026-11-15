using System;
using UnityEngine;

namespace MuseDesk.Motion
{
    /// <summary>Named easing shapes. All map [0,1] to [0,1] with f(0)=0 and f(1)=1. Only SoftBack overshoots.</summary>
    public enum EaseKind
    {
        Linear = 0,
        OutQuad = 1,
        OutCubic = 2,
        InOutCubic = 3,
        InOutSine = 4,
        SmoothStep = 5,
        /// <summary>Gentle overshoot (about 4 %). Replaced by OutCubic under Reduced Motion.</summary>
        SoftBack = 6,
    }

    public static class Ease
    {
        /// <summary>Evaluate an ease at normalised time t (clamped to [0,1]). Exact at both ends.</summary>
        public static float Evaluate(EaseKind kind, float t)
        {
            if (float.IsNaN(t) || t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            switch (kind)
            {
                case EaseKind.OutQuad: return 1f - (1f - t) * (1f - t);
                case EaseKind.OutCubic: { float u = 1f - t; return 1f - u * u * u; }
                case EaseKind.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                case EaseKind.InOutSine: return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
                case EaseKind.SmoothStep: return t * t * (3f - 2f * t);
                case EaseKind.SoftBack:
                {
                    const float c1 = 0.9f, c3 = c1 + 1f;   // ~4 % overshoot, much softer than the classic 1.70158
                    float u = t - 1f;
                    return 1f + c3 * u * u * u + c1 * u * u;
                }
                default: return t;
            }
        }

        public static bool Overshoots(EaseKind kind) { return kind == EaseKind.SoftBack; }
    }

    /// <summary>
    /// Process-wide motion preferences. Reduced Motion shortens transitions and removes overshoot without hiding state
    /// changes. Set by the app's settings layer; read by every transition at start.
    /// </summary>
    public static class MotionSettings
    {
        public static bool ReducedMotion;
        /// <summary>Duration multiplier applied under Reduced Motion.</summary>
        public const float ReducedScale = 0.5f;
        /// <summary>The largest time step a transition will consume in one frame. A resume after a long pause must not
        /// skip a whole transition in one frame, and a hitch must not jump an object across the room.</summary>
        public const float MaxStep = 1f / 20f;

        public static event Action Changed;

        public static void SetReducedMotion(bool on)
        {
            if (ReducedMotion == on) return;
            ReducedMotion = on;
            var h = Changed; if (h != null) h();
        }

        /// <summary>A safe per-frame step: NaN, negative and huge values are clamped.</summary>
        public static float SafeStep(float dt)
        {
            if (float.IsNaN(dt) || dt <= 0f) return 0f;
            return dt > MaxStep ? MaxStep : dt;
        }
    }

    /// <summary>One tunable timing: a duration and a shape (optionally an authored AnimationCurve).</summary>
    [Serializable]
    public struct MotionToken
    {
        public float duration;
        public EaseKind ease;
        [Tooltip("Optional authored curve (0..1 -> 0..1). Used instead of the ease when it has at least two keys.")]
        public AnimationCurve curve;

        public MotionToken(float duration, EaseKind ease) { this.duration = duration; this.ease = ease; curve = null; }

        /// <summary>The duration actually used now (Reduced Motion shortens it). Never negative.</summary>
        public float Duration
        {
            get
            {
                float d = float.IsNaN(duration) ? 0f : Mathf.Max(0f, duration);
                return MotionSettings.ReducedMotion ? d * MotionSettings.ReducedScale : d;
            }
        }

        /// <summary>The shaped progress at normalised time t. Exact at 0 and 1 whatever the curve says.</summary>
        public float Evaluate(float t)
        {
            if (float.IsNaN(t) || t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            if (curve != null && curve.length >= 2) return curve.Evaluate(t);
            EaseKind k = MotionSettings.ReducedMotion && Ease.Overshoots(ease) ? EaseKind.OutCubic : ease;
            return Ease.Evaluate(k, t);
        }
    }

    public enum MotionState { Idle = 0, Running = 1, Completed = 2, Cancelled = 3 }

    /// <summary>
    /// A cancellable, restartable clock for a transition between FIXED endpoints held by the caller. Sampled from elapsed
    /// time (never <c>Lerp(current, target, k*dt)</c>), so it always ends exactly at 1. Zero or negative durations
    /// complete on the first step.
    /// </summary>
    public sealed class MotionClock
    {
        MotionToken _token;
        float _elapsed, _duration;

        public MotionState State { get; private set; }
        public bool Running { get { return State == MotionState.Running; } }
        /// <summary>Raw normalised time 0..1.</summary>
        public float T { get { return _duration <= 0f ? (State == MotionState.Idle ? 0f : 1f) : Mathf.Clamp01(_elapsed / _duration); } }
        /// <summary>Shaped progress (what to interpolate the endpoints with).</summary>
        public float Value { get { return State == MotionState.Completed ? 1f : _token.Evaluate(T); } }
        public float DurationUsed { get { return _duration; } }

        public void Start(MotionToken token)
        {
            _token = token;
            _duration = token.Duration;
            _elapsed = 0f;
            State = MotionState.Running;
        }

        /// <summary>Advance. Returns true on the step that completes the transition.</summary>
        public bool Step(float dt)
        {
            if (State != MotionState.Running) return false;
            _elapsed += MotionSettings.SafeStep(dt);
            if (_duration > 0f && _elapsed < _duration) return false;
            State = MotionState.Completed;
            return true;
        }

        public void Cancel() { if (State == MotionState.Running) State = MotionState.Cancelled; }

        /// <summary>Jump to the end (e.g. an authoritative state arrived and the visual must agree now).</summary>
        public void Finish() { if (State == MotionState.Running) { _elapsed = _duration; State = MotionState.Completed; } }
    }

    /// <summary>
    /// 1-D motion with a bounded acceleration/cruise/deceleration profile toward a target. Lands exactly on the target
    /// with zero velocity (no overshoot, no creeping). Used for elevator cabins, walkers and workspace travel.
    /// </summary>
    public struct ProfiledMover
    {
        public float position;
        public float velocity;   // signed

        public ProfiledMover(float start) { position = start; velocity = 0f; }

        public bool AtRest(float target) { return position == target && velocity == 0f; }

        /// <summary>Advance toward <paramref name="target"/>. Returns true once it has arrived and stopped.</summary>
        public bool Step(float target, float maxSpeed, float accel, float decel, float dt)
        {
            dt = MotionSettings.SafeStep(dt);
            maxSpeed = Mathf.Max(1e-5f, maxSpeed);
            accel = Mathf.Max(1e-5f, accel);
            decel = Mathf.Max(1e-5f, decel);
            float d = target - position;
            if (Mathf.Abs(d) < 1e-6f && Mathf.Abs(velocity) < 1e-4f) { position = target; velocity = 0f; return true; }
            if (dt <= 0f) return false;

            float dir = Mathf.Sign(d);
            float along = velocity * dir;                       // speed toward the target (negative = moving away)
            // The fastest speed from which we can still stop exactly at the target: this alone produces the
            // deceleration ramp (it shrinks as the distance shrinks).
            float want = Mathf.Min(maxSpeed, Mathf.Sqrt(2f * decel * Mathf.Abs(d)));

            float rate = want > along ? accel : decel;
            along = Mathf.MoveTowards(along, want, rate * dt);
            float step = along * dt;
            if (step >= Mathf.Abs(d) || (along <= 0f && Mathf.Abs(d) < 1e-4f))
            {
                position = target; velocity = 0f; return true;
            }
            position += dir * step;
            velocity = dir * along;
            return false;
        }
    }

    /// <summary>
    /// Critically damped follower for angles (degrees) and scalars. Smooth start and stop with no overshoot; snaps
    /// exactly onto the target once within a small epsilon so a turn really ends.
    /// </summary>
    public static class Damped
    {
        /// <summary>One step of a critically damped spring. <paramref name="smoothTime"/> ~ time to reach the target.</summary>
        public static float Follow(float current, float target, ref float velocity, float smoothTime, float dt, float epsilon = 1e-3f)
        {
            dt = MotionSettings.SafeStep(dt);
            if (dt <= 0f) return current;
            smoothTime = Mathf.Max(1e-4f, smoothTime);
            float omega = 2f / smoothTime;
            float x = omega * dt;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float change = current - target;
            float temp = (velocity + omega * change) * dt;
            velocity = (velocity - omega * temp) * exp;
            float result = target + (change + temp) * exp;
            // Never pass the target (critical damping should not, but guard numerics).
            if ((target - current > 0f) == (result > target)) { result = target; velocity = 0f; }
            if (Mathf.Abs(result - target) < epsilon && Mathf.Abs(velocity) < epsilon * 10f) { result = target; velocity = 0f; }
            return result;
        }

        /// <summary>Angle version (degrees), taking the short way round.</summary>
        public static float FollowAngle(float current, float target, ref float velocity, float smoothTime, float dt, float epsilon = 0.05f)
        {
            float t = current + Mathf.DeltaAngle(current, target);
            float r = Follow(current, t, ref velocity, smoothTime, dt, epsilon);
            return r == t ? target : r;
        }
    }
}
