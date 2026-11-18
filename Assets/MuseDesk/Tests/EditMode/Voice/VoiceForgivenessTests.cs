using NUnit.Framework;
using MuseDesk.Voice;

namespace MuseDesk.Tests.Voice
{
    /// <summary>
    /// rc22 (owner, worn rc21: "the mic needs to be less picky so it actually reads better"). Forgiving names (WakeWord) and
    /// filler-tolerant sentences (VoiceFiller), while room chatter stays unaddressed (the hands-free privacy rule).
    /// Pure; the routing in VoiceCommandCenter is driven in Play Mode by AgentScripts/Rc22VoiceProbe.cs.
    /// </summary>
    public class VoiceForgivenessTests
    {
        static (bool ok, VoiceAgentTarget who, string rest) P(string s)
        {
            VoiceAgentTarget a; string r;
            bool ok = WakeWord.Parse(s, out a, out r);
            return (ok, a, r);
        }

        // ───────────── names the speech service misheard ─────────────

        [TestCase("man ager plan a login page", VoiceAgentTarget.Manager, "plan a login page", "Manager, plan a login page")]
        [TestCase("Man ager, plan a login page", VoiceAgentTarget.Manager, "plan a login page", "Manager, plan a login page")]
        [TestCase("man age her, plan a login page", VoiceAgentTarget.Manager, "plan a login page", "Manager, plan a login page")]
        [TestCase("Managers, plan a login page", VoiceAgentTarget.Manager, "plan a login page", "Manager, plan a login page")]
        [TestCase("managers plan a login page", VoiceAgentTarget.Manager, "plan a login page", "Manager, plan a login page")]
        [TestCase("manage her, build a stars program", VoiceAgentTarget.Manager, "build a stars program", "Manager, build a stars program")]
        [TestCase("Develop her, what are you working on?", VoiceAgentTarget.Developer, "what are you working on?", "Developer, what are you working on?")]
        [TestCase("The developer, why did you pick this?", VoiceAgentTarget.Developer, "why did you pick this?", "Developer, why did you pick this?")]
        [TestCase("the developer why did you pick this", VoiceAgentTarget.Developer, "why did you pick this", "Developer, why did you pick this")]
        [TestCase("research her why this source", VoiceAgentTarget.Researcher, "why this source", "Researcher, why this source")]
        [TestCase("re searcher, why this source", VoiceAgentTarget.Researcher, "why this source", "Researcher, why this source")]
        [TestCase("test her, have the tests passed", VoiceAgentTarget.Tester, "have the tests passed", "Tester, have the tests passed")]
        [TestCase("Testa, status", VoiceAgentTarget.Tester, "status", "Tester, status")]
        [TestCase("testa what's failing", VoiceAgentTarget.Tester, "what's failing", "Tester, what's failing")]
        [TestCase("Excuse me Manager, plan a login page", VoiceAgentTarget.Manager, "plan a login page", "Manager, plan a login page")]
        [TestCase("please manager, plan a login page", VoiceAgentTarget.Manager, "plan a login page", "Manager, plan a login page")]
        [TestCase("HEY MANAGE HER: plan a login page!", VoiceAgentTarget.Manager, "plan a login page!", "Manager, plan a login page!")]
        public void MisheardName_IsAddressed_AndReadAsTheName(string s, VoiceAgentTarget who, string rest, string canonical)
        {
            var r = P(s);
            Assert.IsTrue(r.ok, s);
            Assert.AreEqual(who, r.who, s);
            Assert.AreEqual(rest, r.rest, s);
            Assert.AreEqual(canonical, WakeWord.Canonical(s), s);
        }

        [TestCase("Researchers, what are you doing", VoiceAgentTarget.Researcher)]
        [TestCase("um okay manager, plan a login page", VoiceAgentTarget.Manager)]
        [TestCase("hey the tester, status", VoiceAgentTarget.Tester)]
        public void NameInTheFirstThreeWords_AfterLeadIns(string s, VoiceAgentTarget who)
        {
            var r = P(s);
            Assert.IsTrue(r.ok, s);
            Assert.AreEqual(who, r.who, s);
        }

        [TestCase("hey muse, pause everyone", "pause everyone")]
        [TestCase("Hey muse pause", "pause")]
        [TestCase("use desk, pause", "pause")]
        [TestCase("Muse Desk resume", "resume")]
        [TestCase("news desk, status", "status")]
        [TestCase("music desk status", "status")]
        public void MisheardMuseDesk_IsAnAddressToEveryone(string s, string rest)
        {
            var r = P(s);
            Assert.IsTrue(r.ok, s);
            Assert.AreEqual(VoiceAgentTarget.None, r.who, s);
            Assert.AreEqual(rest, r.rest, s);
            Assert.AreEqual(s, WakeWord.Canonical(s), "an address to everyone is left as heard");
        }

        // ───────────── room chatter stays room chatter (privacy: hands-free needs a name FIRST) ─────────────

