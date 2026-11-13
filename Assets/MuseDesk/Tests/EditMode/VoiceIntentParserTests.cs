// EditMode tests for the voice grammar (Assets/MuseDesk/Scripts/Voice/VoiceIntentParser.cs) and the
// spoken answers (VoiceAnswers.cs). Both are plain C# in Assembly-CSharp; this test assembly cannot
// reference Assembly-CSharp, so they are reached by reflection, as SpatialReachTests does.
//
// The important half of this file is the NEGATIVE half: sentences that contain "approve", "reject",
// "yes", "no" or "stop" and must not do anything.

using System;
using System.Reflection;
using NUnit.Framework;

namespace MuseDesk.Tests.EditMode
{
    static class VoiceReflect
    {
        public static Type Find(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        static MethodInfo _describe;

        public static string Describe(string transcript, int context)
        {
            if (_describe == null)
            {
                Type parser = Find("MuseDesk.Voice.VoiceIntentParser");
                Assert.IsNotNull(parser, "MuseDesk.Voice.VoiceIntentParser not found in the loaded assemblies");
                _describe = parser.GetMethod("Describe", BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(_describe, "VoiceIntentParser.Describe(string, int) not found");
            }
            return (string)_describe.Invoke(null, new object[] { transcript, context });
        }
    }

    [TestFixture]
    public class VoiceIntentParserTests
    {
        // VoiceContext values.
        const int None = 0, Decision = 1, Asking = 2, Previewing = 4, Moving = 8, Viewer = 16, Board = 32, Live = 64, Undo = 128;

        static void Expect(string expected, string transcript, int context)
        {
            Assert.AreEqual(expected, VoiceReflect.Describe(transcript, context), "\"" + transcript + "\" (context " + context + ")");
        }

        // ───────────── decisions: said plainly, with a decision waiting ─────────────

        [TestCase("approve")]
        [TestCase("Approve.")]
        [TestCase("Approve it")]
        [TestCase("I approve")]
        [TestCase("approved")]
        [TestCase("ok, approved")]
        [TestCase("yes, approve it")]
        [TestCase("okay approve the plan")]
        [TestCase("approve this proposal please")]
        [TestCase("go ahead and approve it")]
        [TestCase("looks good, approve")]
        [TestCase("I accept")]
        [TestCase("accept it")]
        [TestCase("accept the changes")]
        [TestCase("approve the change")]
        [TestCase("ship it")]
        [TestCase("Researcher, approve it")]
        [TestCase("let's approve this one")]
        public void Approve_WhenDecisionPending(string said) { Expect("Approve", said, Decision); }

        [TestCase("reject")]
        [TestCase("Reject it.")]
        [TestCase("I reject this proposal")]
        [TestCase("no, reject it")]
        [TestCase("rejected")]
        [TestCase("decline")]
        [TestCase("deny it")]
        [TestCase("reject the plan please")]
        public void Reject_WhenDecisionPending(string said) { Expect("Reject", said, Decision); }

        [TestCase("request changes")]
        [TestCase("changes")]
        [TestCase("changes please")]
        [TestCase("it needs changes")]
        [TestCase("make some changes")]
        [TestCase("revise it")]
        [TestCase("I'd like to request a change")]
        public void RequestChanges_WhenDecisionPending(string said) { Expect("RequestChanges", said, Decision); }

        // ───────────── decisions: the same words with NO decision waiting never approve ─────────────

        [TestCase("approve", "approve")]
        [TestCase("I approve", "approve")]
        [TestCase("ship it", "approve")]
        [TestCase("reject it", "reject")]
        [TestCase("request changes", "request changes")]
        public void DecisionWord_WithoutDecision_IsNotADecision(string said, string word)
        {
            Expect("DecisionNotPending:" + word, said, None);
            Expect("DecisionNotPending:" + word, said, Live | Board);
        }

        // ───────────── casual speech and near-misses: never a decision, in ANY context ─────────────

        [TestCase("I don't approve of this layout")]
        [TestCase("I do not approve")]
        [TestCase("don't approve it")]
        [TestCase("do not approve that")]
        [TestCase("never approve this")]
        [TestCase("I can't approve this yet")]
        [TestCase("should I approve it")]
        [TestCase("should I approve this?")]
        [TestCase("approve it?")]
        [TestCase("Approve?")]
        [TestCase("do you think I should approve")]
        [TestCase("can you approve it")]
        [TestCase("I would approve if the tests passed")]
        [TestCase("I might approve it later")]
        [TestCase("maybe approve")]
        [TestCase("not sure whether to approve or reject")]
        [TestCase("approve or reject")]
        [TestCase("approve and reject")]
        [TestCase("what happens if I approve")]
        [TestCase("what happens if I reject it")]
        [TestCase("I don't want to reject it")]
        [TestCase("don't reject it")]
        [TestCase("they rejected my idea yesterday")]
        [TestCase("the developer approved the plan")]
        [TestCase("my boss approved the budget")]
        [TestCase("was it approved")]
        [TestCase("has this been approved already")]
        [TestCase("approval")]
        [TestCase("I need approval for this")]
        [TestCase("disapprove")]
        [TestCase("I approve of the new colour scheme")]
        [TestCase("approve with changes")]
        [TestCase("I think we could accept that risk")]
        [TestCase("that is not acceptable")]
        [TestCase("no approve it")]
        [TestCase("wait, don't approve")]
        [TestCase("yes")]
        [TestCase("yeah")]
        [TestCase("sure")]
        [TestCase("ok")]
        [TestCase("no")]
        [TestCase("no thanks")]
        [TestCase("looks good")]
        [TestCase("sounds good to me")]
        [TestCase("go ahead")]
        [TestCase("do it")]
        [TestCase("I want to make changes to the layout")]
        [TestCase("what changes did you make")]
        [TestCase("nothing changes")]
        [TestCase("the proposal")]
        [TestCase("")]
        [TestCase("   ")]
        public void CasualSpeech_IsNeverADecision(string said)
        {
            foreach (int ctx in new[] { None, Decision, Decision | Live, Decision | Board | Viewer, Moving | Decision, Previewing | Decision })
            {
                string got = VoiceReflect.Describe(said, ctx);
                Assert.IsFalse(got == "Approve" || got == "Reject" || got == "RequestChanges",
                    "\"" + said + "\" (context " + ctx + ") must not be a decision, got " + got);
            }
        }

        // The same sentences with nothing waiting must not even look like a decision word.
        [TestCase("I don't approve of this layout")]
        [TestCase("should I approve it")]
        [TestCase("they rejected my idea yesterday")]
        [TestCase("the developer approved the plan")]
        [TestCase("yes")]
        [TestCase("no")]
        [TestCase("looks good")]
        public void CasualSpeech_WithNothingWaiting_IsNothing(string said)
        {
            string got = VoiceReflect.Describe(said, None);
            Assert.IsFalse(got.StartsWith("DecisionNotPending") || got == "Confirm" || got == "Cancel", "\"" + said + "\" got " + got);
        }

        // ───────────── yes / no: only while the voice layer is asking ─────────────

        [TestCase("yes")]
        [TestCase("Yes please")]
        [TestCase("yeah")]
        [TestCase("yep")]
        [TestCase("ok")]
        [TestCase("okay")]
        [TestCase("sure")]
        [TestCase("confirm")]
        [TestCase("correct")]
        [TestCase("do it")]
        [TestCase("go ahead")]
        [TestCase("yes, send it")]
        [TestCase("that's right")]
        [TestCase("sounds good")]
        public void Confirm_WhileAsking(string said) { Expect("Confirm", said, Asking | Live); }

        [TestCase("no")]
        [TestCase("No.")]
        [TestCase("nope")]
        [TestCase("cancel")]
        [TestCase("cancel that")]
        [TestCase("never mind")]
        [TestCase("no wait")]
        [TestCase("wait")]
        [TestCase("hold on")]
        [TestCase("undo")]
        [TestCase("don't")]
        [TestCase("not yet")]
        [TestCase("forget it")]
        [TestCase("yes... no, cancel")]
        public void Cancel_WhileAsking(string said)
        {
            Expect("Cancel", said, Asking | Live);
            Expect("Cancel", said, Undo | Decision);
        }

        [TestCase("yes", "None")]
        [TestCase("ok", "None")]
        [TestCase("no", "None")]
        [TestCase("sure", "None")]
        [TestCase("wait", "None")]
        public void YesAndNo_MeanNothing_WhenNotAsked(string said, string expected) { Expect(expected, said, None); Expect(expected, said, Decision); }

        [Test]
        public void UndoWindow_StillParsesTheOtherDecisionWord()
        {
            Expect("Reject", "no, reject it", Undo | Decision);
            Expect("Approve", "approve", Undo | Decision);
            Expect("Confirm", "yes", Undo | Decision);
        }

        // ───────────── stop ─────────────

        [TestCase("stop")]
        [TestCase("Stop!")]
        [TestCase("stop it")]
        [TestCase("stop moving")]
        [TestCase("stop the office")]
        [TestCase("please stop")]
        [TestCase("stop stop stop")]
        [TestCase("halt")]
        [TestCase("freeze")]
        [TestCase("whoa")]
        [TestCase("no stop")]
        public void Stop_Always(string said)
        {
            Expect("Stop", said, None);
            Expect("Stop", said, Moving);
            Expect("Stop", said, Moving | Decision | Asking | Undo);
        }

        [TestCase("wait")]
        [TestCase("hold on")]
        [TestCase("hold it")]
        [TestCase("oh no stop that right now it is going to hit the lamp")]
        [TestCase("stop it is too close to me")]
        public void Stop_WhileMoving_IsLiberal(string said) { Expect("Stop", said, Moving); }

        [TestCase("don't stop")]
        [TestCase("do not stop")]
        [TestCase("never stop improving")]
        [TestCase("it runs non-stop")]
        [TestCase("stop the demo")]
        [TestCase("I took the bus stop route today")]
        public void Stop_NearMisses(string said)
        {
            Assert.AreNotEqual("Stop", VoiceReflect.Describe(said, None), said);
        }

        [Test]
        public void Stop_Negated_IsNotStop_EvenWhileMoving()
        {
            Assert.AreNotEqual("Stop", VoiceReflect.Describe("don't stop", Moving));
            Assert.AreNotEqual("Stop", VoiceReflect.Describe("do not stop", Moving));
        }

        [Test]
        public void StopListening_IsCancel_AndStopFocus_IsFocusOff()
        {
            Expect("Cancel", "stop listening", None);
            Expect("FocusOff", "stop focus", None);
            Expect("FocusOff", "stop focus mode", Moving);
        }

        // ───────────── run ─────────────

        [TestCase("start")]
        [TestCase("Start.")]
        [TestCase("begin")]
        [TestCase("let's go")]
        [TestCase("start the task")]
        [TestCase("please start the demo")]
        [TestCase("can you start the run")]
        [TestCase("get started")]
        [TestCase("kick it off")]
        [TestCase("run it")]
        [TestCase("start working")]
        [TestCase("ok start")]
        [TestCase("Researcher, start")]
        public void Start(string said) { Expect("Start", said, None); }

        [TestCase("run again")]
        [TestCase("run it again")]
        [TestCase("again")]
        [TestCase("replay")]
        [TestCase("restart")]
        [TestCase("start over")]
        [TestCase("one more time")]
        [TestCase("do it again")]
        [TestCase("run the demo again")]
        public void RunAgain(string said) { Expect("RunAgain", said, None); }

        // ───────────── paper ─────────────

        [TestCase("show details")]
        [TestCase("details")]
        [TestCase("show me the details")]
        [TestCase("more details")]
        [TestCase("tell me more")]
        [TestCase("more info")]
        public void ShowDetails(string said) { Expect("ShowDetails", said, Decision); }

        [TestCase("hide details")]
        [TestCase("close the details")]
        public void HideDetails(string said) { Expect("HideDetails", said, Decision); }

        [TestCase("why")]
        [TestCase("Why?")]
        [TestCase("why do you recommend this")]
        [TestCase("explain")]
        [TestCase("explain why")]
        [TestCase("what's the reason")]
        [TestCase("tell me why")]
        [TestCase("what if we reject it")]
        [TestCase("what happens if I approve")]
        [TestCase("what are the risks")]
        public void AskWhy(string said) { Expect("AskWhy", said, Decision); }

        [Test]
        public void AskWhy_KeepsTheAgentNamed()
        {
            Expect("AskWhy:developer", "Developer, why this approach?", Decision);
            Expect("AskWhy:tester", "why did the tester flag this", Decision);
        }

        [TestCase("show me the paper")]
        [TestCase("take the paper")]
        [TestCase("open the proposal")]
        [TestCase("let me see it")]
        [TestCase("read it")]
        [TestCase("hand it over")]
        public void TakePaper(string said) { Expect("TakePaper", said, Decision); }

        // ───────────── board, result, viewer ─────────────

        [TestCase("open the board")]
        [TestCase("open board")]
        [TestCase("show me the task board")]
        [TestCase("show tasks")]
        [TestCase("board")]
        [TestCase("tasks")]
        [TestCase("what are my tasks")]
        public void OpenBoard(string said) { Expect("OpenBoard", said, None); }

        [TestCase("close the board")]
        [TestCase("hide the board")]
        [TestCase("put the board away")]
        public void CloseBoard(string said) { Expect("CloseBoard", said, Board); }

        [TestCase("view result")]
        [TestCase("view the result")]
        [TestCase("show me the result")]
        [TestCase("show results")]
        [TestCase("open the report")]
        [TestCase("what did you build")]
        [TestCase("results")]
        public void ViewResult(string said) { Expect("ViewResult", said, None); }

        [TestCase("close")]
        [TestCase("close it")]
        [TestCase("close the result")]
        [TestCase("put away")]
        public void Close(string said) { Expect("Close", said, Viewer); }

        [Test]
        public void Paging()
        {
            Expect("Next", "next", Viewer);
            Expect("Next", "next page", Viewer);
            Expect("Previous", "previous", Viewer);
            Expect("Previous", "go back", Viewer);
            Expect("Previous", "back", Viewer);
            Expect("GoToPage:2", "page two", Viewer);
            Expect("GoToPage:3", "go to page 3", Viewer);
            Expect("None", "back", None);
            Expect("SendBack", "go back", None);
        }

        // ───────────── focus ─────────────

        [TestCase("focus on")]
        [TestCase("focus")]
        [TestCase("focus mode")]
        [TestCase("turn on focus")]
        [TestCase("do not disturb")]
        [TestCase("don't disturb me")]
        [TestCase("I need to focus")]
        public void FocusOn(string said) { Expect("FocusOn", said, None); }

        [TestCase("focus off")]
        [TestCase("turn off focus")]
        [TestCase("turn focus mode off")]
        [TestCase("leave focus")]
        [TestCase("end focus mode")]
        [TestCase("I'm back")]
        public void FocusOff(string said) { Expect("FocusOff", said, None); }

        [Test]
        public void Focus_NearMisses()
        {
            Assert.AreNotEqual("FocusOn", VoiceReflect.Describe("focus on the tester's results first", None));
            Assert.AreNotEqual("FocusOn", VoiceReflect.Describe("I can't focus with all this noise", None));
        }

        // ───────────── modes and placement ─────────────

        [TestCase("desk", "ModeDesk")]
        [TestCase("desk mode", "ModeDesk")]
        [TestCase("back to the desk", "ModeDesk")]
        [TestCase("put it on my desk", "ModeDesk")]
        [TestCase("bring it back to the desk", "ModeDesk")]
        [TestCase("floor", "ModeFloor")]
        [TestCase("floor mode", "ModeFloor")]
        [TestCase("put it on the floor", "ModeFloor")]
        [TestCase("move the office to the floor", "ModeFloor")]
        [TestCase("switch to floor mode please", "ModeFloor")]
        [TestCase("ceiling", "ModeCeiling")]
        [TestCase("ceiling mode", "ModeCeiling")]
        [TestCase("send it up to the ceiling", "ModeCeiling")]
        [TestCase("ceiling pod", "ModeCeiling")]
        [TestCase("go to ceiling mode", "ModeCeiling")]
        public void Modes(string said, string expected) { Expect(expected, said, None); }

        [TestCase("the floor is dirty")]
        [TestCase("I dropped my pen on the floor again")]
        [TestCase("look at the ceiling fan")]
        [TestCase("my desk is a mess")]
        public void Modes_NearMisses(string said)
        {
            string got = VoiceReflect.Describe(said, None);
            Assert.IsFalse(got.StartsWith("Mode"), "\"" + said + "\" got " + got);
        }

        [Test]
        public void Placement()
        {
            Expect("NextSpot", "next spot", Previewing);
            Expect("NextSpot", "another spot", Previewing);
            Expect("NextSpot", "somewhere else", Previewing);
            Expect("NextSpot", "try another one", Previewing);
            Expect("Next", "next", Previewing);
            Expect("SpotNumber:2", "spot two", Previewing);
            Expect("SpotNumber:3", "option 3", Previewing);
            Expect("Confirm", "confirm", Previewing);
            Expect("Confirm", "confirm placement", Previewing);
            Expect("Confirm", "put it there", Previewing);
            Expect("Confirm", "that spot", Previewing);
            Expect("Cancel", "cancel", Previewing);
            Expect("Cancel", "never mind", Previewing);
            // "confirm" is never an approval.
            Expect("Confirm", "confirm", Decision);
        }

        [TestCase("lower the pod", "LowerPod")]
        [TestCase("pod down", "LowerPod")]
        [TestCase("bring the pod down", "LowerPod")]
        [TestCase("bring down the pod please", "LowerPod")]
        [TestCase("raise the pod", "RaisePod")]
        [TestCase("pod up", "RaisePod")]
        [TestCase("send the pod back up", "RaisePod")]
        [TestCase("retract the pod", "RaisePod")]
        public void Pod(string said, string expected) { Expect(expected, said, None); }

        [Test]
        public void Pod_NearMisses()
        {
            Expect("None", "what is the pod", None);
            Expect("None", "don't lower the pod", None);
            Expect("None", "is the pod down", None);
        }

        [TestCase("bring it here", "BringHere")]
        [TestCase("bring the office here", "BringHere")]
        [TestCase("bring down the office", "BringHere")]
        [TestCase("bring the office down", "BringHere")]
        [TestCase("come here", "BringHere")]
        [TestCase("come closer", "BringHere")]
        [TestCase("lower the office", "BringHere")]
        [TestCase("send it back", "SendBack")]
        [TestCase("send the office back", "SendBack")]
        [TestCase("put it back", "SendBack")]
        [TestCase("send it away", "SendBack")]
        [TestCase("raise the office", "SendBack")]
        public void BringAndSend(string said, string expected) { Expect(expected, said, None); }

        // ───────────── status and talking to the agents ─────────────

        [TestCase("status")]
        [TestCase("what's the status")]
        [TestCase("what is the status")]
        [TestCase("how is it going")]
        [TestCase("how's it going?")]
        [TestCase("what's happening")]
        [TestCase("any updates")]
        [TestCase("update me")]
        [TestCase("where are we")]
        [TestCase("what are you working on")]
        [TestCase("is it done yet")]
        [TestCase("give me a status report")]
        public void Status(string said) { Expect("Status", said, None); }

        [TestCase("who needs me")]
        [TestCase("Who needs me?")]
        [TestCase("does anyone need me")]
        [TestCase("anything for me")]
        [TestCase("what needs my attention")]
        [TestCase("is anything waiting for me")]
        public void WhoNeedsMe(string said) { Expect("WhoNeedsMe", said, None); }

        [TestCase("Researcher, what are you working on?", "researcher")]
        [TestCase("Developer, what are you working on?", "developer")]
        [TestCase("Tester, what are you working on?", "tester")]
        [TestCase("developer what's the status", "developer")]
        [TestCase("hey tester how is it going", "tester")]
        [TestCase("Developer?", "developer")]
        [TestCase("hey researcher", "researcher")]
        [TestCase("status, developer", "developer")]
        [TestCase("what is the developer doing", "developer")]
        [TestCase("is the tester done", "tester")]
        [TestCase("how's the researcher getting on, any progress", "researcher")]
        [TestCase("QA, are you there", "tester")]
        [TestCase("dev, do you need me", "developer")]
        public void AskAgent(string said, string role)
        {
            Expect("AskAgent:" + role, said, None);
            Expect("AskAgent:" + role, said, Live | Decision);
        }

        // ───────────── layout, help ─────────────

        [TestCase("reset layout")]
        [TestCase("reset the layout")]
        [TestCase("reset panels")]
        [TestCase("put everything back")]
        public void ResetLayout(string said) { Expect("ResetLayout", said, None); }

        [TestCase("help")]
        [TestCase("what can I say")]
        [TestCase("What can I say?")]
        [TestCase("what can I do")]
        [TestCase("commands")]
        [TestCase("show voice commands")]
        public void Help(string said) { Expect("Help", said, None); }

        // ───────────── new tasks ─────────────

        [TestCase("new task: add a dark mode toggle", "Add a dark mode toggle")]
        [TestCase("New task add a dark mode toggle.", "Add a dark mode toggle")]
        [TestCase("add a task to fix the login page", "Fix the login page")]
        [TestCase("create a new task called Export to CSV", "Export to CSV")]
        [TestCase("add a dark mode toggle", "Add a dark mode toggle")]
        [TestCase("please add a dark mode toggle", "Add a dark mode toggle")]
        [TestCase("could you fix the flaky login test", "Fix the flaky login test")]
        [TestCase("Developer, add a dark mode toggle", "Add a dark mode toggle")]
        [TestCase("add a login page for the tester", "Add a login page for the tester")]
        [TestCase("refactor the session store, don't break the API", "Refactor the session store don't break the API")]
        [TestCase("new task", "")]
        public void CreateTask(string said, string title)
        {
            Expect("CreateTask:" + title, said, Live);
            Expect("CreateTask:" + title, said, None);       // the centre, not the grammar, says "needs the live server"
        }

        // ───────────── things that are nothing ─────────────

        [TestCase("um")]
        [TestCase("please")]
        [TestCase("hello there")]
        [TestCase("this is pretty cool")]
        [TestCase("I think the weather is nice today")]
        [TestCase("can you hear me")]
        [TestCase("testing one two three")]
        [TestCase("the board meeting starts at nine")]
        [TestCase("we should start thinking about lunch")]
        [TestCase("I closed the window earlier")]
        public void Chatter_IsNothing(string said) { Expect("None", said, None); }
    }
}
