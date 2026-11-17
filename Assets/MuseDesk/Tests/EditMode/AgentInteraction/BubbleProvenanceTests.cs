using NUnit.Framework;
using MuseDesk.Agents;

namespace MuseDesk.Tests.AgentInteraction
{
    public class BubbleProvenanceTests
    {
        [Test]
        public void Words_FollowTheRunMode_AndAreNullWhenUnknown()
        {
            Assert.IsNull(BubbleProvenance.Words(true, true));       // rc20: live needs no tag (owner: clutter)
            Assert.AreEqual("SCRIPTED DEMO", BubbleProvenance.Words(true, false));
            Assert.IsNull(BubbleProvenance.Words(false, true));      // an unknown mode is never reported as live
            Assert.IsNull(BubbleProvenance.Words(false, false));
        }

        [Test]
        public void Compose_AddsASmallFooterLine_OnlyWhenThereIsOne()
        {
            Assert.AreEqual("Reading sources", BubbleProvenance.Compose("Reading sources", null));
            Assert.AreEqual("Reading sources", BubbleProvenance.Compose("Reading sources", ""));
            string s = BubbleProvenance.Compose("Reading sources", "SCRIPTED DEMO");
            StringAssert.StartsWith("Reading sources\n", s);
            StringAssert.Contains("SCRIPTED DEMO", s);
            StringAssert.Contains("<size=85%>", s);
        }

        [Test]
        public void MaxLines_AllowsOneMoreForTheFooter()
        {
            Assert.AreEqual(3, BubbleProvenance.MaxLines(null));
            Assert.AreEqual(4, BubbleProvenance.MaxLines("SCRIPTED DEMO"));
        }
    }
}
