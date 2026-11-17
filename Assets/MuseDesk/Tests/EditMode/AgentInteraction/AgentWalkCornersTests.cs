using NUnit.Framework;
using UnityEngine;
using MuseDesk.Agents;

namespace MuseDesk.Tests.AgentInteraction
{
    public class AgentWalkCornersTests
    {
        // L-shaped route: start (0,0) -> (-1,0) -> (-1,1) -> target (0,1). Two 90 degree corners.
        static Vector3[] LRoute()
        {
            var buf = new Vector3[AgentWalkPath.MaxPoints];
            var wps = new[] { new Vector3(-1f, 0f, 0f), new Vector3(-1f, 0f, 1f) };
            AgentWalkPath.Build(Vector3.zero, new Vector3(0f, 0f, 1f), wps, 2, false, buf);
            return buf;
        }

        [Test]
        public void CornerAngle_RightAnglesAreNinety_StraightIsZero()
        {
            var p = LRoute();
            Assert.AreEqual(90f, AgentWalkCorners.CornerAngle(p, 4, 1), 0.01f);
            Assert.AreEqual(90f, AgentWalkCorners.CornerAngle(p, 4, 2), 0.01f);
            var straight = new[] { Vector3.zero, new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 2f) };
            Assert.AreEqual(0f, AgentWalkCorners.CornerAngle(straight, 3, 1), 0.01f);
            Assert.AreEqual(0f, AgentWalkCorners.CornerAngle(straight, 3, 0), 0f, "endpoints have no corner");
        }

        [Test]
        public void NextStop_IsTheFirstSharpCornerThenTheNext_ThenTheEnd()
        {
            var p = LRoute();
            float s = AgentWalkCorners.NextStopDistance(p, 4, 0f, 50f, out int c);
            Assert.AreEqual(1f, s, 1e-4f); Assert.AreEqual(1, c);
            s = AgentWalkCorners.NextStopDistance(p, 4, 1f, 50f, out c);     // standing on the first corner
            Assert.AreEqual(2f, s, 1e-4f); Assert.AreEqual(2, c);
            s = AgentWalkCorners.NextStopDistance(p, 4, 2f, 50f, out c);
            Assert.AreEqual(3f, s, 1e-4f); Assert.AreEqual(-1, c, "no more corners: the end");
        }

        [Test]
        public void CornersBelowTheLimitAreWalkedThrough()
        {
            // A 30 degree bend is under the 50 degree limit: no stop, the walk runs to the end.
            var p = new[] { Vector3.zero, new Vector3(0f, 0f, 1f), new Vector3(0.5f, 0f, 1f + 0.866f) };
            float s = AgentWalkCorners.NextStopDistance(p, 3, 0f, 50f, out int c);
            Assert.AreEqual(-1, c);
            Assert.AreEqual(AgentWalkPath.Length(p, 3), s, 1e-4f);
        }

        [Test]
        public void StopsLieOnTheAuthoredWaypoints_PathUnchanged()
        {
            var p = LRoute();
            var copy = (Vector3[])p.Clone();
            AgentWalkCorners.NextStopDistance(p, 4, 0f, 50f, out int c);
            Assert.AreEqual(copy, p, "reading corners never edits the polyline");
            AgentWalkPath.Sample(p, 4, 1f / 3f, out int seg, out float f, out _);
            Assert.AreEqual(-1f, AgentWalkPath.Sample(p, 4, 1f / 3f, out _, out _, out _).x, 1e-4f);
            Assert.AreEqual(0, seg); Assert.AreEqual(1f, f, 1e-4f);
        }

        [Test]
        public void TurnSign_RightIsPositive_LeftIsNegative_ShortWayRound()
        {
            Assert.AreEqual(1, AgentWalkCorners.TurnSign(0f, 90f));
            Assert.AreEqual(-1, AgentWalkCorners.TurnSign(0f, -90f));
            Assert.AreEqual(-1, AgentWalkCorners.TurnSign(10f, 350f), "350 is 20 degrees to the left of 10");
            Assert.AreEqual(1, AgentWalkCorners.TurnSign(350f, 10f));
            Assert.AreEqual(0, AgentWalkCorners.TurnSign(0f, 0.2f));
        }

        [Test]
        public void YawAndOutYaw_FollowUnityHeading()
        {
            Assert.AreEqual(0f, AgentWalkCorners.YawOf(Vector3.forward), 1e-4f);
            Assert.AreEqual(90f, AgentWalkCorners.YawOf(Vector3.right), 1e-4f);
            var p = LRoute();
            // Leaving the first corner (-1,0) the route heads +Z; leaving the second it heads +X.
            Assert.AreEqual(0f, AgentWalkCorners.OutYaw(p, 4, 1, 123f), 1e-3f);
            Assert.AreEqual(90f, AgentWalkCorners.OutYaw(p, 4, 2, 123f), 1e-3f);
            Assert.AreEqual(123f, AgentWalkCorners.OutYaw(p, 4, 3, 123f), 1e-3f, "no segment after the end: fallback");
        }
    }
}
