// Sprint A4b: stable idempotency keys for focus on/off (LatestWinsToggle over CommandLedger).
#if MUSEDESK_SPATIAL_TESTS
using NUnit.Framework;
using MuseDesk.Spatial;

namespace MuseDesk.Tests.EditMode
{
    [Category("MuseDesk")]
    public class LatestWinsToggleTests
    {
        int _n;
        string Mint(bool on) { return (on ? "on" : "off") + (++_n); }

        [Test]
        public void DoublePoke_SendsOneRequest()
        {
            var t = new LatestWinsToggle();
            t.Want(true);
            Assert.IsTrue(t.TryBegin(Mint, out bool v, out string k));
            Assert.IsTrue(v);
            t.Want(true);
            Assert.IsFalse(t.TryBegin(Mint, out v, out string k2), "one in flight");
            Assert.IsNull(k2);
        }

        [Test]
        public void RetryAfterFailure_ReusesTheKey()
        {
            var t = new LatestWinsToggle();
            t.Want(true);
            t.TryBegin(Mint, out bool v, out string k1);
            Assert.IsFalse(t.Complete(v, k1, false));
            t.Want(true);
            Assert.IsTrue(t.TryBegin(Mint, out v, out string k2), "failure frees the toggle");
            Assert.AreEqual(k1, k2);
        }

        [Test]
        public void AfterAnAnswer_TheNextActionGetsANewKey_AndSameValueIsNotResent()
        {
            var t = new LatestWinsToggle();
            t.Want(true);
            t.TryBegin(Mint, out bool v, out string k1);
            t.Complete(v, k1, true);
            t.Want(true);
            Assert.IsFalse(t.TryBegin(Mint, out v, out _), "server already has it");
            t.Want(false);
            Assert.IsTrue(t.TryBegin(Mint, out v, out string k2));
            Assert.IsFalse(v);
            Assert.AreNotEqual(k1, k2);
        }

        [Test]
        public void NewestWishWins_WhenFlippedWhileInFlight()
        {
            var t = new LatestWinsToggle();
            t.Want(true);
            t.TryBegin(Mint, out bool v, out string k1);
            t.Want(false);   // flipped back before the first answer
            Assert.IsTrue(t.Complete(v, k1, true), "newer wish waiting");
            Assert.IsTrue(t.TryBegin(Mint, out v, out _));
            Assert.IsFalse(v, "the late answer for 'on' does not decide the state");
        }

        [Test]
        public void BackendEchoDuringFlight_DoesNotClobberNewerWish()
        {
            var t = new LatestWinsToggle();
            t.Want(true);
            t.TryBegin(Mint, out bool v, out string k1);
            t.Want(false);
            t.Observe(true);   // echo of the first request
            Assert.IsTrue(t.Desired == false);
            Assert.IsTrue(t.Complete(v, k1, true));
        }

        [Test]
        public void BackendEchoWhenIdle_SyncsDesiredSoNothingIsResent()
        {
            var t = new LatestWinsToggle();
            t.Observe(true);
            Assert.IsFalse(t.TryBegin(Mint, out _, out _));
        }
    }
}
#endif
