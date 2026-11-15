using NUnit.Framework;
using UnityEngine;
using MuseDesk.Agents;

namespace MuseDesk.Tests.AgentInteraction
{
    public class AgentWalkPathTests
    {
        static readonly Vector3[] Wps = { new Vector3(-1f, 0f, 0f), new Vector3(-1f, 0f, 1f) };

        [Test]
        public void NoWaypoints_IsStraightTwoPointLine()
        {
            var buf = new Vector3[AgentWalkPath.MaxPoints];
            int n = AgentWalkPath.Build(Vector3.zero, new Vector3(0f, 0f, 2f), Wps, 0, false, buf);
            Assert.AreEqual(2, n);
            Assert.AreEqual(2f, AgentWalkPath.Length(buf, n), 1e-5f);
            Vector3 p = AgentWalkPath.Sample(buf, n, 0.5f, out int seg, out float f, out Vector3 dir);
            Assert.AreEqual(1f, p.z, 1e-5f); Assert.AreEqual(0, seg); Assert.AreEqual(0.5f, f, 1e-5f);
            Assert.AreEqual(1f, dir.z, 1e-5f);
        }

        [Test]
        public void Waypoints_AreVisitedInOrder_AndReversedOnTheWayHome()
        {
            var buf = new Vector3[AgentWalkPath.MaxPoints];
            int n = AgentWalkPath.Build(Vector3.zero, new Vector3(0f, 0f, 1f), Wps, 2, false, buf);
            Assert.AreEqual(4, n);
            Assert.AreEqual(-1f, buf[1].x); Assert.AreEqual(0f, buf[1].z);
            Assert.AreEqual(1f, buf[2].z);
            n = AgentWalkPath.Build(new Vector3(0f, 0f, 1f), Vector3.zero, Wps, 2, true, buf);
            Assert.AreEqual(1f, buf[1].z); Assert.AreEqual(0f, buf[2].z);
        }

        [Test]
        public void Sample_EndsExactlyAtTarget_AndPassesTheCorner()
        {
            var buf = new Vector3[AgentWalkPath.MaxPoints];
            int n = AgentWalkPath.Build(Vector3.zero, new Vector3(0f, 0f, 1f), Wps, 2, false, buf);
            float len = AgentWalkPath.Length(buf, n);      // 1 + 1 + 1
            Assert.AreEqual(3f, len, 1e-5f);
            Vector3 corner = AgentWalkPath.Sample(buf, n, 1f / 3f, out _, out _, out _);
            Assert.AreEqual(-1f, corner.x, 1e-4f); Assert.AreEqual(0f, corner.z, 1e-4f);
            Vector3 end = AgentWalkPath.Sample(buf, n, 1f, out int seg, out float f, out _);
            Assert.AreEqual(1f, end.z, 1e-5f); Assert.AreEqual(n - 2, seg); Assert.AreEqual(1f, f, 1e-5f);
        }

        [Test]
        public void Duration_IsStretchedOnlyWhenTooFast()
        {
            Assert.AreEqual(1.6f, AgentWalkPath.Duration(1.6f, 0.2f, 0.21f), 1e-5f);
            Assert.AreEqual(3f, AgentWalkPath.Duration(1.6f, 0.63f, 0.21f), 1e-4f);
        }

        // ---- joining a route part-way (desk choreography layout) ----
        // Route used below, the shape of the Researcher's delivery walk: home (0,0) -> P (1,-1) -> Q (3,-1) -> spot (3,2).
        static readonly Vector3 Home = Vector3.zero, Spot = new Vector3(3f, 0f, 2f);
        static readonly Vector3[] Lane = { new Vector3(1f, 0f, -1f), new Vector3(3f, 0f, -1f) };

        [Test]
        public void FromTheStart_NoWaypointIsBehind()
        {
            Assert.AreEqual(0, AgentWalkPath.WaypointsBehind(Home, Lane, 2, false, Spot, 0.05f));
            Assert.AreEqual(0, AgentWalkPath.WaypointsBehind(Spot, Lane, 2, true, Home, 0.05f));
        }

