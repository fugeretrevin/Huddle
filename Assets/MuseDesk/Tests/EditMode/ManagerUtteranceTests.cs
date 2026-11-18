using NUnit.Framework;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests
{
    /// <summary>rc22 (owner: "every question means the manager starts building a plan, allow it to just answer normal questions").</summary>
    public class ManagerUtteranceTests
    {
        [TestCase("what is the developer working on")]
        [TestCase("What's the status?")]
        [TestCase("how long will this take")]
        [TestCase("is anything blocked")]
        [TestCase("why did the tester fail")]
        [TestCase("can you tell me what the plan is")]
        [TestCase("could you explain the second step")]
        [TestCase("tell me what everyone is doing")]
        [TestCase("explain the plan")]
        [TestCase("um, who is working on the login page")]
        [TestCase("any updates")]
        [TestCase("the tests passed?")]
        public void Questions_are_answered(string text)
        {
            Assert.IsTrue(ManagerUtterance.IsQuestion(text), text);
        }

        [TestCase("build a program that prints stars to the console")]
        [TestCase("Build the login flow, use the existing repo, and have Tester verify it")]
        [TestCase("can you build me an expense tracker")]
        [TestCase("could you please make a small game")]
        [TestCase("please write setup docs for the project")]
        [TestCase("find and fix the failing tests")]
        [TestCase("I need a script that renames my photos")]
        [TestCase("let's add dark mode")]
        [TestCase("a program that prints a triangle of stars")]
        [TestCase("")]
        public void Goals_are_planned(string text)
        {
            Assert.IsFalse(ManagerUtterance.IsQuestion(text), text);
        }
    }
}
