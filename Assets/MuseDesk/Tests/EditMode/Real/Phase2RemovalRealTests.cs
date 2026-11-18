// rc22: the REAL Phase2Reducer (Assembly-CSharp, reached by reflection like RealTypeDriftTests) drops a card whose
// task.completed event carries removed:true, raises TaskRemoved once, and never seeds a card for an unknown removed id.

using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;

namespace MuseDesk.Tests.EditMode.Real
{
    [Category("MuseDesk")]
    public class Phase2RemovalRealTests
    {
        const string NS = "MuseDesk.Phase2.Contracts.";

        static Type JObjectType()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Newtonsoft.Json.Linq.JObject", false);
                if (t != null) return t;
            }
            Assert.Fail("Newtonsoft JObject not loaded");
            return null;
        }

        static object Card(string id, string state)
        {
            var t = RealType.Find(NS + "TaskCardV2");
            var c = Activator.CreateInstance(t);
            t.GetField("TaskId").SetValue(c, id);
            t.GetField("TaskState").SetValue(c, state);
            return c;
        }

        static object Reducer(params object[] cards)
        {
            var rt = RealType.Find(NS + "Phase2Reducer");
            var r = Activator.CreateInstance(rt);
            var st = RealType.Find(NS + "SnapshotV2");
            var snap = Activator.CreateInstance(st);
            var list = (IList)st.GetField("Tasks").GetValue(snap);
            list.Clear();
            foreach (var c in cards) list.Add(c);
            st.GetField("LastSeq").SetValue(snap, 1L);
            rt.GetMethod("LoadSnapshot").Invoke(r, new object[] { snap });
            return r;
        }

        static bool Apply(object reducer, long seq, string taskId, string payloadJson)
        {
            var et = RealType.Find(NS + "EventEnvelopeV2");
            var e = Activator.CreateInstance(et);
            et.GetField("EventId").SetValue(e, "evt-" + seq);
            et.GetField("Seq").SetValue(e, seq);
            et.GetField("Type").SetValue(e, "task.completed");
            et.GetField("TaskId").SetValue(e, taskId);
            et.GetField("Payload").SetValue(e, JObjectType().GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { payloadJson }));
            return (bool)reducer.GetType().GetMethod("Apply").Invoke(reducer, new[] { e });
        }

        static int TaskCount(object reducer)
        {
            var tasks = reducer.GetType().GetProperty("Tasks").GetValue(reducer);
            return (int)tasks.GetType().GetProperty("Count").GetValue(tasks);
        }

        static void OnRemoved(object reducer, Action<string> handler)
        {
            var ev = reducer.GetType().GetEvent("TaskRemoved");
            Assert.IsNotNull(ev, "Phase2Reducer.TaskRemoved is missing");
            ev.AddEventHandler(reducer, handler);
        }

        [Test]
        public void RemovedFlag_DropsTheCard_AndRaisesTaskRemovedOnce()
        {
            var r = Reducer(Card("t1", "queued"), Card("t2", "queued"));
            int fired = 0; string who = null;
            OnRemoved(r, id => { fired++; who = id; });

            Assert.IsTrue(Apply(r, 2, "t1", "{\"task_state\":\"cancelled\",\"removed\":true,\"revision\":3}"));
            Assert.AreEqual(1, TaskCount(r), "the removed card is gone from the model");
            Assert.AreEqual(1, fired);
            Assert.AreEqual("t1", who);
        }

        [Test]
        public void ClearBoard_RemovesEveryCard()
        {
            var r = Reducer(Card("a", "queued"), Card("b", "running"), Card("c", "completed"));
            int fired = 0;
            OnRemoved(r, id => fired++);
            long seq = 2;
            foreach (var id in new[] { "a", "b", "c" })
                Apply(r, seq++, id, "{\"task_state\":\"cancelled\",\"removed\":true}");
            Assert.AreEqual(0, TaskCount(r));
            Assert.AreEqual(3, fired);
        }

        [Test]
        public void RemovedFlag_ForAnUnknownTask_DoesNotSeedACard()
        {
            var r = Reducer(Card("t1", "queued"));
            int fired = 0;
            OnRemoved(r, id => fired++);
            Assert.IsTrue(Apply(r, 2, "ghost", "{\"task_state\":\"cancelled\",\"removed\":true}"));
            Assert.AreEqual(1, TaskCount(r), "no card is invented for an id the board never had");
            Assert.AreEqual(0, fired);
        }

        [Test]
        public void PlainCancelled_WithoutRemoved_StaysOnTheBoard()
        {
            var r = Reducer(Card("t1", "queued"));
            Assert.IsTrue(Apply(r, 2, "t1", "{\"task_state\":\"cancelled\"}"));
            Assert.AreEqual(1, TaskCount(r), "a rejected run is cancelled, not removed");
        }
    }
}
