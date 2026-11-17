using NUnit.Framework;
using MuseDesk.Voice;

namespace MuseDesk.Tests.Voice
{
    /// <summary>rc21: a long sentence with a natural pause arrives as ONE sentence; short commands are not slowed.</summary>
    public class SentenceStitcherTests
    {
        static SentenceStitcher New() { var s = new SentenceStitcher(1.5f, 4f, 30f, 5, 2); s.Begin(0f); return s; }

        [Test]
        public void ShortCommand_EmitsAtOnce()
        {
            foreach (string w in new[] { "stop", "approve", "Cancel.", "next spot", "Researcher, why?" })
            {
                var s = New();
                Assert.AreEqual(StitchStep.Emit, s.OnFinal(w, 1f, PieceVerdict.Unknown), w);
                Assert.AreEqual(w, s.TakeSentence());
                Assert.IsFalse(s.Holding);
            }
        }

        [Test]
        public void CompleteVerdict_EmitsAtOnce_EvenWhenLonger()
        {
            var s = New();
            Assert.AreEqual(StitchStep.Emit, s.OnFinal("show me the paper", 1f, PieceVerdict.Complete));
            Assert.AreEqual("show me the paper", s.TakeSentence());
        }

        [Test]
        public void PauseInAGoal_IsStitchedIntoOneSentence()
        {
            var s = New();
            // The service ends the request at the pause.
            Assert.AreEqual(StitchStep.Hold, s.OnFinal("Manager, build a program", 2f, PieceVerdict.Unknown));
            Assert.IsTrue(s.Holding);
            s.OnMicReopened(2.3f);
            Assert.AreEqual(StitchStep.Ignore, s.Tick(3f));
            // The user goes on: new words cancel the wait, however long the next piece takes.
            s.OnPartial("that prints", 3.4f);
            Assert.IsTrue(s.Resumed);
            Assert.AreEqual("Manager, build a program that prints", s.Display("that prints"));
            Assert.AreEqual(StitchStep.Ignore, s.Tick(9f));
            // The next final is appended; it is long, so the stitcher waits once more for the grace.
            Assert.AreEqual(StitchStep.Hold, s.OnFinal("That prints stars.", 9.5f, PieceVerdict.Unknown));
            Assert.AreEqual(StitchStep.Ignore, s.Tick(10.9f));
            Assert.AreEqual(StitchStep.Emit, s.Tick(11.1f));
            Assert.AreEqual("Manager, build a program that prints stars.", s.TakeSentence());
            Assert.IsFalse(s.Active);
        }

        [Test]
        public void HeldPiece_EmitsAfterGrace_WhenNothingMoreIsSaid()
        {
            var s = New();
            Assert.AreEqual(StitchStep.Hold, s.OnFinal("Developer, what are you doing", 1f, PieceVerdict.Unknown));
            Assert.AreEqual(StitchStep.Ignore, s.Tick(2.4f));
            Assert.AreEqual(StitchStep.Emit, s.Tick(2.5f));
            Assert.AreEqual("Developer, what are you doing", s.TakeSentence());
        }

        [Test]
        public void GraceCountsFromTheReopenedMic_ButNeverPastTheHoldCap()
        {
            var s = New();
            s.OnFinal("plan a login page for me", 1f, PieceVerdict.Unknown);
            s.OnMicReopened(1.8f);                       // the microphone took 0.8 s to open again
            Assert.AreEqual(StitchStep.Ignore, s.Tick(3.2f));
            Assert.AreEqual(StitchStep.Emit, s.Tick(3.35f));

            s = New();
            s.OnFinal("plan a login page for me", 1f, PieceVerdict.Unknown);
            for (float t = 1.2f; t < 10f; t += 0.5f) s.OnSound(t);   // a noisy room
            Assert.AreEqual(StitchStep.Ignore, s.Tick(4.9f));
            Assert.AreEqual(StitchStep.Emit, s.Tick(5.0f));     // hold cap 4 s after the piece
        }

        [Test]
        public void RepeatedTranscript_IsNotAppendedTwice()
        {
            var s = New();
            Assert.AreEqual(StitchStep.Hold, s.OnFinal("Manager, build a program", 1f, PieceVerdict.Unknown));
            // The response repeats the transcript before any new words.
            Assert.AreEqual(StitchStep.Ignore, s.OnFinal("manager build a program", 1.1f, PieceVerdict.Unknown));
            Assert.AreEqual(1, s.Pieces);
            Assert.AreEqual("Manager, build a program", s.Text);
        }

