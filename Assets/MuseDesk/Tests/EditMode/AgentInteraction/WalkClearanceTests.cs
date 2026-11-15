using NUnit.Framework;
using UnityEngine;
using MuseDesk.Agents;

namespace MuseDesk.Tests.AgentInteraction
{
    public class WalkClearanceTests
    {
        static readonly ClearRect Box = new ClearRect("box", 1f, -0.5f, 2f, 0.5f);

        [Test]
        public void SignedDistance_OutsideEdgeCornerAndInside()
        {
            Assert.AreEqual(0.25f, WalkClearance.SignedDistance(0.75f, 0f, Box), 1e-5f);          // left of the box, beside an edge
            Assert.AreEqual(0.5f, WalkClearance.SignedDistance(1.5f, 1.0f, Box), 1e-5f);           // above an edge
            Assert.AreEqual(Mathf.Sqrt(0.18f), WalkClearance.SignedDistance(0.7f, 0.8f, Box), 1e-4f);   // off a corner (0.3, 0.3)
            Assert.AreEqual(-0.5f, WalkClearance.SignedDistance(1.5f, 0f, Box), 1e-5f);            // deepest point: centre of a 1 x 1 box
            Assert.AreEqual(0f, WalkClearance.SignedDistance(1f, 0f, Box), 1e-5f);                 // on the edge
        }

        [Test]
        public void Gap_SubtractsTheAgentRadius()
        {
            Assert.AreEqual(0.20f, WalkClearance.Gap(0.75f, 0f, 0.05f, Box), 1e-5f);
            Assert.Less(WalkClearance.Gap(0.97f, 0f, 0.05f, Box), 0f);                              // disc overlaps the box
        }

        [Test]
        public void StraightRoute_ReportsTheClosestApproach()
        {
            // Along z = 0.0 from x = 0 to x = 0.6; the box starts at x = 1: the end is 0.4 away, minus the radius.
            var p = new[] { new Vector3(0f, 0f, 0f), new Vector3(0.6f, 0f, 0f) };
            var r = WalkClearance.Evaluate(p, 2, new[] { Box }, 1, 0.05f);
            Assert.AreEqual(0.35f, r.minClearance, 1e-3f);
            Assert.AreEqual(0, r.obstacle);
            Assert.AreEqual(0.6f, r.atDistance, 0.011f);
            Assert.AreEqual(0.6f, r.length, 1e-5f);
        }

        [Test]
        public void Route_ThroughAnObstacle_IsNegative()
        {
            var p = new[] { new Vector3(0f, 0f, 0f), new Vector3(3f, 0f, 0f) };
            var r = WalkClearance.Evaluate(p, 2, new[] { Box }, 1, 0.05f);
            Assert.Less(r.minClearance, 0f);
            Assert.AreEqual(1.5f, r.atX, 0.06f);
        }

        [Test]
        public void CornerVertex_IsSampled_EvenWhenStepSkipsIt()
        {
            // The corner (0.5, 0.3) is 0.5 from the box; a huge step must still test it.
            var p = new[] { new Vector3(0f, 0f, 0f), new Vector3(0.5f, 0f, 0.3f), new Vector3(0.5f, 0f, 2f) };
            var r = WalkClearance.Evaluate(p, 3, new[] { Box }, 1, 0.05f, 10f);
            Assert.IsTrue(r.samples >= 3);
            // Nearest vertex to the box is (0.5, 0.3): 0.5 from x = 1, inside the z band, so 0.5 - 0.05.
            Assert.AreEqual(0.45f, r.minClearance, 1e-3f);
        }

        [Test]
        public void ObstacleAtTheStart_IsExemptOnlyNearTheStart()
        {
            // Home at (0.9, 0): the box is 0.1 away (touching the 0.05 disc's neighbourhood within 5 mm? gap = 0.05).
            var home = new ClearRect("desk", 0.92f, -0.5f, 2f, 0.5f);     // gap at the start = 0.02 - 0.05 < 0: overlapping at home
            var p = new[] { new Vector3(0.9f, 0f, 0f), new Vector3(0.9f, 0f, 2f) };
            var r = WalkClearance.Evaluate(p, 2, new[] { home }, 1, 0.05f, 0.01f, 0.10f);
            // Walking straight along the desk's edge at x = 0.9 keeps overlapping it (gap -0.03) beyond the grace distance.
            Assert.Less(r.minClearance, 0f);
            Assert.Greater(r.atDistance, 0.10f - 1e-3f);
            // Walking AWAY from it, the same obstacle is fine: the strict minimum ignores the first 10 cm.
            var away = new[] { new Vector3(0.9f, 0f, 0f), new Vector3(0.2f, 0f, 0f) };
            var r2 = WalkClearance.Evaluate(away, 2, new[] { home }, 1, 0.05f, 0.01f, 0.10f);
            Assert.Greater(r2.minClearance, 0f);
            Assert.Less(r2.minClearanceAll, 0f);           // but the unexempted figure still shows the overlap at home
        }

