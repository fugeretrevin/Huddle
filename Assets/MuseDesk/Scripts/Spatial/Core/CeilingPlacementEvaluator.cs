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
        [Tooltip("Forward offsets to try, in order of preference. -0.3 (a little behind the shoulder line) was added " +
                 "after the first worn pass: with the desk against the wall in front, the forward docks are too near that wall.")]
        public float[] forwardOptions = { 0.3f, 0.6f, 0.0f, -0.3f };
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
        [Tooltip("The office root comes to rest this far below the seated eye height (the normal lowering). " +
                 "0.35 until the first worn pass; 0.25 stops it higher and clears a desk top without a raise.")]
        public float inspectDropBelowEye = 0.25f;
        [Tooltip("Further sideways distances tried after inspectSideOptions. With the user keep-out the office " +
                 "has to rest about 0.85 m to the side, so the closer options above rarely survive. 0.9 first and " +
                 "1.0 added after the first worn pass: further from the user by default.")]
        public float[] inspectWideSideOptions = { 0.9f, 0.85f, 1.0f, 0.8f };
        [Tooltip("Added to inspectForward, in order of preference. Negative = back toward the user's side, " +
                 "behind the desk's near edge.")]
        public float[] inspectForwardOffsets = { 0f, -0.15f, -0.3f };
        [Tooltip("The inspection pose may be raised by these amounts (subtracted from inspectDropBelowEye), " +
                 "in order of preference, to clear a desk top.")]
        public float[] inspectRaiseOptions = { 0f, 0.05f, 0.10f };

        [Header("Shallow descent: stop higher instead of refusing (after the first worn pass)")]
        [Tooltip("When no pose of the normal lowering has a clear way down, the office may stop with its base this far " +
                 "above the TOP of the user keep-out (head + userKeepOutAboveHead), in order of preference. Up there the " +
                 "keep-out cylinder does not reach it; the 'never overhead' radius still does. Empty = no shallow descent.")]
        public float[] shallowBaseAboveKeepOut = { 0.05f, 0.10f };
        [Tooltip("A shallow pose is never higher than this above the eyes, so a paper on the office stays within reach.")]
        public float shallowMaxAboveEye = 0.25f;
        [Tooltip("Every engaged pose is at least this far below its dock: a descent the user can see, not a twitch. " +
                 "When even the shallowest pose would be closer to the dock, that dock is refused for the user.")]
        public float minDescent = 0.15f;

        [Header("Compact, fixed obstacles (after the first worn pass)")]
        [Tooltip("Labels of recognised volumes that are compact and fixed (a screen on a wall or on a desk). The EXTRA " +
                 "clearance of the high part of a path (descentClearance) is not added for them: they block only when the " +
                 "office box, its column up to the ceiling or the mast comes within the ordinary sweep margin. Everything " +
                 "else keeps the larger clearance.")]
        public string[] compactObstacleLabels = { "SCREEN" };

        [Header("Low floor clutter (after the first worn pass)")]
        [Tooltip("A recognised volume whose top is at most this far above the floor under it counts as low clutter " +
                 "(a shoe, a bag, a scanned blob a few cm tall).")]
        public float lowObstacleMaxHeight = 0.15f;
        [Tooltip("Low clutter keeps only this horizontal margin from the office (plus half a sweep step per sample), " +
                 "instead of sweepMarginHorizontal: it blocks when it is under the office's footprint or next to it.")]
        public float lowObstacleMargin = 0.03f;

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
        [Tooltip("Above the keep-out cylinder, at any height: no part of the travelling office may come within " +
                 "this horizontal distance of the head's vertical axis (never directly overhead).")]
        public float userOverheadRadius = 0.20f;

        [Header("Moves that start or end at the desk pose")]
        [Tooltip("The desk pose is inside the user keep-out and on or against the user's own desk by design. " +
                 "A move that starts (or ends) there is excused from a margin it already breaks at the desk pose, " +
                 "until it first keeps that margin, and at most for this much travel from the desk end. After " +
                 "that every margin applies in full.")]
        public float deskStartRegion = 2.5f;
        [Tooltip("While excused from the user keep-out, the office may come at most this much closer to the " +
                 "head's axis than it is at the desk pose (and never closer than half its desk distance).")]
        public float deskApproachAllowance = 0.05f;
        [Tooltip("While excused from a furniture or wall margin, the office may not come closer to that " +
                 "furniture or wall, nor sink deeper into its height range, by more than this.")]
        public float deskContactTolerance = 0.01f;

        [Header("Up-and-over routes (rise, travel, come down)")]
        [Tooltip("The office base travels this far above the user's eyes on the level leg (lower under a low ceiling).")]
        public float overRouteAboveHead = 0.25f;

        [Header("Mast and ceiling mount")]
        [Tooltip("Half thickness of the mast between the office and the ceiling mount, for the swept test.")]
        public float mastRadius = 0.03f;
        [Tooltip("Nothing recognised may be within this horizontal distance of the mount point on the ceiling...")]
        public float mountClearance = 0.30f;
        [Tooltip("...in the band from the ceiling down to this far below it.")]
        public float mountDepth = 0.30f;

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

    /// <summary>How a path is to be swept. The default value is the rule of the pod's own descent.</summary>
    public struct PathSweepOptions
    {
        /// <summary>The path starts at the desk pose (see CeilingPlacementSettings.deskStartRegion).</summary>
        public bool fromDesk;
        /// <summary>The path ends at the desk pose. It is swept in reverse, as a path that starts there.</summary>
        public bool toDesk;
        /// <summary>The far end was placed by hand and is not vouched for by the room scan: the room outline
        /// is not tested, and with no room data at all only the user rules are. Never set for a detected spot.</summary>
        public bool handPlaced;
        /// <summary>Also test the mast (a thin line from the top of the office to <see cref="mastTop"/>), and
        /// on the high part of the path everything between the office and the ceiling.</summary>
        public bool mast;
        /// <summary>Where the mast meets the ceiling: above the dock's footprint centre, at ceiling height.</summary>
        public Vector3 mastTop;
        /// <summary>The larger clearance applies wherever the office base is above the top of the user
        /// keep-out, instead of relative to the lower end of the path. Set for mode-change routes.</summary>
        public bool absoluteHigh;
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
        /// <summary>False when there was no room data to test against (a hand-placed spot): only the
        /// user rules were applied.</summary>
        public bool roomChecked;
        /// <summary>True when the mast, not the office box, was what touched the blocking furniture.</summary>
        public bool blockedByMast;

        /// <summary>The refusal as the start of a sentence for the user, for example
        /// "The way there is blocked by TABLE". Empty when the path is clear.</summary>
        public string Words(string what)
        {
            if (clear) return "";
            switch (blockedBy)
            {
                case CeilingPathBlock.Furniture:
                    return what + " is blocked by " + (string.IsNullOrEmpty(blockingLabel) ? "furniture" : blockingLabel);
                case CeilingPathBlock.User: return what + " passes too close to you";
                case CeilingPathBlock.Wall: return what + " passes too close to a wall";
                default: return what + " cannot be checked against the room scan";
            }
        }

        public string Describe()
        {
            if (!clear && blockedBy == CeilingPathBlock.None) return "no path was checked";
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
        /// <summary>Where the mast meets the ceiling above the dock (valid placements only).</summary>
        public Vector3 mastTop;

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
    ///    inspectRaiseOptions, lateralOptions, forwardOptions, shallowBaseAboveKeepOut).
    ///  - Two obstacle classes were loosened on the owner's request after the first worn pass (see
    ///    Classify): low floor clutter keeps lowObstacleMargin, and a compact fixed object (a screen)
    ///    is not given the high-part extra. The user keep-out, the overhead radius, the sweep itself and
    ///    everything else are unchanged.
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
        /// <summary>At most this many poses of the normal lowering are tried per side (keeps the worst case
        /// bounded); <see cref="MaxShallowOptions"/> shallow ones may follow.</summary>
        public const int MaxInspectionOptions = 12;

        static readonly int[] SidesRightFirst = { 1, -1 };
        static readonly int[] SidesLeftFirst = { -1, 1 };
        static readonly float[] ZeroOnly = { 0f };
        static readonly List<RootPose> Options = new List<RootPose>(MaxInspectionOptions);

        /// <summary>Counters of one search, turned into the words the user is shown.</summary>
        struct Tally
        {
            public int tried, overhead, offCeiling, low, dockBlocked, pathFurniture, pathUser, pathWall, noInspect, unreachable, accepted;
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
                    string.IsNullOrEmpty(besideReason) ? "" : " (" + besideReason + ")")
                    + (unreachable > 0 ? string.Format(CultureInfo.InvariantCulture, ", {0} no clear way up from where the office is", unreachable) : "");
            }
        }

        /// <summary>The best dock (first in order of preference) whose whole descent is shown clear.</summary>
        public static bool Evaluate(IRoomGeometry room, SeatedUser user, Vector2 footprint, CeilingPlacementSettings s,
                                    out CeilingPlacement result)
        {
            Tally tally;
            return Search(room, user, footprint, s, null, null, out result, out tally);
        }

        /// <summary>
        /// The best dock whose descent is shown clear AND that <paramref name="accept"/> takes (the
        /// director passes "the way up to it from where the office is now is clear"), so a preview
        /// never offers a dock Confirm would refuse. Docks it refuses are counted as "no clear way up".
        /// </summary>
        public static bool Evaluate(IRoomGeometry room, SeatedUser user, Vector2 footprint, CeilingPlacementSettings s,
                                    Predicate<CeilingPlacement> accept, out CeilingPlacement result)
        {
            Tally tally;
            return Search(room, user, footprint, s, null, accept, out result, out tally);
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
            Search(room, user, footprint, s, results, null, out first, out tally);
            summary = results.Count > 0 || tally.tried > 0 ? tally.Summary() : first.explanation;
            return results.Count;
        }

        static bool Search(IRoomGeometry room, SeatedUser user, Vector2 footprint, CeilingPlacementSettings s,
                           List<CeilingPlacement> all, Predicate<CeilingPlacement> accept, out CeilingPlacement result, out Tally t)
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
                        RootPose dockPose = new RootPose(dockPos, dockRot, 1f);
                        Vector3 mastTop = MastTop(dockPose, ceiling.height, s);
                        if (MountBlocked(room, dockPose, mastTop, s, out dockLabel)) { t.dockBlocked++; continue; }

                        if (Options.Count == 0) { t.noInspect++; continue; }

                        RootPose inspect; CeilingPathCheck check;
                        if (!FirstClearPath(room, user, dockPose, mastTop, footprint, s, out inspect, out check))
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
                        p.mastTop = mastTop;
                        p.explanation = string.Format(CultureInfo.InvariantCulture,
                            "Ceiling dock on your {0}, {1:F1} m to the side, {2:F1} m descent, {3:F2} m from the walls",
                            side > 0 ? "right" : "left", laterals[li], p.descentDistance, edge);
                        // The caller's own condition (the way up to it). It must not touch Options.
                        if (accept != null && !accept(p)) { t.unreachable++; continue; }
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
        static bool FirstClearPath(IRoomGeometry room, SeatedUser user, RootPose dock, Vector3 mastTop, Vector2 footprint,
                                   CeilingPlacementSettings s, out RootPose inspect, out CeilingPathCheck check)
        {
            inspect = dock;
            check = default(CeilingPathCheck);
            bool haveFail = false, tooShallow = false;
            PathSweepOptions o = PodPathOptions(mastTop);
            float lowestStop = dock.position.y - Mathf.Max(0f, s.minDescent) + 1e-4f;
            for (int i = 0; i < Options.Count; i++)
            {
                // A pose the office could not even come down to (a low ceiling, a raised head): the
                // pod would hang at the dock beside the head. Not a descent; not offered.
                if (Options[i].position.y > lowestStop) { tooShallow = true; continue; }
                CeilingPathCheck c;
                if (SweepPath(room, user, dock, Options[i], footprint, s, o, out c))
                {
                    inspect = Options[i];
                    check = c;
                    return true;
                }
                if (!haveFail) { check = c; haveFail = true; }
            }
            // Every pose was too close under the dock: the head is too near the dock for any descent.
            if (!haveFail) check.blockedBy = tooShallow ? CeilingPathBlock.User : CeilingPathBlock.Unverifiable;
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
            var why = new BesideReason();

            // 1. The normal lowering: beside the user, below the eyes. Up to MaxInspectionOptions usable poses.
            for (int ri = 0; ri < raises.Length && Options.Count < MaxInspectionOptions; ri++)
                for (int oi = 0; oi < offsets.Length && Options.Count < MaxInspectionOptions; oi++)
                    for (int di = 0; di < near + wide && Options.Count < MaxInspectionOptions; di++)
                    {
                        float lateral = di < near ? s.inspectSideOptions[di] : s.inspectWideSideOptions[di - near];
                        TryAddInspection(room, user, right, side, lateral, offsets[oi],
                                         user.head.y - s.inspectDropBelowEye + raises[ri], footprint, s, ref why);
                    }

            // 2. The shallow descent, tried only after every pose above: the office stops with its base
            //    above the top of the user keep-out (and never higher than shallowMaxAboveEye), so the
            //    cylinder around the head and torso does not reach it. The same margins, the same sweep;
            //    only the height differs. FirstClearPath also keeps each pose minDescent below its dock.
            float[] shallow = s.shallowBaseAboveKeepOut;
            if (shallow != null)
            {
                int cap = Options.Count + MaxShallowOptions;
                float keepOutTop = user.head.y + Mathf.Max(0f, s.userKeepOutAboveHead);
                float highest = user.head.y + s.shallowMaxAboveEye + 1e-4f;
                for (int hi = 0; hi < shallow.Length && Options.Count < cap; hi++)
                {
                    float y = keepOutTop + Mathf.Max(0f, shallow[hi]);
                    if (y > highest) continue;
                    for (int oi = 0; oi < offsets.Length && Options.Count < cap; oi++)
                        for (int di = 0; di < near + wide && Options.Count < cap; di++)
                        {
                            float lateral = di < near ? s.inspectSideOptions[di] : s.inspectWideSideOptions[di - near];
                            TryAddInspection(room, user, right, side, lateral, offsets[oi], y, footprint, s, ref why);
                        }
                }
            }
            if (Options.Count == 0 && string.IsNullOrEmpty(t.besideReason)) t.besideReason = why.Words();
        }

        /// <summary>At most this many shallow poses per side, after the normal ones.</summary>
        public const int MaxShallowOptions = 6;

        /// <summary>
        /// Why no pose beside the user was usable, in words. The first refusal of each kind is kept and
        /// furniture is named before a wall, a wall before "too close to you": the closest options
        /// (0.5 to 0.7 m) are tried first and always fail the user keep-out, so naming the first refusal
        /// said "too close to you" even when the poses that could work were stopped by a screen or a wall.
        /// </summary>
        struct BesideReason
        {
            public string furniture, wall, user;

            public void Note(CeilingPathBlock kind, string label)
            {
                if (kind == CeilingPathBlock.Furniture) { if (furniture == null) furniture = LabelOf(label) + " in the way"; }
                else if (kind == CeilingPathBlock.User) { if (user == null) user = "too close to you"; }
                else if (wall == null) wall = "too close to a wall";
            }

            public string Words() { return furniture ?? wall ?? user; }
        }

        static void TryAddInspection(IRoomGeometry room, SeatedUser user, Vector3 right, int side, float lateral, float offset,
                                     float y, Vector2 footprint, CeilingPlacementSettings s, ref BesideReason why)
        {
            Vector3 p = user.head + right * (side * lateral) + user.forward * (s.inspectForward + offset);
            p.y = y;
            Quaternion r = SpatialGeometry.FaceUserYaw(p, user.head);
            OrientedRect rect = SpatialGeometry.Footprint(p, r, 1f, footprint, s.footprintCenterOffset);
            string label;
            if (OverlapsFurniture(room, rect, p.y - 0.03f, p.y + s.officeHeight, out label))
            {
                why.Note(CeilingPathBlock.Furniture, label);
                return;
            }
            RootPose pose = new RootPose(p, r, 1f);
            CeilingPathCheck c;
            if (!SweepPath(room, user, pose, pose, footprint, s, out c))
            {
                why.Note(c.blockedBy, c.blockingLabel);
                return;
            }
            Options.Add(pose);
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
        /// The swept-volume test for one straight path, with the rule of the pod's own descent.
        /// Samples the office box along the path from <paramref name="from"/> to <paramref name="to"/>
        /// (see the class summary for the rule) and returns true only when every sample keeps its
        /// margins to furniture, the user and the room outline. The result does not depend on the
        /// direction: sweeping to -> from tests the same boxes.
        /// </summary>
        public static bool SweepPath(IRoomGeometry room, SeatedUser user, RootPose from, RootPose to, Vector2 footprint,
                                     CeilingPlacementSettings s, out CeilingPathCheck check)
        {
            return SweepRoute(room, user, WorkspaceRoute.Straight(from, to), footprint, s, default(PathSweepOptions), out check);
        }

        /// <summary>The swept-volume test for one straight path, with options (desk end, mast).</summary>
        public static bool SweepPath(IRoomGeometry room, SeatedUser user, RootPose from, RootPose to, Vector2 footprint,
                                     CeilingPlacementSettings s, PathSweepOptions o, out CeilingPathCheck check)
        {
            return SweepRoute(room, user, WorkspaceRoute.Straight(from, to), footprint, s, o, out check);
        }

        /// <summary>The options the pod's own path (dock to inspection pose and back) is swept with.</summary>
        public static PathSweepOptions PodPathOptions(Vector3 mastTop)
        {
            var o = default(PathSweepOptions);
            o.mast = true;
            o.mastTop = mastTop;
            return o;
        }

        /// <summary>Where the mast meets the ceiling: straight above the centre of the docked office's footprint.</summary>
        public static Vector3 MastTop(RootPose dock, float ceilingHeight, CeilingPlacementSettings s)
        {
            Vector3 c = dock.position + dock.rotation * new Vector3(s.footprintCenterOffset.x * dock.scale, 0f, s.footprintCenterOffset.y * dock.scale);
            return new Vector3(c.x, ceilingHeight, c.z);
        }

        /// <summary>
        /// True when a recognised volume is within mountClearance of the mount point, in the band from
        /// the ceiling down to mountDepth below it. Ceiling lamps and fans are usually NOT in the room
        /// scan, so a "false" here says nothing about them.
        /// </summary>
        public static bool MountBlocked(IRoomGeometry room, RootPose dock, Vector3 mastTop, CeilingPlacementSettings s, out string label)
        {
            label = null;
            float r = Mathf.Max(0f, s.mountClearance);
            OrientedRect area = OrientedRect.FromYaw(new Vector2(mastTop.x, mastTop.z), new Vector2(r, r), dock.rotation.eulerAngles.y);
            float lo = mastTop.y - Mathf.Max(0f, s.mountDepth), hi = mastTop.y;
            for (int i = 0; i < room.FurnitureCount; i++)
            {
                FurnitureBox box = room.GetFurniture(i);
                if (!SpatialGeometry.VerticalRangesTouch(lo, hi, box.minY, box.maxY, Mathf.Max(0f, s.sweepMarginVertical))) continue;
                if (area.Overlaps(box.Footprint)) { label = box.label; return true; }
            }
            return false;
        }

        // Start contacts of a path that begins at the desk pose: which furniture boxes were already
        // inside the margin there, how close, and how far into their height range. Scratch, main thread.
        static bool[] _excused = new bool[32];
        static float[] _excusedDistance = new float[32];
        static float[] _excusedOverlap = new float[32];
        static byte[] _kind = new byte[32];

        public const byte KindUsual = 0, KindLow = 1, KindCompact = 2;

        /// <summary>
        /// How the sweep treats one recognised volume:
        ///  - KindLow: low floor clutter. Its top is at most lowObstacleMaxHeight above the detected floor
        ///    under its centre. It keeps lowObstacleMargin (not sweepMarginHorizontal) and never the
        ///    high-part extra: a sock or a shoe blocks only when it is under the office or next to it.
        ///    No floor under it: not low (fail closed).
        ///  - KindCompact: its label is in compactObstacleLabels (a screen). It keeps the ordinary sweep
        ///    margin everywhere, but not the extra clearance of the high part of a path.
        ///  - KindUsual: everything else, unchanged.
        /// </summary>
        public static byte Classify(IRoomGeometry room, FurnitureBox box, CeilingPlacementSettings s)
        {
            if (room != null && s.lowObstacleMaxHeight > 0f)
            {
                int fl = SpatialGeometry.FindFloor(room, box.center);
                if (fl >= 0 && box.maxY <= room.GetFloor(fl).height + s.lowObstacleMaxHeight) return KindLow;
            }
            string[] compact = s.compactObstacleLabels;
            if (compact != null && !string.IsNullOrEmpty(box.label))
                for (int i = 0; i < compact.Length; i++)
                    if (string.Equals(compact[i], box.label, StringComparison.OrdinalIgnoreCase)) return KindCompact;
            return KindUsual;
        }

        /// <summary>
        /// The swept-volume test for a route of one to three straight legs. Every leg is sampled as
        /// in the class summary. What the options add:
        ///
        ///  - Desk end (fromDesk / toDesk). The desk pose is where the user put the office: it sits
        ///    0.6 to 0.7 m in front of the seated user, lower than the head, inside the keep-out
        ///    cylinder, and usually on or against the user's own desk and screen. Demanding the
        ///    margins there would refuse every move. So a margin that is ALREADY broken at the desk
        ///    pose (the user keep-out, a furniture volume, the room outline) is excused until the
        ///    office first keeps it. While excused the office may not make it worse: it may not come
        ///    closer to the head's axis than its desk distance less deskApproachAllowance, may not
        ///    come closer to that furniture or sink deeper into its height range (deskContactTolerance),
        ///    and may not come closer to the wall. Once a margin has been kept it applies for the
        ///    rest of the path, and after deskStartRegion metres of travel every margin applies
        ///    whether it was reached or not. A path that ends at the desk is the same path reversed.
        ///  - Above the keep-out cylinder the office must stay userOverheadRadius from the head's
        ///    axis: it never passes directly overhead.
        ///  - Mast: a thin line from the top of the office to the mount point on the ceiling is
        ///    tested against every furniture volume at every sample, and on the high part of the
        ///    path the office box is extended up to the ceiling.
        ///  - handPlaced: the outline is not tested; with no room data only the user rules are.
        ///
        /// Everything else fails closed, as before.
        /// </summary>
        public static bool SweepRoute(IRoomGeometry room, SeatedUser user, WorkspaceRoute route, Vector2 footprint,
                                      CeilingPlacementSettings s, PathSweepOptions o, out CeilingPathCheck check)
        {
            check = default(CeilingPathCheck);
            check.minFurnitureClearance = float.PositiveInfinity;
            check.minUserClearance = float.PositiveInfinity;
            check.minWallClearance = float.PositiveInfinity;
            if (route.count < 2) { check.blockedBy = CeilingPathBlock.Unverifiable; return false; }
            check.start = route.First.position;
            check.end = route.Last.position;
            check.startYawDeg = route.First.rotation.eulerAngles.y;
            check.endYawDeg = route.Last.rotation.eulerAngles.y;

            // A path that ends at the desk is swept from the desk end, so the same rule covers both.
            bool flipped = o.toDesk && !o.fromDesk;
            if (flipped) route = route.Reversed();
            bool deskStart = o.fromDesk || o.toDesk;

            bool haveRoom = room != null && room.HasRoom;
            check.roomChecked = haveRoom;
            if (!haveRoom && !o.handPlaced) { check.blockedBy = CeilingPathBlock.Unverifiable; return false; }

            float step = EffectiveSweepStep(s);
            float pad = 0.5f * step;
            float marginH = Mathf.Max(0f, s.sweepMarginHorizontal);
            float marginV = Mathf.Max(0f, s.sweepMarginVertical);
            float extraHigh = Mathf.Max(0f, s.descentClearance);
            check.samplePadding = pad;

            // Travel: how far the fastest point of the box moves. Translation, plus the arc of the
            // footprint corner farthest from the root while the office turns. Summed over the legs.
            int legs = route.count - 1;
            float total = 0f, routeLo = float.PositiveInfinity;
            int samples = 0;
            for (int l = 0; l < legs; l++)
            {
                float legTravel = LegTravel(route[l], route[l + 1], footprint, s);
                if (float.IsNaN(legTravel) || float.IsInfinity(legTravel)) { check.blockedBy = CeilingPathBlock.Unverifiable; return false; }
                total += legTravel;
                samples += Mathf.Max(1, Mathf.CeilToInt(legTravel / step)) + 1;
                routeLo = Mathf.Min(routeLo, Mathf.Min(route[l].position.y, route[l + 1].position.y));
            }
            check.travel = total;
            if (samples > MaxSweepSamples + legs) { check.blockedBy = CeilingPathBlock.Unverifiable; return false; }

            // The room outline the path must stay inside: the floor outline around the user, or
            // failing that a ceiling outline over either end. None: the path cannot be checked.
            Vector2[] outline = null;
            float userBottom = float.NegativeInfinity;     // no floor under the user: the keep-out has no lower end
            Vector2 userXZ = new Vector2(user.head.x, user.head.z);
            if (haveRoom)
            {
                int fl = SpatialGeometry.FindFloor(room, userXZ);
                if (fl >= 0) { RoomPlane floor = room.GetFloor(fl); outline = floor.polygon; userBottom = floor.height; }
                else
                {
                    int ci = SpatialGeometry.FindCeiling(room, SpatialGeometry.XZ(route.First.position));
                    if (ci < 0) ci = SpatialGeometry.FindCeiling(room, SpatialGeometry.XZ(route.Last.position));
                    if (ci >= 0) outline = room.GetCeiling(ci).polygon;
                }
            }
            if (o.handPlaced) outline = null;               // a hand-placed spot is not vouched for by the scan
            else if (outline == null) { check.blockedBy = CeilingPathBlock.Unverifiable; return false; }

            float userTop = user.head.y + s.userKeepOutAboveHead;
            float userNeed = Mathf.Max(0f, s.userKeepOutRadius) + pad;
            float overNeed = Mathf.Max(0f, s.userOverheadRadius) + pad;
            // Where the larger clearance starts: relative to the lower end (the pod's descent), or
            // wherever the office is above the user's keep-out (mode-change routes).
            float highAbove = o.absoluteHigh ? userTop : routeLo + s.highClearanceAboveInspection;
            int furnitureCount = haveRoom ? room.FurnitureCount : 0;

            // Per box, once per sweep: low floor clutter keeps lowObstacleMargin instead of the sweep
            // margin; a compact fixed object (a screen) never gets the extra clearance of the high part.
            if (_kind.Length < furnitureCount) _kind = new byte[furnitureCount];
            float lowMargin = Mathf.Max(0f, s.lowObstacleMargin);
            for (int f = 0; f < furnitureCount; f++) _kind[f] = Classify(room, room.GetFurniture(f), s);

            // The desk-end excuses (see the summary).
            float region = Mathf.Max(0f, s.deskStartRegion);
            float tolerance = Mathf.Max(0f, s.deskContactTolerance);
            bool userExcused = deskStart, wallExcused = deskStart;
            float userFloor = 0f, wallFloor = 0f;
            if (deskStart && _excused.Length < furnitureCount)
            {
                _excused = new bool[furnitureCount];
                _excusedDistance = new float[furnitureCount];
                _excusedOverlap = new float[furnitureCount];
            }

            float before = 0f;
            bool first = true;
            for (int l = 0; l < legs; l++)
            {
                RootPose from = route[l], to = route[l + 1];
                float legTravel = LegTravel(from, to, footprint, s);
                int n = Mathf.Max(1, Mathf.CeilToInt(legTravel / step));

                // Vertical extent of the leg. A sample stands for the poses up to half a step before
                // and after it, so its box is stretched by half the height change per step, never
                // beyond the ends of the leg.
                float pathLo = Mathf.Min(from.position.y, to.position.y);
                float pathHi = Mathf.Max(from.position.y, to.position.y);
                float dyHalf = 0.5f * (pathHi - pathLo) / n;

                for (int i = 0; i <= n; i++)
                {
                    float k = (float)i / n;
                    RootPose p = PathPose(from, to, k);
                    OrientedRect rect = SpatialGeometry.Footprint(p.position, p.rotation, p.scale, footprint, s.footprintCenterOffset);
                    float lo = Mathf.Max(pathLo, p.position.y - dyHalf);
                    float hiBase = Mathf.Min(pathHi, p.position.y + dyHalf);
                    float hi = hiBase + s.officeHeight * p.scale;
                    // High part of the path: out of the user's sight and reach, so the larger clearance applies
                    // (not to low clutter or compact fixed objects, see Classify).
                    bool high = hiBase > highAbove;
                    float needUsual = marginH + pad + (high ? extraHigh : 0f);
                    float needCompact = marginH + pad;
                    float needLow = Mathf.Min(marginH, lowMargin) + pad;
                    // With a mast, everything between a high office and the ceiling is in the way too.
                    float boxTop = o.mast && high && o.mastTop.y > hi ? o.mastTop.y : hi;
                    float travelled = before + k * legTravel;
                    bool inRegion = deskStart && travelled <= region;
                    float fraction = total > 1e-6f ? travelled / total : 0f;
                    if (flipped) fraction = 1f - fraction;
                    check.sampleCount++;

                    // 1. Recognised furniture at the office's height.
                    for (int f = 0; f < furnitureCount; f++)
                    {
                        FurnitureBox box = room.GetFurniture(f);
                        float need = _kind[f] == KindLow ? needLow : (_kind[f] == KindCompact ? needCompact : needUsual);
                        bool touches = SpatialGeometry.VerticalRangesTouch(lo, boxTop, box.minY, box.maxY, marginV);
                        float d = float.PositiveInfinity;
                        if (touches)
                        {
                            d = rect.DistanceTo(box.Footprint);
                            if (d < check.minFurnitureClearance) { check.minFurnitureClearance = d; check.nearestFurnitureLabel = box.label; }
                        }
                        bool breaks = touches && d <= need;
                        if (deskStart)
                        {
                            // How far the office box reaches into the furniture's height range (negative: a gap).
                            float overlap = Mathf.Min(p.position.y + s.officeHeight * p.scale, box.maxY) - Mathf.Max(p.position.y, box.minY);
                            if (first)
                            {
                                _excused[f] = breaks;
                                _excusedDistance[f] = d;
                                _excusedOverlap[f] = overlap;
                            }
                            else if (!breaks) _excused[f] = false;          // margin kept once: it applies from here on
                            if (breaks && _excused[f] && inRegion
                                && d >= _excusedDistance[f] - tolerance && overlap <= _excusedOverlap[f] + tolerance)
                                breaks = false;                             // still leaving it, and not getting worse
                        }
                        if (breaks)
                        {
                            check.blockedBy = CeilingPathBlock.Furniture;
                            check.blockingLabel = box.label;
                            check.blockedAtFraction = fraction;
                            return false;
                        }

                        // The mast: a thin line from the top of the office to the mount on the ceiling.
                        if (o.mast && hi < o.mastTop.y)
                        {
                            Vector3 foot = new Vector3(rect.center.x, hi, rect.center.y);
                            if (SpatialGeometry.SegmentHitsBox(foot, o.mastTop, box, Mathf.Max(0f, s.mastRadius) + marginH + pad, marginV))
                            {
                                check.blockedBy = CeilingPathBlock.Furniture;
                                check.blockingLabel = box.label;
                                check.blockedAtFraction = fraction;
                                check.blockedByMast = true;
                                return false;
                            }
                        }
                    }

                    // 2. The user: a cylinder from the floor to above the head, and above it "never overhead".
                    {
                        float d = rect.DistanceToPoint(userXZ);
                        bool atUserHeight = hi >= userBottom && lo <= userTop;
                        if (atUserHeight && d < check.minUserClearance) check.minUserClearance = d;
                        bool kept = atUserHeight ? d > userNeed : d > overNeed;
                        if (first && deskStart) userFloor = Mathf.Max(d - Mathf.Max(0f, s.deskApproachAllowance), 0.5f * d);
                        if (kept) userExcused = false;
                        else if (!(userExcused && inRegion && d >= userFloor))
                        {
                            if (d < check.minUserClearance) check.minUserClearance = d;
                            check.blockedBy = CeilingPathBlock.User;
                            check.blockedAtFraction = fraction;
                            return false;
                        }
                    }

                    // 3. The room outline (walls).
                    if (outline != null)
                    {
                        float edge;
                        bool inside = SpatialGeometry.RectInsidePolygon(outline, rect, 0f, out edge);
                        if (edge < check.minWallClearance) check.minWallClearance = edge;
                        bool kept = inside && edge > marginH + pad;
                        if (first && deskStart) wallFloor = edge - tolerance;
                        if (kept) wallExcused = false;
                        else if (!(wallExcused && inRegion && edge >= wallFloor))
                        {
                            check.blockedBy = CeilingPathBlock.Wall;
                            check.blockedAtFraction = fraction;
                            return false;
                        }
                    }
                    first = false;
                }
                before += legTravel;
            }

            check.clear = true;
            return true;
        }

        static float LegTravel(RootPose from, RootPose to, Vector2 footprint, CeilingPlacementSettings s)
        {
            float reach = SpatialGeometry.FootprintReach(footprint, s.footprintCenterOffset, Mathf.Max(from.scale, to.scale));
            float turn = Quaternion.Angle(from.rotation, to.rotation) * Mathf.Deg2Rad;
            return (to.position - from.position).magnitude + reach * turn;
        }

        /// <summary>The height (of the office base) at which an up-and-over route travels: overRouteAboveHead
        /// above the eyes, lowered so the office still hangs dockDrop below any detected ceiling over either end.</summary>
        public static float TransitHeight(IRoomGeometry room, SeatedUser user, RootPose from, RootPose to, CeilingPlacementSettings s)
        {
            float h = user.head.y + s.overRouteAboveHead;
            if (room != null && room.HasRoom)
            {
                int a = SpatialGeometry.FindCeiling(room, SpatialGeometry.XZ(from.position));
                int b = SpatialGeometry.FindCeiling(room, SpatialGeometry.XZ(to.position));
                if (a >= 0) h = Mathf.Min(h, room.GetCeiling(a).height - s.dockDrop);
                if (b >= 0) h = Mathf.Min(h, room.GetCeiling(b).height - s.dockDrop);
            }
            return h;
        }

        /// <summary>
        /// Finds a route for a mode change (desk, floor spot, dock or inspection pose to another of
        /// them) whose swept volume is clear, or says why there is none. Two shapes are tried:
        ///
        ///  1. the straight line, when <paramref name="allowStraight"/> (never for a trip to the dock:
        ///     a diagonal from the desk would turn the office at the user's head height);
        ///  2. up and over: straight up without turning until the office base is above the user's
        ///     keep-out, across at that height (the turn happens up there), straight down.
        ///
        /// The first that passes <see cref="SweepRoute"/> is returned. When none passes, the route
        /// and check returned are those of the last shape tried. Nothing is relaxed.
        /// </summary>
        public static bool PlanRoute(IRoomGeometry room, SeatedUser user, RootPose from, RootPose to, Vector2 footprint,
                                     CeilingPlacementSettings s, PathSweepOptions o, bool allowStraight,
                                     out WorkspaceRoute route, out CeilingPathCheck check)
        {
            o.absoluteHigh = true;
            route = WorkspaceRoute.Straight(from, to);
            check = default(CeilingPathCheck);
            if (allowStraight && SweepRoute(room, user, route, footprint, s, o, out check)) return true;

            WorkspaceRoute over = WorkspaceRoute.UpAndOver(from, to, TransitHeight(room, user, from, to, s));
            if (over.count == 2 && allowStraight) return false;      // the same straight line again
            route = over;
            return SweepRoute(room, user, over, footprint, s, o, out check);
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
            if (ci < 0) { result.explanation = "The ceiling dock is not under the detected ceiling"; return false; }
            RoomPlane ceiling = room.GetCeiling(ci);
            if (Mathf.Abs((ceiling.height - s.dockDrop) - dock.position.y) > 0.15f) { result.explanation = "The ceiling dock is not at ceiling height"; return false; }
            float dx = dock.position.x - user.head.x, dz = dock.position.z - user.head.z;
            if (Mathf.Sqrt(dx * dx + dz * dz) < s.minHorizontalFromUser) { result.explanation = "The ceiling dock is now overhead"; return false; }
            OrientedRect rect = SpatialGeometry.Footprint(dock.position, dock.rotation, 1f, footprint, s.footprintCenterOffset);
            float edge;
            if (!SpatialGeometry.RectInsidePolygon(ceiling.polygon, rect, s.wallMargin, out edge)) { result.explanation = "The ceiling dock is too close to a wall"; return false; }
            string dockLabel;
            if (OverlapsFurniture(room, rect, dock.position.y, dock.position.y + s.officeHeight, out dockLabel))
            {
                result.explanation = "The ceiling dock is now blocked by " + LabelOf(dockLabel);
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
            Vector3 mastTop = MastTop(dockPose, ceiling.height, s);
            if (MountBlocked(room, dockPose, mastTop, s, out dockLabel))
            {
                Options.Clear();
                result.explanation = "The ceiling above the dock is now blocked by " + LabelOf(dockLabel);
                return false;
            }
            RootPose inspect; CeilingPathCheck check;
            bool clear = FirstClearPath(room, user, dockPose, mastTop, footprint, s, out inspect, out check);
            Options.Clear();
            if (!clear)
            {
                result.path = check;
                switch (check.blockedBy)
                {
                    case CeilingPathBlock.Furniture:
                        result.explanation = "The way down from the dock is blocked by " + LabelOf(check.blockingLabel); break;
                    case CeilingPathBlock.User:
                        result.explanation = "The way down from the dock passes too close to you"; break;
                    case CeilingPathBlock.Wall:
                        result.explanation = "The way down from the dock passes too close to a wall"; break;
                    default:
                        result.explanation = "The way down from the dock could not be checked against the room"; break;
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
            result.mastTop = mastTop;
            result.explanation = "Saved ceiling dock restored";
            return true;
        }
    }
}
