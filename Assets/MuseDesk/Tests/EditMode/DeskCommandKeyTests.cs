using NUnit.Framework;
using MuseDesk.Spatial;

namespace MuseDesk.Tests.EditMode
{
    // Desk idempotency audit: the keying pattern used by BoardPresenter create / Phase2Bootstrap start /
    // SessionController.StartSampleTask (lock + KeyFor + Unlock(definitive)).
    public class DeskCommandKeyTests
    {
        [Test]
        public void Retry_after_transport_failure_reuses_key_and_double_tap_is_locked()
        {
            var l = new CommandLedger();
            int n = 0;
            Assert.IsTrue(l.TryLock("create|Fix bug"));
            string k1 = l.KeyFor("board|create|Fix bug", () => "k" + (++n));
            Assert.IsFalse(l.TryLock("create|Fix bug"), "a second create in flight is ignored");
            l.Unlock("create|Fix bug", k1, false);              // transport failure: not an answer
            Assert.IsTrue(l.TryLock("create|Fix bug"));
            string k2 = l.KeyFor("board|create|Fix bug", () => "k" + (++n));
            Assert.AreEqual(k1, k2);
            l.Unlock("create|Fix bug", k2, true);               // answered
            string k3 = l.KeyFor("board|create|Fix bug", () => "k" + (++n));
            Assert.AreNotEqual(k1, k3, "the next action after an answer mints a new key");
        }
    }
}
