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
