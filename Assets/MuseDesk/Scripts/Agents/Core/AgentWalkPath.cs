using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Pure polyline maths for an agent's walk: start, optional waypoints (stepping out around a
    /// desk), target. Planar (XZ) only; height is handled by the caller. No allocations: the caller
    /// owns the buffers.
    /// </summary>
    public static class AgentWalkPath
    {
        public const int MaxPoints = 10;

        /// <summary>Fills buf with start, the waypoints (in order, or reversed), then target. Returns the point count.</summary>
        public static int Build(Vector3 start, Vector3 target, Vector3[] waypoints, int waypointCount,
                                bool reverse, Vector3[] buf)
        {
            int n = 0;
            buf[n++] = start;
            int max = buf.Length - 1;
            for (int i = 0; i < waypointCount && n < max; i++)
            {
                Vector3 w = waypoints[reverse ? waypointCount - 1 - i : i];
                w.y = start.y;
                buf[n++] = w;
            }
            buf[n++] = target;
            return n;
        }

        /// <summary>How close (metres, planar) the start must be to a route segment to count as standing on it.</summary>
        public const float JoinTolerance = 0.015f;

        /// <summary>Planar distance from p to the segment a-b.</summary>
        public static float PointSegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float l2 = dx * dx + dz * dz;
            float t = l2 > 1e-12f ? Mathf.Clamp01(((p.x - a.x) * dx + (p.z - a.z) * dz) / l2) : 0f;
            float qx = a.x + dx * t - p.x, qz = a.z + dz * t - p.z;
            return Mathf.Sqrt(qx * qx + qz * qz);
        }

        /// <summary>
        /// How many leading waypoints are already behind an agent that starts ON the route. The waypoints are in travel
        /// order and the route ends at <paramref name="target"/>. A start that stands on the segment from waypoint i to
        /// the next point (within <paramref name="tolerance"/>) has passed waypoints 0..i, so the walk continues to the
        /// next point and never doubles back to an earlier corner. A start that is not on the route returns 0 (the whole
        /// route is walked). This is what makes a walk that is re-targeted part-way (sent home during the walk out, or
        /// out again during the walk home) keep to the authored lane.
        /// </summary>
        public static int WaypointsBehind(Vector3 start, Vector3[] waypoints, int waypointCount, bool reverse,
                                          Vector3 target, float tolerance = JoinTolerance)
        {
            int skip = 0;
            for (int i = 0; i < waypointCount; i++)
            {
                Vector3 a = waypoints[reverse ? waypointCount - 1 - i : i];
                Vector3 b = i + 1 < waypointCount ? waypoints[reverse ? waypointCount - 2 - i : i + 1] : target;
                if (PointSegmentDistance(start, a, b) <= tolerance) skip = i + 1;
            }
            return skip;
        }

        /// <summary>Like <see cref="Build"/>, leaving out the first <paramref name="skip"/> waypoints (in travel order).</summary>
        public static int BuildFrom(Vector3 start, Vector3 target, Vector3[] waypoints, int waypointCount,
                                    bool reverse, int skip, Vector3[] buf)
        {
            int n = 0;
            buf[n++] = start;
            int max = buf.Length - 1;
            for (int i = Mathf.Max(0, skip); i < waypointCount && n < max; i++)
            {
                Vector3 w = waypoints[reverse ? waypointCount - 1 - i : i];
                w.y = start.y;
                buf[n++] = w;
            }
            buf[n++] = target;
            return n;
        }

        static float Seg(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static float Length(Vector3[] p, int n)
        {
            float len = 0f;
            for (int i = 1; i < n; i++) len += Seg(p[i - 1], p[i]);
            return len;
        }

        /// <summary>
        /// Planar position at fraction k (0..1) of the total length. segIndex is the segment the
        /// point is on, segFrac how far along that segment, dir its unit direction (zero if degenerate).
        /// </summary>
        public static Vector3 Sample(Vector3[] p, int n, float k, out int segIndex, out float segFrac, out Vector3 dir)
        {
            segIndex = 0; segFrac = 0f; dir = Vector3.zero;
            if (n < 2) return n == 1 ? p[0] : Vector3.zero;
            float total = Length(p, n);
            if (total < 1e-6f) { segIndex = n - 2; segFrac = 1f; return p[n - 1]; }
            float d = Mathf.Clamp01(k) * total;
            for (int i = 1; i < n; i++)
            {
                float s = Seg(p[i - 1], p[i]);
                if (d <= s || i == n - 1)
                {
                    float f = s > 1e-6f ? Mathf.Clamp01(d / s) : 1f;
                    segIndex = i - 1; segFrac = f;
                    Vector3 v = p[i] - p[i - 1]; v.y = 0f;
                    dir = v.sqrMagnitude > 1e-10f ? v.normalized : Vector3.zero;
                    return Vector3.Lerp(p[i - 1], p[i], f);
                }
                d -= s;
            }
            segIndex = n - 2; segFrac = 1f;
            return p[n - 1];
        }

        /// <summary>Duration for a routed walk: never faster than maxSpeed, never shorter than baseSeconds.</summary>
        public static float Duration(float baseSeconds, float length, float maxSpeed)
        {
            if (maxSpeed <= 1e-4f) return baseSeconds;
            return Mathf.Max(baseSeconds, length / maxSpeed);
        }
    }
}
