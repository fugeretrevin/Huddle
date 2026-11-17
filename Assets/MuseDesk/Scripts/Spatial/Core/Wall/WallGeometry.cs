using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    // Wall Habitat (Phase 3) room data. Additive: the Desk/Floor/Ceiling evaluators never read walls.
    //
    // Wall-local convention, documented once (also in docs/Engineering/WALL_HABITAT_ARCHITECTURE.md):
    //   +Z = the wall's normal, pointing OUT of the wall into the room (toward the user)
    //   +Y = world up
    //   +X = Quaternion.LookRotation(normal, up) * Vector3.right
    //   u  = signed horizontal offset from the wall centre along +X, metres
    //   y  = world height, metres
    // The habitat root uses the same frame (rotation LookRotation(normal, up)), so its +Z points at
    // the room, matching the project's "root +Z points at the user" rule. No negative scale anywhere.

    /// <summary>A vertical, rectangular wall face as the room scan describes it.</summary>
    public struct RoomWall
    {
        /// <summary>Stable id (the scan anchor's UUID on device; any string in tests).</summary>
        public string id;
        /// <summary>World centre of the wall rectangle.</summary>
        public Vector3 center;
        /// <summary>Horizontal unit normal pointing into the room.</summary>
        public Vector3 normal;
        public float width;
        public float bottomY;
        public float topY;
        /// <summary>True for a wall the user drew as invisible (no physical surface): never mount on it.</summary>
        public bool invisible;

        public Quaternion Rotation => Quaternion.LookRotation(normal, Vector3.up);
        public Vector3 Right => Rotation * Vector3.right;
        public float Height => topY - bottomY;

        /// <summary>World point on the wall surface at (u, y).</summary>
        public Vector3 PointAt(float u, float y)
        {
            Vector3 p = center + Right * u;
            p.y = y;
            return p;
        }

        /// <summary>u of a world point projected onto the wall line.</summary>
        public float UOf(Vector3 world)
        {
            Vector3 d = world - center;
            d.y = 0f;
            return Vector3.Dot(d, Right);
        }

        /// <summary>Signed distance of a point in front of the wall (positive in the room).</summary>
        public float SignedDistance(Vector3 world)
        {
            Vector3 d = world - center;
            d.y = 0f;
            return Vector3.Dot(d, normal);
        }

        /// <summary>The wall's two bottom end points in world XZ (for "another wall is in the way" tests).</summary>
        public void Ends(out Vector2 a, out Vector2 b)
        {
            Vector3 r = Right * (width * 0.5f);
            a = new Vector2(center.x - r.x, center.z - r.z);
            b = new Vector2(center.x + r.x, center.z + r.z);
        }

        public static RoomWall FromEnds(string id, Vector2 a, Vector2 b, float bottomY, float topY, Vector2 roomInsidePoint)
        {
            Vector2 mid = (a + b) * 0.5f;
            Vector2 dir = b - a;
            float len = dir.magnitude;
            Vector2 n = len < 1e-6f ? Vector2.up : new Vector2(-dir.y, dir.x) / len;
            if (Vector2.Dot(roomInsidePoint - mid, n) < 0f) n = -n;
            return new RoomWall
            {
                id = id,
                center = new Vector3(mid.x, (bottomY + topY) * 0.5f, mid.y),
                normal = new Vector3(n.x, 0f, n.y),
                width = len,
                bottomY = bottomY,
                topY = topY,
            };
        }
    }

    public enum WallOpeningKind { Door = 0, Window = 1, Other = 2 }

    /// <summary>A door or window rectangle on one wall, in that wall's (u, y) coordinates.</summary>
    public struct WallOpening
    {
        public string wallId;
        public WallOpeningKind kind;
        public float uMin, uMax, yMin, yMax;

        public bool Overlaps(float u0, float u1, float y0, float y1, float margin)
        {
            return u1 > uMin - margin && u0 < uMax + margin && y1 > yMin - margin && y0 < yMax + margin;
        }

        /// <summary>Gap between this opening and a rectangle (0 when they overlap).</summary>
        public float Gap(float u0, float u1, float y0, float y1)
        {
            float du = Mathf.Max(0f, Mathf.Max(uMin - u1, u0 - uMax));
            float dy = Mathf.Max(0f, Mathf.Max(yMin - y1, y0 - yMax));
            return Mathf.Sqrt(du * du + dy * dy);
        }
    }

    /// <summary>
    /// Walls and their openings. Implemented by MrukRoomGeometry (device scan) and by
    /// <see cref="SyntheticWallRoom"/> (tests, Editor). Readers check <see cref="WallAvailability"/> first.
    /// Furniture comes from the companion <see cref="IRoomGeometry"/>.
    /// </summary>
    public interface IWallGeometry
    {
        RoomAvailability WallAvailability { get; }
        int WallCount { get; }
        RoomWall GetWall(int index);
        int OpeningCount { get; }
        WallOpening GetOpening(int index);
    }

    /// <summary>A hand-built room with walls for EditMode tests and Editor Play Mode.</summary>
    public sealed class SyntheticWallRoom : IWallGeometry
    {
        readonly List<RoomWall> _walls = new List<RoomWall>();
        readonly List<WallOpening> _openings = new List<WallOpening>();
        public RoomAvailability forcedAvailability = RoomAvailability.Available;

        /// <summary>The floor/ceiling/furniture part of the same room.</summary>
        public readonly SyntheticRoomGeometry room;

        public SyntheticWallRoom(SyntheticRoomGeometry room) { this.room = room; }

        public RoomAvailability WallAvailability
        {
            get
            {
                if (forcedAvailability != RoomAvailability.Available) return forcedAvailability;
                if (room != null && room.Availability != RoomAvailability.Available) return room.Availability;
                return _walls.Count > 0 ? RoomAvailability.Available : RoomAvailability.NoRoomSetup;
            }
        }

        public int WallCount => WallAvailability == RoomAvailability.Available ? _walls.Count : 0;
        public RoomWall GetWall(int index) => _walls[index];
        public int OpeningCount => WallAvailability == RoomAvailability.Available ? _openings.Count : 0;
        public WallOpening GetOpening(int index) => _openings[index];

        public void AddWall(RoomWall w) { _walls.Add(w); }
        public void AddOpening(WallOpening o) { _openings.Add(o); }
        public void ClearWalls() { _walls.Clear(); _openings.Clear(); }

        /// <summary>Opening centred at (u, y) on the wall with the given id.</summary>
        public void AddOpening(string wallId, WallOpeningKind kind, float u, float y, float width, float height)
        {
            _openings.Add(new WallOpening { wallId = wallId, kind = kind, uMin = u - width * 0.5f, uMax = u + width * 0.5f, yMin = y - height * 0.5f, yMax = y + height * 0.5f });
        }

        /// <summary>
        /// An axis-aligned rectangular room (same arguments as SyntheticRoomGeometry.Rectangle) with its
        /// four walls: "wall-N" (+Z side), "wall-S", "wall-E" (+X side), "wall-W". Wall height runs from the
        /// floor to the ceiling (2.4 m above the floor when the room has no ceiling).
        /// </summary>
        public static SyntheticWallRoom Rectangle(float centerX, float centerZ, float width, float depth,
                                                  float floorHeight, float ceilingHeight)
        {
            var geo = SyntheticRoomGeometry.Rectangle(centerX, centerZ, width, depth, floorHeight, ceilingHeight);
            var w = new SyntheticWallRoom(geo);
            float top = ceilingHeight > floorHeight ? ceilingHeight : floorHeight + 2.4f;
            float hx = width * 0.5f, hz = depth * 0.5f;
            var inside = new Vector2(centerX, centerZ);
            w.AddWall(RoomWall.FromEnds("wall-N", new Vector2(centerX - hx, centerZ + hz), new Vector2(centerX + hx, centerZ + hz), floorHeight, top, inside));
            w.AddWall(RoomWall.FromEnds("wall-S", new Vector2(centerX + hx, centerZ - hz), new Vector2(centerX - hx, centerZ - hz), floorHeight, top, inside));
            w.AddWall(RoomWall.FromEnds("wall-E", new Vector2(centerX + hx, centerZ + hz), new Vector2(centerX + hx, centerZ - hz), floorHeight, top, inside));
            w.AddWall(RoomWall.FromEnds("wall-W", new Vector2(centerX - hx, centerZ - hz), new Vector2(centerX - hx, centerZ + hz), floorHeight, top, inside));
            return w;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < _walls.Count; i++) if (_walls[i].id == id) return i;
            return -1;
        }
    }

    /// <summary>Plain wall maths used by the evaluators. No allocation.</summary>
    public static class WallMath
    {
        /// <summary>Ray against every wall rectangle; the nearest hit in front of the ray. u and y in that wall's frame.</summary>
        public static bool Raycast(IWallGeometry walls, Vector3 origin, Vector3 dir, float maxDistance,
                                   out int wallIndex, out float u, out float y, out float distance)
        {
            wallIndex = -1; u = 0f; y = 0f; distance = float.MaxValue;
            if (walls == null || walls.WallAvailability != RoomAvailability.Available) return false;
            if (dir.sqrMagnitude < 1e-8f) return false;
            dir = dir.normalized;
            for (int i = 0; i < walls.WallCount; i++)
            {
                RoomWall w = walls.GetWall(i);
                float denom = Vector3.Dot(dir, w.normal);
                if (denom > -1e-4f) continue;                         // ray must come from the room side, toward the wall
                float t = Vector3.Dot(w.center - origin, w.normal) / denom;
                if (t <= 0f || t > maxDistance || t >= distance) continue;
                Vector3 hit = origin + dir * t;
                float hu = w.UOf(hit);
                if (Mathf.Abs(hu) > w.width * 0.5f || hit.y < w.bottomY || hit.y > w.topY) continue;
                wallIndex = i; u = hu; y = hit.y; distance = t;
            }
            return wallIndex >= 0;
        }

        /// <summary>True when the XZ segment a-b crosses (or touches) the oriented rectangle.</summary>
        public static bool SegmentHitsRect(Vector2 a, Vector2 b, OrientedRect r)
        {
            if (r.Contains(a) || r.Contains(b)) return true;
            for (int i = 0; i < 4; i++)
                if (SegmentsIntersect(a, b, r.Corner(i), r.Corner((i + 1) & 3))) return true;
            return false;
        }

        public static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            float d1 = Cross(q2 - q1, p1 - q1), d2 = Cross(q2 - q1, p2 - q1);
            float d3 = Cross(p2 - p1, q1 - p1), d4 = Cross(p2 - p1, q2 - p1);
            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f))) return true;
            const float eps = 1e-7f;
            if (Mathf.Abs(d1) < eps && OnSeg(q1, q2, p1)) return true;
            if (Mathf.Abs(d2) < eps && OnSeg(q1, q2, p2)) return true;
            if (Mathf.Abs(d3) < eps && OnSeg(p1, p2, q1)) return true;
            if (Mathf.Abs(d4) < eps && OnSeg(p1, p2, q2)) return true;
            return false;
        }

        static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }

        static bool OnSeg(Vector2 a, Vector2 b, Vector2 p)
        {
            return Mathf.Min(a.x, b.x) - 1e-6f <= p.x && p.x <= Mathf.Max(a.x, b.x) + 1e-6f
                && Mathf.Min(a.y, b.y) - 1e-6f <= p.y && p.y <= Mathf.Max(a.y, b.y) + 1e-6f;
        }

        /// <summary>Distance from a point to a box given in a local frame (pose + half extents, box centred on pose).</summary>
        public static float DistanceToBox(Vector3 point, Vector3 boxCenter, Quaternion boxRotation, Vector3 half)
        {
            Vector3 l = Quaternion.Inverse(boxRotation) * (point - boxCenter);
            float dx = Mathf.Max(0f, Mathf.Abs(l.x) - half.x);
            float dy = Mathf.Max(0f, Mathf.Abs(l.y) - half.y);
            float dz = Mathf.Max(0f, Mathf.Abs(l.z) - half.z);
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>Unsigned horizontal angle (degrees) between the user's forward and the direction to a point.</summary>
        public static float HorizontalAngleFromForward(SeatedUser user, Vector3 point)
        {
            Vector3 d = point - user.head;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-8f) return 0f;
            return Vector3.Angle(user.forward, d.normalized);
        }
    }
}
