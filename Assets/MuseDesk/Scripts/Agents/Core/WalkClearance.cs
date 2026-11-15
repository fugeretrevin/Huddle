using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>An axis-aligned obstacle on the desk plane (x, z in one shared frame, normally WorkspaceRoot-local).</summary>
    public struct ClearRect
    {
        public float minX, minZ, maxX, maxZ;
        public string name;

        public ClearRect(string name, float minX, float minZ, float maxX, float maxZ)
        {
            this.name = name; this.minX = minX; this.minZ = minZ; this.maxX = maxX; this.maxZ = maxZ;
        }
    }

    /// <summary>An obstacle as the plan-view triangles of a mesh (x, z pairs, six floats per triangle) with its bounds. A wall seen
    /// edge-on is a degenerate triangle (a line) and still counts.</summary>
    public sealed class ClearMesh
    {
        public string name;
        public float[] tris;
        public int triCount;
        public float minX = float.PositiveInfinity, minZ = float.PositiveInfinity, maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;

        public ClearMesh(string name, int capacityTris)
        {
            this.name = name; tris = new float[Mathf.Max(1, capacityTris) * 6];
        }

        public void Add(float ax, float az, float bx, float bz, float cx, float cz)
        {
            if (triCount * 6 + 6 > tris.Length) System.Array.Resize(ref tris, tris.Length * 2);
            int i = triCount * 6;
            tris[i] = ax; tris[i + 1] = az; tris[i + 2] = bx; tris[i + 3] = bz; tris[i + 4] = cx; tris[i + 5] = cz;
            triCount++;
            minX = Mathf.Min(minX, Mathf.Min(ax, Mathf.Min(bx, cx))); maxX = Mathf.Max(maxX, Mathf.Max(ax, Mathf.Max(bx, cx)));
            minZ = Mathf.Min(minZ, Mathf.Min(az, Mathf.Min(bz, cz))); maxZ = Mathf.Max(maxZ, Mathf.Max(az, Mathf.Max(bz, cz)));
        }
    }

    /// <summary>
    /// Pure planar geometry for the desk walk clearance check. The agent is a disc of a given radius on the XZ plane; a route
    /// is a polyline (AgentWalkPath); an obstacle is an axis-aligned rectangle or the plan-view triangles of a mesh. Clearance
    /// is the gap between the disc's edge and the obstacle: positive is free space, zero touching, negative is overlap. Every
    /// sample along the route is tested, plus every polyline vertex (corners are where the agent stops and turns in place).
    ///
    /// An obstacle the agent already stands next to at an endpoint (its own station at home, the delivery spot at the far end)
    /// is not an error for the first and last <c>endGrace</c> metres of the route; the strict minimum ignores it there and the
    /// "all" minimum does not, so a report can show both.
    /// </summary>
    public static class WalkClearance
    {
        /// <summary>Agent root CapsuleCollider radius in the agent prefabs (BuildAgentCharacters), metres. The driver uses
        /// the live collider (radius x scale) when it has one and falls back to this.</summary>
        public const float DefaultAgentRadius = 0.05f;

        /// <summary>Samples are this far apart along the route (metres).</summary>
        public const float SampleStep = 0.01f;

        public struct Result
        {
            /// <summary>Smallest gap over the route, ignoring endpoint-adjacent obstacles within endGrace of that endpoint. +Infinity: no obstacle.</summary>
            public float minClearance;
            public int obstacle;              // index of the obstacle at minClearance (-1 if none)
            public float atDistance;          // metres along the route where it happens
            public float atX, atZ;
            /// <summary>Smallest gap over the route with no endpoint exemption.</summary>
            public float minClearanceAll;
            public int obstacleAll;
            public float atDistanceAll;
            public int samples;
            public float length;
        }

        /// <summary>Signed distance from a point to the rectangle: distance outside, negative depth inside.</summary>
        public static float SignedDistance(float x, float z, ClearRect r)
        {
            float cx = (r.minX + r.maxX) * 0.5f, cz = (r.minZ + r.maxZ) * 0.5f;
            float hx = (r.maxX - r.minX) * 0.5f, hz = (r.maxZ - r.minZ) * 0.5f;
            float dx = Mathf.Abs(x - cx) - hx, dz = Mathf.Abs(z - cz) - hz;
            float ox = Mathf.Max(dx, 0f), oz = Mathf.Max(dz, 0f);
            float outside = Mathf.Sqrt(ox * ox + oz * oz);
            float inside = Mathf.Min(Mathf.Max(dx, dz), 0f);
            return outside + inside;
        }

        /// <summary>Gap between an agent disc of the given radius centred on (x, z) and the rectangle.</summary>
        public static float Gap(float x, float z, float radius, ClearRect r) { return SignedDistance(x, z, r) - radius; }

        static float SegDist(float px, float pz, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            float l2 = dx * dx + dz * dz;
            float t = l2 > 1e-12f ? Mathf.Clamp01(((px - ax) * dx + (pz - az) * dz) / l2) : 0f;
            float qx = ax + dx * t - px, qz = az + dz * t - pz;
            return Mathf.Sqrt(qx * qx + qz * qz);
        }

        /// <summary>Distance from a point to a plan-view triangle: 0 inside or on it, else the distance to the nearest edge.</summary>
        public static float PointTriangleDistance(float px, float pz, float ax, float az, float bx, float bz, float cx, float cz)
        {
            float d1 = (bx - ax) * (pz - az) - (bz - az) * (px - ax);
            float d2 = (cx - bx) * (pz - bz) - (cz - bz) * (px - bx);
            float d3 = (ax - cx) * (pz - cz) - (az - cz) * (px - cx);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
            float area2 = (bx - ax) * (cz - az) - (bz - az) * (cx - ax);
            if (!(neg && pos) && Mathf.Abs(area2) > 1e-12f) return 0f;
            float a = SegDist(px, pz, ax, az, bx, bz), b = SegDist(px, pz, bx, bz, cx, cz), c = SegDist(px, pz, cx, cz, ax, az);
            return Mathf.Min(a, Mathf.Min(b, c));
        }

        /// <summary>Gap between the disc and a mesh's plan triangles: the distance to the nearest triangle minus the radius
        /// (a disc centred inside the mesh is reported as -radius: how deep is not tracked).</summary>
        public static float Gap(float x, float z, float radius, ClearMesh m)
        {
            if (m == null || m.triCount == 0) return float.PositiveInfinity;
            // Far from the bounds: the distance to the bounds is already a (slightly low) answer, and no triangle is tested.
            float toBounds = SignedDistance(x, z, new ClearRect(null, m.minX, m.minZ, m.maxX, m.maxZ));
            if (toBounds > 0.25f) return toBounds - radius;
            float best = float.PositiveInfinity;
            var t = m.tris;
            for (int i = 0, n = m.triCount * 6; i < n; i += 6)
            {
                float d = PointTriangleDistance(x, z, t[i], t[i + 1], t[i + 2], t[i + 3], t[i + 4], t[i + 5]);
                if (d < best) { best = d; if (best <= 0f) break; }
            }
            return best - radius;
        }

        static float Seg(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Gap of obstacle number <paramref name="obstacle"/> at a point (supplied by the caller's obstacle kind).</summary>
        public delegate float GapFn(int obstacle, float x, float z);

        /// <summary>Rectangles. <paramref name="endGrace"/> is how far from each end an obstacle that the agent's disc already
        /// touches AT THAT END is exempt (its own desk at home, the spot it delivers to).</summary>
        public static Result Evaluate(Vector3[] p, int n, ClearRect[] obstacles, int count, float radius,
                                      float step = SampleStep, float endGrace = 0.10f)
        {
            return Evaluate(p, n, count, (o, x, z) => Gap(x, z, radius, obstacles[o]), radius, step, endGrace);
        }

        /// <summary>Mesh plan triangles.</summary>
        public static Result Evaluate(Vector3[] p, int n, ClearMesh[] obstacles, int count, float radius,
                                      float step = SampleStep, float endGrace = 0.10f)
        {
            return Evaluate(p, n, count, (o, x, z) => Gap(x, z, radius, obstacles[o]), radius, step, endGrace);
        }

        public static Result Evaluate(Vector3[] p, int n, int count, GapFn gap, float radius, float step, float endGrace)
        {
            var res = new Result
            {
                minClearance = float.PositiveInfinity, obstacle = -1, minClearanceAll = float.PositiveInfinity, obstacleAll = -1,
            };
            if (p == null || n < 1 || gap == null || count < 1) return res;
            float total = 0f;
            for (int i = 1; i < n; i++) total += Seg(p[i - 1], p[i]);
            res.length = total;
            step = Mathf.Max(0.001f, step);

            // Endpoint-adjacent obstacles: the disc already overlaps or touches them at the start (or at the end).
            var nearStart = new bool[count];
            var nearEnd = new bool[count];
            for (int o = 0; o < count; o++)
            {
                nearStart[o] = gap(o, p[0].x, p[0].z) <= 0.005f;
                nearEnd[o] = gap(o, p[n - 1].x, p[n - 1].z) <= 0.005f;
            }

            float walked = 0f;
            for (int seg = 0; seg < Mathf.Max(1, n - 1); seg++)
            {
                Vector3 a = p[seg], b = n > 1 ? p[seg + 1] : p[seg];
                float len = Seg(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / step));
                // The vertex itself is sampled as t = 0 of the next segment (and t = 1 of the last).
                int last = seg == n - 2 || n == 1 ? steps : steps - 1;
                for (int s = 0; s <= last; s++)
                {
                    float t = (float)s / steps;
                    float x = Mathf.Lerp(a.x, b.x, t), z = Mathf.Lerp(a.z, b.z, t);
                    float d = walked + len * t;
                    res.samples++;
                    for (int o = 0; o < count; o++)
                    {
                        float g = gap(o, x, z);
                        if (g < res.minClearanceAll) { res.minClearanceAll = g; res.obstacleAll = o; res.atDistanceAll = d; }
                        bool exempt = (nearStart[o] && d <= endGrace) || (nearEnd[o] && d >= total - endGrace);
                        if (!exempt && g < res.minClearance)
                        {
                            res.minClearance = g; res.obstacle = o; res.atDistance = d; res.atX = x; res.atZ = z;
                        }
                    }
                }
                walked += len;
            }
            return res;
        }
    }
}
