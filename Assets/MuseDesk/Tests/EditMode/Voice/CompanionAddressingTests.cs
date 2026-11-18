using NUnit.Framework;
using MuseDesk.Voice;

namespace MuseDesk.Tests.Voice
{
    /// <summary>rc18: talk naturally to the agent that is with you (perched or out of the portal), no name needed.</summary>
    public class CompanionAddressingTests
    {
        static bool For(string heard, VoiceAgentTarget who, out string q, bool command = false) => CompanionAddressing.ForCompanion(heard, who, command, out q);

        [Test]
        public void NaturalQuestionGoesToTheCompanion()
        {
            string q;
            Assert.IsTrue(For("hey why are you doing this instead of that", VoiceAgentTarget.Developer, out q));
            Assert.AreEqual("Why are you doing this instead of that", q);
            Assert.IsTrue(For("what have you changed so far?", VoiceAgentTarget.Developer, out q));
            Assert.AreEqual("What have you changed so far?", q);
        }

        [Test]
        public void NobodyVisitingMeansNothingChanges()
        {
            string q;
            Assert.IsFalse(For("why are you doing this", VoiceAgentTarget.None, out q), "name-first still applies when no agent is with you");
        }

        [Test]
        public void TheCompanionsOwnNameIsDroppedAndAnotherNameIsNotForIt()
        {
            string q;
            Assert.IsTrue(For("Developer, can you change the password minimum to 12 characters?", VoiceAgentTarget.Developer, out q));
            Assert.AreEqual("Can you change the password minimum to 12 characters?", q);
            Assert.IsFalse(For("Tester, come here", VoiceAgentTarget.Developer, out q), "addressed to someone else");
        }

        [Test]
        public void CommandsAndFragmentsAreNotQuestions()
        {
            string q;
            Assert.IsFalse(For("approve", VoiceAgentTarget.Researcher, out q, command: true));
            Assert.IsFalse(For("um", VoiceAgentTarget.Researcher, out q));
            Assert.IsFalse(For("yeah", VoiceAgentTarget.Researcher, out q), "one non-question word is not a question");
            Assert.IsTrue(For("why?", VoiceAgentTarget.Researcher, out q));
            Assert.AreEqual("Why?", q);
        }
    }
}