        [Test]
        public void DanglingEnd_Holds_EvenWhenTheAppSaysComplete()
        {
            var s = New();
            Assert.AreEqual(StitchStep.Hold, s.OnFinal("open the board and", 1f, PieceVerdict.Complete));
            Assert.IsTrue(SentenceStitcher.EndsDangling("build a program that"));
            Assert.IsFalse(SentenceStitcher.EndsDangling("build a program"));
        }

        [Test]
        public void NameAlone_IsIncomplete_AndHolds()
        {
            var s = New();
            Assert.AreEqual(StitchStep.Hold, s.OnFinal("Manager", 1f, PieceVerdict.Incomplete));
            s.OnPartial("build", 2f);
            Assert.AreEqual(StitchStep.Hold, s.OnFinal("build a calculator app", 3f, PieceVerdict.Unknown));
            Assert.AreEqual(StitchStep.Emit, s.Tick(4.6f));
            Assert.AreEqual("Manager build a calculator app", s.TakeSentence());
        }

        [Test]
        public void Closing_EmitsWhateverIsIn()
        {
            var s = New();
            Assert.AreEqual(StitchStep.Emit, s.OnFinal("Manager, build a program", 1f, PieceVerdict.Unknown, closing: true));
            Assert.AreEqual("Manager, build a program", s.TakeSentence());
        }

        [Test]
        public void Finish_KeepsTheWordsStillInProgress()
        {
            var s = New();
            s.OnFinal("Manager, build a program", 1f, PieceVerdict.Unknown);
            s.OnPartial("that prints stars", 3f);
            Assert.AreEqual("Manager, build a program that prints stars", s.Finish());
            Assert.IsFalse(s.Active);

            // No new words since the piece: only the piece.
            s = New();
            s.OnFinal("Manager, build a program", 1f, PieceVerdict.Unknown);
            Assert.AreEqual("Manager, build a program", s.Finish());
        }

        [Test]
        public void Caps_PiecesAndLength()
        {
            var s = new SentenceStitcher(1.5f, 4f, 30f, 3, 2);
            s.Begin(0f);
            Assert.AreEqual(StitchStep.Hold, s.OnFinal("one two three", 1f, PieceVerdict.Unknown));
            s.OnPartial("four", 2f);
            Assert.AreEqual(StitchStep.Hold, s.OnFinal("four five six", 3f, PieceVerdict.Unknown));
            s.OnPartial("seven", 4f);
            Assert.AreEqual(StitchStep.Emit, s.OnFinal("seven eight nine", 5f, PieceVerdict.Unknown));

            s = New();
            Assert.AreEqual(StitchStep.Emit, s.OnFinal("a long goal that went on and on", 31f, PieceVerdict.Unknown));

            s = New();
            s.OnFinal("a long goal for the team", 20f, PieceVerdict.Unknown);
            s.OnPartial("still talking", 21f);
            Assert.AreEqual(StitchStep.Emit, s.Tick(31.6f));     // speaking for ever is still cut at the length cap
        }

        [Test]
        public void ServiceResendingTheWholeSentence_Replaces()
        {
            var s = New();
            s.OnFinal("build a program", 1f, PieceVerdict.Unknown);
            s.OnPartial("that", 2f);
            s.OnFinal("build a program that prints stars", 3f, PieceVerdict.Unknown);
            Assert.AreEqual("build a program that prints stars", s.Text);
        }

        [Test]
        public void Join_TidiesThePause()
        {
            Assert.AreEqual("Build a program that prints stars", SentenceStitcher.Join("Build a program.", "That prints stars"));
            Assert.AreEqual("Why? Because", SentenceStitcher.Join("Why?", "Because"));
            Assert.AreEqual("so I think", SentenceStitcher.Join("so", "I think"));
            Assert.AreEqual("a", SentenceStitcher.Join("", "a"));
            Assert.AreEqual("a", SentenceStitcher.Join("a", " "));
            Assert.AreEqual(4, SentenceStitcher.CountWords("Manager, build a program"));
            Assert.AreEqual(0, SentenceStitcher.CountWords("  "));
        }
    }
}
