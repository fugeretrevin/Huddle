using UnityEngine;

namespace MuseDesk.Spatial
{
    public enum GuideTarget { None = 0, Floor = 1, Ceiling = 2 }

    public enum GuideState { Idle = 0, Guiding = 1, Requested = 2, Previewing = 3, Done = 4, Cancelled = 5 }

    /// <summary>
    /// Pure logic of the guided placement step between pressing Floor or Ceiling and the preview
    /// starting. No scene access: the component feeds it the gaze direction and the director's state.
    ///
    ///   Idle -> Guiding (Begin) -> Requested (dwell filled, or PressStart) -> Previewing (director opened
    ///   the preview) -> Done (preview ended by confirm, cancel or desk). Cancel / Abort from Guiding or
    ///   Requested gives Cancelled; a refused request gives Cancelled too. Done and Cancelled are
    ///   terminal until the next Begin.
    ///
    /// Nothing here moves the office. It only decides when to ask for the mode.
    /// </summary>
    public sealed class PlacementGuideLogic
    {
        public float ceilingPitchDeg = 35f;
        public float floorPitchDeg = 30f;
        public float dwellSeconds = 0.6f;
        public float timeoutSeconds = 8f;
        /// <summary>The fill drains this many times faster than it fills when the gaze leaves the target.</summary>
        public float drainFactor = 2f;

        public GuideState State { get; private set; }
        public GuideTarget Target { get; private set; }
        /// <summary>0..1 dwell fill.</summary>
        public float Fill { get; private set; }
        /// <summary>Seconds spent in Guiding.</summary>
        public float Elapsed { get; private set; }
        public bool GazeOnTarget { get; private set; }
        /// <summary>True once the user has been guiding for <see cref="timeoutSeconds"/> without the request.</summary>
        public bool StartOffered => State == GuideState.Guiding && Elapsed >= timeoutSeconds;
        /// <summary>True when the request was made by the Start key rather than by gaze.</summary>
        public bool RequestedByKey { get; private set; }
        public bool Active => State == GuideState.Guiding || State == GuideState.Requested || State == GuideState.Previewing;

        bool _requestEdge;

        // ───────────────────────── pure helpers ─────────────────────────

        /// <summary>Pitch of a direction in degrees: +90 straight up, -90 straight down.</summary>
        public static float PitchDeg(Vector3 forward)
        {
            float m = forward.magnitude;
            if (m < 1e-5f) return 0f;
            return Mathf.Asin(Mathf.Clamp(forward.y / m, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>True when the gaze is past the pitch threshold for the target.</summary>
        public static bool GazeAtTarget(GuideTarget target, Vector3 forward, float ceilingPitchDeg, float floorPitchDeg)
        {
            float p = PitchDeg(forward);
            if (target == GuideTarget.Ceiling) return p >= ceilingPitchDeg;
            if (target == GuideTarget.Floor) return p <= -floorPitchDeg;
            return false;
        }

        /// <summary>Intersect a ray with the horizontal plane y = planeY. False when parallel or behind.</summary>
        public static bool RayPlane(Vector3 origin, Vector3 dir, float planeY, out Vector3 hit)
        {
            hit = origin;
            if (Mathf.Abs(dir.y) < 1e-4f) return false;
            float t = (planeY - origin.y) / dir.y;
            if (t <= 0f) return false;
            hit = origin + dir * t;
            return true;
        }

        /// <summary>Point on the way from the gaze hit to the candidate: ease-out cubic over duration.</summary>
        public static Vector3 SweepCentre(Vector3 from, Vector3 to, float t, float duration)
        {
            if (duration <= 0f) return to;
            float u = Mathf.Clamp01(t / duration);
            float inv = 1f - u;
            return Vector3.LerpUnclamped(from, to, 1f - inv * inv * inv);
        }

        /// <summary>Limit how far from the candidate the sweep starts, so a grazing gaze does not start the wipe in the next room.</summary>
        public static Vector3 ClampOrigin(Vector3 origin, Vector3 candidate, float maxHorizontal)
        {
            Vector3 d = origin - candidate; d.y = 0f;
            float m = d.magnitude;
            if (m <= maxHorizontal || m < 1e-5f) return origin;
            Vector3 c = candidate + d * (maxHorizontal / m);
            c.y = origin.y;
            return c;
        }

        // ───────────────────────── state machine ─────────────────────────

        /// <summary>Start guiding towards a target. Restarts from any state.</summary>
        public void Begin(GuideTarget target)
        {
            Target = target;
            State = target == GuideTarget.None ? GuideState.Idle : GuideState.Guiding;
            Fill = 0f; Elapsed = 0f; GazeOnTarget = false; RequestedByKey = false; _requestEdge = false;
        }

        /// <summary>
        /// Advance by dt with the current gaze direction. Returns true exactly once, on the tick the
        /// mode should be requested (state becomes Requested).
        /// </summary>
        public bool Tick(float dt, Vector3 gazeForward)
        {
            if (State != GuideState.Guiding) return false;
            dt = Mathf.Max(0f, dt);
            Elapsed += dt;
            GazeOnTarget = GazeAtTarget(Target, gazeForward, ceilingPitchDeg, floorPitchDeg);
            float rate = dwellSeconds <= 0f ? 1000f : 1f / dwellSeconds;
            if (GazeOnTarget) Fill = Mathf.Min(1f, Fill + dt * rate);
            else Fill = Mathf.Max(0f, Fill - dt * rate * drainFactor);
            if (Fill >= 1f) return Request(false);
            return false;
        }

        /// <summary>The "Start here" key. Only meaningful once offered; returns true when it requests.</summary>
        public bool PressStart()
        {
            if (!StartOffered) return false;
            return Request(true);
        }

        bool Request(bool byKey)
        {
            State = GuideState.Requested;
            RequestedByKey = byKey;
            _requestEdge = true;
            return true;
        }

        /// <summary>The director answered the request: opened a preview (true) or refused it (false).</summary>
        public void ReportRequestResult(bool previewOpened)
        {
            if (State != GuideState.Requested) return;
            State = previewOpened ? GuideState.Previewing : GuideState.Cancelled;
        }

        /// <summary>Called each tick with the director's state while Previewing: the preview ending is Done.</summary>
        public void ReportPreviewing(bool previewingTargetNow)
        {
            if (State == GuideState.Previewing && !previewingTargetNow) State = GuideState.Done;
        }

        /// <summary>The user cancelled (Cancel key, Desk key, mode change). Nothing was moved.</summary>
        public void Cancel()
        {
            if (State == GuideState.Guiding || State == GuideState.Requested) State = GuideState.Cancelled;
            else if (State == GuideState.Previewing) State = GuideState.Done;
            Fill = 0f;
        }

        public bool ConsumeRequestEdge() { bool e = _requestEdge; _requestEdge = false; return e; }
    }
}
