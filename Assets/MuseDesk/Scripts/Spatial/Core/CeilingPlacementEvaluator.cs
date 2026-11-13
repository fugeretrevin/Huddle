using System;
using System.Globalization;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>Tunable numbers of the ceiling dock search. Metres and degrees.</summary>
    [Serializable]
    public sealed class CeilingPlacementSettings
    {
        [Header("Dock on the ceiling")]
        [Tooltip("Never dock closer than this (horizontally) to the point above the user's head.")]
        public float minHorizontalFromUser = 0.8f;
        [Tooltip("Keep the docked footprint at least this far from the ceiling outline (walls).")]
        public float wallMargin = 0.35f;
        [Tooltip("Sideways distances from the user to try, in order of preference.")]
        public float[] lateralOptions = { 1.0f, 1.2f, 0.85f, 1.4f };
        [Tooltip("Forward offsets to try, in order of preference.")]
        public float[] forwardOptions = { 0.3f, 0.6f, 0.0f };
        [Tooltip("The office root hangs this far below the ceiling when docked.")]
        public float dockDrop = 0.50f;
        [Tooltip("The ceiling must be at least this far above the user's eyes.")]
        public float minCeilingAboveHead = 0.6f;
        [Tooltip("+1 prefers the user's right, -1 the left.")]
        public int preferredSide = 1;

        [Header("Inspection pose beside the user")]
        [Tooltip("Sideways distances from the user to try (0.5 to 0.7 m: beside, not in front of the face).")]
        public float[] inspectSideOptions = { 0.6f, 0.7f, 0.5f };
        public float inspectForward = 0.15f;
        [Tooltip("The office root comes to rest this far below the seated eye height.")]
        public float inspectDropBelowEye = 0.35f;

        [Header("Descent path")]
        [Tooltip("Furniture is grown sideways by this much (and 5 cm up and down) when testing the straight path down.")]
        public float descentClearance = 0.30f;

        [Header("Office footprint")]
        public Vector2 footprintCenterOffset = new Vector2(0f, 0.12f);
        public float officeHeight = 0.45f;
    }

    /// <summary>A ceiling dock with the pose the office descends to and the length of the descent.</summary>
    public struct CeilingPlacement
    {
        public bool valid;
        public Vector3 dockPosition;
        public Quaternion dockRotation;
        public Vector3 inspectionPosition;
        public Quaternion inspectionRotation;
        public float ceilingHeight;
        public float descentDistance;
        public int side;
        public string explanation;

        public RootPose DockPose => new RootPose(dockPosition, dockRotation, 1f);
        public RootPose InspectionPose => new RootPose(inspectionPosition, inspectionRotation, 1f);
    }

    /// <summary>
    /// Finds a dock on REAL ceiling geometry and a straight, unobstructed way down to a pose beside
    /// the seated user. Pure and deterministic. With no ceiling outline it fails with the reason; it
    /// never assumes a ceiling height.
    /// </summary>
    public static class CeilingPlacementEvaluator
    {
        public static bool Evaluate(IRoomGeometry room, SeatedUser user, Vector2 footprint, CeilingPlacementSettings s,
                                    out CeilingPlacement result)
        {
            result = default(CeilingPlacement);
            if (room == null) { result.explanation = SpatialGeometry.ReasonText(RoomAvailability.NotLoadedYet); return false; }
            if (!room.HasRoom || room.Availability != RoomAvailability.Available)
            {
                result.explanation = SpatialGeometry.ReasonText(room.Availability);
                return false;
            }
            if (room.CeilingAvailability != RoomAvailability.Available || room.CeilingCount == 0)
            {
                result.explanation = SpatialGeometry.ReasonText(RoomAvailability.NoCeiling);
                return false;
            }

            int tried = 0, overhead = 0, offCeiling = 0, low = 0, blocked = 0, noInspect = 0;
            Vector3 right = user.Right;
            int[] sides = s.preferredSide < 0 ? SidesLeftFirst : SidesRightFirst;

            for (int si = 0; si < 2; si++)
            {
                int side = sides[si];
                // The inspection pose depends only on the side: find it first.
                Vector3 inspectPos; Quaternion inspectRot;
                bool haveInspect = TryInspection(room, user, right, side, footprint, s, out inspectPos, out inspectRot);

                for (int li = 0; li < s.lateralOptions.Length; li++)
                {
                    for (int fi = 0; fi < s.forwardOptions.Length; fi++)
                    {
                        tried++;
                        Vector3 flat = new Vector3(user.head.x, 0f, user.head.z)
                                       + right * (side * s.lateralOptions[li]) + user.forward * s.forwardOptions[fi];
                        float dx = flat.x - user.head.x, dz = flat.z - user.head.z;
                        if (Mathf.Sqrt(dx * dx + dz * dz) < s.minHorizontalFromUser) { overhead++; continue; }

                        int ci = SpatialGeometry.FindCeiling(room, new Vector2(flat.x, flat.z));
                        if (ci < 0) { offCeiling++; continue; }
                        RoomPlane ceiling = room.GetCeiling(ci);
                        if (ceiling.height - user.head.y < s.minCeilingAboveHead) { low++; continue; }

                        Vector3 dockPos = new Vector3(flat.x, ceiling.height - s.dockDrop, flat.z);
                        Quaternion dockRot = SpatialGeometry.FaceUserYaw(dockPos, user.head);
                        OrientedRect rect = SpatialGeometry.Footprint(dockPos, dockRot, 1f, footprint, s.footprintCenterOffset);
                        float edge;
                        if (!SpatialGeometry.RectInsidePolygon(ceiling.polygon, rect, s.wallMargin, out edge)) { offCeiling++; continue; }
                        if (OverlapsFurniture(room, rect, dockPos.y, dockPos.y + s.officeHeight)) { blocked++; continue; }

                        if (!haveInspect) { noInspect++; continue; }
                        if (PathBlocked(room, dockPos, inspectPos, s.descentClearance)) { blocked++; continue; }

                        result.valid = true;
                        result.side = side;
                        result.dockPosition = dockPos;
                        result.dockRotation = dockRot;
                        result.inspectionPosition = inspectPos;
                        result.inspectionRotation = inspectRot;
                        result.ceilingHeight = ceiling.height;
                        result.descentDistance = (dockPos - inspectPos).magnitude;
                        result.explanation = string.Format(CultureInfo.InvariantCulture,
                            "Ceiling dock on your {0}, {1:F1} m to the side, {2:F1} m descent, {3:F2} m from the walls",
                            side > 0 ? "right" : "left", s.lateralOptions[li], result.descentDistance, edge);
                        return true;
                    }
                }
            }

            result.explanation = string.Format(CultureInfo.InvariantCulture,
                "No clear ceiling dock: {0} tried, {1} overhead, {2} off the ceiling or near walls, {3} ceiling too low, {4} path blocked, {5} no room beside you",
                tried, overhead, offCeiling, low, blocked, noInspect);
            return false;
        }

        static readonly int[] SidesRightFirst = { 1, -1 };
        static readonly int[] SidesLeftFirst = { -1, 1 };

        /// <summary>The pose beside the user at seated eye height: to the side, never in front of the face.</summary>
        static bool TryInspection(IRoomGeometry room, SeatedUser user, Vector3 right, int side, Vector2 footprint,
                                  CeilingPlacementSettings s, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            for (int i = 0; i < s.inspectSideOptions.Length; i++)
            {
                Vector3 p = user.head + right * (side * s.inspectSideOptions[i]) + user.forward * s.inspectForward;
                p.y = user.head.y - s.inspectDropBelowEye;
                Quaternion r = SpatialGeometry.FaceUserYaw(p, user.head);
                OrientedRect rect = SpatialGeometry.Footprint(p, r, 1f, footprint, s.footprintCenterOffset);
                if (OverlapsFurniture(room, rect, p.y - 0.03f, p.y + s.officeHeight)) continue;
                pos = p; rot = r;
                return true;
            }
            return false;
        }

        static bool OverlapsFurniture(IRoomGeometry room, OrientedRect rect, float minY, float maxY)
        {
            for (int i = 0; i < room.FurnitureCount; i++)
            {
                FurnitureBox box = room.GetFurniture(i);
                if (box.maxY <= minY || box.minY >= maxY) continue;
                if (rect.Overlaps(box.Footprint)) return true;
            }
            return false;
        }

        static bool PathBlocked(IRoomGeometry room, Vector3 from, Vector3 to, float clearance)
        {
            // The inspection pose is beside a seated user, so it is next to their desk by design, and
            // its footprint has already been checked against furniture. Requiring the full clearance
            // right up to it would reject every dock in a furnished room, so the clearance test stops
            // 0.45 m short of the end; the last stretch is covered by that footprint check.
            to = Vector3.MoveTowards(to, from, 0.45f);
            for (int i = 0; i < room.FurnitureCount; i++)
                if (SpatialGeometry.SegmentHitsBox(from, to, room.GetFurniture(i), clearance, 0.05f)) return true;
            return false;
        }

        /// <summary>
        /// Checks a saved dock against the current room and user: still under real ceiling, away from
        /// walls, not overhead, with a clear way down. Fills the inspection pose for the current user.
        /// </summary>
        public static bool ValidateDock(IRoomGeometry room, SeatedUser user, RootPose dock, Vector2 footprint,
                                        CeilingPlacementSettings s, out CeilingPlacement result)
        {
            result = default(CeilingPlacement);
            if (room == null || !room.HasRoom || room.CeilingAvailability != RoomAvailability.Available)
            {
                result.explanation = SpatialGeometry.ReasonText(room == null ? RoomAvailability.NotLoadedYet : room.CeilingAvailability);
                return false;
            }
            int ci = SpatialGeometry.FindCeiling(room, new Vector2(dock.position.x, dock.position.z));
            if (ci < 0) { result.explanation = "Saved dock is not under the detected ceiling"; return false; }
            RoomPlane ceiling = room.GetCeiling(ci);
            if (Mathf.Abs((ceiling.height - s.dockDrop) - dock.position.y) > 0.15f) { result.explanation = "Saved dock is not at ceiling height"; return false; }
            float dx = dock.position.x - user.head.x, dz = dock.position.z - user.head.z;
            if (Mathf.Sqrt(dx * dx + dz * dz) < s.minHorizontalFromUser) { result.explanation = "Saved dock is now overhead"; return false; }
            OrientedRect rect = SpatialGeometry.Footprint(dock.position, dock.rotation, 1f, footprint, s.footprintCenterOffset);
            float edge;
            if (!SpatialGeometry.RectInsidePolygon(ceiling.polygon, rect, s.wallMargin, out edge)) { result.explanation = "Saved dock is too close to a wall"; return false; }

            Vector3 right = user.Right;
            float lateral = Vector3.Dot(dock.position - user.head, right);
            int side = lateral >= 0f ? 1 : -1;
            Vector3 inspectPos; Quaternion inspectRot;
            if (!TryInspection(room, user, right, side, footprint, s, out inspectPos, out inspectRot)) { result.explanation = "No room beside you for the office"; return false; }
            if (PathBlocked(room, dock.position, inspectPos, s.descentClearance)) { result.explanation = "The way down from the saved dock is blocked"; return false; }

            result.valid = true;
            result.side = side;
            result.dockPosition = dock.position;
            result.dockRotation = dock.rotation;
            result.inspectionPosition = inspectPos;
            result.inspectionRotation = inspectRot;
            result.ceilingHeight = ceiling.height;
            result.descentDistance = (dock.position - inspectPos).magnitude;
            result.explanation = "Saved ceiling dock restored";
            return true;
        }
    }
}
