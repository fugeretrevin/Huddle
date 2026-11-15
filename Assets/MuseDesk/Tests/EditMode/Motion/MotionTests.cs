#if MUSEDESK_SPATIAL_TESTS
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Motion;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Motion
{
    public class MotionTests
    {
        [TearDown] public void Reset() { MotionSettings.SetReducedMotion(false); }

        [Test]
        public void Eases_AreExactAtEndsAndMonotonicWithoutOvershoot()
        {
            foreach (EaseKind k in System.Enum.GetValues(typeof(EaseKind)))
            {
                Assert.AreEqual(0f, Ease.Evaluate(k, 0f), 1e-6f, k.ToString());
                Assert.AreEqual(1f, Ease.Evaluate(k, 1f), 1e-6f, k.ToString());
                Assert.AreEqual(0f, Ease.Evaluate(k, float.NaN), 1e-6f, k + " NaN");
                if (Ease.Overshoots(k)) continue;
                float prev = 0f;
                for (int i = 1; i <= 100; i++)
                {
                    float v = Ease.Evaluate(k, i / 100f);
                    Assert.GreaterOrEqual(v + 1e-5f, prev, k + " not monotonic at " + i);
                    Assert.LessOrEqual(v, 1f + 1e-5f, k + " overshoots");
                    prev = v;
                }
            }
            float peak = 0f;
            for (int i = 0; i <= 200; i++) peak = Mathf.Max(peak, Ease.Evaluate(EaseKind.SoftBack, i / 200f));
            Assert.Less(peak, 1.06f, "SoftBack stays gentle");
            Assert.Greater(peak, 1.0f);
        }

        [Test]
        public void Clock_CompletesExactlyAndReportsOnce()
        {
            var c = new MotionClock();
            c.Start(new MotionToken(0.3f, EaseKind.OutCubic));
            int completions = 0;
            for (int i = 0; i < 40; i++) if (c.Step(0.016f)) completions++;
            Assert.AreEqual(1, completions);
            Assert.AreEqual(MotionState.Completed, c.State);
            Assert.AreEqual(1f, c.Value);
        }

        [Test]
        public void Clock_ZeroOrNegativeDurationCompletesOnFirstStep()
        {
            foreach (float d in new[] { 0f, -1f, float.NaN })
            {
                var c = new MotionClock();
                c.Start(new MotionToken(d, EaseKind.Linear));
                Assert.IsTrue(c.Step(0.016f), "duration " + d);
                Assert.AreEqual(1f, c.Value);
            }
        }

        [Test]
        public void Clock_CancelStopsAndRestartBeginsAgain()
        {
            var c = new MotionClock();
            c.Start(new MotionToken(1f, EaseKind.Linear));
            c.Step(0.04f);
            c.Cancel();
            Assert.AreEqual(MotionState.Cancelled, c.State);
            Assert.IsFalse(c.Step(5f), "a cancelled clock never completes");
            c.Start(new MotionToken(1f, EaseKind.Linear));
            Assert.AreEqual(MotionState.Running, c.State);
            Assert.AreEqual(0f, c.T, 1e-6f);
        }

        [Test]
        public void Clock_LongPauseCannotSkipTheTransitionInOneFrame()
        {
            var c = new MotionClock();
            c.Start(new MotionToken(0.4f, EaseKind.Linear));
            Assert.IsFalse(c.Step(30f), "a 30 s hitch advances at most MaxStep");
            Assert.LessOrEqual(c.T, MotionSettings.MaxStep / 0.4f + 1e-5f);
        }

        [Test]
        public void ReducedMotion_HalvesDurationsAndRemovesOvershoot()
        {
            var t = new MotionToken(0.6f, EaseKind.SoftBack);
            Assert.AreEqual(0.6f, t.Duration, 1e-6f);
            MotionSettings.SetReducedMotion(true);
            Assert.AreEqual(0.3f, t.Duration, 1e-6f);
            for (int i = 0; i <= 50; i++) Assert.LessOrEqual(t.Evaluate(i / 50f), 1f + 1e-6f);
        }

        [Test]
        public void ProfiledMover_LandsExactlyRespectsLimitsAndNeverOvershoots()
        {
            var m = new ProfiledMover(0f);
            float maxV = 0.22f, accel = 0.22f / 0.6f, decel = 0.22f / 0.7f;
            float prevV = 0f;
            bool done = false;
            int steps = 0;
            while (!done && steps < 5000)
            {
                done = m.Step(0.55f, maxV, accel, decel, 1f / 72f);
                steps++;
                Assert.LessOrEqual(m.position, 0.55f + 1e-6f, "overshoot");
                Assert.LessOrEqual(Mathf.Abs(m.velocity), maxV + 1e-5f, "speed limit");
                Assert.LessOrEqual(m.velocity - prevV, accel / 72f + 1e-5f, "acceleration limit");
                prevV = m.velocity;
            }
            Assert.IsTrue(done, "arrives");
            Assert.AreEqual(0.55f, m.position);
            Assert.AreEqual(0f, m.velocity);
            // Distance 0.55 at these limits: about 0.6/2 + 0.7/2 + cruise; well under 4 s.
            Assert.Less(steps / 72f, 4f);
        }

        [Test]
        public void ProfiledMover_ReversesSmoothlyWhenTheTargetChanges()
        {
            var m = new ProfiledMover(0f);
            for (int i = 0; i < 60; i++) m.Step(1f, 0.3f, 0.5f, 0.5f, 1f / 60f);
            Assert.Greater(m.velocity, 0f);
            float v0 = m.velocity;
            m.Step(0f, 0.3f, 0.5f, 0.5f, 1f / 60f);       // target now behind
            Assert.Greater(m.velocity, v0 - 0.5f / 60f - 1e-5f, "brakes at the decel limit, no instant reversal");
            bool done = false;
            for (int i = 0; i < 2000 && !done; i++) done = m.Step(0f, 0.3f, 0.5f, 0.5f, 1f / 60f);
            Assert.IsTrue(done);
            Assert.AreEqual(0f, m.position);
        }

        [Test]
        public void DampedAngle_TakesShortWayAndSettlesExactly()
        {
            float yaw = 170f, vel = 0f;
            for (int i = 0; i < 400; i++) yaw = Damped.FollowAngle(yaw, -170f, ref vel, 0.16f, 1f / 72f);
            Assert.AreEqual(-170f, yaw, "settles exactly (a turn really ends)");
            Assert.AreEqual(0f, vel);
            // Short way: from 170 to -170 crosses 180, never passes through 0.
            yaw = 170f; vel = 0f;
            for (int i = 0; i < 30; i++)
            {
                yaw = Damped.FollowAngle(yaw, -170f, ref vel, 0.16f, 1f / 72f);
                Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)), -1f);
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)), 10.5f, "stays near 180, the short way");
            }
        }

        [Test]
        public void Presence_MapsOnlyRealStates()
        {
            Assert.AreEqual(DeskActivity.Idle, HabitatPresence.FromTaskState(null));
            Assert.AreEqual(DeskActivity.Idle, HabitatPresence.FromTaskState("queued"), "queued is not working");
            Assert.AreEqual(DeskActivity.Idle, HabitatPresence.FromTaskState("completed"));
            Assert.AreEqual(DeskActivity.Working, HabitatPresence.FromTaskState("running"));
            Assert.AreEqual(DeskActivity.Waiting, HabitatPresence.FromTaskState("awaiting_approval"));
            Assert.AreEqual(DeskActivity.Waiting, HabitatPresence.FromTaskState("paused"));
            Assert.AreEqual(DeskActivity.Blocked, HabitatPresence.FromTaskState("blocked"));
            Assert.AreEqual(DeskActivity.Problem, HabitatPresence.FromTaskState("failed"));
        }
    }
}
#endif
