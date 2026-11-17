// MuseDesk · EditMode tests · the Software Factory is off, and completion does not need it.
// Reflection only (the test assembly cannot reference Assembly-CSharp).

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MuseDesk.Tests.EditMode
{
    public class FactoryRemovedTests
    {
        static Type Find(string name)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(name, false);
                if (t != null) return t;
            }
            return null;
        }

        [Test]
        public void ShowFactory_IsOffByDefault()
        {
            var t = Find("MuseDesk.Art.SoftwareFactoryVisualController");
            Assert.IsNotNull(t);
            Assert.IsFalse((bool)t.GetField("ShowFactory").GetValue(null));
        }

        [Test]
        public void DeliveryBeats_WithNoFactory_DoNotThrow_AndCompletionCanProceed()
        {
            var t = Find("MuseDesk.Art.FactoryEventController");
            Assert.IsNotNull(t);
            var go = new GameObject("FactoryEventsTest");
            try
            {
                var c = go.AddComponent(t);   // factory reference stays null, as after RemoveFactoryV1
                Assert.DoesNotThrow(() => t.GetMethod("PlayDeliveryTransport").Invoke(c, null));
                bool gate = (bool)t.GetMethod("PlayDeliveryGate").Invoke(c, new object[] { true });
                Assert.IsFalse(gate, "no factory, so no gate is lit");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void StageClips_AreInert_WhenFactoryOff()
        {
            var t = Find("MuseDesk.Art.SoftwareFactoryVisualController");
            var go = new GameObject("FactoryVisualTest");
            try
            {
                var c = go.AddComponent(t);
                Assert.DoesNotThrow(() => t.GetMethod("PlayCompletedDelivery").Invoke(c, null));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
