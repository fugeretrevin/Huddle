using System;
using NUnit.Framework;
using MuseDesk.Spatial;

namespace MuseDesk.Tests.EditMode
{
    public class CeilingVfxMapTests
    {
        [TestCase(CeilingPodState.Docked, CeilingVfxPhase.DockedIdle)]
        [TestCase(CeilingPodState.PreparingToDescend, CeilingVfxPhase.Unlocking)]
        [TestCase(CeilingPodState.Descending, CeilingVfxPhase.Descending)]
        [TestCase(CeilingPodState.Stabilizing, CeilingVfxPhase.Stabilizing)]
        [TestCase(CeilingPodState.Engaged, CeilingVfxPhase.DeployedIdle)]
        [TestCase(CeilingPodState.PreparingToRetract, CeilingVfxPhase.ClosingShelf)]
        [TestCase(CeilingPodState.Retracting, CeilingVfxPhase.ReturningToDock)]
        [TestCase(CeilingPodState.ReturningToDock, CeilingVfxPhase.DockLocking)]
        [TestCase(CeilingPodState.ErrorRecovery, CeilingVfxPhase.ReturningToDock)]
        public void MapsEachPodState(CeilingPodState state, CeilingVfxPhase expected)
        {
            Assert.AreEqual(expected, CeilingVfxMap.Map(state));
        }

        [Test]
        public void EveryPodStateHasAPhase()
        {
            foreach (CeilingPodState s in Enum.GetValues(typeof(CeilingPodState)))
                Assert.IsTrue(Enum.IsDefined(typeof(CeilingVfxPhase), CeilingVfxMap.Map(s)), s.ToString());
        }

        [Test]
        public void ShownOnlyInCeilingModeWithVisualsAndSwitchOn()
        {
            Assert.IsTrue(CeilingVfxMap.ShouldShow(true, true, true));
            Assert.IsFalse(CeilingVfxMap.ShouldShow(false, true, true), "not Ceiling mode");
            Assert.IsFalse(CeilingVfxMap.ShouldShow(true, false, true), "pod visuals off");
            Assert.IsFalse(CeilingVfxMap.ShouldShow(true, true, false), "switched off");
        }
    }
}