        [Test]
        public void PointTriangleDistance_InsideEdgeAndVertex()
        {
            // Right triangle (0,0) (1,0) (0,1).
            Assert.AreEqual(0f, WalkClearance.PointTriangleDistance(0.2f, 0.2f, 0f, 0f, 1f, 0f, 0f, 1f), 1e-6f);        // inside
            Assert.AreEqual(0.5f, WalkClearance.PointTriangleDistance(0.5f, -0.5f, 0f, 0f, 1f, 0f, 0f, 1f), 1e-5f);    // below the long edge
            Assert.AreEqual(Mathf.Sqrt(2f), WalkClearance.PointTriangleDistance(-1f, -1f, 0f, 0f, 1f, 0f, 0f, 1f), 1e-5f);   // off a vertex
            Assert.AreEqual(Mathf.Sqrt(0.5f) , WalkClearance.PointTriangleDistance(1f, 1f, 0f, 0f, 1f, 0f, 0f, 1f), 1e-5f);   // off the hypotenuse
        }

        [Test]
        public void DegenerateTriangle_ActsAsAWall()
        {
            // A wall seen edge-on: (0,0)-(2,0), zero area.
            Assert.AreEqual(0.3f, WalkClearance.PointTriangleDistance(1f, 0.3f, 0f, 0f, 2f, 0f, 2f, 0f), 1e-5f);
            Assert.AreEqual(1f, WalkClearance.PointTriangleDistance(3f, 0f, 0f, 0f, 2f, 0f, 1f, 0f), 1e-5f);
        }

        [Test]
        public void MeshObstacle_HoleInAnAABB_IsNotAnObstacle()
        {
            // An L-shaped mesh: the corner of its bounding box is empty. A route through the empty corner is clear.
            var m = new ClearMesh("L", 4);
            m.Add(0f, 0f, 2f, 0f, 2f, 0.4f); m.Add(0f, 0f, 2f, 0.4f, 0f, 0.4f);        // bottom bar z 0..0.4
            m.Add(0f, 0f, 0.4f, 0f, 0.4f, 2f); m.Add(0f, 0f, 0.4f, 2f, 0f, 2f);        // left bar x 0..0.4
            Assert.AreEqual(2f, m.maxX, 1e-5f);
            Assert.AreEqual(2f, m.maxZ, 1e-5f);
            var p = new[] { new Vector3(1.5f, 0f, 1.5f), new Vector3(1.0f, 0f, 1.5f) };           // far inside the empty corner
            var r = WalkClearance.Evaluate(p, 2, new[] { m }, 1, 0.05f);
            Assert.Greater(r.minClearance, 0.5f);
            // The same route against the bounding rectangle would have failed.
            var bb = new ClearRect("bb", m.minX, m.minZ, m.maxX, m.maxZ);
            Assert.Less(WalkClearance.Evaluate(p, 2, new[] { bb }, 1, 0.05f).minClearance, 0f);
        }

        [Test]
        public void MeshObstacle_RouteGrazingTheBar_ReportsTheGap()
        {
            var m = new ClearMesh("bar", 2);
            m.Add(0f, 0f, 2f, 0f, 2f, 0.4f); m.Add(0f, 0f, 2f, 0.4f, 0f, 0.4f);
            var p = new[] { new Vector3(0f, 0f, 0.6f), new Vector3(2f, 0f, 0.6f) };           // 0.2 above the bar, disc 0.05
            var r = WalkClearance.Evaluate(p, 2, new[] { m }, 1, 0.05f);
            Assert.AreEqual(0.15f, r.minClearance, 1e-4f);
        }

        [Test]
        public void NoObstacles_IsInfinite()
        {
            var p = new[] { Vector3.zero, new Vector3(1f, 0f, 0f) };
            var r = WalkClearance.Evaluate(p, 2, new ClearRect[0], 0, 0.05f);
            Assert.IsTrue(float.IsPositiveInfinity(r.minClearance));
            Assert.AreEqual(-1, r.obstacle);
        }

        [Test]
        public void SinglePointRoute_DoesNotThrow()
        {
            var r = WalkClearance.Evaluate(new[] { new Vector3(0f, 0f, 0f) }, 1, new[] { Box }, 1, 0.05f);
            Assert.AreEqual(0.95f, r.minClearance, 1e-3f);
        }
    }
}
