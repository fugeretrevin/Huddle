using System;
using System.Collections.Generic;
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

        [Header("Inspection pose beside the user (the SEARCH; widen this, never the margins)")]
        [Tooltip("Sideways distances from the user to try first, in order of preference.")]
        public float[] inspectSideOptions = { 0.6f, 0.7f, 0.5f };
        [Tooltip("Forward offset of the inspection pose from the user's head.")]
        public float inspectForward = 0.15f;
        [Tooltip("The office root comes to rest this far below the seated eye height.")]
        public float inspectDropBelowEye = 0.35f;
        [Tooltip("Further sideways distances tried after inspectSideOptions. With the user keep-out the office " +
                 "has to rest about 0.85 m to the side, so the closer options above rarely survive.")]
        public float[] inspectWideSideOptions = { 0.85f, 0.9f, 0.8f };
        [Tooltip("Added to inspectForward, in order of preference. Negative = back toward the user's side, " +
                 "behind the desk's near edge.")]
        public float[] inspectForwardOffsets = { 0f, -0.15f, -0.3f };
        [Tooltip("The inspection pose may be raised by these amounts (subtracted from inspectDropBelowEye), " +
                 "in order of preference, to clear a desk top.")]
        public float[] inspectRaiseOptions = { 0f, 0.05f, 0.10f };

        [Header("Descent path: swept volume, fail closed")]
        [Tooltip("The office box is tested along the whole path at steps of at most this much travel " +
                 "(translation plus the turn of its farthest corner). Both end poses are always tested.")]
        public float sweepStep = 0.05f;
        [Tooltip("Every point of the travelling office keeps at least this horizontal distance from " +
                 "recognised furniture and from the room outline. Each sample is tested with this plus " +
                 "half a sweepStep, which covers the travel between samples.")]
        public float sweepMarginHorizontal = 0.08f;
        [Tooltip("Furniture counts as being at the office's height when it is within this distance above or below it.")]
        public float sweepMarginVertical = 0.05f;
        [Tooltip("EXTRA horizontal margin for the high part of the path (see highClearanceAboveInspection), " +
                 "where the user cannot see or stop the office.")]
        public float descentClearance = 0.30f;
        [Tooltip("While the office base is more than this far above the lower end of the path (the inspection " +
                 "height), descentClearance is added to the horizontal margin.")]
        public float highClearanceAboveInspection = 0.5f;

        [Header("User keep-out (a cylinder around the seated head and torso)")]
        [Tooltip("No part of the travelling office may come within this horizontal distance of the head's vertical axis.")]
        public float userKeepOutRadius = 0.35f;
        [Tooltip("The keep-out cylinder runs from the floor to this far above the head.")]
        public float userKeepOutAboveHead = 0.15f;

        [Header("Office footprint")]
        public Vector2 footprintCenterOffset = new Vector2(0f, 0.12f);
        public float officeHeight = 0.45f;
    }

    /// <summary>What stopped a swept path, if anything.</summary>
    public enum CeilingPathBlock
    {
        None = 0,
        /// <summary>The office would come within the margin of a recognised furniture volume.</summary>
        Furniture = 1,
        /// <summary>The office would enter the keep-out cylinder around the user.</summary>
        User = 2,
        /// <summary>The office would come within the margin of the room outline, or leave it.</summary>
        Wall = 3,
        /// <summary>The path could not be checked at all (no room outline around it, too many samples). Treated as blocked.</summary>
        Unverifiable = 4,
    }

    /// <summary>
    /// Description of one swept-path check, for a debug view or a log. Filled by
    /// <see cref="CeilingPlacementEvaluator.SweepPath"/>; treat it as read-only.
    /// "Clear" means clear of the furniture the room scan RECOGNISED, never "clear of everything".
    /// </summary>
    public struct CeilingPathCheck
    {
        /// <summary>True when every sample kept its margins. False = blocked or unverifiable.</summary>
        public bool clear;
        public Vector3 start;
        public Vector3 end;
        public float startYawDeg;
        public float endYawDeg;
        /// <summary>Number of office boxes tested, both end poses included (when blocked: tested so far).</summary>
        public int sampleCount;
        /// <summary>Translation plus the arc of the footprint's farthest corner, in metres.</summary>
        public float travel;
        /// <summary>Half a sweep step: added to every horizontal margin to cover the travel between samples.</summary>
        public float samplePadding;
        /// <summary>Smallest horizontal distance found between the office and furniture at its height
        /// (PositiveInfinity when no furniture was ever at its height). When blocked: at the blocking sample.</summary>
        public float minFurnitureClearance;
        public string nearestFurnitureLabel;
        /// <summary>Smallest horizontal distance found between the office and the head's vertical axis while
        /// the office was at the user's height (PositiveInfinity when it never was).</summary>
        public float minUserClearance;
        /// <summary>Smallest distance found between the office and the room outline.</summary>
        public float minWallClearance;
        public CeilingPathBlock blockedBy;
        /// <summary>Label of the first furniture volume that blocked the path, or null.</summary>
        public string blockingLabel;
        /// <summary>Where along the path (0 = start, 1 = end) the first blocking sample was.</summary>
        public float blockedAtFraction;

        public string Describe()
        {
            string head = string.Format(CultureInfo.InvariantCulture,
                "path ({0:F2}, {1:F2}, {2:F2}) -> ({3:F2}, {4:F2}, {5:F2}), {6} samples over {7:F2} m",
                start.x, start.y, start.z, end.x, end.y, end.z, sampleCount, travel);
            if (clear)
            {
                return head + string.Format(CultureInfo.InvariantCulture,
                    ", clear of recognised furniture by {0}, of you by {1}, of the walls by {2}",
                    Metres(minFurnitureClearance), Metres(minUserClearance), Metres(minWallClearance));
            }
            switch (blockedBy)
            {
                case CeilingPathBlock.Furniture:
                    return head + string.Format(CultureInfo.InvariantCulture, ", blocked by {0} at {1:F0}% of the way ({2} clear)",
                        string.IsNullOrEmpty(blockingLabel) ? "furniture" : blockingLabel, blockedAtFraction * 100f, Metres(minFurnitureClearance));
                case CeilingPathBlock.User:
                    return head + string.Format(CultureInfo.InvariantCulture, ", too close to you at {0:F0}% of the way ({1} clear)",
                        blockedAtFraction * 100f, Metres(minUserClearance));
                case CeilingPathBlock.Wall:
                    return head + string.Format(CultureInfo.InvariantCulture, ", too close to a wall at {0:F0}% of the way", blockedAtFraction * 100f);
                default:
                    return head + ", could not be checked against the room";
            }
        }

        static string Metres(float v)
        {
            return float.IsInfinity(v) || v >= float.MaxValue ? "more than the path needs"
                                                               : v.ToString("F2", CultureInfo.InvariantCulture) + " m";
        }
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
        /// <summary>The swept path that was validated (valid placement), or the first path that was
        /// refused (no placement found). Retraction travels the same poses in reverse.</summary>
        public CeilingPathCheck path;

        public RootPose DockPose => new RootPose(dockPosition, dockRotation, 1f);
        public RootPose InspectionPose => new RootPose(inspectionPosition, inspectionRotation, 1f);
    }

    /// <summary>
    /// Finds a dock on REAL ceiling geometry and a way down to a pose beside the seated user whose
    /// SWEPT VOLUME is shown clear. Pure and deterministic. With no ceiling outline it fails with the
    /// reason; it never assumes a ceiling height.
    ///
    /// The path rule (fail closed):
    ///  - The office travels from the dock pose to the inspection pose exactly as
    ///    WorkspaceTransitionMachine moves it: <see cref="RootPose.Lerp"/> (straight line, yaw slerp),
    ///    eased in time only. Going back up is the same set of poses in reverse.
    ///  - The office's real box (its footprint rectangle at the interpolated position and yaw, from
    ///    its base to base + officeHeight) is sampled along that path every sweepStep of travel or
    ///    less, both end poses included. There is no untested stretch.
    ///  - Every sample must keep sweepMarginHorizontal from each recognised furniture volume that is
    ///    at its height (within sweepMarginVertical), must stay out of the user keep-out cylinder,
    ///    and must stay inside the room outline by sweepMarginHorizontal. Half a step is added to
    ///    those horizontal margins, and the vertical extent of a sample is stretched half a step
    ///    each way, so nothing can pass between two samples.
    ///  - While the office base is more than highClearanceAboveInspection above the lower end of the
    ///    path, descentClearance is added on top: up there the user cannot see or stop it.
    ///  - Touching a margin counts as blocked. Anything that cannot be checked counts as blocked.
    ///  - When no candidate survives, the answer is "no", never a smaller margin. To find more
    ///    candidates, widen the search lists (inspectWideSideOptions, inspectForwardOffsets,
    ///    inspectRaiseOptions, lateralOptions, forwardOptions).
    ///
    /// A clear result means clear of what the room scan recognised. Scans miss monitors, lamps,
    /// cables and people; the user's Confirm and an always-available Cancel / Retract stay part of
    /// the safety design.
    /// </summary>
    public static class CeilingPlacementEvaluator
    {
        /// <summary>Hard limits of the sampling, whatever the settings say.</summary>
        public const float MinSweepStep = 0.005f, MaxSweepStep = 0.10f;
        public const int MaxSweepSamples = 4096;
        /// <summary>At most this many inspection poses are tried per side (keeps the worst case bounded).</summary>
        public const int MaxInspectionOptions = 12;

        static readonly int[] SidesRightFirst = { 1, -1 };
        static readonly int[] SidesLeftFirst = { -1, 1 };
        static readonly float[] ZeroOnly = { 0f };
        static readonly List<RootPose> Options = new List<RootPose>(MaxInspectionOptions);

        /// <summary>Counters of one search, turned into the words the user is shown.</summary>
        struct Tally
        {
            public int tried, overhead, offCeiling, low, dockBlocked, pathFurniture, pathUser, pathWall, noInspect, accepted;
            public string pathLabel;        // first furniture that blocked a path
            public string besideReason;     // why no inspection pose was found on a side, in words
            public bool hasFirstBlocked;
            public CeilingPathCheck firstBlocked;

            public string Summary()
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0} tried, {1} overhead, {2} off the ceiling or near walls, {3} ceiling too low, " +
                    "{4} dock blocked by furniture, {5} path blocked by furniture{6}, {7} path too close to you, " +
                    "{8} path too close to a wall, {9} no room beside you{10}",
                    tried, overhead, offCeiling, low, dockBlocked, pathFurniture,
                    string.IsNullOrEmpty(pathLabel) ? "" : " (first: " + pathLabel + ")",
                    pathUser, pathWall, noInspect,
                    string.IsNullOrEmpty(besideReason) ? "" : " (" + besideReason + ")");
            }
        }

        /// <summary>The best dock (first in order of preference) whose whole descent is shown clear.</summary>
        public static bool Evaluate(IRoomGeometry room, SeatedUser user, Vector2 footprint, CeilingPlacementSettings s,
                                    out CeilingPlacement result)
        {
            Tally tally;
            return Search(room, user, footprint, s, null, out result, out tally);
        }

        /// <summary>
        /// Every dock candidate that passes all checks, in order of preference (diagnostics, debug view,
        /// tests). Returns their number; <paramref name="summary"/> has the rejection counts in words.
        /// </summary>
        public static int EvaluateAll(IRoomGeometry room, SeatedUser user, Vector2 footprint, CeilingPlacementSettings s,
                                      List<CeilingPlacement> results, out string summary)
        {
            if (results == null) results = new List<CeilingPlacement>();
            results.Clear();
            CeilingPlacement first; Tally tally;
            Search(room, user, footprint, s, results, out first, out tally);
            summary = results.Count > 0 || tally.tried > 0 ? tally.Summary() : first.explanation;
            return results.Count;
        }

        static bool Search(IRoomGeometry room, SeatedUser user, Vector2 footprint, CeilingPlacementSettings s,
                           List<CeilingPlacement> all, out CeilingPlacement result, out Tally t)
        {
            result = default(CeilingPlacement);
            t = default(Tally);
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

            Vector3 right = user.Right;
            int[] sides = s.preferredSide < 0 ? SidesLeftFirst : SidesRightFirst;
            float[] laterals = s.lateralOptions ?? ZeroOnly;
            float[] forwards = s.forwardOptions ?? ZeroOnly;
            bool found = false;

            for (int si = 0; si < 2; si++)
            {
                int side = sides[si];
                // The inspection poses depend only on the side: collect the usable ones first.
                BuildInspections(room, user, right, side, footprint, s, ref t);

                for (int li = 0; li < laterals.Length; li++)
                {
                    for (int fi = 0; fi < forwards.Length; fi++)
                    {
                        t.tried++;
                        Vector3 flat = new Vector3(user.head.x, 0f, user.head.z)
                                       + right * (side * laterals[li]) + user.forward * forwards[fi];
                        float dx = flat.x - user.head.x, dz = flat.z - user.head.z;
                        if (Mathf.Sqrt(dx * dx + dz * dz) < s.minHorizontalFromUser) { t.overhead++; continue; }

                        int ci = SpatialGeometry.FindCeiling(room, new Vector2(flat.x, flat.z));
                        if (ci < 0) { t.offCeiling++; continue; }
                        RoomPlane ceiling = room.GetCeiling(ci);
                        if (ceiling.height - user.head.y < s.minCeilingAboveHead) { t.low++; continue; }

                        Vector3 dockPos = new Vector3(flat.x, ceiling.height - s.dockDrop, flat.z);
                        Quaternion dockRot = SpatialGeometry.FaceUserYaw(dockPos, user.head);
                        OrientedRect rect = SpatialGeometry.Footprint(dockPos, dockRot, 1f, footprint, s.footprintCenterOffset);
                        float edge;
                        if (!SpatialGeometry.RectInsidePolygon(ceiling.polygon, rect, s.wallMargin, out edge)) { t.offCeiling++; continue; }
                        string dockLabel;
                        if (OverlapsFurniture(room, rect, dockPos.y, dockPos.y + s.officeHeight, out dockLabel)) { t.dockBlocked++; continue; }

                        if (Options.Count == 0) { t.noInspect++; continue; }

                        RootPose dockPose = new RootPose(dockPos, dockRot, 1f);
                        RootPose inspect; CeilingPathCheck check;
                        if (!FirstClearPath(room, user, dockPose, footprint, s, out inspect, out check))
                        {
                            Count(ref t, check);
                            continue;
                        }

                        CeilingPlacement p = default(CeilingPlacement);
                        p.valid = true;
                        p.side = side;
                        p.dockPosition = dockPos;
                        p.dockRotation = dockRot;
                        p.inspectionPosition = inspect.position;
                        p.inspectionRotation = inspect.rotation;
                        p.ceilingHeight = ceiling.height;
                        p.descentDistance = (dockPos - inspect.position).magnitude;
                        p.path = check;
                        p.explanation = string.Format(CultureInfo.InvariantCulture,
                            "Ceiling dock on your {0}, {1:F1} m to the side, {2:F1} m descent, {3:F2} m from the walls",
                            side > 0 ? "right" : "left", laterals[li], p.descentDistance, edge);
                        t.accepted++;
                        if (!found) { result = p; found = true; }
                        if (all == null) { Options.Clear(); return true; }
                        all.Add(p);
                    }
                }
            }
            Options.Clear();
            if (found) return true;

            result.explanation = "No clear ceiling dock: " + t.Summary();
            if (t.hasFirstBlocked) result.path = t.firstBlocked;
            return false;
        }

        static void Count(ref Tally t, CeilingPathCheck check)
        {
            if (!t.hasFirstBlocked) { t.firstBlocked = check; t.hasFirstBlocked = true; }
            switch (check.blockedBy)
            {
                case CeilingPathBlock.User: t.pathUser++; break;
                case CeilingPathBlock.Wall:
                case CeilingPathBlock.Unverifiable: t.pathWall++; break;
                default:
                    t.pathFurniture++;
                    if (string.IsNullOrEmpty(t.pathLabel)) t.pathLabel = LabelOf(check.blockingLabel);
                    break;
            }
        }

        static string LabelOf(string label) { return string.IsNullOrEmpty(label) ? "furniture" : label; }

        /// <summary>
        /// Sweeps from the dock to each usable inspection pose (in order of preference) and returns the
        /// first whose whole path is clear. On failure <paramref name="check"/> is the refusal of the
        /// most preferred inspection pose.
        /// </summary>
        static bool FirstClearPath(IRoomGeometry room, SeatedUser user, RootPose dock, Vector2 footprint,
                                   CeilingPlacementSettings s, out RootPose inspect, out CeilingPathCheck check)
        {
            inspect = dock;
            check = default(CeilingPathCheck);
            bool haveFail = false;
            for (int i = 0; i < Options.Count; i++)
            {
                CeilingPathCheck c;
                if (SweepPath(room, user, dock, Options[i], footprint, s, out c))
                {
                    inspect = Options[i];
                    check = c;
                    return true;
                }
                if (!haveFail) { check = c; haveFail = true; }
            }
            if (!haveFail) check.blockedBy = CeilingPathBlock.Unverifiable;
            return false;
        }

        /// <summary>
        /// Fills <see cref="Options"/> with the inspection poses beside the user on one side that are
        /// usable on their own: the office standing there overlaps no furniture (the footprint test)
        /// and, tested as a path of zero length, keeps the sweep margins to furniture, the user
        /// keep-out and the room outline. To the side, never in front of the face.
        /// </summary>
        static void BuildInspections(IRoomGeometry room, SeatedUser user, Vector3 right, int side, Vector2 footprint,
                                     CeilingPlacementSettings s, ref Tally t)
        {
            Options.Clear();
            float[] raises = s.inspectRaiseOptions != null && s.inspectRaiseOptions.Length > 0 ? s.inspectRaiseOptions : ZeroOnly;
            float[] offsets = s.inspectForwardOffsets != null && s.inspectForwardOffsets.Length > 0 ? s.inspectForwardOffsets : ZeroOnly;
            int near = s.inspectSideOptions != null ? s.inspectSideOptions.Length : 0;
            int wide = s.inspectWideSideOptions != null ? s.inspectWideSideOptions.Length : 0;
            string why = null;

            for (int ri = 0; ri < raises.Length; ri++)
            {
                for (int oi = 0; oi < offsets.Length; oi++)
                {
                    for (int di = 0; di < near + wide; di++)
                    {
                        float lateral = di < near ? s.inspectSideOptions[di] : s.inspectWideSideOptions[di - near];
                        Vector3 p = user.head + right * (side * lateral) + user.forward * (s.inspectForward + offsets[oi]);
                        p.y = user.head.y - s.inspectDropBelowEye + raises[ri];
                        Quaternion r = SpatialGeometry.FaceUserYaw(p, user.head);
                        OrientedRect rect = SpatialGeometry.Footprint(p, r, 1f, footprint, s.footprintCenterOffset);
                        string label;
                        if (OverlapsFurniture(room, rect, p.y - 0.03f, p.y + s.officeHeight, out label))
                        {
                            if (why == null) why = LabelOf(label) + " in the way";
                            continue;
                        }
                        RootPose pose = new RootPose(p, r, 1f);
                        CeilingPathCheck c;
                        if (!SweepPath(room, user, pose, pose, footprint, s, out c))
                        {
                            if (why == null)
                            {
                                if (c.blockedBy == CeilingPathBlock.Furniture) why = LabelOf(c.blockingLabel) + " in the way";
                                else if (c.blockedBy == CeilingPathBlock.User) why = "too close to you";
                                else why = "too close to a wall";
                            }
                            continue;
                        }
                        Options.Add(pose);
                        if (Options.Count >= MaxInspectionOptions) return;
                    }
                }
            }
            if (Options.Count == 0 && string.IsNullOrEmpty(t.besideReason)) t.besideReason = why;
        }

        static bool OverlapsFurniture(IRoomGeometry room, OrientedRect rect, float minY, float maxY, out string label)
        {
            label = null;
            for (int i = 0; i < room.FurnitureCount; i++)
            {
                FurnitureBox box = room.GetFurniture(i);
                if (box.maxY <= minY || box.minY >= maxY) continue;
                if (rect.Overlaps(box.Footprint)) { label = box.label; return true; }
            }
            return false;
        }

        /// <summary>The pose of the office root a fraction <paramref name="k"/> (0..1) of the way along a
        /// path. This IS the interpolation WorkspaceTransitionMachine.Tick uses, so what is swept is
        /// what is travelled; PathPose(a, b, k) and PathPose(b, a, 1 - k) are the same pose.</summary>
        public static RootPose PathPose(RootPose from, RootPose to, float k)
        {
            return RootPose.Lerp(from, to, k);
        }

        /// <summary>The sampling step actually used: the setting, limited to MinSweepStep..MaxSweepStep.</summary>
        public static float EffectiveSweepStep(CeilingPlacementSettings s)
        {
            return Mathf.Clamp(s.sweepStep, MinSweepStep, MaxSweepStep);
        }

        /// <summary>The horizontal margin a SAMPLE is tested with on the low part of the path:
        /// sweepMarginHorizontal plus half a step (the padding that covers the travel between samples).</summary>
        public static float SampleMarginHorizontal(CeilingPlacementSettings s)
        {
            return Mathf.Max(0f, s.sweepMarginHorizontal) + 0.5f * EffectiveSweepStep(s);
        }

        /// <summary>
        /// The swept-volume test. Samples the office box along the path from <paramref name="from"/> to
        /// <paramref name="to"/> (see the class summary for the rule) and returns true only when every
        /// sample keeps its margins to furniture, the user and the room outline. The result does not
        /// depend on the direction: sweeping to -> from tests the same boxes.
        /// </summary>
        public static bool SweepPath(IRoomGeometry room, SeatedUser user, RootPose from, RootPose to, Vector2 footprint,
                                     CeilingPlacementSettings s, out CeilingPathCheck check)
        {
            check = default(CeilingPathCheck);
            check.start = from.position;
            check.end = to.position;
            check.startYawDeg = from.rotation.eulerAngles.y;
            check.endYawDeg = to.rotation.eulerAngles.y;
            check.minFurnitureClearance = float.PositiveInfinity;
            check.minUserClearance = float.PositiveInfinity;
            check.minWallClearance = float.PositiveInfinity;

            if (room == null || !room.HasRoom) { check.blockedBy = CeilingPathBlock.Unverifiable; return false; }

            float step = EffectiveSweepStep(s);
            float pad = 0.5f * step;
            float marginH = Mathf.Max(0f, s.sweepMarginHorizontal);
            float marginV = Mathf.Max(0f, s.sweepMarginVertical);
            float extraHigh = Mathf.Max(0f, s.descentClearance);
            check.samplePadding = pad;

            // Travel: how far the fastest point of the box moves. Translation, plus the arc of the
            // footprint corner farthest from the root while the office turns.
            float reach = SpatialGeometry.FootprintReach(footprint, s.footprintCenterOffset, Mathf.Max(from.scale, to.scale));
            float turn = Quaternion.Angle(from.rotation, to.rotation) * Mathf.Deg2Rad;
            float travel = (to.position - from.position).magnitude + reach * turn;
            check.travel = travel;
            if (float.IsNaN(travel) || float.IsInfinity(travel)) { check.blockedBy = CeilingPathBlock.Unverifiable; return false; }
            int n = Mathf.Max(1, Mathf.CeilToInt(travel / step));
            if (n > MaxSweepSamples) { check.blockedBy = CeilingPathBlock.Unverifiable; return false; }

            // The room outline the path must stay inside: the floor outline around the user, or
            // failing that a ceiling outline over either end. None: the path cannot be checked.
            Vector2[] outline = null;
            float userBottom = float.NegativeInfinity;     // no floor under the user: the keep-out has no lower end
            Vector2 userXZ = new Vector2(user.head.x, user.head.z);
            int fl = SpatialGeometry.FindFloor(room, userXZ);
            if (fl >= 0) { RoomPlane floor = room.GetFloor(fl); outline = floor.polygon; userBottom = floor.height; }
            else
            {
                int ci = SpatialGeometry.FindCeiling(room, SpatialGeometry.XZ(from.position));
                if (ci < 0) ci = SpatialGeometry.FindCeiling(room, SpatialGeometry.XZ(to.position));
                if (ci >= 0) outline = room.GetCeiling(ci).polygon;
            }
            if (outline == null) { check.blockedBy = CeilingPathBlock.Unverifiable; return false; }
            float userTop = user.head.y + s.userKeepOutAboveHead;
            float userNeed = Mathf.Max(0f, s.userKeepOutRadius) + pad;

            // Vertical extent of the path. A sample stands for the poses up to half a step before
            // and after it, so its box is stretched by half the height change per step, never
            // beyond the ends of the path.
            float pathLo = Mathf.Min(from.position.y, to.position.y);
            float pathHi = Mathf.Max(from.position.y, to.position.y);
            float dyHalf = 0.5f * (pathHi - pathLo) / n;
            float highAbove = pathLo + s.highClearanceAboveInspection;
            int furnitureCount = room.FurnitureCount;

            for (int i = 0; i <= n; i++)
            {
                float k = (float)i / n;
                RootPose p = PathPose(from, to, k);
                OrientedRect rect = SpatialGeometry.Footprint(p.position, p.rotation, p.scale, footprint, s.footprintCenterOffset);
                float lo = Mathf.Max(pathLo, p.position.y - dyHalf);
                float hiBase = Mathf.Min(pathHi, p.position.y + dyHalf);
                float hi = hiBase + s.officeHeight * p.scale;
                // High part of the path: out of the user's sight and reach, so the larger clearance applies.
                float need = marginH + pad + (hiBase > highAbove ? extraHigh : 0f);
                check.sampleCount = i + 1;

                // 1. Recognised furniture at the office's height.
                for (int f = 0; f < furnitureCount; f++)
                {
                    FurnitureBox box = room.GetFurniture(f);
                    if (!SpatialGeometry.VerticalRangesTouch(lo, hi, box.minY, box.maxY, marginV)) continue;
                    float d = rect.DistanceTo(box.Footprint);
                    if (d < check.minFurnitureClearance) { check.minFurnitureClearance = d; check.nearestFurnitureLabel = box.label; }
                    if (d <= need)
                    {
                        check.blockedBy = CeilingPathBlock.Furniture;
                        check.blockingLabel = box.label;
                        check.blockedAtFraction = k;
                        return false;
                    }
                }

                // 2. The user: a cylinder from the floor to above the head.
                if (hi >= userBottom && lo <= userTop)
                {
                    float d = rect.DistanceToPoint(userXZ);
                    if (d < check.minUserClearance) check.minUserClearance = d;
                    if (d <= userNeed)
                    {
                        check.blockedBy = CeilingPathBlock.User;
                        check.blockedAtFraction = k;
                        return false;
                    }
                }

                // 3. The room outline (walls).
                float edge;
                bool inside = SpatialGeometry.RectInsidePolygon(outline, rect, 0f, out edge);
                if (edge < check.minWallClearance) check.minWallClearance = edge;
                if (!inside || edge <= marginH + pad)
                {
                    check.blockedBy = CeilingPathBlock.Wall;
                    check.blockedAtFraction = k;
                    return false;
                }
            }

            check.clear = true;
            return true;
        }

        /// <summary>
        /// Checks a saved dock against the current room and user: still under real ceiling, away from
        /// walls, not overhead, with a way down whose swept volume is clear (the same test as
        /// <see cref="Evaluate"/>). Fills the inspection pose for the current user.
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
            string dockLabel;
            if (OverlapsFurniture(room, rect, dock.position.y, dock.position.y + s.officeHeight, out dockLabel))
            {
                result.explanation = "Saved dock is now blocked by " + LabelOf(dockLabel);
                return false;
            }

            Vector3 right = user.Right;
            float lateral = Vector3.Dot(dock.position - user.head, right);
            int side = lateral >= 0f ? 1 : -1;
            Tally t = default(Tally);
            BuildInspections(room, user, right, side, footprint, s, ref t);
            if (Options.Count == 0)
            {
                result.explanation = "No room beside you for the office" + (string.IsNullOrEmpty(t.besideReason) ? "" : " (" + t.besideReason + ")");
                return false;
            }
            RootPose dockPose = new RootPose(dock.position, dock.rotation, 1f);
            RootPose inspect; CeilingPathCheck check;
            bool clear = FirstClearPath(room, user, dockPose, footprint, s, out inspect, out check);
            Options.Clear();
            if (!clear)
            {
                result.path = check;
                switch (check.blockedBy)
                {
                    case CeilingPathBlock.Furniture:
                        result.explanation = "The way down from the saved dock is blocked by " + LabelOf(check.blockingLabel); break;
                    case CeilingPathBlock.User:
                        result.explanation = "The way down from the saved dock passes too close to you"; break;
                    case CeilingPathBlock.Wall:
                        result.explanation = "The way down from the saved dock passes too close to a wall"; break;
                    default:
                        result.explanation = "The way down from the saved dock could not be checked against the room"; break;
                }
                return false;
            }

            result.valid = true;
            result.side = side;
            result.dockPosition = dock.position;
            result.dockRotation = dock.rotation;
            result.inspectionPosition = inspect.position;
            result.inspectionRotation = inspect.rotation;
            result.ceilingHeight = ceiling.height;
            result.descentDistance = (dock.position - inspect.position).magnitude;
            result.path = check;
            result.explanation = "Saved ceiling dock restored";
            return true;
        }
    }
}
