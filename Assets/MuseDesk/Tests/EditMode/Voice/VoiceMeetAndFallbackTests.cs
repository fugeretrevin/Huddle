using NUnit.Framework;
using MuseDesk.Voice;

namespace MuseDesk.Tests.Voice
{
    /// <summary>
    /// rc22, two owner requests:
    ///  - "'Developer, come down here' and then it triggers the Meet feature": natural come-to-me phrasings, name first or
    ///    last, forgiving names, filler (PortalVoiceRequest).
    ///  - "if something's not understood ... 'can't do that, but here's what I can do'": the line and its options
    ///    (VoiceFallbackLine), which never offer a feature the caller did not say is there.
    /// </summary>
    public class VoiceMeetAndFallbackTests
    {
        static VoiceAgentTarget Meet(string s)
        {
            VoiceAgentTarget a;
            return PortalVoiceRequest.TryParse(s, out a) ? a : VoiceAgentTarget.None;
        }

        // ───────────── Meet by voice ─────────────

        [TestCase("Developer, come down here", VoiceAgentTarget.Developer)]
        [TestCase("Developer, come down", VoiceAgentTarget.Developer)]
        [TestCase("come down here, developer", VoiceAgentTarget.Developer)]
        [TestCase("Come down here developer.", VoiceAgentTarget.Developer)]
        [TestCase("Tester, come over here", VoiceAgentTarget.Tester)]
        [TestCase("Researcher, come here", VoiceAgentTarget.Researcher)]
        [TestCase("get over here, researcher", VoiceAgentTarget.Researcher)]
        [TestCase("Manager, come to me", VoiceAgentTarget.Manager)]
        [TestCase("Researcher, come see me", VoiceAgentTarget.Researcher)]
        [TestCase("Tester, meet me", VoiceAgentTarget.Tester)]
        [TestCase("come meet me, tester", VoiceAgentTarget.Tester)]
        [TestCase("Developer, can you come here?", VoiceAgentTarget.Developer)]
        [TestCase("can you come here, developer?", VoiceAgentTarget.Developer)]
        [TestCase("um, okay, developer, could you come down here please", VoiceAgentTarget.Developer)]
        [TestCase("I was wondering if you could come down here, developer, for me please", VoiceAgentTarget.Developer)]
        [TestCase("come down here manage her", VoiceAgentTarget.Manager)]
        [TestCase("testa, come here", VoiceAgentTarget.Tester)]
        [TestCase("develop her, come down here", VoiceAgentTarget.Developer)]
        [TestCase("meet the developer", VoiceAgentTarget.Developer)]        // still works
        [TestCase("Hey MuseDesk, meet the developer", VoiceAgentTarget.Developer)]
        public void ComeToMe_NamedAgent_IsAMeetRequest(string heard, VoiceAgentTarget who)
        {
            Assert.AreEqual(who, Meet(heard), heard);
        }

        [TestCase("come down here")]                                   // nobody named
        [TestCase("MuseDesk, come down here")]
        [TestCase("Developer, don't come down here")]
        [TestCase("come down here and fix the build, developer")]       // carries another request
        [TestCase("Tester, come back")]
        [TestCase("the developer should come down here")]              // about the developer, not to them
        [TestCase("you should come see the developer")]
        [TestCase("Researcher, come here, developer")]                 // two different agents
        public void NotAMeetRequest(string heard)
        {
            Assert.AreEqual(VoiceAgentTarget.None, Meet(heard), heard);
        }

        [Test]
        public void HandsFree_TheNameIsTheAddress()
        {
            VoiceAgentTarget a;
            Assert.IsTrue(PortalVoiceRequest.Routes("come down here, developer", true, false, out a));
            Assert.AreEqual(VoiceAgentTarget.Developer, a);
            Assert.IsTrue(PortalVoiceRequest.AllowsHandsFree(false, false, false, true), "a named come request counts as addressed");
            Assert.IsFalse(PortalVoiceRequest.Routes("come down here", true, false, out a), "no name: not a request, so hands-free stays quiet");
            Assert.IsFalse(PortalVoiceRequest.Routes("Developer, come down here", false, false, out a), "no Wall registered: the old way");
        }

        [TestCase("can you come down here", true)]
        [TestCase("come here please", true)]
        [TestCase("come down here, developer", false)]
        [TestCase("don't come here", false)]
        [TestCase("come here and fix it", false)]
        [TestCase("plan a login page", false)]
        public void UnnamedComeRequest(string heard, bool expected)
        {
            Assert.AreEqual(expected, PortalVoiceRequest.IsUnnamedComeRequest(heard), heard);
        }

