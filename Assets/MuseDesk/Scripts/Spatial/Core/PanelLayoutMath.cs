using System.Globalization;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The scene-free rules of the movable control panels (MovablePanel) and of the mode console
    /// (ModeConsole): where a released panel may rest, how two panels are nudged apart, how a panel
    /// pose is written to and read from a string, and which console keys apply in which state.
    /// Pure functions on plain values, so the EditMode tests can call them directly.
    /// </summary>
    public static class PanelLayoutMath
    {
        /// <summary>Nearest a panel may rest to the head (m).</summary>
        public const float MinReach = 0.25f;
        /// <summary>Farthest a panel may rest from the head (m): arm's reach.</summary>
        public const float MaxReach = 0.75f;
        /// <summary>A panel is kept at least this far in front of the head's vertical plane (m).</summary>
        public const float MinAhead = 0.05f;
        /// <summary>Saved positions farther than this from the frame origin are refused as corrupt (m).</summary>
        public const float MaxSavedDistance = 3f;

        // ───────────────────────── reach ─────────────────────────

        /// <summary>
        /// The nearest point to <paramref name="point"/> that is within [min, max] metres of the
        /// head and not behind it. "Behind" is judged against the head's level forward direction.
        /// </summary>
        public static Vector3 ClampToReach(Vector3 point, Vector3 head, Vector3 headForward,
                                           float min = MinReach, float max = MaxReach, float minAhead = MinAhead)
        {
            Vector3 f = headForward; f.y = 0f;
            Vector3 d = point - head;
            if (f.sqrMagnitude < 1e-6f) { f = d; f.y = 0f; }
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            f.Normalize();

            float ahead = Vector3.Dot(d, f);
            if (ahead < minAhead) d += f * (minAhead - ahead);      // never behind the head

            float m = d.magnitude;
            if (m < 1e-5f) d = f * min;
            else if (m < min) d *= min / m;
            else if (m > max) d *= max / m;
            return head + d;
        }

        /// <summary>
        /// The height of a panel's base after it is kept out of the desk: lifted to the desk plane
        /// when it is below it and within <paramref name="nearRadius"/> (level distance) of the desk
        /// frame's origin; left alone elsewhere (beside the desk a panel may hang lower).
        /// </summary>
        public static float LiftAboveDesk(float baseY, float deskY, float levelDistance, float nearRadius)
        {
            return levelDistance <= nearRadius && baseY < deskY ? deskY : baseY;
        }

        // ───────────────────────── height ─────────────────────────

        /// <summary>Default lowest base height of a panel relative to the desk plane (m, negative = below).</summary>
        public const float DefaultBelowDesk = 0.05f;
        /// <summary>Default highest base height of a panel above the desk plane (m).</summary>
        public const float DefaultAboveDesk = 0.45f;

        /// <summary>
        /// The band a panel's base may sit in: from <paramref name="belowDesk"/> under the desk plane
        /// to <paramref name="aboveDesk"/> over it, widened if needed to include the panel's default
        /// height so the default is always legal. Never below <paramref name="floorY"/> when given.
        /// </summary>
        public static void HeightBand(float deskY, float defaultBaseY, float belowDesk, float aboveDesk,
                                      out float lowest, out float highest, float floorY = float.NegativeInfinity)
        {
            lowest = Mathf.Min(deskY - Mathf.Max(0f, belowDesk), defaultBaseY);
            highest = Mathf.Max(deskY + Mathf.Max(0f, aboveDesk), defaultBaseY);
            if (lowest < floorY) lowest = Mathf.Min(floorY, defaultBaseY);
            if (highest < lowest) highest = lowest;
        }

        public static float ClampHeight(float y, float lowest, float highest)
        {
            return Mathf.Clamp(y, lowest, highest);
        }

        /// <summary>Where the pivot is while dragging: it follows the hand 1:1 in all three axes.</summary>
        public static Vector3 FollowHand(Vector3 pivotAtGrab, Vector3 handAtGrab, Vector3 handNow)
        {
            return pivotAtGrab + (handNow - handAtGrab);
        }

        /// <summary>The panel's rotation while dragged: its rotation at the grab turned about world up only (never rolls or tilts).</summary>
        public static Quaternion UprightTurn(Quaternion rotationAtGrab, float yawDeltaDegrees)
        {
            return Quaternion.AngleAxis(yawDeltaDegrees, Vector3.up) * rotationAtGrab;
        }

        /// <summary>
        /// Like <see cref="ClampToReach"/> but the point's height is kept exactly: only the level
        /// position is moved, so the 3D distance to the head stays within [min, max] (when the height
        /// difference alone exceeds max the point goes straight above/below the head, nearest in front).
        /// </summary>
        public static Vector3 ClampToReachKeepHeight(Vector3 point, Vector3 head, Vector3 headForward,
                                                     float min = MinReach, float max = MaxReach, float minAhead = MinAhead)
        {
            float dy = point.y - head.y;
            float maxLevel = Mathf.Sqrt(Mathf.Max(0f, max * max - dy * dy));
            float minLevel = Mathf.Sqrt(Mathf.Max(0f, min * min - dy * dy));
            Vector3 f = headForward; f.y = 0f;
            Vector3 d = point - head; d.y = 0f;
            if (f.sqrMagnitude < 1e-6f) f = d;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            f.Normalize();
            float ahead = Vector3.Dot(d, f);
            if (ahead < minAhead) d += f * (minAhead - ahead);
            float m = d.magnitude;
            if (m < 1e-5f) d = f * Mathf.Max(minLevel, 1e-3f);
            else if (m < minLevel) d *= minLevel / m;
            else if (m > maxLevel) d *= maxLevel / m;
            return new Vector3(head.x + d.x, point.y, head.z + d.z);
        }

        // ───────────────────────── facing ─────────────────────────

        /// <summary>Yaw (degrees about +Y) that turns +Z from <paramref name="from"/> toward
        /// <paramref name="to"/> on the level plane; <paramref name="fallback"/> when they coincide.</summary>
        public static float YawToward(Vector3 from, Vector3 to, float fallback)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            if (dx * dx + dz * dz < 1e-8f) return fallback;
            return Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
        }

        /// <summary>Frame-rate independent smoothing factor: the share of the remaining distance
        /// covered in <paramref name="deltaTime"/> with time constant <paramref name="tau"/>.</summary>
        public static float Smooth(float deltaTime, float tau)
        {
            if (tau <= 1e-5f) return 1f;
            return 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / tau);
        }

        /// <summary>Half extents of the axis-aligned box around a rectangle turned by a yaw.</summary>
        public static Vector2 RotatedHalfExtents(Vector2 half, float yawDegrees)
        {
            float a = yawDegrees * Mathf.Deg2Rad;
            float c = Mathf.Abs(Mathf.Cos(a)), s = Mathf.Abs(Mathf.Sin(a));
            return new Vector2(c * half.x + s * half.y, s * half.x + c * half.y);
        }

        // ───────────────────────── overlap ─────────────────────────

        /// <summary>
        /// Moves a rectangle (centre, half extents; level plane, x and z) the least distance that
        /// leaves <paramref name="gap"/> between it and every other rectangle. Each pass looks at the
        /// first rectangle it overlaps and at the four ways out of it (left, right, nearer, farther),
        /// shortest first, and takes the shortest one that lands clear of EVERY rectangle (so it does
        /// not bounce between two neighbours); if none is clear it takes the shortest and tries again.
        /// Between two equally short ways out, the sign of <paramref name="preferred"/> decides.
        /// </summary>
        public static Vector2 ResolveOverlap(Vector2 centre, Vector2 half, Vector2[] otherCentres, Vector2[] otherHalves,
                                             int count, float gap, Vector2 preferred, int iterations = 8)
        {
            if (otherCentres == null || otherHalves == null) return centre;
            count = Mathf.Min(count, Mathf.Min(otherCentres.Length, otherHalves.Length));
            for (int pass = 0; pass < iterations; pass++)
            {
                int hit = FirstOverlap(centre, half, otherCentres, otherHalves, count, gap);
                if (hit < 0) break;
                float rx = half.x + otherHalves[hit].x + gap, rz = half.y + otherHalves[hit].y + gap;
                Vector2 oc = otherCentres[hit];
                // The four ways out, in the order the preferred side comes first on each axis.
                float sx = preferred.x < 0f ? -1f : 1f, sz = preferred.y < 0f ? -1f : 1f;
                Vector2 a = new Vector2(oc.x + sx * rx, centre.y), b = new Vector2(oc.x - sx * rx, centre.y);
                Vector2 c = new Vector2(centre.x, oc.y + sz * rz), d = new Vector2(centre.x, oc.y - sz * rz);
                Vector2 best = centre; float bestCost = float.MaxValue; bool bestFree = false;
                for (int k = 0; k < 4; k++)
                {
                    Vector2 cand = k == 0 ? a : k == 1 ? b : k == 2 ? c : d;
                    float cost = (cand - centre).magnitude;
                    bool free = FirstOverlap(cand, half, otherCentres, otherHalves, count, gap) < 0;
                    // A clear way out beats a blocked one; then the shorter; ties keep the earlier (preferred) one.
                    if ((free && !bestFree) || (free == bestFree && cost < bestCost - 1e-6f))
                    {
                        best = cand; bestCost = cost; bestFree = free;
                    }
                }
                centre = best;
                if (bestFree) break;
            }
            return centre;
        }

        static int FirstOverlap(Vector2 centre, Vector2 half, Vector2[] otherCentres, Vector2[] otherHalves, int count, float gap)
        {
            for (int i = 0; i < count; i++)
                if (Overlaps(centre, half, otherCentres[i], otherHalves[i], gap)) return i;
            return -1;
        }

        /// <summary>True when two rectangles are closer than <paramref name="gap"/>.</summary>
        public static bool Overlaps(Vector2 centreA, Vector2 halfA, Vector2 centreB, Vector2 halfB, float gap)
        {
            return halfA.x + halfB.x + gap - Mathf.Abs(centreA.x - centreB.x) > 1e-5f
                && halfA.y + halfB.y + gap - Mathf.Abs(centreA.y - centreB.y) > 1e-5f;
        }

        // ───────────────────────── persistence codec ─────────────────────────

        const string Version = "v1";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The PlayerPrefs key of a panel's saved pose.</summary>
        public static string PrefsKey(string panelId) { return "MuseDesk.Panel." + (panelId ?? ""); }

        /// <summary>A pose (relative to the near-user frame) as one line of text.</summary>
        public static string EncodePose(Vector3 position, Quaternion rotation)
        {
            return string.Join("|", new[]
            {
                Version,
                position.x.ToString("R", Inv), position.y.ToString("R", Inv), position.z.ToString("R", Inv),
                rotation.x.ToString("R", Inv), rotation.y.ToString("R", Inv), rotation.z.ToString("R", Inv), rotation.w.ToString("R", Inv),
            });
        }

        /// <summary>
        /// Reads a pose written by <see cref="EncodePose"/>. False (and an identity pose) for
        /// anything else: another version, a wrong number of fields, text that is not a number,
        /// a non-finite value, a position implausibly far away, or a degenerate rotation.
        /// </summary>
        public static bool TryDecodePose(string text, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero; rotation = Quaternion.identity;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split('|');
            if (parts.Length != 8 || parts[0] != Version) return false;
            float[] v = new float[7];
            for (int i = 0; i < 7; i++)
            {
                if (!float.TryParse(parts[i + 1], NumberStyles.Float, Inv, out v[i])) return false;
                if (float.IsNaN(v[i]) || float.IsInfinity(v[i])) return false;
            }
            Vector3 p = new Vector3(v[0], v[1], v[2]);
            if (p.magnitude > MaxSavedDistance) return false;
            float n = Mathf.Sqrt(v[3] * v[3] + v[4] * v[4] + v[5] * v[5] + v[6] * v[6]);
            if (n < 0.5f || n > 1.5f) return false;
            position = p;
            rotation = new Quaternion(v[3] / n, v[4] / n, v[5] / n, v[6] / n);
            return true;
        }
    }

    /// <summary>Which keys of the mode console apply, and how the three place keys look.</summary>
    public struct ModeConsoleKeys
    {
        /// <summary>0 Desk, 1 Floor, 2 Ceiling: the place key that is lit.</summary>
        public int active;
        public bool deskDisabled, floorDisabled, ceilingDisabled;
        public bool next, confirm, cancel, stop, pod, bring, status;
        /// <summary>Words on the pod key: Lower, Raise or Waiting.</summary>
        public string podWords;
    }

    /// <summary>The mode console's state table, free of scene access.</summary>
    public static class ModeConsoleLogic
    {
        public const string Lower = "Lower", Raise = "Raise", Waiting = "Waiting";

        /// <summary>
        ///   always ................... Desk | Floor | Ceiling (the current place lit; a place whose
        ///                              flag is off, and every place while the office moves, disabled)
        ///   placement preview ........ Next (when there is more than one spot), Confirm, Cancel
        ///   the office is moving ..... Stop
        ///   Floor, at rest ........... Bring, Status
        ///   Ceiling, at rest ......... Lower / Raise (pod docked or engaged), Status
        /// </summary>
        public static ModeConsoleKeys Compute(bool floorEnabled, bool ceilingEnabled, PlacementMode mode, bool previewing,
                                              bool canCycle, bool transitioning, CeilingPodState podState, bool podWaiting)
        {
            ModeConsoleKeys k = new ModeConsoleKeys();
            k.active = mode == PlacementMode.Floor ? 1 : mode == PlacementMode.Ceiling ? 2 : 0;
            bool moving = transitioning && !previewing;
            k.deskDisabled = moving;
            k.floorDisabled = moving || !floorEnabled;
            k.ceilingDisabled = moving || !ceilingEnabled;

            k.next = previewing && canCycle;
            k.confirm = previewing;
            k.cancel = previewing;
            k.stop = moving;

            bool awayAtRest = mode != PlacementMode.Desk && !previewing && !transitioning;
            bool podDown = podState == CeilingPodState.Engaged, podUp = podState == CeilingPodState.Docked;
            k.pod = awayAtRest && mode == PlacementMode.Ceiling && (podDown || podUp);
            k.podWords = podDown ? (podWaiting ? Waiting : Raise) : Lower;
            k.bring = awayAtRest && mode == PlacementMode.Floor;
            k.status = awayAtRest;
            return k;
        }
    }
}
