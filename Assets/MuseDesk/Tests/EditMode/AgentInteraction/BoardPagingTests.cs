using NUnit.Framework;
using MuseDesk.Agents;

namespace MuseDesk.Tests.AgentInteraction
{
    public class BoardPagingTests
    {
        // 3 columns, 2 rows -> capacity 6.
        const int Cols = 3, Cap = 6;

        [Test]
        public void Capacity_IsRowsTimesCols_AndNeverZero()
        {
            Assert.AreEqual(6, BoardPaging.Capacity(2, 3));
            Assert.AreEqual(1, BoardPaging.Capacity(0, 0));
        }

        [Test]
        public void Fits_NoOverflow_NoArrows()
        {
            Assert.IsFalse(BoardPaging.Overflows(6, Cap));
            Assert.IsFalse(BoardPaging.CanScrollUp(0));
            Assert.IsFalse(BoardPaging.CanScrollDown(0, 6, Cap));
            Assert.AreEqual("6", BoardPaging.CounterText(0, 6, Cap));
            Assert.AreEqual(0, BoardPaging.MaxOffset(6, Cols, Cap));
        }

        [Test]
        public void Nine_Cards_PageByOneRow()
        {
            Assert.IsTrue(BoardPaging.Overflows(9, Cap));
            Assert.IsTrue(BoardPaging.CanScrollDown(0, 9, Cap));
            Assert.AreEqual(3, BoardPaging.MaxOffset(9, Cols, Cap));
            int o = BoardPaging.Step(0, +1, 9, Cols, Cap);
            Assert.AreEqual(3, o);
            Assert.IsFalse(BoardPaging.CanScrollDown(o, 9, Cap));
            Assert.IsTrue(BoardPaging.CanScrollUp(o));
            Assert.AreEqual("4-9 of 9", BoardPaging.CounterText(o, 9, Cap));
            Assert.AreEqual(3, BoardPaging.Step(o, +1, 9, Cols, Cap));   // clamped at the end
            Assert.AreEqual(0, BoardPaging.Step(o, -1, 9, Cols, Cap));
            Assert.AreEqual(0, BoardPaging.Step(0, -1, 9, Cols, Cap));   // clamped at the start
        }

        [Test]
        public void PartialLastRow_StillReachesTheLastCard()
        {
            // 10 cards: windows start at 0, 3, 6 (6..11 shows cards 7-10).
            Assert.AreEqual(6, BoardPaging.MaxOffset(10, Cols, Cap));
            int o = BoardPaging.Step(BoardPaging.Step(0, +1, 10, Cols, Cap), +1, 10, Cols, Cap);
            Assert.AreEqual(6, o);
            Assert.IsFalse(BoardPaging.CanScrollDown(o, 10, Cap));
            Assert.AreEqual("7-10 of 10", BoardPaging.CounterText(o, 10, Cap));
        }

        [Test]
        public void Visibility_FollowsTheWindow()
        {
            Assert.IsTrue(BoardPaging.IsVisible(3, 3, Cap));
            Assert.IsTrue(BoardPaging.IsVisible(8, 3, Cap));
            Assert.IsFalse(BoardPaging.IsVisible(2, 3, Cap));
            Assert.IsFalse(BoardPaging.IsVisible(9, 3, Cap));
        }

        [Test]
        public void Clamp_AfterCardsAreRemoved()
        {
            Assert.AreEqual(3, BoardPaging.Clamp(6, 9, Cols, Cap));    // 12 -> 9 cards
            Assert.AreEqual(0, BoardPaging.Clamp(6, 5, Cols, Cap));    // now fits: back to the top
            Assert.AreEqual(0, BoardPaging.Clamp(3, 0, Cols, Cap));    // empty column
            Assert.AreEqual(0, BoardPaging.Clamp(-4, 9, Cols, Cap));
            Assert.AreEqual(3, BoardPaging.Clamp(4, 12, Cols, Cap));   // snaps to a row start
        }
    }
}
