// EditMode tests for what voice says (Assets/MuseDesk/Scripts/Voice/VoiceAnswers.cs), by reflection
// (this test assembly cannot reference Assembly-CSharp).

using System;
using System.Reflection;
using NUnit.Framework;

namespace MuseDesk.Tests.EditMode
{
    [TestFixture]
    public class VoiceAnswersTests
    {
        static object Call(string method, params object[] args)
        {
            Type t = VoiceReflect.Find("MuseDesk.Voice.VoiceAnswers");
            Assert.IsNotNull(t, "MuseDesk.Voice.VoiceAnswers not found in the loaded assemblies");
            MethodInfo m = t.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(m, "VoiceAnswers." + method + " not found");
            return m.Invoke(null, args);
        }

        static string AgentLine(string agentId, bool known, string activity, string progress, bool waiting, string summary,
                                int passed, int failed, bool running, bool started, string outcome, bool live)
        {
            return (string)Call("DescribeAgentLine", agentId, known, activity, progress, waiting, summary, passed, failed, running, started, outcome, live);
        }

        static string Phrases(int context, int mode, int flags, int max) { return (string)Call("DescribePhrases", context, mode, flags, max); }

        [Test]
        public void AgentLine_QuotesTheRunState_AndLabelsTheScriptedDemo()
        {
            Assert.AreEqual("Researching. Comparing session and token auth. (scripted demo)",
                AgentLine("researcher", true, "Researching", "Comparing session and token auth", false, null, -1, -1, true, true, null, false));
            Assert.AreEqual("Researching. Comparing session and token auth.",
                AgentLine("researcher", true, "Researching", "Comparing session and token auth", false, null, -1, -1, true, true, null, true));
        }

        [Test]
        public void AgentLine_WaitingOnTheUser_SaysSo()
        {
            Assert.AreEqual("I need your decision. Use JWT sessions.",
                AgentLine("researcher", true, "Waiting for you", "ignored", true, "Use JWT sessions", -1, -1, true, true, null, true));
        }

        [Test]
        public void AgentLine_Tester_AddsOnlyReportedTestCounts()
        {
            Assert.AreEqual("Testing. Running the suite. Tests: 12 passed, 0 failed.",
                AgentLine("tester", true, "Testing", "Running the suite", false, null, 12, 0, true, true, null, true));
            Assert.AreEqual("Testing. Running the suite.",
                AgentLine("tester", true, "Testing", "Running the suite", false, null, -1, -1, true, true, null, true));
            // The Developer never quotes test counts.
            Assert.AreEqual("Coding.",
                AgentLine("developer", true, "Coding", null, false, null, 12, 0, true, true, null, true));
        }

        [Test]
        public void AgentLine_NothingStarted_AndUnknownAgent()
        {
            Assert.AreEqual("Idle. No task has been started. (scripted demo)",
                AgentLine("developer", true, "Idle", null, false, null, -1, -1, false, false, null, false));
            Assert.AreEqual("No report from the Tester yet.",
                AgentLine("tester", false, null, null, false, null, -1, -1, false, false, null, true));
        }

        [Test]
        public void Phrases_FitTheMoment()
        {
            // Office moving: stop comes first.
            Assert.IsTrue(Phrases(8, 1, 2 | 4, 3).StartsWith("stop"));
            // A decision waits and the paper is still in the agent's hand: the paper first, then the decision words.
            Assert.AreEqual("show me the paper | approve | reject", Phrases(1, 0, 0, 3));
            Assert.AreEqual("approve | reject", Phrases(1, 0, 256, 2));
            // Nothing waiting: never offers approve or reject.
            string idle = Phrases(0, 0, 16 | 2 | 4, 12);
            Assert.IsFalse(idle.Contains("approve") || idle.Contains("reject"), idle);
            Assert.IsTrue(idle.StartsWith("start"), idle);
            // A new task is only offered on the live server.
            Assert.IsFalse(Phrases(0, 0, 0, 20).Contains("new task"));
            Assert.IsTrue(Phrases(64, 0, 1, 20).Contains("new task"));
            // While asked, confirm and cancel appear once each.
            Assert.AreEqual("confirm | cancel", Phrases(2, 0, 0, 2));
        }
    }
}
