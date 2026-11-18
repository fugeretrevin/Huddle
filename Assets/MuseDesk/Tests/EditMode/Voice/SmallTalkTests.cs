// rc22 polish: agents answer small talk (owner: "basic conversation even without context; small talk and playful banter;
// flirting platonically reciprocated"). Pure SmallTalk only: what counts as small talk, what never does (work questions,
// commands), and that every kind has a line for every agent. The voice routing itself is PLAY / WORN only.
using NUnit.Framework;
using MuseDesk.Voice;

namespace MuseDesk.Tests.Voice
{
    public class SmallTalkTests
    {
        [TestCase("hi", SmallTalkKind.Greeting)]
        [TestCase("Hey there!", SmallTalkKind.Greeting)]
        [TestCase("good morning everyone", SmallTalkKind.Greeting)]
        [TestCase("How are you?", SmallTalkKind.HowAreYou)]
        [TestCase("hey, how are you doing today", SmallTalkKind.HowAreYou)]
        [TestCase("how's it going buddy", SmallTalkKind.HowAreYou)]
        [TestCase("what's up", SmallTalkKind.HowAreYou)]
        [TestCase("thanks so much, developer", SmallTalkKind.Thanks)]
        [TestCase("thank you", SmallTalkKind.Thanks)]
        [TestCase("good job", SmallTalkKind.Praise)]
        [TestCase("you're awesome", SmallTalkKind.Praise)]
        [TestCase("you're cute", SmallTalkKind.Flirt)]
        [TestCase("I love you", SmallTalkKind.Flirt)]
        [TestCase("will you marry me", SmallTalkKind.Flirt)]
        [TestCase("tell me a joke", SmallTalkKind.Joke)]
        [TestCase("who are you", SmallTalkKind.WhoAreYou)]
        [TestCase("are you real", SmallTalkKind.WhoAreYou)]
        [TestCase("what's your favorite color", SmallTalkKind.Feelings)]
        [TestCase("are you bored", SmallTalkKind.Feelings)]
        [TestCase("you're slow", SmallTalkKind.Tease)]
        [TestCase("sorry", SmallTalkKind.Sorry)]
        [TestCase("ok bye", SmallTalkKind.Bye)]
        [TestCase("good night", SmallTalkKind.Bye)]
        public void Classify_SmallTalk(string said, SmallTalkKind want)
        {
            Assert.AreEqual(want, SmallTalk.Classify(said), said);
        }

        [TestCase("what are you working on")]
        [TestCase("how is the task going")]
        [TestCase("what's the status")]
        [TestCase("pause")]
        [TestCase("stop")]
        [TestCase("approve")]
        [TestCase("cancel that")]
        [TestCase("build a login page")]
        [TestCase("come here")]
        [TestCase("why are you doing that instead of the login flow")]
        [TestCase("hi can you build me a login page with oauth and tests please")]
        [TestCase("")]
        [TestCase("manager")]
        [TestCase("ok")]
        public void Classify_WorkAndCommands_AreNeverSmallTalk(string said)
        {
            Assert.AreEqual(SmallTalkKind.None, SmallTalk.Classify(said), said);
        }

        [Test]
        public void EveryKind_HasALine_ForEveryAgent_AndTheLinesRotate()
        {
            var agents = new[] { VoiceAgentTarget.Researcher, VoiceAgentTarget.Developer, VoiceAgentTarget.Tester, VoiceAgentTarget.Manager, VoiceAgentTarget.None };
            foreach (SmallTalkKind k in System.Enum.GetValues(typeof(SmallTalkKind)))
            {
                if (k == SmallTalkKind.None) continue;
                foreach (var a in agents)
                {
                    string first = SmallTalk.Reply(k, a, 0);
                    Assert.IsFalse(string.IsNullOrEmpty(first), k + " / " + a);
                    Assert.Less(first.Length, 160, "short enough to say: " + first);
                    Assert.AreNotEqual(first, SmallTalk.Reply(k, a, 1), "a second turn says something else: " + k + " / " + a);
                }
            }
            Assert.IsNull(SmallTalk.Reply(SmallTalkKind.None, VoiceAgentTarget.Developer, 0));
        }

        [Test]
        public void Flirting_IsAnsweredWarmly_AndPlatonically()
        {
            foreach (var a in new[] { VoiceAgentTarget.Researcher, VoiceAgentTarget.Developer, VoiceAgentTarget.Tester, VoiceAgentTarget.Manager })
                for (int i = 0; i < 3; i++)
                {
                    string l = SmallTalk.Reply(SmallTalkKind.Flirt, a, i).ToLowerInvariant();
                    bool platonic = l.Contains("friend") || l.Contains("colleague") || l.Contains("professional") || l.Contains("boss")
                                    || l.Contains("partner") || l.Contains("plan with") || l.Contains("research for");
                    Assert.IsTrue(platonic, "kept platonic: " + l);
                }
        }
    }
}
