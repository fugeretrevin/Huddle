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