        [Test]
        public void StandingBetweenTheWaypoints_TheFirstOneIsBehind()
        {
            var mid = new Vector3(2f, 0f, -1f);
            Assert.AreEqual(1, AgentWalkPath.WaypointsBehind(mid, Lane, 2, false, Spot, 0.05f));
            // Sent home from the same place: the corner at Q is behind it, P is still ahead.
            Assert.AreEqual(1, AgentWalkPath.WaypointsBehind(mid, Lane, 2, true, Home, 0.05f));
        }

        [Test]
        public void StandingOnTheLastLeg_GoesStraightToTheEnd()
        {
            var inAisle = new Vector3(3f, 0f, 0.5f);
            Assert.AreEqual(2, AgentWalkPath.WaypointsBehind(inAisle, Lane, 2, false, Spot, 0.05f));
            // Sent home from the aisle: it is on the leg BEFORE the first reversed waypoint, so both corners are still ahead.
            Assert.AreEqual(0, AgentWalkPath.WaypointsBehind(inAisle, Lane, 2, true, Home, 0.05f));
        }

        [Test]
        public void StandingOnACorner_ThatCornerIsBehind()
        {
            Assert.AreEqual(1, AgentWalkPath.WaypointsBehind(Lane[0], Lane, 2, false, Spot, 0.05f));
            Assert.AreEqual(2, AgentWalkPath.WaypointsBehind(Lane[1], Lane, 2, false, Spot, 0.05f));
            Assert.AreEqual(1, AgentWalkPath.WaypointsBehind(Lane[1], Lane, 2, true, Home, 0.05f));
        }

        [Test]
        public void OffTheRoute_WalksTheWholeRoute()
        {
            Assert.AreEqual(0, AgentWalkPath.WaypointsBehind(new Vector3(2f, 0f, 0.5f), Lane, 2, false, Spot, 0.05f));
        }

        [Test]
        public void ARetargetedWalk_NeverDoublesBackToAnEarlierCorner()
        {
            // Every point along the walk out, sent home from there: the path home must be a suffix of the reversed route
            // (it never visits a corner that lies behind the agent in the direction of travel).
            var buf = new Vector3[AgentWalkPath.MaxPoints];
            var full = new Vector3[AgentWalkPath.MaxPoints];
            int fn = AgentWalkPath.Build(Home, Spot, Lane, 2, false, full);
            float total = AgentWalkPath.Length(full, fn);
            for (int s = 0; s <= 100; s++)
            {
                Vector3 at = AgentWalkPath.Sample(full, fn, s / 100f, out _, out _, out _);
                int skip = AgentWalkPath.WaypointsBehind(at, Lane, 2, true, Home, 0.01f);
                int n = AgentWalkPath.BuildFrom(at, Home, Lane, 2, true, skip, buf);
                float home = AgentWalkPath.Length(buf, n);
                float walked = total * (s / 100f);
                Assert.AreEqual(walked, home, 1e-3f, "the way home from " + at + " should be exactly the way already walked");
            }
        }

        [Test]
        public void BuildFrom_LeavesOutTheSkippedWaypoints()
        {
            var buf = new Vector3[AgentWalkPath.MaxPoints];
            int n = AgentWalkPath.BuildFrom(new Vector3(2f, 0f, -1f), Spot, Lane, 2, false, 1, buf);
            Assert.AreEqual(3, n);
            Assert.AreEqual(3f, buf[1].x); Assert.AreEqual(-1f, buf[1].z);
            Assert.AreEqual(Spot, buf[2]);
            n = AgentWalkPath.BuildFrom(Home, Spot, Lane, 2, false, 0, buf);
            Assert.AreEqual(4, n);
        }

        [Test]
        public void ZeroLength_DoesNotProduceNaN()
        {
            var buf = new Vector3[AgentWalkPath.MaxPoints];
            int n = AgentWalkPath.Build(Vector3.one, Vector3.one, Wps, 0, false, buf);
            Vector3 p = AgentWalkPath.Sample(buf, n, 0.5f, out _, out _, out _);
            Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.z));
        }
    }
}
