using NUnit.Framework;
using MuseDesk.Voice;

namespace MuseDesk.Tests.Voice
{
    public class VoiceIntentClassifierTests
    {
        static VoiceClassification C(string s, VoiceAgentTarget focus = VoiceAgentTarget.None)
        {
            return VoiceIntentClassifier.Classify(s, focus);
        }

        [TestCase("approve")]
        [TestCase("Approve it.")]
        [TestCase("yes, approve this please")]
        [TestCase("ship it")]
        [TestCase("looks good")]
        [TestCase("I want to approve")]
        public void Approve_requires_confirmation(string s)
        {
            var r = C(s);
            Assert.AreEqual(VoiceAction.Approve, r.Kind, s);
            Assert.IsTrue(r.RequiresConfirmation);
            Assert.Greater(r.Confidence, 0.5f);
        }

        [TestCase("reject")]
        [TestCase("Reject it please")]
        [TestCase("decline")]
        public void Reject_requires_confirmation(string s)
        {
            var r = C(s);
            Assert.AreEqual(VoiceAction.Reject, r.Kind, s);
            Assert.IsTrue(r.RequiresConfirmation);
        }

        [TestCase("don't approve")]
        [TestCase("do not approve")]
        [TestCase("do not reject")]
        [TestCase("never approve it")]
        [TestCase("don't reject this")]
        public void Negated_decisions_are_unknown(string s)
        {
            var r = C(s);
            Assert.AreEqual(VoiceAction.Unknown, r.Kind, s);
            Assert.IsTrue(r.Negated, s);
            Assert.IsFalse(r.RequiresConfirmation);
        }

        [TestCase("maybe approve?")]
        [TestCase("should I approve it")]
        [TestCase("perhaps reject")]
        [TestCase("approve?")]
        [TestCase("could we approve this")]
        public void Hedged_decisions_are_unknown(string s)
        {
            var r = C(s);
            Assert.AreEqual(VoiceAction.Unknown, r.Kind, s);
            Assert.IsTrue(r.Hedged, s);
        }

        [Test]
        public void Approve_or_reject_is_ambiguous()
        {
            Assert.AreEqual(VoiceAction.Unknown, C("approve or reject").Kind);
        }

        [Test]
        public void Decision_word_in_long_sentence_is_not_a_decision()
        {
            Assert.AreEqual(VoiceAction.Unknown, C("I think the layout of the kitchen approves of nothing").Kind);
            Assert.IsFalse(C("they rejected my idea about colours yesterday").RequiresConfirmation);
        }

        [TestCase("never mind")]
        [TestCase("nevermind")]
        [TestCase("cancel")]
        [TestCase("stop")]
        [TestCase("forget it")]
        public void Cancel(string s)
        {
            var r = C(s);
            Assert.AreEqual(VoiceAction.Cancel, r.Kind, s);
            Assert.IsFalse(r.RequiresConfirmation);
        }

        [Test]
        public void Negated_cancel_is_unknown()
        {
            Assert.AreEqual(VoiceAction.Unknown, C("don't stop").Kind);
        }

        [TestCase("pause", VoiceAction.Pause)]
        [TestCase("hold on", VoiceAction.Pause)]
        [TestCase("resume", VoiceAction.Resume)]
        [TestCase("keep going", VoiceAction.Resume)]
        [TestCase("start", VoiceAction.StartWork)]
        [TestCase("get to work", VoiceAction.StartWork)]
        [TestCase("let's go", VoiceAction.StartWork)]
        [TestCase("come here", VoiceAction.Summon)]
        [TestCase("come over please", VoiceAction.Summon)]
        public void Plain_commands(string s, VoiceAction expected)
        {
            Assert.AreEqual(expected, C(s).Kind, s);
        }

        [Test]
        public void Negated_commands_are_unknown()
        {
            Assert.AreEqual(VoiceAction.Unknown, C("don't pause").Kind);
            Assert.AreEqual(VoiceAction.Unknown, C("do not start yet").Kind);
        }

        [Test]
        public void Polite_request_is_still_a_command()
        {
            Assert.AreEqual(VoiceAction.StartWork, C("could you start").Kind);
        }

