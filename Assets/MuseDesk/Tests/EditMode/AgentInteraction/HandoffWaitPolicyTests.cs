using NUnit.Framework;
using MuseDesk.Agents;

namespace MuseDesk.Tests.AgentInteraction
{
    public class HandoffWaitPolicyTests
    {
        [Test] public void WaitsWhilePendingWithinBound() => Assert.IsTrue(HandoffWaitPolicy.KeepWaiting(true, 3f, 15f));
        [Test] public void StopsWaitingWhenNothingPending() => Assert.IsFalse(HandoffWaitPolicy.KeepWaiting(false, 0f, 15f));
        [Test] public void StopsWaitingAtBound() => Assert.IsFalse(HandoffWaitPolicy.KeepWaiting(true, 15f, 15f));
        [Test] public void AbandonsOnlyAfterBoundWhilePending()
        {
            Assert.IsFalse(HandoffWaitPolicy.ShouldAbandon(true, true, 5f, 15f), "a queued real handoff is not dropped early");
            Assert.IsTrue(HandoffWaitPolicy.ShouldAbandon(true, true, 15f, 15f));
            Assert.IsFalse(HandoffWaitPolicy.ShouldAbandon(true, false, 20f, 15f));
            Assert.IsFalse(HandoffWaitPolicy.ShouldAbandon(false, true, 20f, 15f));
        }
    }
}
