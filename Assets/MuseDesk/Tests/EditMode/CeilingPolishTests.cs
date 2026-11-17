// =============================================================================
// MuseDesk · EditMode tests · ceiling polish (attention cue honesty and uprightness)
// =============================================================================
//
// Like SpatialReachTests.cs these reach the REAL runtime types by reflection (the test
// assembly cannot reference Assembly-CSharp). No scene and no GameObject is involved.
//
//  - AttentionPendingLedger: "Needs you" is withdrawn when the session no longer reports
//    the decision as waiting, and never otherwise.
//  - AttentionBeacon.UprightFacing: the cue's up is exactly world up wherever the head is
//    (above, below, straight overhead), and its printed side (-Z) faces the head.
// =============================================================================

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MuseDesk.Tests.EditMode
{
    [Category("MuseDesk")]
    public class CeilingPolishAttentionTests
    {
        const string Model = "MuseDesk.Spatial.AttentionModel";
        const string Ledger = "MuseDesk.Spatial.AttentionPendingLedger";

        static Type T(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            Assert.Fail("Runtime type not found: " + fullName);
            return null;
        }

        static object Call(object target, string method, params object[] args)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(m, "Method not found: " + target.GetType().Name + "." + method);
            return m.Invoke(target, args);
        }

        static object Get(object target, string property)
        {
            PropertyInfo p = target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(p, "Property not found: " + target.GetType().Name + "." + property);
            return p.GetValue(target, null);
        }

        static string Level(object model) { return Convert.ToString(Get(model, "Level")); }

        static int Reconcile(object ledger, object model, Func<string, bool> stillPending)
        {
            return (int)Call(ledger, "Reconcile", model, stillPending);
        }

        [Test]
        public void A_decision_the_session_still_has_waiting_is_kept()
        {
            object m = Activator.CreateInstance(T(Model));
            object l = Activator.CreateInstance(T(Ledger));
            Call(m, "DecisionRequested", "d1", "researcher");
            Call(l, "Add", "d1");

            Assert.AreEqual(0, Reconcile(l, m, id => true));
            Assert.AreEqual("Blocking", Level(m));
            Assert.AreEqual(1, (int)Get(l, "Count"));
            Assert.AreEqual(1, (int)Get(m, "PendingDecisions"));
        }

        [Test]
        public void A_decision_the_session_no_longer_has_waiting_is_withdrawn()
        {
            object m = Activator.CreateInstance(T(Model));
            object l = Activator.CreateInstance(T(Ledger));
            Call(m, "DecisionRequested", "d1", "researcher");
            Call(l, "Add", "d1");
            // The resolution reached the model under another id: on its own the model keeps asking.
            Call(m, "DecisionResolved", "some-other-id");
            Assert.AreEqual("Blocking", Level(m));

            Assert.AreEqual(1, Reconcile(l, m, id => false));
            Assert.AreEqual("None", Level(m), "no decision waiting, so no 'Needs you'");
            Assert.AreEqual(0, (int)Get(m, "PendingDecisions"));
            Assert.AreEqual(0, (int)Get(l, "Count"));
            Assert.AreEqual(0, Reconcile(l, m, id => false), "nothing left to withdraw");
        }

        [Test]
        public void Only_the_stale_decision_is_withdrawn_and_what_lies_under_it_shows()
        {
            object m = Activator.CreateInstance(T(Model));
            object l = Activator.CreateInstance(T(Ledger));
            Call(m, "DecisionRequested", "old", "researcher");
            Call(l, "Add", "old");
            Call(m, "DecisionRequested", "live", "developer");
            Call(l, "Add", "live");
            Call(m, "TaskFailed", "tester");

            Assert.AreEqual(1, Reconcile(l, m, id => id == "live"));
            Assert.AreEqual("Blocking", Level(m));
            Assert.AreEqual("developer", (string)Get(m, "AgentId"));

            Call(m, "DecisionResolved", "live");
            Call(l, "Remove", "live");
            Assert.AreEqual("Error", Level(m), "the failure that arrived meanwhile is what remains");
            Assert.AreEqual(0, Reconcile(l, m, id => false));
        }

        [Test]
        public void The_ledger_never_raises_a_level()
        {
            object m = Activator.CreateInstance(T(Model));
            object l = Activator.CreateInstance(T(Ledger));
            Call(l, "Add", "ghost");                    // the model was never told about it
            Assert.AreEqual("None", Level(m));
            Reconcile(l, m, id => true);
            Assert.AreEqual("None", Level(m), "a remembered id is not a decision");
            Reconcile(l, m, id => false);
            Assert.AreEqual("None", Level(m));
            Call(l, "Add", new object[] { null });
            Call(l, "Add", "");
            Assert.AreEqual(0, (int)Get(l, "Count"));
        }

        [Test]
        public void Clear_forgets_every_id()
        {
            object m = Activator.CreateInstance(T(Model));
            object l = Activator.CreateInstance(T(Ledger));
            Call(m, "DecisionRequested", "d1", "researcher");
            Call(l, "Add", "d1");
            Call(l, "Add", "d1");
            Assert.AreEqual(1, (int)Get(l, "Count"), "an id is remembered once");
            Call(l, "Clear");
            Assert.AreEqual(0, Reconcile(l, m, id => false), "a cleared ledger withdraws nothing");
            Assert.AreEqual("Blocking", Level(m));
        }

        // ── upright cue ──

        static Quaternion Upright(Vector3 position, Vector3 head, bool hasHead, Quaternion current)
        {
            MethodInfo mi = T("MuseDesk.Spatial.AttentionBeacon").GetMethod("UprightFacing", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(mi, "Static method not found: AttentionBeacon.UprightFacing");
            return (Quaternion)mi.Invoke(null, new object[] { position, head, hasHead, current });
        }

        static void AssertUpright(Quaternion q, string why)
        {
            Vector3 up = q * Vector3.up;
            Assert.AreEqual(1f, up.y, 1e-4f, why + ": up must be world up");
            Assert.AreEqual(0f, (q * Vector3.forward).y, 1e-4f, why + ": no pitch");
            Assert.AreEqual(0f, (q * Vector3.right).y, 1e-4f, why + ": no roll");
        }

        [Test]
        public void Cue_is_upright_and_faces_the_head_from_any_height()
        {
            Vector3 head = new Vector3(0f, 1.2f, 0f);
            Vector3[] cues =
            {
                new Vector3(1.0f, 2.3f, 0.6f),      // under a ceiling pod, high above the eyes
                new Vector3(-1.4f, 0.7f, 1.1f),     // over a floor office, below the eyes
                new Vector3(0.1f, 0.95f, 0.45f),    // the mirror near the user
                new Vector3(0.6f, 1.2f, -0.8f),     // level with the eyes, behind the shoulder
            };
            foreach (Vector3 p in cues)
            {
                // A deliberately tilted starting rotation must not leak into the result.
                Quaternion q = Upright(p, head, true, Quaternion.Euler(35f, 80f, -20f));
                AssertUpright(q, p.ToString());
                // The printed side is local -Z: it must point toward the head (horizontally).
                Vector3 toHead = head - p; toHead.y = 0f; toHead.Normalize();
                Vector3 printed = q * Vector3.back;
                Assert.Greater(Vector3.Dot(printed, toHead), 0.999f, p + ": the printed side faces the head");
            }
        }

        [Test]
        public void Cue_stays_upright_with_the_head_straight_below_or_missing()
        {
            Quaternion tilted = Quaternion.Euler(40f, 120f, 25f);
            Quaternion a = Upright(new Vector3(0.5f, 2.4f, 0.5f), new Vector3(0.5f, 1.2f, 0.5f), true, tilted);
            AssertUpright(a, "head straight below");
            Quaternion b = Upright(new Vector3(0.5f, 2.4f, 0.5f), Vector3.zero, false, tilted);
            AssertUpright(b, "no head");
            // The heading that was there is kept (flattened), not replaced by an arbitrary one.
            Vector3 kept = tilted * Vector3.forward; kept.y = 0f; kept.Normalize();
            Assert.Greater(Vector3.Dot(b * Vector3.forward, kept), 0.999f);
            Quaternion c = Upright(Vector3.zero, Vector3.zero, false, Quaternion.Euler(90f, 0f, 0f));
            AssertUpright(c, "degenerate heading");
        }
    }
}
