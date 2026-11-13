using NUnit.Framework;
using UnityEngine;
using MuseDesk.Agents;

namespace MuseDesk.Tests.AgentInteraction
{
    /// <summary>Pure logic of the "characters respond to the person" pass (MuseDesk.Agents.Core).</summary>
    public class AgentStatusSentenceTests
    {
        [Test]
        public void Working_sentences_name_the_work_and_the_step()
        {
            Assert.AreEqual("Reading the brief. Step 1 of 3.",
                AgentStatusSentence.Describe("Researcher", AgentStatusKind.Researching, true, true));
            Assert.AreEqual("Writing the code. Step 2 of 3.",
                AgentStatusSentence.Describe("developer", AgentStatusKind.Coding, true, true));
            Assert.AreEqual("Running the tests. Step 3 of 3.",
                AgentStatusSentence.Describe("TESTER", AgentStatusKind.Testing, true, true));
        }

        [Test]
        public void Unknown_role_gets_no_step()
        {
            Assert.AreEqual("Writing the code.",
                AgentStatusSentence.Describe("intern", AgentStatusKind.Coding, true, true));
            Assert.AreEqual(0, AgentStatusSentence.StepOf(null));
        }

        [Test]
        public void Idle_depends_on_the_run_flags()
        {
            Assert.AreEqual("Idle. Press Start to begin.",
                AgentStatusSentence.Describe("Tester", AgentStatusKind.Idle, false, false));
            Assert.AreEqual("Idle. The last run is finished.",
                AgentStatusSentence.Describe("Tester", AgentStatusKind.Idle, false, true));
            Assert.AreEqual("Standing by for my turn.",
                AgentStatusSentence.Describe("Tester", AgentStatusKind.Idle, true, true));
            Assert.AreEqual("Standing by. The Developer is writing the code.",
                AgentStatusSentence.Describe("Tester", AgentStatusKind.Idle, true, true, "developer", AgentStatusKind.Coding));
        }

        [Test]
        public void Idle_never_claims_a_teammate_is_working_when_it_is_not()
        {
            // A teammate that is not in a working state must not appear in the sentence.
            Assert.AreEqual("Standing by for my turn.",
                AgentStatusSentence.Describe("Tester", AgentStatusKind.Idle, true, true, "Developer", AgentStatusKind.Idle));
            // No run in flight: no "standing by" at all.
            Assert.AreEqual("Idle. Press Start to begin.",
                AgentStatusSentence.Describe("Tester", AgentStatusKind.Idle, false, false, "Developer", AgentStatusKind.Coding));
        }

        [Test]
        public void Waiting_and_outcome_sentences()
        {
            Assert.AreEqual("Waiting for your decision.",
                AgentStatusSentence.Describe("Researcher", AgentStatusKind.WaitingForUser, true, true));
            Assert.AreEqual("Waiting.",
                AgentStatusSentence.Describe("Researcher", AgentStatusKind.Waiting, true, true));
            Assert.AreEqual("Bringing you a paper to decide on.",
                AgentStatusSentence.Describe("Researcher", AgentStatusKind.Approaching, true, true));
            Assert.AreEqual("Done. My part is finished.",
                AgentStatusSentence.Describe("Developer", AgentStatusKind.Complete, true, true));
            StringAssert.StartsWith("Something went wrong",
                AgentStatusSentence.Describe("Tester", AgentStatusKind.Error, true, true));
        }

        [Test]
        public void Sentences_are_short_plain_ascii()
        {
            var kinds = (AgentStatusKind[])System.Enum.GetValues(typeof(AgentStatusKind));
            foreach (string role in new[] { "Researcher", "Developer", "Tester" })
                foreach (AgentStatusKind k in kinds)
                    foreach (bool run in new[] { false, true })
                    {
                        string s = AgentStatusSentence.Describe(role, k, run, run, "Developer", AgentStatusKind.Coding);
                        Assert.IsNotEmpty(s);
                        Assert.LessOrEqual(s.Length, 72, s);      // three bubble lines of about 24 characters
                        foreach (char c in s) Assert.IsTrue(c >= ' ' && c <= '~', "non-ASCII in: " + s);
                    }
        }

