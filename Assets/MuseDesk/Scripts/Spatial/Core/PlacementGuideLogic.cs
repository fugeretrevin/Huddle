using UnityEngine;

namespace MuseDesk.Spatial
{
    public enum GuideTarget { None = 0, Floor = 1, Ceiling = 2, Wall = 3 }

    public enum GuideState { Idle = 0, Guiding = 1, Requested = 2, Previewing = 3, Done = 4, Cancelled = 5 }

    /// <summary>How the Wall card went from step 1 ("Look at a wall") to step 2 ("Point and pinch to move it").</summary>
    public enum WallGuideAdvance { None = 0, Gaze = 1, Aim = 2, Timeout = 3 }

    /// <summary>
    /// Pure logic of the guided placement step between pressing Floor or Ceiling and the preview
    /// starting. No scene access: the component feeds it the gaze direction and the director's state.
    ///
    ///   Idle -> Guiding (Begin) -> Requested (dwell filled, or PressStart) -> Previewing (director opened
    ///   the preview) -> Done (preview ended by confirm, cancel or desk). Cancel / Abort from Guiding or
    ///   Requested gives Cancelled; a refused request gives Cancelled too. Done and Cancelled are
    ///   terminal until the next Begin.
    ///
    /// WALL (rc9 owner: "the user should get ... the onboarding placing process"). The wall preview is already open
    /// when the card appears (the Wall host opened it), so there is no request: step 1 is Guiding ("Look at a wall",
    /// the dwell ring fills while the gaze rests on a wall spot the evaluator accepts) and step 2 is Previewing
    /// ("Point and pinch to move it, then press Place"). Step 1 also ends when the user moves the spot themselves
    /// (they are already doing step 2) or after <see cref="timeoutSeconds"/> (no Start key: the dock carries Place /
    /// Next spot / Cancel, and the card never shows keys of its own for the wall). The host closing the preview
    /// (Place, Cancel, mode change) ends the card: Guiding gives Cancelled, Previewing gives Done.
    ///
    /// Nothing here moves the office. It only decides when to ask for the mode, and what the card says.
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
        public bool StartOffered => State == GuideState.Guiding && Target != GuideTarget.Wall && Elapsed >= timeoutSeconds;
        /// <summary>Wall card step: 1 "Look at a wall", 2 "Point and pinch to move it", 0 otherwise.</summary>
        public int WallStep => Target != GuideTarget.Wall ? 0 : State == GuideState.Guiding ? 1 : State == GuideState.Previewing ? 2 : 0;
        /// <summary>How the wall card reached step 2 (None until it has).</summary>
        public WallGuideAdvance WallAdvancedBy { get; private set; }
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

        // ───────────────────────── wall card: pure helpers ─────────────────────────

        public const string WallLookTitle = "Look at a wall";
        public const string WallLookBody = "Hold your gaze on a clear spot beside you";
        public const string WallAimTitle = "Point and pinch to move it";
        /// <summary>The shown spot moved at least this far (m) since the card appeared: the user is aiming already.</summary>
        public const float WallAimedMetres = 0.05f;

        /// <summary>The card's title for a wall step (1 or 2).</summary>
        public static string WallTitle(int step) { return step == 2 ? WallAimTitle : WallLookTitle; }

        /// <summary>
        /// The card's body for a wall step. Step 2 uses the DOCK's own key words (<see cref="SpacesDockLogic.Place"/>,
        /// <see cref="SpacesDockLogic.NextSpot"/>): the card has no keys of its own for the wall.
        /// </summary>
        public static string WallBody(int step, bool hasSpot, bool spotValid)
        {
            if (step != 2) return WallLookBody;
            if (!hasSpot) return "Point at a wall, or press " + SpacesDockLogic.NextSpot;
            if (!spotValid) return "Won't fit: point elsewhere, or press " + SpacesDockLogic.NextSpot;
            return "Then press " + SpacesDockLogic.Place + " on the dock";
        }

        /// <summary>
        /// Does a ray (unit or not) pass through an oriented box? Slab test in the box's frame; only hits in front of the
        /// origin and within <paramref name="maxDistance"/> count.
        /// </summary>
        public static bool RayHitsBox(Vector3 origin, Vector3 dir, Vector3 center, Quaternion rotation, Vector3 halfExtents, float maxDistance)
        {
            if (dir.sqrMagnitude < 1e-10f) return false;
            dir.Normalize();
            Quaternion inv = Quaternion.Inverse(rotation);
            Vector3 o = inv * (origin - center);
            Vector3 d = inv * dir;
            float tMin = 0f, tMax = maxDistance;
            for (int a = 0; a < 3; a++)
            {
                float oa = o[a], da = d[a], h = Mathf.Abs(halfExtents[a]);
                if (Mathf.Abs(da) < 1e-8f)
                {
                    if (oa < -h || oa > h) return false;
                    continue;
                }
                float t1 = (-h - oa) / da, t2 = (h - oa) / da;
                if (t1 > t2) { float s = t1; t1 = t2; t2 = s; }
                if (t1 > tMin) tMin = t1;
                if (t2 < tMax) tMax = t2;
                if (tMin > tMax) return false;
            }
            return true;
        }

