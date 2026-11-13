using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>Tunable numbers of the floor search. All distances in metres, angles in degrees.</summary>
    [Serializable]
    public sealed class FloorPlacementSettings
    {
        [Header("Where to look (sideward sectors around the seated user)")]
        public float minDistance = 1.0f;
        public float maxDistance = 2.2f;
        public float distanceStep = 0.3f;
        public float preferredDistance = 1.4f;
        [Tooltip("Sector measured from the user's forward direction toward each side.")]
        public float sectorStartDeg = 50f;
        public float sectorEndDeg = 130f;
        public float sectorStepDeg = 20f;
        [Tooltip("+1 prefers the user's right, -1 the left, 0 neither.")]
        public int preferredSide = 1;

        [Header("Rejections")]
        [Tooltip("Half angle of the forward cone (toward the user's desk and computer) that is never used.")]
        public float forwardConeHalfAngleDeg = 35f;
        [Tooltip("Keep the office at least this far from the user's feet and chair.")]
        public float userClearance = 0.6f;
        public float furnitureClearance = 0.15f;
        [Tooltip("Keep the footprint at least this far from the floor outline (walls).")]
        public float wallMargin = 0.10f;

        [Header("Office footprint")]
        [Tooltip("Footprint centre in root-local XZ at scale 1 (the office is not centred on its root).")]
        public Vector2 footprintCenterOffset = new Vector2(0f, 0.12f);
        [Tooltip("Height of the office above its root at scale 1 (furniture lower than the root is ignored).")]
        public float officeHeight = 0.45f;
        [Tooltip("Height above the root that the user looks at, at scale 1.")]
        public float viewTargetHeight = 0.15f;
        [Tooltip("Uniform scales to try. Values outside minScale..maxScale are skipped.")]
        public float[] scaleOptions = { 1f };
        public float minScale = 0.8f;
        public float maxScale = 1.5f;

        [Header("Scoring")]
        public float comfortLookDownMinDeg = 15f;
        public float comfortLookDownMaxDeg = 50f;
        public float weightVisibility = 0.35f;
        public float weightClearance = 0.25f;
        public float weightSide = 0.15f;
        public float weightDistance = 0.15f;
        public float weightLookDown = 0.10f;

        [Header("Result")]
        public int maxCandidates = 6;
        [Tooltip("Returned candidates are at least this far apart, so cycling shows different places.")]
        public float minCandidateSeparation = 0.35f;
    }

    /// <summary>One place the office could stand on the detected floor.</summary>
    public struct FloorCandidate
    {
        public Vector3 position;        // root position, on the floor
        public Quaternion rotation;     // root +Z toward the user
        public float scale;
        public float score;             // 0..1
        public int side;                // +1 right, -1 left
        public float distance;          // horizontal, from the user's feet
        public float clearance;         // to the nearest furniture or wall (capped at 1 m)
        public float lookDownDeg;
        public float visibility;        // 0..1 share of unobstructed sight lines
        public int order;               // generation order: deterministic tie-break
        public string explanation;

        public RootPose Pose => new RootPose(position, rotation, scale);
    }

    /// <summary>Why candidates were rejected; used for the status text and by tests.</summary>
    public sealed class FloorEvaluationReport
    {
        public RoomAvailability availability;
        public int generated, offFloor, footprintOffFloor, furniture, nearUser, forwardCone, hidden, accepted;

        public void Clear()
        {
            availability = RoomAvailability.Available;
            generated = offFloor = footprintOffFloor = furniture = nearUser = forwardCone = hidden = accepted = 0;
        }

        public string Summary()
        {
            if (availability != RoomAvailability.Available) return SpatialGeometry.ReasonText(availability);
            if (accepted > 0)
                return string.Format(CultureInfo.InvariantCulture, "{0} floor spots found", accepted);
            return string.Format(CultureInfo.InvariantCulture,
                "No clear floor spot: {0} tried, {1} off the floor, {2} too close to walls, {3} blocked by furniture, {4} too close to you, {5} hidden",
                generated, offFloor, footprintOffFloor, furniture, nearUser, hidden);
        }
    }

    /// <summary>
    /// Finds places on the DETECTED floor where the miniature office can stand beside the seated
    /// user. Pure and deterministic: same room, user and settings give the same ranked list. It
    /// never invents a floor: with no usable room data the list is empty and the report says why.
    /// </summary>
    public static class FloorPlacementEvaluator
    {
        static readonly Comparison<FloorCandidate> ByScore = (a, b) =>
        {
            int c = b.score.CompareTo(a.score);
            return c != 0 ? c : a.order.CompareTo(b.order);
        };

        static readonly List<FloorCandidate> Scratch = new List<FloorCandidate>(64);

        public static void Evaluate(IRoomGeometry room, SeatedUser user, Vector2 footprint, FloorPlacementSettings s,
                                    List<FloorCandidate> results, FloorEvaluationReport report)
        {
            results.Clear();
            if (report == null) report = new FloorEvaluationReport();
            report.Clear();
            if (room == null) { report.availability = RoomAvailability.NotLoadedYet; return; }
            report.availability = room.Availability;
            if (!room.HasRoom || room.Availability != RoomAvailability.Available || room.FloorCount == 0)
            {
                if (report.availability == RoomAvailability.Available) report.availability = RoomAvailability.InvalidFloor;
                return;
            }

            Scratch.Clear();
            Vector3 right = user.Right;
            int order = 0;
            float[] scales = (s.scaleOptions != null && s.scaleOptions.Length > 0) ? s.scaleOptions : null;
            int scaleCount = scales != null ? scales.Length : 1;
            float angStep = Mathf.Max(1f, s.sectorStepDeg), distStep = Mathf.Max(0.05f, s.distanceStep);

            for (int sideIdx = 0; sideIdx < 2; sideIdx++)
            {
                int side = sideIdx == 0 ? 1 : -1;
                for (float ang = s.sectorStartDeg; ang <= s.sectorEndDeg + 0.01f; ang += angStep)
                {
                    float rad = ang * Mathf.Deg2Rad;
                    Vector3 dir = user.forward * Mathf.Cos(rad) + right * (side * Mathf.Sin(rad));
                    for (float dist = s.minDistance; dist <= s.maxDistance + 0.001f; dist += distStep)
                    {
                        // Best scale at this spot.
                        bool have = false;
                        FloorCandidate best = default(FloorCandidate);
                        for (int k = 0; k < scaleCount; k++)
                        {
                            float scale = scales != null ? scales[k] : 1f;
                            if (scale < s.minScale || scale > s.maxScale) continue;
                            report.generated++;
                            Vector3 flat = new Vector3(user.head.x, 0f, user.head.z) + dir * dist;
                            FloorCandidate c;
                            if (!TryBuild(room, user, flat, scale, footprint, s, report, out c)) continue;
                            c.side = side;
                            c.order = order++;
                            Score(ref c, s);
                            if (!have || c.score > best.score) { best = c; have = true; }
                        }
                        if (have) Scratch.Add(best);
                    }
                }
            }

            Scratch.Sort(ByScore);
            float sep2 = s.minCandidateSeparation * s.minCandidateSeparation;
            for (int i = 0; i < Scratch.Count && results.Count < Mathf.Max(1, s.maxCandidates); i++)
            {
                var c = Scratch[i];
                bool tooClose = false;
                for (int j = 0; j < results.Count; j++)
                    if ((results[j].position - c.position).sqrMagnitude < sep2) { tooClose = true; break; }
                if (tooClose) continue;
                c.explanation = Explain(c);
                results.Add(c);
            }
            report.accepted = results.Count;
            Scratch.Clear();
        }

        static bool TryBuild(IRoomGeometry room, SeatedUser user, Vector3 flat, float scale, Vector2 footprint,
                             FloorPlacementSettings s, FloorEvaluationReport report, out FloorCandidate c)
        {
            c = default(FloorCandidate);
            int floor = SpatialGeometry.FindFloor(room, new Vector2(flat.x, flat.z));
            if (floor < 0) { report.offFloor++; return false; }
            RoomPlane plane = room.GetFloor(floor);
            Vector3 pos = new Vector3(flat.x, plane.height, flat.z);
            Quaternion rot = SpatialGeometry.FaceUserYaw(pos, user.head);

            float clearance; int why;
            if (!CheckPose(room, user, plane, pos, rot, scale, footprint, s, true, out clearance, out why))
            {
                if (why == RejectFootprint) report.footprintOffFloor++;
                else if (why == RejectFurniture) report.furniture++;
                else if (why == RejectNearUser) report.nearUser++;
                else if (why == RejectCone) report.forwardCone++;
                return false;
            }

            float visibility = Visibility(room, user, pos, rot, scale, footprint, s);
            if (visibility <= 0f) { report.hidden++; return false; }

            Vector3 target = pos + Vector3.up * (s.viewTargetHeight * scale);
            float dx = target.x - user.head.x, dz = target.z - user.head.z;
            float horiz = Mathf.Sqrt(dx * dx + dz * dz);

            c.position = pos;
            c.rotation = rot;
            c.scale = scale;
            c.distance = horiz;
            c.clearance = clearance;
            c.visibility = visibility;
            c.lookDownDeg = Mathf.Atan2(user.head.y - target.y, Mathf.Max(0.01f, horiz)) * Mathf.Rad2Deg;
            return true;
        }

        const int RejectNone = 0, RejectFootprint = 1, RejectFurniture = 2, RejectNearUser = 3, RejectCone = 4;

        static bool CheckPose(IRoomGeometry room, SeatedUser user, RoomPlane plane, Vector3 pos, Quaternion rot, float scale,
                              Vector2 footprint, FloorPlacementSettings s, bool checkUser, out float clearance, out int why)
        {
            why = RejectNone;
            clearance = 0f;
            OrientedRect rect = SpatialGeometry.Footprint(pos, rot, scale, footprint, s.footprintCenterOffset);

            float wall;
            if (!SpatialGeometry.RectInsidePolygon(plane.polygon, rect, s.wallMargin, out wall)) { why = RejectFootprint; return false; }
            clearance = wall;

            float top = plane.height + s.officeHeight * scale;
            for (int i = 0; i < room.FurnitureCount; i++)
            {
                FurnitureBox box = room.GetFurniture(i);
                if (box.maxY <= plane.height + 0.02f || box.minY >= top) continue;   // not at office height
                float d = rect.DistanceTo(box.Footprint);
                if (d < s.furnitureClearance) { why = RejectFurniture; return false; }
                if (d < clearance) clearance = d;
            }
            if (clearance > 1f) clearance = 1f;

            if (checkUser)
            {
                Vector2 feet = new Vector2(user.head.x, user.head.z);
                if (rect.DistanceToPoint(feet) < s.userClearance) { why = RejectNearUser; return false; }
                Vector3 to = pos - user.head; to.y = 0f;
                if (to.sqrMagnitude > 1e-6f && Vector3.Angle(user.forward, to) < s.forwardConeHalfAngleDeg) { why = RejectCone; return false; }
            }
            return true;
        }

        /// <summary>Share of three sight lines (centre and both ends of the office) not blocked by furniture.</summary>
        static float Visibility(IRoomGeometry room, SeatedUser user, Vector3 pos, Quaternion rot, float scale,
                                Vector2 footprint, FloorPlacementSettings s)
        {
            Vector3 up = Vector3.up * (s.viewTargetHeight * scale);
            Vector3 mid = pos + rot * new Vector3(s.footprintCenterOffset.x * scale, 0f, s.footprintCenterOffset.y * scale) + up;
            Vector3 half = rot * new Vector3(footprint.x * 0.4f * scale, 0f, 0f);
            int clear = 0;
            if (!Blocked(room, user.head, mid)) clear++;
            if (!Blocked(room, user.head, mid + half)) clear++;
            if (!Blocked(room, user.head, mid - half)) clear++;
            return clear / 3f;
        }

        static bool Blocked(IRoomGeometry room, Vector3 from, Vector3 to)
        {
            for (int i = 0; i < room.FurnitureCount; i++)
                if (SpatialGeometry.SegmentHitsBox(from, to, room.GetFurniture(i), 0f, 0f)) return true;
            return false;
        }

        static void Score(ref FloorCandidate c, FloorPlacementSettings s)
        {
            float clearance = Mathf.Clamp01(c.clearance / 0.5f);
            float side = s.preferredSide == 0 ? 1f : (c.side == s.preferredSide ? 1f : 0.5f);
            float range = Mathf.Max(0.1f, s.maxDistance - s.minDistance);
            float distance = 1f - Mathf.Clamp01(Mathf.Abs(c.distance - s.preferredDistance) / range);
            float look;
            if (c.lookDownDeg < s.comfortLookDownMinDeg) look = Mathf.Clamp01(c.lookDownDeg / Mathf.Max(1f, s.comfortLookDownMinDeg));
            else if (c.lookDownDeg > s.comfortLookDownMaxDeg) look = Mathf.Clamp01(1f - (c.lookDownDeg - s.comfortLookDownMaxDeg) / 30f);
            else look = 1f;
            float total = s.weightVisibility + s.weightClearance + s.weightSide + s.weightDistance + s.weightLookDown;
            c.score = (s.weightVisibility * c.visibility + s.weightClearance * clearance + s.weightSide * side
                       + s.weightDistance * distance + s.weightLookDown * look) / Mathf.Max(1e-4f, total);
        }

        static string Explain(FloorCandidate c)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} side, {1:F1} m away, scale {2:F2}, {3:F2} m clear, {4:F0} deg look-down, {5:F0}% in view, score {6:F2}",
                c.side > 0 ? "Right" : "Left", c.distance, c.scale, c.clearance, c.lookDownDeg, c.visibility * 100f, c.score);
        }

        /// <summary>
        /// Checks one specific root pose against the detected floor (a nudged candidate or a saved
        /// placement). <paramref name="checkUser"/> also applies the user-clearance and forward-cone
        /// rules. Returns false with a reason in words when the pose is not on clear detected floor.
        /// </summary>
        public static bool ValidatePose(IRoomGeometry room, SeatedUser user, RootPose pose, Vector2 footprint,
                                        FloorPlacementSettings s, bool checkUser, out string reason)
        {
            reason = null;
            if (room == null || !room.HasRoom || room.Availability != RoomAvailability.Available)
            {
                reason = SpatialGeometry.ReasonText(room == null ? RoomAvailability.NotLoadedYet : room.Availability);
                return false;
            }
            if (pose.scale < s.minScale || pose.scale > s.maxScale) { reason = "Scale outside the safe range"; return false; }
            int floor = SpatialGeometry.FindFloor(room, new Vector2(pose.position.x, pose.position.z));
            if (floor < 0) { reason = "Not on the detected floor"; return false; }
            RoomPlane plane = room.GetFloor(floor);
            if (Mathf.Abs(pose.position.y - plane.height) > 0.10f) { reason = "Not at floor height"; return false; }
            float clearance; int why;
            if (CheckPose(room, user, plane, pose.position, pose.rotation, pose.scale, footprint, s, checkUser, out clearance, out why))
                return true;
            switch (why)
            {
                case RejectFootprint: reason = "Too close to a wall or off the detected floor"; break;
                case RejectFurniture: reason = "Too close to furniture"; break;
                case RejectNearUser: reason = "Too close to your chair"; break;
                case RejectCone: reason = "In front of your desk"; break;
                default: reason = "Not a clear floor spot"; break;
            }
            return false;
        }
    }
}
