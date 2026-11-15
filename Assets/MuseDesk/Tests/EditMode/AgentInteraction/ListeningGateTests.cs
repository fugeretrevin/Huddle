using NUnit.Framework;
using MuseDesk.Agents;

namespace MuseDesk.Tests.AgentInteraction
{
    public class ListeningGateTests
    {
        [Test] public void NotAddressed_NeverShows() { Assert.IsFalse(ListeningGate.Show(false, false, false, false, false)); }
        [Test] public void AddressedAndFree_Shows() { Assert.IsTrue(ListeningGate.Show(true, false, false, false, false)); }
        [Test] public void Walking_Blocks() { Assert.IsFalse(ListeningGate.Show(true, true, false, false, false)); }
        [Test] public void HoldingPaper_Blocks() { Assert.IsFalse(ListeningGate.Show(true, false, true, false, false)); }
        [Test] public void OneShot_Blocks() { Assert.IsFalse(ListeningGate.Show(true, false, false, true, false)); }
        [Test] public void Critical_Blocks() { Assert.IsFalse(ListeningGate.Show(true, false, false, false, true)); }
    }
}