        /// <summary>
        /// Which side of the user a spot is on, from the level head forward: +1 the user's right, -1 their left, 0 within
        /// <paramref name="aheadDeg"/> of straight ahead (or no usable direction). The wall card's arrow points that way.
        /// </summary>
        public static int SideOf(Vector3 head, Vector3 headForward, Vector3 spot, float aheadDeg = 15f)
        {
            Vector3 f = headForward; f.y = 0f;
            Vector3 to = spot - head; to.y = 0f;
            if (f.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return 0;
            f.Normalize(); to.Normalize();
            if (Vector3.Angle(f, to) <= aheadDeg) return 0;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            return Vector3.Dot(to, right) >= 0f ? 1 : -1;
        }

        /// <summary>True when the shown wall spot moved enough since the baseline to count as the user aiming (or a spot
        /// appeared where there was none).</summary>
        public static bool WallSpotMoved(bool hadBaseline, Vector3 baseline, bool hasSpot, Vector3 spot)
        {
            if (!hasSpot) return false;
            if (!hadBaseline) return true;
            return (spot - baseline).sqrMagnitude > WallAimedMetres * WallAimedMetres;
        }

        // ───────────────────────── state machine ─────────────────────────

        /// <summary>Start guiding towards a target. Restarts from any state.</summary>
        public void Begin(GuideTarget target)
        {
            Target = target;
            State = target == GuideTarget.None ? GuideState.Idle : GuideState.Guiding;
            Fill = 0f; Elapsed = 0f; GazeOnTarget = false; RequestedByKey = false; _requestEdge = false;
            WallAdvancedBy = WallGuideAdvance.None;
        }

        /// <summary>
        /// Wall step 1: advance by dt. <paramref name="gazeAccepted"/>: the gaze rests on a wall spot the evaluator accepts
        /// (or on the shown spot that fits). <paramref name="userAimed"/>: the user moved the shown spot themselves.
        /// Returns true exactly once, on the tick the card moves to step 2 (state becomes Previewing). Never requests a mode
        /// and never moves anything: the wall preview is already open.
        /// </summary>
        public bool TickWall(float dt, bool gazeAccepted, bool userAimed)
        {
            if (State != GuideState.Guiding || Target != GuideTarget.Wall) return false;
            dt = Mathf.Max(0f, dt);
            Elapsed += dt;
            GazeOnTarget = gazeAccepted;
            float rate = dwellSeconds <= 0f ? 1000f : 1f / dwellSeconds;
            if (gazeAccepted) Fill = Mathf.Min(1f, Fill + dt * rate);
            else Fill = Mathf.Max(0f, Fill - dt * rate * drainFactor);
            if (Fill >= 1f) return AdvanceWall(WallGuideAdvance.Gaze);
            if (userAimed) return AdvanceWall(WallGuideAdvance.Aim);
            if (Elapsed >= timeoutSeconds) return AdvanceWall(WallGuideAdvance.Timeout);
            return false;
        }

        bool AdvanceWall(WallGuideAdvance how)
        {
            State = GuideState.Previewing;
            WallAdvancedBy = how;
            Fill = 1f;
            return true;
        }

        /// <summary>
        /// Wall: called each tick with whether the host's wall preview is still open. When it closed (Place, Cancel,
        /// a mode change, the flag off) the card ends: step 1 gives Cancelled, step 2 gives Done. No-op for Floor / Ceiling.
        /// </summary>
        public void ReportWallOpen(bool previewOpen)
        {
            if (Target != GuideTarget.Wall || previewOpen) return;
            if (State == GuideState.Guiding || State == GuideState.Requested) { State = GuideState.Cancelled; Fill = 0f; }
            else if (State == GuideState.Previewing) { State = GuideState.Done; Fill = 0f; }
        }

        /// <summary>
        /// Advance by dt with the current gaze direction. Returns true exactly once, on the tick the
        /// mode should be requested (state becomes Requested). Floor and Ceiling only (the wall uses <see cref="TickWall"/>).
        /// </summary>
        public bool Tick(float dt, Vector3 gazeForward)
        {
            if (State != GuideState.Guiding || Target == GuideTarget.Wall) return false;
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

        /// <summary>
        /// A preview of <paramref name="target"/> was opened without the guide (a direct request, Send back's
        /// fallback, a driver): carry the card through it, so its keys (Place here, Other spot, Cancel) are
        /// there too. Only from a resting state (Idle, Done, Cancelled); false otherwise.
        /// </summary>
        public bool Adopt(GuideTarget target)
        {
            if (target == GuideTarget.None || Active) return false;
            Target = target;
            State = GuideState.Previewing;
            Fill = 0f; Elapsed = 0f; GazeOnTarget = false; RequestedByKey = false; _requestEdge = false;
            WallAdvancedBy = WallGuideAdvance.None;
            return true;
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