        [Test]
        public void Who_needs_user_prefers_a_waiting_decision()
        {
            var roles = new[] { "Researcher", "Developer", "Tester" };
            var kinds = new[] { AgentStatusKind.WaitingForUser, AgentStatusKind.Idle, AgentStatusKind.Error };
            Assert.AreEqual("Researcher", AgentStatusSentence.RoleThatNeedsUser(roles, kinds, 3));
            Assert.AreEqual("The Researcher is waiting for your decision.", AgentStatusSentence.WhoNeedsUser(roles, kinds, 3));

            kinds = new[] { AgentStatusKind.Idle, AgentStatusKind.Coding, AgentStatusKind.Error };
            Assert.AreEqual("Tester", AgentStatusSentence.RoleThatNeedsUser(roles, kinds, 3));
            StringAssert.StartsWith("The Tester needs your attention", AgentStatusSentence.WhoNeedsUser(roles, kinds, 3));

            kinds = new[] { AgentStatusKind.Approaching, AgentStatusKind.Idle, AgentStatusKind.Idle };
            Assert.AreEqual("The Researcher is bringing you a paper.", AgentStatusSentence.WhoNeedsUser(roles, kinds, 3));

            kinds = new[] { AgentStatusKind.Idle, AgentStatusKind.Coding, AgentStatusKind.Complete };
            Assert.IsNull(AgentStatusSentence.RoleThatNeedsUser(roles, kinds, 3));
            Assert.AreEqual("Nobody needs you right now.", AgentStatusSentence.WhoNeedsUser(roles, kinds, 3));
        }

        [Test]
        public void Normalize_role()
        {
            Assert.AreEqual("Researcher", AgentStatusSentence.NormalizeRole(" researcher "));
            Assert.AreEqual("Tester", AgentStatusSentence.NormalizeRole("TESTER"));
            Assert.IsNull(AgentStatusSentence.NormalizeRole("system"));
            Assert.IsNull(AgentStatusSentence.NormalizeRole(""));
        }
    }

    public class GazeDwellTimerTests
    {
        const float Dt = 1f / 72f;

        static int Run(GazeDwellTimer t, int target, float seconds)
        {
            int shown = t.Shown;
            for (float s = 0f; s < seconds; s += Dt) shown = t.Tick(target, Dt);
            return shown;
        }

        [Test]
        public void Shows_after_the_dwell_and_not_before()
        {
            var t = new GazeDwellTimer();
            // The first look has to outlast the grace period before it becomes the candidate.
            Assert.AreEqual(-1, Run(t, 1, 1.0f));
            Assert.AreEqual(1, Run(t, 1, 0.6f));
        }

        [Test]
        public void A_short_loss_does_not_restart_the_dwell()
        {
            var t = new GazeDwellTimer();
            Run(t, 2, 1.0f);
            Run(t, -1, 0.15f);                      // a wobble shorter than the grace
            Assert.AreEqual(2, Run(t, 2, 0.6f));
        }

        [Test]
        public void A_long_loss_restarts_the_dwell()
        {
            var t = new GazeDwellTimer();
            Run(t, 2, 1.0f);
            Run(t, -1, 0.5f);
            Assert.AreEqual(-1, Run(t, 2, 1.0f));
            Assert.AreEqual(2, Run(t, 2, 0.6f));
        }

        [Test]
        public void Hides_after_looking_away_for_the_linger()
        {
            var t = new GazeDwellTimer();
            Run(t, 0, 2f);
            Assert.AreEqual(0, t.Shown);
            Assert.AreEqual(0, Run(t, -1, 0.4f));   // still up
            Assert.AreEqual(-1, Run(t, -1, 0.4f));  // gone
        }

        [Test]
        public void Moving_to_another_character_swaps_after_its_own_dwell()
        {
            var t = new GazeDwellTimer();
            Run(t, 0, 2f);
            Assert.AreEqual(-1, Run(t, 1, 1.0f));   // the first has left, the second is not there yet
            Assert.AreEqual(1, Run(t, 1, 0.7f));
        }

        [Test]
        public void Sweeping_across_never_shows_anything()
        {
            var t = new GazeDwellTimer();
            for (int i = 0; i < 20; i++) Assert.AreEqual(-1, Run(t, i % 3, 0.4f));
        }
    }

    public class WaveDetectorTests
    {
        const float Dt = 1f / 72f;

        static bool Feed(WaveDetector w, System.Func<float, float> lateral, float seconds, bool valid = true)
        {
            bool fired = false;
            for (float s = 0f; s < seconds; s += Dt) fired |= w.Tick(lateral(s), valid, s);
            return fired;
        }

        [Test]
        public void A_side_to_side_wave_fires()
        {
            var w = new WaveDetector();
            Assert.IsTrue(Feed(w, s => 0.05f * Mathf.Sin(s * 2.5f * 6.2831853f), 1.4f));
        }

        [Test]
        public void A_steady_reach_does_not_fire()
        {
            var w = new WaveDetector();
            Assert.IsFalse(Feed(w, s => 0.3f * s, 2f));
        }

        [Test]
        public void A_slow_sway_does_not_fire()
        {
            var w = new WaveDetector();
            Assert.IsFalse(Feed(w, s => 0.06f * Mathf.Sin(s * 0.3f * 6.2831853f), 4f));
        }

        [Test]
        public void Tracking_jitter_does_not_fire()
        {
            var w = new WaveDetector();
            Assert.IsFalse(Feed(w, s => 0.006f * Mathf.Sin(s * 9f * 6.2831853f), 3f));
        }

