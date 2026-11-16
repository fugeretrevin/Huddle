using NUnit.Framework;
using MuseDesk.Voice;

namespace MuseDesk.Tests.Voice
{
    /// <summary>
    /// Meet by voice (PortalVoiceRequest): which sentences ask an agent to step out beside the user, and the routing rule
    /// VoiceCommandCenter applies (only while the Wall registered, never inside an open conversation, the hands-free name
    /// rule kept). Pure; the scene side (the Wall's handler, the card) is covered by AgentScripts/MeetDrive.cs in Play Mode.
    /// </summary>
    public class PortalVoiceRequestTests
    {
        static VoiceAgentTarget Parse(string s)
        {
            VoiceAgentTarget a;
            return PortalVoiceRequest.TryParse(s, out a) ? a : VoiceAgentTarget.None;
        }

        [TestCase("Researcher, come here", VoiceAgentTarget.Researcher)]
        [TestCase("Tester, meet me", VoiceAgentTarget.Tester)]
        [TestCase("meet the developer", VoiceAgentTarget.Developer)]
        [TestCase("Meet the Developer.", VoiceAgentTarget.Developer)]
        [TestCase("Hey tester, come over here please", VoiceAgentTarget.Tester)]
        [TestCase("Developer, could you come over?", VoiceAgentTarget.Developer)]
        [TestCase("Manager, come to me", VoiceAgentTarget.Manager)]
        [TestCase("researcher come here now", VoiceAgentTarget.Researcher)]
        [TestCase("qa, come here", VoiceAgentTarget.Tester)]
        [TestCase("dev, step out", VoiceAgentTarget.Developer)]
        [TestCase("let me meet the tester", VoiceAgentTarget.Tester)]
        [TestCase("I want to meet the researcher", VoiceAgentTarget.Researcher)]
        [TestCase("I'd like to meet the manager", VoiceAgentTarget.Manager)]
        [TestCase("Hey MuseDesk, meet the developer", VoiceAgentTarget.Developer)]
        [TestCase("meet with the tester please", VoiceAgentTarget.Tester)]
        [TestCase("meet the researcher full size", VoiceAgentTarget.Researcher)]
        [TestCase("Researcher, meet the tester", VoiceAgentTarget.Tester)]
        public void Accepted(string heard, VoiceAgentTarget who)
        {
            Assert.AreEqual(who, Parse(heard), heard);
        }

        [TestCase("")]
        [TestCase("come here")]                                     // nobody named
        [TestCase("MuseDesk, come here")]                            // the office, not an agent
        [TestCase("meet me")]
        [TestCase("meet the team")]
        [TestCase("Researcher, don't come here")]                    // negation
        [TestCase("I don't want to meet the tester")]
        [TestCase("Tester, never come over here")]
        [TestCase("you should meet the developer")]                  // room chatter: does not start with a name or with "meet"
        [TestCase("the developer is meeting me later")]
        [TestCase("Researcher, come here and fix the build")]        // carries another request
        [TestCase("Researcher, why this source?")]
        [TestCase("Tester, come back")]
        [TestCase("Developer, stop")]
        [TestCase("Developer, approve")]
        [TestCase("meet the developer and approve the plan")]
        public void Refused(string heard)
        {
            Assert.AreEqual(VoiceAgentTarget.None, Parse(heard), heard ?? "(null)");
        }

        [Test]
        public void Null_IsRefused()
        {
            VoiceAgentTarget a;
            Assert.IsFalse(PortalVoiceRequest.TryParse(null, out a));
            Assert.AreEqual(VoiceAgentTarget.None, a);
        }

        [Test]
        public void Routes_OnlyWhileAHandlerIsRegistered()
        {
            VoiceAgentTarget a;
            Assert.IsFalse(PortalVoiceRequest.Routes("Researcher, come here", false, false, out a), "no Wall: the sentence goes the old way");
            Assert.AreEqual(VoiceAgentTarget.None, a);
            Assert.IsTrue(PortalVoiceRequest.Routes("Researcher, come here", true, false, out a));
            Assert.AreEqual(VoiceAgentTarget.Researcher, a);
        }

        [Test]
        public void Routes_NeverInsideAnOpenConversation()
        {
            VoiceAgentTarget a;
            Assert.IsFalse(PortalVoiceRequest.Routes("Tester, come here", true, true, out a), "a decision countdown or task confirmation keeps its own words");
        }

        [Test]
        public void Routes_NotForOtherSentences()
        {
            VoiceAgentTarget a;
            Assert.IsFalse(PortalVoiceRequest.Routes("Manager, plan a login page", true, false, out a), "the Manager's goal is not a portal request");
            Assert.IsFalse(PortalVoiceRequest.Routes("bring the office here", true, false, out a));
        }

        [Test]
        public void HandsFree_GateIsTheWakeWordRulePlusPortalRequests()
        {
            // Unchanged when it is not a portal request.
            for (int bits = 0; bits < 8; bits++)
            {
                bool addressed = (bits & 1) != 0, open = (bits & 2) != 0, halting = (bits & 4) != 0;
                Assert.AreEqual(WakeWord.Allows(addressed, open, halting), PortalVoiceRequest.AllowsHandsFree(addressed, open, halting, false), "bits " + bits);
                Assert.IsTrue(PortalVoiceRequest.AllowsHandsFree(addressed, open, halting, true), "a portal request names its agent: bits " + bits);
            }
        }

        [Test]
        public void HandsFree_MeetFirstSentenceIsNotAddressedByTheWakeWord_ButIsARequest()
        {
            // "meet the developer" does not start with a name, so only the portal exception lets it through hands-free.
            Assert.IsFalse(WakeWord.IsAddressed("meet the developer"));
            Assert.AreEqual(VoiceAgentTarget.Developer, Parse("meet the developer"));
            // Chatter that mentions an agent stays out on both counts.
            Assert.IsFalse(WakeWord.IsAddressed("you should meet the developer"));
            Assert.AreEqual(VoiceAgentTarget.None, Parse("you should meet the developer"));
        }
    }
}