        [Test]
        public void NameAtEnd_ForgivingSpellings()
        {
            int at;
            Assert.AreEqual(VoiceAgentTarget.Developer, WakeWord.NameAtEnd(new[] { "come", "here", "developer" }, out at));
            Assert.AreEqual(2, at);
            Assert.AreEqual(VoiceAgentTarget.Manager, WakeWord.NameAtEnd(new[] { "come", "here", "man", "ager" }, out at));
            Assert.AreEqual(2, at);
            Assert.AreEqual(VoiceAgentTarget.Tester, WakeWord.NameAtEnd(new[] { "come", "here", "testa" }, out at));
            Assert.AreEqual(VoiceAgentTarget.None, WakeWord.NameAtEnd(new[] { "come", "here" }, out at));
            Assert.AreEqual(VoiceAgentTarget.None, WakeWord.NameAtEnd(null, out at));
        }

        // ───────────── "can't do that, but here's what I can do" ─────────────

        [Test]
        public void CompanionLine_NamesThreeThingsThatWork()
        {
            var f = new FallbackFacts
            {
                Speaker = VoiceAgentTarget.Developer, Companion = VoiceAgentTarget.Developer, ManagerCanPlan = true, PortalAvailable = true,
                BoardAvailable = true, Live = true,
            };
            Assert.AreEqual("I can't do that, but I can: answer a question about my work, tell you what I'm working on, or ask the Manager for a plan.",
                VoiceFallbackLine.Line(f));
            Assert.AreEqual("developer", VoiceFallbackLine.SpeakerId(f));
        }

        [Test]
        public void NobodyNamed_TheManagerAnswers()
        {
            var f = new FallbackFacts { Speaker = VoiceAgentTarget.None, ManagerCanPlan = true, BoardAvailable = true };
            Assert.AreEqual("manager", VoiceFallbackLine.SpeakerId(f));
            string line = VoiceFallbackLine.Line(f);
            StringAssert.StartsWith("I can't do that, but I can: draft a plan for a goal you give me", line);
        }

        [Test]
        public void TheMomentComesFirst()
        {
            var moving = new FallbackFacts { Speaker = VoiceAgentTarget.Tester, Moving = true, BoardAvailable = true };
            Assert.AreEqual("stop the office if you say stop", VoiceFallbackLine.Options(moving, 3)[0]);

            var paper = new FallbackFacts { Speaker = VoiceAgentTarget.Researcher, DecisionWaiting = true, PaperOpen = false };
            Assert.AreEqual("hand you the decision paper", VoiceFallbackLine.Options(paper, 3)[0]);

            var open = new FallbackFacts { Speaker = VoiceAgentTarget.Researcher, DecisionWaiting = true, PaperOpen = true };
            CollectionAssert.Contains(VoiceFallbackLine.Options(open, 3), "explain why we recommend it");

            var preview = new FallbackFacts { Speaker = VoiceAgentTarget.Manager, Previewing = true };
            Assert.AreEqual("show the next spot", VoiceFallbackLine.Options(preview, 3)[0]);
        }

        [Test]
        public void NeverOffersWhatIsNotThere()
        {
            var f = new FallbackFacts { Speaker = VoiceAgentTarget.Developer, ManagerCanPlan = false, PortalAvailable = false, BoardAvailable = false };
            foreach (string o in VoiceFallbackLine.Options(f, 10))
            {
                StringAssert.DoesNotContain("Manager", o);
                StringAssert.DoesNotContain("come over", o);
                StringAssert.DoesNotContain("board", o);
                StringAssert.DoesNotContain("floor", o);
                StringAssert.DoesNotContain("answer a question", o);    // nobody beside the user
            }
        }

        [Test]
        public void AlwaysTwoToThreeOptions_AndShort()
        {
            for (int bits = 0; bits < 256; bits++)
            {
                var f = new FallbackFacts
                {
                    Speaker = (VoiceAgentTarget)(bits % 5),
                    Companion = (bits & 8) != 0 ? VoiceAgentTarget.Developer : VoiceAgentTarget.None,
                    Mode = (bits >> 4) & 3,
                    Moving = (bits & 64) != 0,
                    DecisionWaiting = (bits & 128) != 0,
                    ManagerCanPlan = (bits & 1) != 0,
                    PortalAvailable = (bits & 2) != 0,
                    BoardAvailable = true,
                };
                var o = VoiceFallbackLine.Options(f, VoiceFallbackLine.MaxOptions);
                Assert.That(o.Count, Is.InRange(2, 3), "bits " + bits);
                Assert.Less(VoiceFallbackLine.Line(f).Length, 170, "bits " + bits);
            }
        }

        [Test]
        public void ComeWithoutAName_SaysWhoToName()
        {
            var f = new FallbackFacts { Speaker = VoiceAgentTarget.Manager, PortalAvailable = true, AskedToComeWithoutName = true, BoardAvailable = true };
            string line = VoiceFallbackLine.Line(f);
            StringAssert.StartsWith("I'm not sure who you're asking, but I can: bring an agent over if you say its name first", line);
        }

        [Test]
        public void OutsideTheDesk_OffersTheWayBack()
        {
            var f = new FallbackFacts { Speaker = VoiceAgentTarget.Tester, Mode = 3 };
            CollectionAssert.Contains(VoiceFallbackLine.Options(f, 5), "put the office back on the desk");
        }
    }
}
