using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Routes for the moves that start and end low, beside the user: desk to floor, floor to floor,
    /// floor back to the desk. It only CHOOSES shapes; every shape is judged by the unchanged
    /// <see cref="CeilingPlacementEvaluator.SweepRoute"/> with the caller's options, and a shape is
    /// used only when its whole swept volume is clear. Nothing is relaxed, nothing is skipped.
    ///
    /// Why more than one shape: the usual up-and-over route rises at the desk pose, then crosses
    /// diagonally to above the spot while it turns. For a spot beside or behind the user's shoulder
    /// that diagonal swings the near corner of the 1.0 m wide office to within 7 to 8 cm of the
    /// head's vertical axis while it is overhead, and the sweep (correctly) refuses it: "passes too
    /// close to you". In the 5 x 5 m test office (desk in front, user seated at its middle) that
    /// refused 63 of the 76 grid spots that pass the floor rules for a chosen spot, every one of
    /// them between beside and behind the shoulder; with the side steps below 58 of 76 are
    /// reachable, and what is left is straight behind the user (worked out with a port of the
    /// sweep; the EditMode tests pin the cases).
    ///
    /// The shapes, tried in this order (deterministic):
    ///  1. the two shapes of <see cref="CeilingPlacementEvaluator.PlanRoute"/>: the straight line,
    ///     then up and over;
    ///  2. side step: from the low end at the desk (or the start), rise AND step sideways toward the
    ///     spot's side at the same time, keeping the yaw (or half of the turn), then cross at that
    ///     height to above the spot turning, then come straight down. Stepping out first keeps the
    ///     office's near edge away from the head while it turns;
    ///  3. the same with the whole turn made on the way down, over the spot (away from the user).
    /// A route that ends at the desk is built from the desk end and reversed, so the way back is
    /// the same set of poses as the way out. When no shape is clear the route and words returned are
    /// those of the shape that got furthest from the desk end before it was stopped (the usual
    /// shape on a tie), so the user hears about the cabinet by the spot rather than about a turn
    /// over their head that a side step avoids anyway.
    ///
    /// Routes from or to the ceiling dock (mast option) try the usual shapes, then the dock side step
    /// (<see cref="PlanDockSideStep"/>, worn pass 1): rise, step sideways at the transit height, go to the dock.
    /// </summary>
    public static class FloorRoutePlanner
    {
        public struct SideStep
        {
            /// <summary>Sideways distance of the step, metres, toward the spot's side.</summary>
            public float step;
            /// <summary>The office has made half of its turn at the top of the step (else none of it).</summary>
            public bool halfTurnOnRise;
            /// <summary>The (rest of the) turn is made on the way down over the spot, not while crossing.</summary>
            public bool turnOnDescent;

            public SideStep(float step, bool halfTurnOnRise, bool turnOnDescent)
            {
                this.step = step; this.halfTurnOnRise = halfTurnOnRise; this.turnOnDescent = turnOnDescent;
            }
        }

        /// <summary>The side steps tried after the usual shapes, in order.</summary>
        public static readonly SideStep[] SideSteps =
        {
            new SideStep(0.5f, false, false),
            new SideStep(0.8f, false, false),
            new SideStep(0.5f, true, false),
            new SideStep(0.8f, true, false),
            new SideStep(0.6f, false, true),
            new SideStep(0.9f, false, true),
        };

        /// <summary>
        /// Plan a route from <paramref name="from"/> to <paramref name="to"/> whose swept volume is clear,
        /// or say why there is none (see the class summary for which shape's words). Same arguments and
        /// options as <see cref="CeilingPlacementEvaluator.PlanRoute"/>.
        /// </summary>
        public static bool Plan(IRoomGeometry room, SeatedUser user, RootPose from, RootPose to, Vector2 footprint,
                                CeilingPlacementSettings s, PathSweepOptions o, bool allowStraight,
                                out WorkspaceRoute route, out CeilingPathCheck check)
        {
            if (CeilingPlacementEvaluator.PlanRoute(room, user, from, to, footprint, s, o, allowStraight, out route, out check))
                return true;
            if (o.mast) return PlanDockSideStep(room, user, from, to, footprint, s, o, ref route, ref check);

            float h = CeilingPlacementEvaluator.TransitHeight(room, user, from, to, s);
            bool reverse = o.toDesk && !o.fromDesk;
            RootPose near = reverse ? to : from;
            RootPose far = reverse ? from : to;
            if (near.position.y >= h - 0.001f || far.position.y >= h - 0.001f) return false;

            // Sideways as seen by the user: the office faces them (+Z toward the user), so its -X is the user's right.
            Vector3 lateral = near.rotation * Vector3.left;
            lateral.y = 0f;
            if (lateral.sqrMagnitude < 1e-6f) return false;
            lateral.Normalize();
            Vector3 across = far.position - near.position;
            across.y = 0f;
            Vector3 side = Vector3.Dot(across, lateral) >= 0f ? lateral : -lateral;

            PathSweepOptions so = o;
            so.absoluteHigh = true;                     // as PlanRoute sweeps its shapes
            float best = Progress(check, reverse);
            for (int i = 0; i < SideSteps.Length; i++)
            {
                WorkspaceRoute r = Build(near, far, h, side, SideSteps[i]);
                if (reverse) r = r.Reversed();
                CeilingPathCheck c;
                if (CeilingPlacementEvaluator.SweepRoute(room, user, r, footprint, s, so, out c))
                {
                    route = r;
                    check = c;
                    return true;
                }
                // The words: of the shape that got furthest before it was stopped. The usual shape is
                // often stopped early by its own turn over the head; what stops a side step further on
                // (a cabinet by the spot) is what the user can do something about.
                float p = Progress(c, reverse);
                if (p > best + 1e-4f) { best = p; route = r; check = c; }
            }
            return false;
        }

        /// <summary>Sideways steps (metres) tried for a trip between a low pose and the ceiling dock.</summary>
        public static readonly float[] DockSteps = { 0.6f, 0.9f, 1.2f };

        /// <summary>
        /// A trip between a low pose (the desk, a floor spot) and the ceiling dock when the usual
        /// up-and-over is refused (worn pass 1). Up-and-over rises at the desk and then crosses to the
        /// dock while it turns; with the dock beside or behind the shoulder (the only docks left when
        /// the desk stands against the wall in front) that turn swings the office's near corner to 7 or
        /// 8 cm from the head's axis and the sweep refuses it, so no dock could be reached at all.
        ///
        /// Shape: rise straight up at the low pose without turning (as before: everything below the
        /// transit height is a pure vertical move), step sideways toward the dock at the transit height
        /// still without turning (the office moves away from the head, its near edge never closer than
        /// at the desk), then go to the dock, turning on that last leg. Every shape is judged by the
        /// unchanged SweepRoute with the caller's options; a trip from the dock is the same shape
        /// reversed. Nothing is relaxed.
        /// </summary>
        static bool PlanDockSideStep(IRoomGeometry room, SeatedUser user, RootPose from, RootPose to, Vector2 footprint,
                                     CeilingPlacementSettings s, PathSweepOptions o, ref WorkspaceRoute route, ref CeilingPathCheck check)
        {
            float h = CeilingPlacementEvaluator.TransitHeight(room, user, from, to, s);
            bool fromHigh = from.position.y > to.position.y;
            RootPose low = fromHigh ? to : from;
            RootPose high = fromHigh ? from : to;
            // Only a trip between a pose below the transit height and one at or above it (the dock).
            if (low.position.y >= h - 0.001f || high.position.y < h - 0.001f) return false;

            Vector3 lateral = low.rotation * Vector3.left;
            lateral.y = 0f;
            if (lateral.sqrMagnitude < 1e-6f) return false;
            lateral.Normalize();
            Vector3 across = high.position - low.position;
            across.y = 0f;
            Vector3 side = Vector3.Dot(across, lateral) >= 0f ? lateral : -lateral;

            PathSweepOptions so = o;
            so.absoluteHigh = true;                     // as PlanRoute sweeps its shapes
            float best = Progress(check, fromHigh);
            for (int i = 0; i < DockSteps.Length; i++)
            {
                WorkspaceRoute r = BuildDockSideStep(low, high, h, side * DockSteps[i]);
                if (fromHigh) r = r.Reversed();
                CeilingPathCheck c;
                if (CeilingPlacementEvaluator.SweepRoute(room, user, r, footprint, s, so, out c))
                {
                    route = r;
                    check = c;
                    return true;
                }
                float p = Progress(c, fromHigh);
                if (p > best + 1e-4f) { best = p; route = r; check = c; }
            }
            return false;
        }

        /// <summary>low, (above low at height), (stepped sideways at height, same yaw), high.</summary>
        public static WorkspaceRoute BuildDockSideStep(RootPose low, RootPose high, float height, Vector3 step)
        {
            var r = new WorkspaceRoute();
            r.Add(low);
            r.Add(new RootPose(new Vector3(low.position.x, height, low.position.z), low.rotation, low.scale));
            r.Add(new RootPose(new Vector3(low.position.x + step.x, height, low.position.z + step.z), low.rotation, low.scale));
            r.Add(high);
            return r;
        }

        /// <summary>How far from the low (desk) end a refused shape got, 0..1.</summary>
        static float Progress(CeilingPathCheck c, bool reverse)
        {
            if (c.clear || c.blockedBy == CeilingPathBlock.None) return -1f;
            return reverse ? 1f - c.blockedAtFraction : c.blockedAtFraction;
        }

        /// <summary>near, (step sideways and up), (above far), far.</summary>
        public static WorkspaceRoute Build(RootPose near, RootPose far, float height, Vector3 side, SideStep v)
        {
            Quaternion topYaw = v.halfTurnOnRise ? Quaternion.Slerp(near.rotation, far.rotation, 0.5f) : near.rotation;
            Vector3 p1 = near.position + side * v.step;
            p1.y = height;
            Vector3 p2 = new Vector3(far.position.x, height, far.position.z);
            var r = new WorkspaceRoute();
            r.Add(near);
            r.Add(new RootPose(p1, topYaw, near.scale));
            r.Add(new RootPose(p2, v.turnOnDescent ? topYaw : far.rotation, far.scale));
            r.Add(far);
            return r;
        }
    }
}
