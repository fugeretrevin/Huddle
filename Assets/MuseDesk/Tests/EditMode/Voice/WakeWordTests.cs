using NUnit.Framework;
using MuseDesk.Voice;

namespace MuseDesk.Tests.Voice
{
    public class WakeWordTests
    {
        static (bool ok, VoiceAgentTarget who, string rest) P(string s)
        {
            VoiceAgentTarget a; string r;
            bool ok = WakeWord.Parse(s, out a, out r);
            return (ok, a, r);
        }

        [TestCase("Researcher, why this source?", VoiceAgentTarget.Researcher, "why this source?")]
        [TestCase("developer approve", VoiceAgentTarget.Developer, "approve")]
        [TestCase("Hey tester, run it again", VoiceAgentTarget.Tester, "run it again")]
        [TestCase("Okay Manager: plan a login page", VoiceAgentTarget.Manager, "plan a login page")]
        [TestCase("dev, pause", VoiceAgentTarget.Developer, "pause")]
        [TestCase("Researcher's idea, show me", VoiceAgentTarget.Researcher, "idea, show me")]
        public void NamedAgent_IsAddressed(string s, VoiceAgentTarget who, string rest)
        {
            var r = P(s);
            Assert.IsTrue(r.ok);
            Assert.AreEqual(who, r.who);
            Assert.AreEqual(rest, r.rest);
        }

        [TestCase("Hey MuseDesk, pause everyone", "pause everyone")]
        [TestCase("muse desk resume", "resume")]
        [TestCase("Team, stop", "stop")]
        [TestCase("everyone listen up", "listen up")]
        public void GenericAddress_HasNoAgent(string s, string rest)
        {
            var r = P(s);
            Assert.IsTrue(r.ok);
            Assert.AreEqual(VoiceAgentTarget.None, r.who);
            Assert.AreEqual(rest, r.rest);
        }

        [TestCase("approve")]
        [TestCase("I'd approve that one")]
        [TestCase("reject it")]
        [TestCase("tell the researcher to approve")]          // the name is not first: chatter about an agent
        [TestCase("so I said to my developer friend")]        // "so" is filler, then "i": not an address
        [TestCase("hey hey hey researcher")]                  // more than two filler words
        [TestCase("")]
        [TestCase(null)]
        [TestCase("   ,,, ")]
        public void Chatter_IsNotAddressed(string s)
        {
            Assert.IsFalse(P(s).ok);
            Assert.IsFalse(WakeWord.IsAddressed(s));
        }

        [Test]
        public void NameAlone_IsAddressedWithEmptyRest()
        {
            var r = P("Researcher");
            Assert.IsTrue(r.ok);
            Assert.AreEqual(VoiceAgentTarget.Researcher, r.who);
            Assert.AreEqual("", r.rest);
        }

        [Test]
        public void Allows_Unaddressed_OnlyHaltingOrOpenConversation()
        {
            Assert.IsFalse(WakeWord.Allows(false, false, false), "plain chatter never acts");
            Assert.IsTrue(WakeWord.Allows(true, false, false), "addressed acts");
            Assert.IsTrue(WakeWord.Allows(false, false, true), "stop / cancel always act");
            Assert.IsTrue(WakeWord.Allows(false, true, false), "a follow-up inside an opened conversation acts");
        }
    }
}