        [TestCase("the manager said we need budget cuts")]
        [TestCase("my manager is annoying")]
        [TestCase("the testers are slow today")]
        [TestCase("developers love coffee")]
        [TestCase("research a new charting library")]           // "a" after a stem is an ordinary word here
        [TestCase("test a login page")]
        [TestCase("I told the developer about it")]
        [TestCase("so the developer said no")]
        [TestCase("hey hey hey manager")]                       // the name is the fourth word
        [TestCase("tell the researcher to approve")]
        public void Chatter_StaysUnaddressed(string s)
        {
            Assert.IsFalse(WakeWord.IsAddressed(s), s);
            Assert.AreEqual(s, WakeWord.Canonical(s), s);
        }

        [Test]
        public void MentionsAgent_KnowsTheMisheardSpelling()
        {
            Assert.IsTrue(WakeWord.MentionsAgent("why did testa fail"));
            Assert.IsFalse(WakeWord.MentionsAgent("what's for lunch"));
        }

        // ───────────── the classifier hears the misheard name too ─────────────

        [Test]
        public void Classifier_ManagerRequest_FromASplitName()
        {
            var c = VoiceIntentClassifier.Classify("man ager, plan a login page");
            Assert.AreEqual(VoiceAction.ManagerRequest, c.Kind);
            Assert.AreEqual(VoiceAgentTarget.Manager, c.Target);
            Assert.AreEqual("plan a login page", c.Question);

            c = VoiceIntentClassifier.Classify(WakeWord.Canonical("managers, build a program that prints stars"));
            Assert.AreEqual(VoiceAction.ManagerRequest, c.Kind);
            Assert.AreEqual("build a program that prints stars", c.Question);
        }

        [Test]
        public void Classifier_AskAgent_FromAMisheardName()
        {
            var c = VoiceIntentClassifier.Classify("testa, why did the login test fail?");
            Assert.AreEqual(VoiceAction.AskAgent, c.Kind);
            Assert.AreEqual(VoiceAgentTarget.Tester, c.Target);
            Assert.AreEqual("why did the login test fail?", c.Question);
        }

        [Test]
        public void Classifier_MisheardName_NeverMakesADecisionWithoutConfirmation()
        {
            var c = VoiceIntentClassifier.Classify("testa approve");
            if (c.Kind == VoiceAction.Approve) Assert.IsTrue(c.RequiresConfirmation);
            Assert.AreNotEqual(VoiceAction.Reject, c.Kind);
        }

        // ───────────── filler and politeness around a command ─────────────

        [TestCase("um, okay so can you open the, uh, board for me please", "open the board")]
        [TestCase("Could you please switch to floor mode?", "switch to floor mode?")]
        [TestCase("hey team, start the task please", "start the task")]
        [TestCase("Okay so um, what's the status, please?", "what's the status?")]
        [TestCase("uh, would you mind closing the board", "closing the board")]
        [TestCase("I was wondering if you could show me the result", "show me the result")]
        [TestCase("Add a dark mode toggle, uh, thanks", "Add a dark mode toggle")]
        [TestCase("so yeah, bring it here real quick", "bring it here")]
        [TestCase("hmm, next page", "next page")]
        public void Clean_RemovesFillerAndPoliteness(string said, string clean)
        {
            Assert.AreEqual(clean, VoiceFiller.Clean(said));
        }

        [TestCase("open the board")]
        [TestCase("don't stop")]
        [TestCase("is it done yet?")]
        [TestCase("Manager, plan a login page")]
        public void Clean_LeavesPlainSentencesAlone(string said)
        {
            Assert.AreEqual(said, VoiceFiller.Clean(said));
        }

        [TestCase("um")]
        [TestCase("please")]
        [TestCase("okay")]
        [TestCase("can you")]
        public void Clean_NeverEmptiesASentence(string said)
        {
            string c = VoiceFiller.Clean(said);
            Assert.IsFalse(string.IsNullOrWhiteSpace(c), "\"" + said + "\" -> \"" + c + "\"");
        }

        [Test]
        public void Clean_KeepsNegations()
        {
            Assert.AreEqual("don't open the board", VoiceFiller.Clean("um, please don't open the board"));
            Assert.AreEqual("not now", VoiceFiller.Clean("not now"));
        }

        [Test]
        public void Clean_NullAndEmptyAreSafe()
        {
            Assert.AreEqual("", VoiceFiller.Clean(null));
            Assert.AreEqual("", VoiceFiller.Clean(""));
            Assert.AreEqual(0, VoiceFiller.ContentWords(null));
            Assert.IsFalse(VoiceFiller.IsOnlyFiller(null));
        }

        // ───────────── does a sentence carry anything (the Manager fallback, the stitcher's wait) ─────────────

        [TestCase("thank you", 0)]
        [TestCase("can you hear me", 0)]
        [TestCase("um, uh", 0)]
        [TestCase("hello there", 0)]
        [TestCase("plan a login page", 3)]
        [TestCase("why is the build slow", 3)]
        public void ContentWords(string said, int n)
        {
            Assert.AreEqual(n, VoiceFiller.ContentWords(said), said);
        }

        [TestCase("okay so", true)]
        [TestCase("Hey", true)]
        [TestCase("um, uh", true)]
        [TestCase("and", true)]
        [TestCase("open the board", false)]
        [TestCase("stop", false)]
        [TestCase("yes", false)]                 // an answer, not someone starting to talk
        [TestCase("", false)]
        public void IsOnlyFiller(string said, bool filler)
        {
            Assert.AreEqual(filler, VoiceFiller.IsOnlyFiller(said), said);
        }
    }
}
