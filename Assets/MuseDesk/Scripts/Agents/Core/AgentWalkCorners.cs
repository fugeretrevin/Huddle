using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Pure corner rules for a routed desk walk (same rule the Wall walker uses): a corner sharper than the turn limit
    /// is a place to brake to rest, turn on the spot, then continue. The polyline itself is never changed, so waypoints
    /// and positions stay exactly as authored; only timing and heading differ. Planar (XZ).
    /// </summary>
    public static class AgentWalkCorners
    {
        /// <summary>Heading (degrees, Unity yaw: 0 = +Z, positive turns toward +X, i.e. to the right) of a planar direction.</summary>
        public static float YawOf(Vector3 dir) { return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; }

        static bool Dir(Vector3 a, Vector3 b, out Vector3 d)
        {
            d = b - a; d.y = 0f;
            if (d.sqrMagnitude < 1e-8f) { d = Vector3.zero; return false; }
            d.Normalize();
            return true;
        }

        /// <summary>Unsigned turn (degrees, 0..180) at interior point i between segment i-1 and segment i. Zero-length
        /// segments are skipped (0 when no heading can be read).</summary>
        public static float CornerAngle(Vector3[] p, int n, int i)
        {
            if (i < 1 || i > n - 2) return 0f;
            if (!Dir(p[i - 1], p[i], out Vector3 a) || !Dir(p[i], p[i + 1], out Vector3 b)) return 0f;
            return Mathf.Abs(Mathf.DeltaAngle(YawOf(a), YawOf(b)));
        }

        /// <summary>Distance along the polyline of the next corner sharper than <paramref name="limitDegrees"/> that lies
        /// strictly beyond <paramref name="fromDistance"/>; otherwise the total length (corner = -1). The corner index is
        /// the point index in p.</summary>
        public static float NextStopDistance(Vector3[] p, int n, float fromDistance, float limitDegrees, out int corner)
        {
            corner = -1;
            float cum = 0f, total = AgentWalkPath.Length(p, n);
            for (int i = 1; i < n - 1; i++)
            {
                float dx = p[i].x - p[i - 1].x, dz = p[i].z - p[i - 1].z;
                cum += Mathf.Sqrt(dx * dx + dz * dz);
                if (cum <= fromDistance + 1e-3f) continue;
                if (CornerAngle(p, n, i) > limitDegrees) { corner = i; return Mathf.Min(cum, total); }
            }
            return total;
        }

        /// <summary>Heading wanted after the corner at point i (direction of the next non-degenerate segment).</summary>
        public static float OutYaw(Vector3[] p, int n, int i, float fallback)
        {
            for (int k = i; k < n - 1; k++)
                if (Dir(p[k], p[k + 1], out Vector3 d)) return YawOf(d);
            return fallback;
        }

        /// <summary>-1 turn left, +1 turn right, 0 already facing it (within epsilon degrees).</summary>
        public static int TurnSign(float fromYaw, float toYaw, float epsilon = 0.5f)
        {
            float d = Mathf.DeltaAngle(fromYaw, toYaw);
            if (Mathf.Abs(d) <= epsilon) return 0;
            return d > 0f ? 1 : -1;
        }
    }
}
