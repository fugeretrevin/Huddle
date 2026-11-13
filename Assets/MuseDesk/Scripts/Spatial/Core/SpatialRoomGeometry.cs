using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>Why room data can or cannot be used. Only <see cref="Available"/> allows a detected placement.</summary>
    public enum RoomAvailability
    {
        Available = 0,
        NotLoadedYet = 1,
        NoRoomSetup = 2,
        PermissionDenied = 3,
        InvalidFloor = 4,
        NoCeiling = 5,
    }

    /// <summary>Mirror of MuseDesk.Spatial.WorkspaceMode (same integer values) for the engine-free core,
    /// which cannot see the contract type in Assembly-CSharp.</summary>
    public enum PlacementMode { Desk = 0, Floor = 1, Ceiling = 2 }

    /// <summary>The two switches of the room-aware system. Both false by default: the system then only
    /// publishes Desk state and never moves anything.</summary>
    [Serializable]
    public sealed class SpatialFeatureFlags
    {
        public bool floorModeEnabled;
        public bool ceilingModeEnabled;
        public bool Any => floorModeEnabled || ceilingModeEnabled;
    }

    /// <summary>A horizontal floor or ceiling surface: outline in world XZ (stored as Vector2 x = X, y = Z)
    /// and its world height.</summary>
    public sealed class RoomPlane
    {
        public Vector2[] polygon;
        public float height;
    }

    /// <summary>An upright (gravity-aligned) furniture volume: an oriented rectangle in world XZ with a
    /// vertical extent, and the label the room scan gave it.</summary>
    public struct FurnitureBox
    {
        public Vector2 center;
        public Vector2 halfSize;
        public float yawDeg;
        public float minY;
        public float maxY;
        public string label;

        public OrientedRect Footprint => OrientedRect.FromYaw(center, halfSize, yawDeg);
    }

    /// <summary>The seated user: head position and flattened, normalised view direction (toward the desk).</summary>
    public struct SeatedUser
    {
        public Vector3 head;
        public Vector3 forward;

        public SeatedUser(Vector3 headPosition, Vector3 headForward)
        {
            head = headPosition;
            headForward.y = 0f;
            forward = headForward.sqrMagnitude < 1e-6f ? Vector3.forward : headForward.normalized;
        }

        /// <summary>The user's right (same construction as WorkspacePlacement.OffDeskPosition).</summary>
        public Vector3 Right => Vector3.Cross(Vector3.up, forward).normalized;
    }

    /// <summary>Position, rotation and uniform scale of the office root.</summary>
    public struct RootPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public float scale;

        public RootPose(Vector3 p, Quaternion r, float s) { position = p; rotation = r; scale = s; }

        public static RootPose Lerp(RootPose a, RootPose b, float k)
        {
            return new RootPose(Vector3.LerpUnclamped(a.position, b.position, k),
                                Quaternion.SlerpUnclamped(a.rotation, b.rotation, k),
                                Mathf.LerpUnclamped(a.scale, b.scale, k));
        }

        public bool Approximately(RootPose o, float posTol = 0.0005f, float angTol = 0.05f)
        {
            return (position - o.position).sqrMagnitude <= posTol * posTol
                && Quaternion.Angle(rotation, o.rotation) <= angTol
                && Mathf.Abs(scale - o.scale) <= 0.0005f;
        }
    }

    /// <summary>
    /// What the placement code may know about the room. Implementations: MrukRoomGeometry (the real
    /// scan, the only code that touches MRUK) and <see cref="SyntheticRoomGeometry"/> (tests, Editor).
    /// Readers must never assume data exists: check <see cref="Availability"/> first.
    /// </summary>
    public interface IRoomGeometry
    {
        /// <summary>True when a room with at least one usable floor outline is loaded.</summary>
        bool HasRoom { get; }
        /// <summary>Floor-level availability (Available only with a usable floor).</summary>
        RoomAvailability Availability { get; }
        /// <summary>Available when at least one real ceiling outline exists, NoCeiling when the room has
        /// a floor but no ceiling, otherwise the same reason as <see cref="Availability"/>.</summary>
        RoomAvailability CeilingAvailability { get; }
        /// <summary>Stable identifier of the room across sessions, or empty when there is none.</summary>
        string RoomId { get; }
        Bounds RoomBounds { get; }
        int FloorCount { get; }
        RoomPlane GetFloor(int index);
        int CeilingCount { get; }
        RoomPlane GetCeiling(int index);
        int FurnitureCount { get; }
        FurnitureBox GetFurniture(int index);
        /// <summary>World pose of a stable room reference (the first floor anchor). Saved placements are
        /// stored relative to it, so they survive a recenter and a restart.</summary>
        bool TryGetReferencePose(out Pose pose);
        /// <summary>Raised when any of the above changed (scene loaded, room updated or removed).</summary>
        event Action Changed;
    }

    /// <summary>Hand-built room for EditMode tests and for exercising the modes in the Editor.</summary>
    public sealed class SyntheticRoomGeometry : IRoomGeometry
    {
        readonly List<RoomPlane> _floors = new List<RoomPlane>();
        readonly List<RoomPlane> _ceilings = new List<RoomPlane>();
        readonly List<FurnitureBox> _furniture = new List<FurnitureBox>();

        public RoomAvailability forcedAvailability = RoomAvailability.Available;
        public string roomId = "synthetic-room";
        public Pose referencePose = Pose.identity;
        public bool hasReferencePose = true;

        public event Action Changed;

        public bool HasRoom => forcedAvailability == RoomAvailability.Available && _floors.Count > 0;

        public RoomAvailability Availability
        {
            get
            {
                if (forcedAvailability != RoomAvailability.Available) return forcedAvailability;
                return _floors.Count > 0 ? RoomAvailability.Available : RoomAvailability.InvalidFloor;
            }
        }

        public RoomAvailability CeilingAvailability
        {
            get
            {
                var a = Availability;
                if (a != RoomAvailability.Available) return a;
                return _ceilings.Count > 0 ? RoomAvailability.Available : RoomAvailability.NoCeiling;
            }
        }

        public string RoomId => roomId;

        public Bounds RoomBounds
        {
            get
            {
                bool any = false;
                Vector3 mn = Vector3.zero, mx = Vector3.zero;
                Encapsulate(_floors, ref any, ref mn, ref mx);
                Encapsulate(_ceilings, ref any, ref mn, ref mx);
                var b = new Bounds();
                if (any) b.SetMinMax(mn, mx);
                return b;
            }
        }

        static void Encapsulate(List<RoomPlane> planes, ref bool any, ref Vector3 mn, ref Vector3 mx)
        {
            for (int i = 0; i < planes.Count; i++)
            {
                var poly = planes[i].polygon;
                for (int j = 0; j < poly.Length; j++)
                {
                    var p = new Vector3(poly[j].x, planes[i].height, poly[j].y);
                    if (!any) { mn = mx = p; any = true; }
                    else { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
                }
            }
        }

        public int FloorCount => HasRoom ? _floors.Count : 0;
        public RoomPlane GetFloor(int index) => _floors[index];
        public int CeilingCount => HasRoom ? _ceilings.Count : 0;
        public RoomPlane GetCeiling(int index) => _ceilings[index];
        public int FurnitureCount => HasRoom ? _furniture.Count : 0;
        public FurnitureBox GetFurniture(int index) => _furniture[index];

        public bool TryGetReferencePose(out Pose pose)
        {
            pose = referencePose;
            return HasRoom && hasReferencePose;
        }

        /// <summary>An axis-aligned rectangular room centred on (centerX, centerZ) with the floor at
        /// floorHeight. Pass ceilingHeight &lt;= floorHeight for a room with no ceiling data.</summary>
        public static SyntheticRoomGeometry Rectangle(float centerX, float centerZ, float width, float depth,
                                                      float floorHeight, float ceilingHeight)
        {
            var room = new SyntheticRoomGeometry();
            room.AddFloor(RectPolygon(centerX, centerZ, width, depth), floorHeight);
            if (ceilingHeight > floorHeight) room.AddCeiling(RectPolygon(centerX, centerZ, width, depth), ceilingHeight);
            room.referencePose = new Pose(new Vector3(centerX, floorHeight, centerZ), Quaternion.identity);
            return room;
        }

        public static Vector2[] RectPolygon(float centerX, float centerZ, float width, float depth)
        {
            float hx = width * 0.5f, hz = depth * 0.5f;
            return new[]
            {
                new Vector2(centerX - hx, centerZ - hz), new Vector2(centerX + hx, centerZ - hz),
                new Vector2(centerX + hx, centerZ + hz), new Vector2(centerX - hx, centerZ + hz),
            };
        }

        public void AddFloor(Vector2[] polygon, float height) { _floors.Add(new RoomPlane { polygon = polygon, height = height }); }
        public void AddCeiling(Vector2[] polygon, float height) { _ceilings.Add(new RoomPlane { polygon = polygon, height = height }); }
        public void ClearCeilings() { _ceilings.Clear(); }
        public void ClearFurniture() { _furniture.Clear(); }

        public void AddFurniture(string label, Vector3 center, Vector3 size, float yawDeg = 0f)
        {
            _furniture.Add(new FurnitureBox
            {
                label = label,
                center = new Vector2(center.x, center.z),
                halfSize = new Vector2(size.x * 0.5f, size.z * 0.5f),
                yawDeg = yawDeg,
                minY = center.y - size.y * 0.5f,
                maxY = center.y + size.y * 0.5f,
            });
        }

        /// <summary>Tell listeners the room changed (after editing it, or to simulate a room event).</summary>
        public void RaiseChanged()
        {
            var h = Changed;
            if (h != null) h();
        }
    }

    /// <summary>An oriented rectangle in the XZ plane (Vector2 x = world X, y = world Z).</summary>
    public struct OrientedRect
    {
        public Vector2 center;
        public Vector2 half;
        public Vector2 axisX;   // unit, the rectangle's local right
        public Vector2 axisY;   // unit, the rectangle's local forward

        /// <summary>Yaw as Unity's rotation about +Y: forward = (sin, cos), right = (cos, -sin).</summary>
        public static OrientedRect FromYaw(Vector2 center, Vector2 half, float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            float s = Mathf.Sin(r), c = Mathf.Cos(r);
            return new OrientedRect { center = center, half = half, axisX = new Vector2(c, -s), axisY = new Vector2(s, c) };
        }

        public Vector2 Corner(int i)
        {
            float sx = (i == 0 || i == 3) ? -1f : 1f;
            float sy = i < 2 ? -1f : 1f;
            return center + axisX * (half.x * sx) + axisY * (half.y * sy);
        }

        public OrientedRect Inflated(float margin)
        {
            var r = this;
            r.half = new Vector2(half.x + margin, half.y + margin);
            return r;
        }

        public bool Contains(Vector2 p)
        {
            Vector2 d = p - center;
            return Mathf.Abs(Vector2.Dot(d, axisX)) <= half.x && Mathf.Abs(Vector2.Dot(d, axisY)) <= half.y;
        }

        /// <summary>Distance from a point to the rectangle; 0 when inside.</summary>
        public float DistanceToPoint(Vector2 p)
        {
            Vector2 d = p - center;
            float dx = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, axisX)) - half.x);
            float dy = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, axisY)) - half.y);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Separating-axis overlap test of two oriented rectangles.</summary>
        public bool Overlaps(OrientedRect o)
        {
            return !Separated(this, o, axisX) && !Separated(this, o, axisY)
                && !Separated(this, o, o.axisX) && !Separated(this, o, o.axisY);
        }

        static bool Separated(OrientedRect a, OrientedRect b, Vector2 axis)
        {
            float ra = a.half.x * Mathf.Abs(Vector2.Dot(a.axisX, axis)) + a.half.y * Mathf.Abs(Vector2.Dot(a.axisY, axis));
            float rb = b.half.x * Mathf.Abs(Vector2.Dot(b.axisX, axis)) + b.half.y * Mathf.Abs(Vector2.Dot(b.axisY, axis));
            return Mathf.Abs(Vector2.Dot(b.center - a.center, axis)) > ra + rb;
        }

        /// <summary>Shortest distance between two rectangles; 0 when they overlap. Exact for convex
        /// shapes: the minimum is always reached at a corner of one of them.</summary>
        public float DistanceTo(OrientedRect o)
        {
            if (Overlaps(o)) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                best = Mathf.Min(best, o.DistanceToPoint(Corner(i)));
                best = Mathf.Min(best, DistanceToPoint(o.Corner(i)));
            }
            return best;
        }
    }

    /// <summary>Plain geometry used by the evaluators. No scene access, no allocation.</summary>
    public static class SpatialGeometry
    {
        public static Vector2 XZ(Vector3 v) { return new Vector2(v.x, v.z); }

        public static bool PointInPolygon(Vector2[] poly, Vector2 p)
        {
            if (poly == null || poly.Length < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-12f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Distance from a point to the nearest edge of a polygon (regardless of inside/outside).</summary>
        public static float DistanceToPolygonEdge(Vector2[] poly, Vector2 p)
        {
            float best = float.MaxValue;
            if (poly == null) return best;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                best = Mathf.Min(best, DistanceToSegment(p, poly[i], poly[j]));
            return best;
        }

        /// <summary>True when the whole rectangle lies inside the polygon and at least <paramref name="margin"/>
        /// from its edges. <paramref name="edgeDistance"/> is the smallest corner-to-edge distance.</summary>
        public static bool RectInsidePolygon(Vector2[] poly, OrientedRect rect, float margin, out float edgeDistance)
        {
            edgeDistance = float.MaxValue;
            if (poly == null || poly.Length < 3) { edgeDistance = 0f; return false; }
            for (int i = 0; i < 4; i++)
            {
                Vector2 c = rect.Corner(i);
                if (!PointInPolygon(poly, c)) { edgeDistance = 0f; return false; }
                edgeDistance = Mathf.Min(edgeDistance, DistanceToPolygonEdge(poly, c));
            }
            // Concave outlines: a wall corner poking into the rectangle.
            for (int i = 0; i < poly.Length; i++)
                if (rect.Contains(poly[i])) { edgeDistance = 0f; return false; }
            return edgeDistance >= margin;
        }

        /// <summary>True when the straight segment a-b passes through the furniture box grown by
        /// <paramref name="inflateHorizontal"/> metres sideways and <paramref name="inflateVertical"/> up and down.</summary>
        public static bool SegmentHitsBox(Vector3 a, Vector3 b, FurnitureBox box, float inflateHorizontal, float inflateVertical)
        {
            OrientedRect r = box.Footprint;
            Vector3 la = ToBoxLocal(a, r), lb = ToBoxLocal(b, r);
            var mn = new Vector3(-r.half.x - inflateHorizontal, box.minY - inflateVertical, -r.half.y - inflateHorizontal);
            var mx = new Vector3(r.half.x + inflateHorizontal, box.maxY + inflateVertical, r.half.y + inflateHorizontal);
            return SegmentAabb(la, lb, mn, mx);
        }

        static Vector3 ToBoxLocal(Vector3 p, OrientedRect r)
        {
            Vector2 d = new Vector2(p.x, p.z) - r.center;
            return new Vector3(Vector2.Dot(d, r.axisX), p.y, Vector2.Dot(d, r.axisY));
        }

        static bool SegmentAabb(Vector3 a, Vector3 b, Vector3 mn, Vector3 mx)
        {
            float t0 = 0f, t1 = 1f;
            Vector3 d = b - a;
            for (int i = 0; i < 3; i++)
            {
                float o = a[i], di = d[i], lo = mn[i], hi = mx[i];
                if (Mathf.Abs(di) < 1e-7f)
                {
                    if (o < lo || o > hi) return false;
                }
                else
                {
                    float inv = 1f / di;
                    float ta = (lo - o) * inv, tb = (hi - o) * inv;
                    if (ta > tb) { float tmp = ta; ta = tb; tb = tmp; }
                    if (ta > t0) t0 = ta;
                    if (tb < t1) t1 = tb;
                    if (t0 > t1) return false;
                }
            }
            return true;
        }

        /// <summary>Index of the floor (or ceiling) outline containing the XZ point, or -1.</summary>
        public static int FindFloor(IRoomGeometry room, Vector2 p)
        {
            for (int i = 0; i < room.FloorCount; i++)
                if (PointInPolygon(room.GetFloor(i).polygon, p)) return i;
            return -1;
        }

        public static int FindCeiling(IRoomGeometry room, Vector2 p)
        {
            for (int i = 0; i < room.CeilingCount; i++)
                if (PointInPolygon(room.GetCeiling(i).polygon, p)) return i;
            return -1;
        }

        /// <summary>The office footprint on the ground for a root pose. The office is not centred on its
        /// root: <paramref name="centerOffset"/> is the footprint centre in root-local XZ at scale 1.</summary>
        public static OrientedRect Footprint(Vector3 rootPosition, Quaternion rootRotation, float scale,
                                             Vector2 footprint, Vector2 centerOffset)
        {
            Vector3 c = rootPosition + rootRotation * new Vector3(centerOffset.x * scale, 0f, centerOffset.y * scale);
            return OrientedRect.FromYaw(new Vector2(c.x, c.z), footprint * (0.5f * scale), rootRotation.eulerAngles.y);
        }

        /// <summary>Largest horizontal distance from the office root to a corner of its footprint: the
        /// lever arm of the fastest-moving point when the office turns about its root. Same inputs
        /// as <see cref="Footprint"/>.</summary>
        public static float FootprintReach(Vector2 footprint, Vector2 centerOffset, float scale)
        {
            float x = Mathf.Abs(centerOffset.x) + Mathf.Abs(footprint.x) * 0.5f;
            float z = Mathf.Abs(centerOffset.y) + Mathf.Abs(footprint.y) * 0.5f;
            return Mathf.Sqrt(x * x + z * z) * Mathf.Abs(scale);
        }

        /// <summary>True when two vertical intervals overlap, touch, or are within <paramref name="margin"/>
        /// of each other. Touching counts (fail closed).</summary>
        public static bool VerticalRangesTouch(float minA, float maxA, float minB, float maxB, float margin)
        {
            return maxB >= minA - margin && minB <= maxA + margin;
        }

        /// <summary>Rotation whose +Z points from <paramref name="from"/> toward the user, yaw only
        /// (the project's facing convention: the root's +Z points AT the user).</summary>
        public static Quaternion FaceUserYaw(Vector3 from, Vector3 userHead)
        {
            Vector3 to = userHead - from;
            to.y = 0f;
            return to.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        public static string ReasonText(RoomAvailability a)
        {
            switch (a)
            {
                case RoomAvailability.Available: return "Room scan available";
                case RoomAvailability.NotLoadedYet: return "Room scan not loaded yet";
                case RoomAvailability.NoRoomSetup: return "No room scan on this headset";
                case RoomAvailability.PermissionDenied: return "Room scan permission was not given";
                case RoomAvailability.InvalidFloor: return "The room scan has no usable floor";
                case RoomAvailability.NoCeiling: return "The room scan has no ceiling";
                default: return "Room scan not available";
            }
        }
    }
}