        [TestCase("developer, pause", VoiceAgentTarget.Developer)]
        [TestCase("tester come here", VoiceAgentTarget.Tester)]
        [TestCase("hey researcher start", VoiceAgentTarget.Researcher)]
        [TestCase("dev, resume", VoiceAgentTarget.Developer)]
        [TestCase("QA come over", VoiceAgentTarget.Tester)]
        public void Target_by_name_or_role_word(string s, VoiceAgentTarget expected)
        {
            Assert.AreEqual(expected, C(s).Target, s);
        }

        [Test]
        public void Ask_agent_carries_the_question()
        {
            var r = C("Developer, why did you pick this approach?");
            Assert.AreEqual(VoiceAction.AskAgent, r.Kind);
            Assert.AreEqual(VoiceAgentTarget.Developer, r.Target);
            Assert.AreEqual("why did you pick this approach?", r.Question);
        }

        [Test]
        public void Decision_word_inside_a_question_to_an_agent_is_a_question()
        {
            var r = C("Tester, why would you approve this?");
            Assert.AreEqual(VoiceAction.AskAgent, r.Kind);
            Assert.IsFalse(r.RequiresConfirmation);
        }

        [Test]
        public void Just_the_name_addresses_the_agent()
        {
            var r = C("Tester?");
            Assert.AreEqual(VoiceAction.AskAgent, r.Kind);
            Assert.AreEqual(VoiceAgentTarget.Tester, r.Target);
        }

        [Test]
        public void You_resolves_to_the_focused_agent()
        {
            var r = C("what are you working on?", VoiceAgentTarget.Researcher);
            Assert.AreEqual(VoiceAction.AskAgent, r.Kind);
            Assert.AreEqual(VoiceAgentTarget.Researcher, r.Target);
        }

        [Test]
        public void You_without_focus_has_no_target()
        {
            var r = C("what are you working on?");
            Assert.AreEqual(VoiceAgentTarget.None, r.Target);
            Assert.AreEqual(VoiceAction.Unknown, r.Kind);
        }

        [Test]
        public void You_that_is_not_a_question_is_not_an_address()
        {
            Assert.AreEqual(VoiceAction.Unknown, C("you look nice", VoiceAgentTarget.Tester).Kind);
        }

        [Test]
        public void Manager_request()
        {
            var r = C("manager I need a new plan");
            Assert.AreEqual(VoiceAction.ManagerRequest, r.Kind);
            Assert.AreEqual(VoiceAgentTarget.Manager, r.Target);
            Assert.AreEqual("I need a new plan", r.Question);
        }

        [Test]
        public void Manager_new_goal_in_the_owners_words_is_a_manager_request()
        {
            // rc20 (worn rc19): this sentence became a board task instead of a plan. The classifier must give the goal
            // to the Manager whole (the command centre now lets a named Manager win over the "new task" grammar).
            var r = C("manager build a program to output stars to the console");
            Assert.AreEqual(VoiceAction.ManagerRequest, r.Kind);
            Assert.AreEqual("build a program to output stars to the console", r.Question);
            r = C("Manager, make me a small expense tracker with tests");
            Assert.AreEqual(VoiceAction.ManagerRequest, r.Kind);
        }

        [Test]
        public void Approve_with_focus_still_confirms()
        {
            var r = C("you approve", VoiceAgentTarget.Developer);
            Assert.AreEqual(VoiceAction.Approve, r.Kind);
            Assert.IsTrue(r.RequiresConfirmation);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("purple monkey dishwasher")]
        [TestCase("hmm")]
        public void Noise_is_unknown_with_zero_confidence(string s)
        {
            var r = C(s);
            Assert.AreEqual(VoiceAction.Unknown, r.Kind);
            Assert.AreEqual(0f, r.Confidence);
        }

        [Test]
        public void Null_is_safe()
        {
            Assert.AreEqual(VoiceAction.Unknown, C(null).Kind);
        }

        [Test]
        public void Curly_apostrophe_negates()
        {
            Assert.IsTrue(C("don’t approve").Negated);
        }

        [Test]
        public void FromId_and_AgentId_round_trip()
        {
            Assert.AreEqual(VoiceAgentTarget.Tester, VoiceIntentClassifier.FromId("Tester"));
            Assert.AreEqual("developer", VoiceIntentClassifier.AgentId(VoiceAgentTarget.Developer));
            Assert.AreEqual(VoiceAgentTarget.None, VoiceIntentClassifier.FromId(null));
        }
    }
}
