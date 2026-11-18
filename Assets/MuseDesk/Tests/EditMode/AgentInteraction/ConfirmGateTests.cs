using NUnit.Framework;
using MuseDesk.Agents;

namespace MuseDesk.Tests.AgentInteraction
{
    public class ConfirmGateTests
    {
        [Test]
        public void FirstPress_Arms_SecondPress_Confirms()
        {
            var g = new ConfirmGate(3f);
            Assert.IsFalse(g.Press(10f));
            Assert.IsTrue(g.IsArmed(11f));
            Assert.IsTrue(g.Press(12.9f));
            Assert.IsFalse(g.IsArmed(13f), "confirming disarms");
        }

        [Test]
        public void StrayPoke_AfterTheWindow_OnlyArmsAgain()
        {
            var g = new ConfirmGate(3f);
            Assert.IsFalse(g.Press(0f));
            Assert.IsFalse(g.IsArmed(3.1f));
            Assert.IsFalse(g.Press(3.2f), "window ran out: this press is a first press again");
            Assert.IsTrue(g.Press(4f));
        }

        [Test]
        public void Reset_Disarms()
        {
            var g = new ConfirmGate(3f);
            g.Press(0f);
            g.Reset();
            Assert.IsFalse(g.IsArmed(1f));
            Assert.IsFalse(g.Press(1f));
        }

        [Test]
        public void Label_FollowsState()
        {
            var g = new ConfirmGate(3f);
            Assert.AreEqual("Remove", g.Label(0f, false, "Remove", "Sure?"));
            g.Press(0f);
            Assert.AreEqual("Sure?", g.Label(1f, false, "Remove", "Sure?"));
            Assert.AreEqual("...", g.Label(1f, true, "Remove", "Sure?"));
            Assert.AreEqual("Remove", g.Label(3.5f, false, "Remove", "Sure?"));
        }
    }
}