        [Test]
        public void A_closed_or_hidden_hand_does_not_fire()
        {
            var w = new WaveDetector();
            Assert.IsFalse(Feed(w, s => 0.05f * Mathf.Sin(s * 2.5f * 6.2831853f), 2f, valid: false));
        }

        [Test]
        public void Fires_once_per_wave()
        {
            var w = new WaveDetector();
            int count = 0;
            for (float s = 0f; s < 1.0f; s += Dt)
                if (w.Tick(0.05f * Mathf.Sin(s * 2.5f * 6.2831853f), true, s)) count++;
            Assert.LessOrEqual(count, 1);
        }
    }

    public class SmallLogicTests
    {
        [Test]
        public void Poke_combo_counts_follow_ups_within_two_seconds()
        {
            var c = new PokeCombo();
            Assert.AreEqual(0, c.Register(10f));
            Assert.AreEqual(1, c.Register(11.5f));
            Assert.AreEqual(2, c.Register(12.0f));
            Assert.AreEqual(0, c.Register(15.0f));
        }

        [Test]
        public void Cooldown_gates()
        {
            var c = new Cooldown();
            Assert.IsTrue(c.TryFire(0f, 5f));
            Assert.IsFalse(c.TryFire(4.9f, 5f));
            Assert.IsTrue(c.TryFire(5f, 5f));
        }

        [Test]
        public void Hold_still_fires_once_and_rearms_on_leaving()
        {
            var h = new HoldStillDetector();
            Vector3 p = new Vector3(0.1f, 0.2f, 0.3f);
            int fired = 0;
            for (int i = 0; i < 100; i++) if (h.Tick(p, true, 0.0139f)) fired++;
            Assert.AreEqual(1, fired);
            h.Tick(p, false, 0.0139f);
            for (int i = 0; i < 100; i++) if (h.Tick(p, true, 0.0139f)) fired++;
            Assert.AreEqual(2, fired);
        }

        [Test]
        public void Hold_still_does_not_fire_for_a_moving_point()
        {
            var h = new HoldStillDetector();
            bool fired = false;
            for (int i = 0; i < 200; i++) fired |= h.Tick(new Vector3(i * 0.004f, 0f, 0f), true, 0.0139f);
            Assert.IsFalse(fired);
        }

        [Test]
        public void Distance_scale_keeps_cap_height_per_distance()
        {
            Assert.AreEqual(1f, AgentViewingMath.DistanceScale(0.3f), 1e-4f);    // never smaller than authored
            Assert.AreEqual(1f, AgentViewingMath.DistanceScale(0.5f), 1e-4f);
            Assert.AreEqual(1.4f, AgentViewingMath.DistanceScale(0.7f), 1e-4f);  // the desk
            Assert.AreEqual(4f, AgentViewingMath.DistanceScale(2f), 1e-4f);      // the floor
            Assert.AreEqual(7f, AgentViewingMath.DistanceScale(10f), 1e-4f);     // capped
            // 6.5 mm text has a cap height of about 4.7 mm: scaled, that is 4 to 5 mm per 0.5 m.
            float capAt2m = 4.7f * AgentViewingMath.DistanceScale(2f);
            Assert.That(capAt2m / 4f, Is.InRange(4f, 5f));
        }

        [Test]
        public void Gaze_cone_is_generous_near_and_tight_far()
        {
            float near = AgentViewingMath.GazeConeDegrees(0.7f, 0.045f);
            float far = AgentViewingMath.GazeConeDegrees(3f, 0.045f);
            Assert.Greater(near, far);
            Assert.LessOrEqual(near, 11f);
            Assert.GreaterOrEqual(far, 5f);
        }

        [Test]
        public void Gaze_pick_takes_the_closest_inside_its_cone()
        {
            var angles = new[] { 12f, 4f, 6f };
            var cones = new[] { 8f, 8f, 8f };
            Assert.AreEqual(1, AgentViewingMath.PickGazeTarget(angles, cones, 3));
            angles = new[] { 12f, 9f, 20f };
            Assert.AreEqual(-1, AgentViewingMath.PickGazeTarget(angles, cones, 3));
        }

        [Test]
        public void Squash_keeps_volume_and_curves_start_and_end_at_rest()
        {
            float v = 0.8f, s = AgentViewingMath.SideForVertical(v);
            Assert.AreEqual(1f, v * s * s, 1e-4f);
            float[] t = { 0f, 0.5f, 1f };
            float[] val = { 1f, 0.8f, 1f };
            Assert.AreEqual(1f, AgentViewingMath.Keys(t, val, 0f), 1e-5f);
            Assert.AreEqual(0.8f, AgentViewingMath.Keys(t, val, 0.5f), 1e-5f);
            Assert.AreEqual(1f, AgentViewingMath.Keys(t, val, 1f), 1e-5f);
            Assert.AreEqual(1f, AgentViewingMath.Keys(t, val, 2f), 1e-5f);
        }
    }
}
