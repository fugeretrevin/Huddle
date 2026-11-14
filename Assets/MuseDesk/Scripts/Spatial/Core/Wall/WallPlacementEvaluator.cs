using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>The two habitat layouts. One code path builds both (HabitatLayoutBuilder).</summary>
    public enum WallLayoutKind { Compact = 0, Full = 1 }

    /// <summary>Why a wall spot was refused (or None). Ordered roughly by how fundamental the problem is.</summary>
    public enum WallProblem
    {
        None = 0,
        NoRoom = 1,
        NoWalls = 2,
        InvisibleWall = 3,
        WallTooSmall = 4,
        OutsideWall = 5,
        TooCloseToEdge = 6,
        TooLow = 7,
        TooHigh = 8,
        OverlapsDoor = 9,
        OverlapsWindow = 10,
        HitsFurniture = 11,
        HitsOtherWall = 12,
        FacingAway = 13,
        ViewAngleTooSteep = 14,
        TooCloseToUser = 15,
        TooFar = 16,
        OverMonitor = 17,
        Unverified = 18,
    }

    [Serializable]
    public sealed class WallPlacementSettings
    {
        [Tooltip("Full 3-level habitat: width, height, depth from the wall (m).")]
        public Vector3 fullSize = new Vector3(1.05f, 0.90f, 0.26f);
        [Tooltip("Compact 2-level habitat: width, height, depth (m).")]
        public Vector3 compactSize = new Vector3(0.74f, 0.62f, 0.22f);
        [Tooltip("Keep this far from the wall's side, top and bottom edges (m).")]
        public float edgeInset = 0.08f;
        [Tooltip("Keep this far from a door or window rectangle (m).")]
        public float openingMargin = 0.10f;
        [Tooltip("The habitat volume grown by this much must not touch scanned furniture (m).")]
        public float furnitureMargin = 0.06f;
        [Tooltip("The habitat volume must stay this far from the user's head (m).")]
        public float minHeadDistance = 0.75f;
        [Tooltip("Farther than this (head to habitat) and it is too small to read (m).")]
        public float maxHeadDistance = 3.4f;
        public float preferredDistance = 1.6f;
        [Tooltip("A habitat centred within this horizontal angle of the user's forward sits over the monitor: refused (deg).")]
        public float monitorConeDeg = 15f;
        [Tooltip("Within this angle it is allowed but scored lower (deg).")]
        public float monitorSoftDeg = 32f;
        [Tooltip("Angle between the wall normal and the direction to the user above which the wall is seen too edge-on (deg).")]
        public float maxViewAngleDeg = 72f;
        [Tooltip("Habitat centre height relative to the seated eye: allowed range and preferred value (m).")]
        public float minCenterAboveEye = -0.25f;
        public float maxCenterAboveEye = 0.55f;
        public float preferredCenterAboveEye = 0.15f;
        [Tooltip("Search step along the wall (m).")]
        public float uStep = 0.12f;
        [Tooltip("Centre heights tried, relative to the eye (m).")]
        public float[] heightOffsets = { 0.15f, 0.30f, 0.0f, 0.45f, -0.12f };
        [Tooltip("Two offered spots on the same wall are at least this far apart (m).")]
        public float minCandidateSpacing = 0.35f;
        [Tooltip("At most this many spots are offered.")]
        public int maxCandidates = 8;
        [Tooltip("Small offset off the wall surface so the backplate never z-fights the real wall (m).")]
        public float wallStandoff = 0.004f;
        [Tooltip("Margins below this multiple of the required margin mark the spot as 'needs review' (amber).")]
        public float reviewMarginFactor = 2.0f;

        public Vector3 SizeOf(WallLayoutKind k) { return k == WallLayoutKind.Full ? fullSize : compactSize; }
    }

    /// <summary>One evaluated wall spot. position/rotation are the habitat root's pose: the centre of the
    /// backplate on the wall surface, +Z out of the wall.</summary>
    public struct WallCandidate
    {
        public int wallIndex;
        public string wallId;
        public WallLayoutKind layout;
        public float u;
        public float centerY;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 size;
        public float score;
        public bool valid;
        /// <summary>Valid but close to a limit, or checked with incomplete room data: shown amber.</summary>
        public bool needsReview;
        /// <summary>True when the room scan was available and every check could run.</summary>
        public bool roomChecked;
        public WallProblem problem;
        public string reason;
        /// <summary>Smallest clearance found to an opening, furniture or another wall (m), for the review flag.</summary>
        public float minClearance;

        public Pose Pose => new Pose(position, rotation);
        /// <summary>Centre of the habitat volume (half the depth out from the wall).</summary>
        public Vector3 VolumeCenter => position + rotation * new Vector3(0f, 0f, size.z * 0.5f);
    }

    /// <summary>
    /// Pure scoring of habitat spots on scanned walls. No scene access. Never assumes unscanned space
    /// is empty: a spot is "roomChecked" only against what the scan contains, and the UI says so.
    /// </summary>
    public static class WallPlacementEvaluator
    {
        /// <summary>Evaluate the habitat centred at (u, centerY) on wall <paramref name="wallIndex"/>.</summary>
        public static WallCandidate Evaluate(IRoomGeometry room, IWallGeometry walls, int wallIndex, float u, float centerY,
                                             WallLayoutKind layout, SeatedUser user, WallPlacementSettings s)
        {
            var c = new WallCandidate { wallIndex = wallIndex, layout = layout, u = u, centerY = centerY, size = s.SizeOf(layout), minClearance = float.MaxValue };
            if (walls == null || walls.WallAvailability != RoomAvailability.Available)
                return Fail(ref c, walls == null ? WallProblem.NoRoom : (walls.WallAvailability == RoomAvailability.Available ? WallProblem.NoWalls : WallProblem.NoRoom),
                            walls == null ? "No room scan" : SpatialGeometry.ReasonText(walls.WallAvailability));
            if (wallIndex < 0 || wallIndex >= walls.WallCount) return Fail(ref c, WallProblem.NoWalls, "No wall here");

            RoomWall w = walls.GetWall(wallIndex);
            c.wallId = w.id;
            c.rotation = w.Rotation;
            c.position = w.PointAt(u, centerY) + w.normal * s.wallStandoff;
            c.roomChecked = room != null && room.Availability == RoomAvailability.Available;

            float hw = c.size.x * 0.5f, hh = c.size.y * 0.5f, d = c.size.z;
            if (w.invisible) return Fail(ref c, WallProblem.InvisibleWall, "This wall was drawn as open space");
            if (w.width < c.size.x + 2f * s.edgeInset || w.Height < c.size.y + 2f * s.edgeInset)
                return Fail(ref c, WallProblem.WallTooSmall, "Wall too small for this layout");
            if (Mathf.Abs(u) + hw > w.width * 0.5f) return Fail(ref c, WallProblem.OutsideWall, "Runs past the end of the wall");
            if (Mathf.Abs(u) + hw > w.width * 0.5f - s.edgeInset) return Fail(ref c, WallProblem.TooCloseToEdge, "Too close to the wall's corner");
            if (centerY - hh < w.bottomY + s.edgeInset) return Fail(ref c, WallProblem.TooLow, "Too close to the floor");
            if (centerY + hh > w.topY - s.edgeInset) return Fail(ref c, WallProblem.TooHigh, "Too close to the ceiling");

            float eyeRel = centerY - user.head.y;
            if (eyeRel < s.minCenterAboveEye) return Fail(ref c, WallProblem.TooLow, "Too low to see from your seat");
            if (eyeRel > s.maxCenterAboveEye) return Fail(ref c, WallProblem.TooHigh, "Too high to see comfortably");

            // Doors and windows on this wall.
            for (int i = 0; i < walls.OpeningCount; i++)
            {
                WallOpening o = walls.GetOpening(i);
                if (o.wallId != w.id) continue;
                if (o.Overlaps(u - hw, u + hw, centerY - hh, centerY + hh, s.openingMargin))
                    return Fail(ref c, o.kind == WallOpeningKind.Window ? WallProblem.OverlapsWindow : WallProblem.OverlapsDoor,
                                o.kind == WallOpeningKind.Window ? "Covers a window" : "Covers a door");
                c.minClearance = Mathf.Min(c.minClearance, o.Gap(u - hw, u + hw, centerY - hh, centerY + hh));
            }

            // The facing test comes before volume tests: a wall seen from behind is never offered.
            Vector3 vc = c.VolumeCenter;
            Vector3 toUser = user.head - vc;
            toUser.y = 0f;
            if (w.SignedDistance(user.head) <= 0.05f || toUser.sqrMagnitude < 1e-6f) return Fail(ref c, WallProblem.FacingAway, "This wall faces away from you");
            float viewAngle = Vector3.Angle(w.normal, toUser.normalized);
            if (viewAngle > s.maxViewAngleDeg) return Fail(ref c, WallProblem.ViewAngleTooSteep, "You would see it edge-on");

            // Habitat volume against scanned furniture (grown by the margin).
            OrientedRect foot = OrientedRect.FromYaw(new Vector2(vc.x, vc.z), new Vector2(hw, d * 0.5f), c.rotation.eulerAngles.y);
            if (room != null && c.roomChecked)
            {
                for (int i = 0; i < room.FurnitureCount; i++)
                {
                    FurnitureBox f = room.GetFurniture(i);
                    if (!SpatialGeometry.VerticalRangesTouch(centerY - hh, centerY + hh, f.minY, f.maxY, s.furnitureMargin)) continue;
                    OrientedRect fr = f.Footprint;
                    float gap = foot.DistanceTo(fr);
                    if (gap <= s.furnitureMargin) return Fail(ref c, WallProblem.HitsFurniture, "Furniture is in the way" + (string.IsNullOrEmpty(f.label) ? "" : " (" + Pretty(f.label) + ")"));
                    c.minClearance = Mathf.Min(c.minClearance, gap);
                }
            }

            // Another wall cutting through the volume (alcoves, short return walls).
            for (int i = 0; i < walls.WallCount; i++)
            {
                if (i == wallIndex) continue;
                RoomWall o = walls.GetWall(i);
                if (!SpatialGeometry.VerticalRangesTouch(centerY - hh, centerY + hh, o.bottomY, o.topY, 0f)) continue;
                Vector2 a, b;
                o.Ends(out a, out b);
                if (WallMath.SegmentHitsRect(a, b, foot.Inflated(-0.01f)))
                    return Fail(ref c, WallProblem.HitsOtherWall, "Another wall is in the way");
            }

            // The user's head and the monitor in front of them.
            float headDist = WallMath.DistanceToBox(user.head, vc, c.rotation, new Vector3(hw, hh, d * 0.5f));
            if (headDist < s.minHeadDistance) return Fail(ref c, WallProblem.TooCloseToUser, "Too close to your head");
            if (headDist > s.maxHeadDistance) return Fail(ref c, WallProblem.TooFar, "Too far away to see");
            float fromForward = WallMath.HorizontalAngleFromForward(user, vc);
            if (fromForward < s.monitorConeDeg) return Fail(ref c, WallProblem.OverMonitor, "Straight ahead, over your screen");

            // Valid: score it.
            c.valid = true;
            c.problem = WallProblem.None;
            c.score = Score(s, headDist, viewAngle, fromForward, eyeRel, c.minClearance);
            float review = Mathf.Max(s.openingMargin, s.furnitureMargin) * s.reviewMarginFactor;
            c.needsReview = !c.roomChecked || c.minClearance < review || viewAngle > s.maxViewAngleDeg - 15f || fromForward < s.monitorSoftDeg;
            c.reason = !c.roomChecked ? "Room scan incomplete: check the spot yourself"
                     : c.needsReview ? "Fits, but close to something: please check"
                     : "Fits this wall";
            return c;
        }

        static string Pretty(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            return label.Replace('_', ' ').ToLowerInvariant();
        }

        static float Score(WallPlacementSettings s, float headDist, float viewAngle, float fromForward, float eyeRel, float clearance)
        {
            float distTerm = 1f - Mathf.Clamp01(Mathf.Abs(headDist - s.preferredDistance) / Mathf.Max(0.1f, s.preferredDistance));
            float viewTerm = Mathf.Cos(viewAngle * Mathf.Deg2Rad);
            // Side preference: best between 35 and 80 degrees off forward; behind the shoulder is worse.
            float side;
            if (fromForward < 35f) side = Mathf.InverseLerp(s.monitorConeDeg, 35f, fromForward);
            else if (fromForward <= 80f) side = 1f;
            else side = Mathf.Lerp(1f, 0.2f, Mathf.InverseLerp(80f, 150f, fromForward));
            float heightTerm = 1f - Mathf.Clamp01(Mathf.Abs(eyeRel - s.preferredCenterAboveEye) / 0.4f);
            float clearTerm = clearance >= 1e6f ? 1f : Mathf.Clamp01(clearance / 0.4f);
            return 0.28f * distTerm + 0.22f * viewTerm + 0.25f * side + 0.15f * heightTerm + 0.10f * clearTerm;
        }

        static WallCandidate Fail(ref WallCandidate c, WallProblem p, string reason)
        {
            c.valid = false;
            c.needsReview = false;
            c.problem = p;
            c.reason = reason;
            c.score = 0f;
            return c;
        }

        /// <summary>
        /// Search every wall for valid spots (both layouts unless <paramref name="onlyLayout"/> is given),
        /// best score first, spaced apart. Empty when nothing fits (the caller then offers manual
        /// placement or stays in Desk). Allocates the result list only.
        /// </summary>
        public static List<WallCandidate> FindCandidates(IRoomGeometry room, IWallGeometry walls, SeatedUser user,
                                                         WallPlacementSettings s, WallLayoutKind? onlyLayout = null)
        {
            var all = new List<WallCandidate>();
            if (walls == null || walls.WallAvailability != RoomAvailability.Available) return all;
            for (int wi = 0; wi < walls.WallCount; wi++)
            {
                RoomWall w = walls.GetWall(wi);
                for (int li = 1; li >= 0; li--)
                {
                    var layout = (WallLayoutKind)li;
                    if (onlyLayout.HasValue && onlyLayout.Value != layout) continue;
                    Vector3 size = s.SizeOf(layout);
                    float span = w.width * 0.5f - size.x * 0.5f - s.edgeInset;
                    if (span < 0f) continue;
                    int steps = Mathf.Max(0, Mathf.FloorToInt(span / Mathf.Max(0.02f, s.uStep)));
                    for (int k = -steps; k <= steps; k++)
                    {
                        float u = k * s.uStep;
                        for (int h = 0; h < s.heightOffsets.Length; h++)
                        {
                            WallCandidate c = Evaluate(room, walls, wi, u, user.head.y + s.heightOffsets[h], layout, user, s);
                            if (c.valid) all.Add(c);
                        }
                    }
                }
            }
            // Full layout preferred when it fits equally well (it shows the elevator and manager better).
            all.Sort((a, b) =>
            {
                float sa = a.score + (a.layout == WallLayoutKind.Full ? 0.03f : 0f) - (a.needsReview ? 0.05f : 0f);
                float sb = b.score + (b.layout == WallLayoutKind.Full ? 0.03f : 0f) - (b.needsReview ? 0.05f : 0f);
                return sb.CompareTo(sa);
            });
            var picked = new List<WallCandidate>();
            for (int i = 0; i < all.Count && picked.Count < s.maxCandidates; i++)
            {
                bool tooClose = false;
                for (int j = 0; j < picked.Count; j++)
                    if (picked[j].wallIndex == all[i].wallIndex && (picked[j].VolumeCenter - all[i].VolumeCenter).magnitude < s.minCandidateSpacing) { tooClose = true; break; }
                if (!tooClose) picked.Add(all[i]);
            }
            return picked;
        }

        /// <summary>
        /// Hand-ray placement: the spot under the ray on whichever wall it hits, for the given layout.
        /// The ray hit is the habitat centre. Invalid spots come back with their reason (the ghost turns
        /// red). False when the ray hits no wall.
        /// </summary>
        public static bool FromRay(IRoomGeometry room, IWallGeometry walls, Vector3 origin, Vector3 dir, WallLayoutKind layout,
                                   SeatedUser user, WallPlacementSettings s, out WallCandidate result)
        {
            result = default(WallCandidate);
            int wi; float u, y, dist;
            if (!WallMath.Raycast(walls, origin, dir, 8f, out wi, out u, out y, out dist)) return false;
            result = Evaluate(room, walls, wi, u, y, layout, user, s);
            return true;
        }

        /// <summary>
        /// No usable room data: a manual spot in front-side of the user at <paramref name="distance"/>, on a
        /// virtual vertical plane facing them. Never "roomChecked": it is offered amber and the UI says
        /// nothing was checked. Rejects only what can be known without a scan (too close, over the monitor).
        /// </summary>
        public static WallCandidate Manual(SeatedUser user, float yawFromForwardDeg, float distance, float centerAboveEye,
                                           WallLayoutKind layout, WallPlacementSettings s)
        {
            var c = new WallCandidate { wallIndex = -1, wallId = "", layout = layout, size = s.SizeOf(layout), minClearance = float.MaxValue };
            Vector3 dir = Quaternion.AngleAxis(yawFromForwardDeg, Vector3.up) * user.forward;
            Vector3 center = user.head + dir * Mathf.Max(0.1f, distance);
            center.y = user.head.y + centerAboveEye;
            Vector3 normal = -dir; normal.y = 0f; normal.Normalize();
            c.rotation = Quaternion.LookRotation(normal, Vector3.up);
            c.position = center;
            c.centerY = center.y;
            c.roomChecked = false;
            Vector3 vc = c.VolumeCenter;
            float headDist = WallMath.DistanceToBox(user.head, vc, c.rotation, c.size * 0.5f);
            if (headDist < s.minHeadDistance) return Fail(ref c, WallProblem.TooCloseToUser, "Too close to your head");
            if (WallMath.HorizontalAngleFromForward(user, vc) < s.monitorConeDeg) return Fail(ref c, WallProblem.OverMonitor, "Straight ahead, over your screen");
            c.valid = true;
            c.needsReview = true;
            c.problem = WallProblem.Unverified;
            c.reason = "No room scan: nothing around it was checked. Place only on a clear wall.";
            c.score = 0f;
            return c;
        }

        /// <summary>Plain words for a problem (labels supplement the ghost colour).</summary>
        public static string Words(WallProblem p)
        {
            switch (p)
            {
                case WallProblem.None: return "Fits";
                case WallProblem.NoRoom: return "No room scan";
                case WallProblem.NoWalls: return "No walls in the room scan";
                case WallProblem.InvisibleWall: return "Open side of the room";
                case WallProblem.WallTooSmall: return "Wall too small";
                case WallProblem.OutsideWall: return "Past the end of the wall";
                case WallProblem.TooCloseToEdge: return "Too close to a corner";
                case WallProblem.TooLow: return "Too low";
                case WallProblem.TooHigh: return "Too high";
                case WallProblem.OverlapsDoor: return "Covers a door";
                case WallProblem.OverlapsWindow: return "Covers a window";
                case WallProblem.HitsFurniture: return "Furniture in the way";
                case WallProblem.HitsOtherWall: return "Another wall in the way";
                case WallProblem.FacingAway: return "Wall faces away";
                case WallProblem.ViewAngleTooSteep: return "Seen edge-on";
                case WallProblem.TooCloseToUser: return "Too close to you";
                case WallProblem.TooFar: return "Too far away";
                case WallProblem.OverMonitor: return "Over your screen";
                case WallProblem.Unverified: return "Not checked (no scan)";
                default: return p.ToString();
            }
        }
    }
}
