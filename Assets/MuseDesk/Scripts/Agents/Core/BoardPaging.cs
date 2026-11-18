namespace MuseDesk.Agents
{
    /// <summary>
    /// Pure paging maths for one task-board column (rc22). A column shows at most <c>capacity</c> cards
    /// (rows x columns); the rest are scrolled out of view. The window starts at <c>offset</c>, always a
    /// whole row (a multiple of <c>cols</c>), and one arrow press moves it by one row.
    /// </summary>
    public static class BoardPaging
    {
        /// <summary>Cards a column can show: rows x cols, never below one row.</summary>
        public static int Capacity(int rows, int cols)
        {
            if (rows < 1) rows = 1;
            if (cols < 1) cols = 1;
            return rows * cols;
        }

        /// <summary>The largest allowed offset: the first card of the last row that still fills the window.</summary>
        public static int MaxOffset(int total, int cols, int capacity)
        {
            if (cols < 1) cols = 1;
            if (total <= capacity) return 0;
            int over = total - capacity;
            return ((over + cols - 1) / cols) * cols;
        }

        /// <summary>Keep an offset inside [0, MaxOffset] and on a row boundary (clamps after cards are removed).</summary>
        public static int Clamp(int offset, int total, int cols, int capacity)
        {
            if (cols < 1) cols = 1;
            if (offset < 0) offset = 0;
            int max = MaxOffset(total, cols, capacity);
            if (offset > max) offset = max;
            return (offset / cols) * cols;
        }

        /// <summary>Move the window one row: dir &lt; 0 up (earlier cards), dir &gt; 0 down. Clamped.</summary>
        public static int Step(int offset, int dir, int total, int cols, int capacity)
        {
            if (cols < 1) cols = 1;
            int o = Clamp(offset, total, cols, capacity);
            if (dir < 0) o -= cols;
            else if (dir > 0) o += cols;
            return Clamp(o, total, cols, capacity);
        }

        public static bool CanScrollUp(int offset) => offset > 0;

        public static bool CanScrollDown(int offset, int total, int capacity) => offset + capacity < total;

        /// <summary>True when the column has more cards than it can show (the arrows and the range appear).</summary>
        public static bool Overflows(int total, int capacity) => total > capacity;

        /// <summary>Is the card at <paramref name="index"/> (0-based, in column order) inside the window?</summary>
        public static bool IsVisible(int index, int offset, int capacity) => index >= offset && index < offset + capacity;

        /// <summary>Counter text: "3" when everything fits, "4-9 of 12" (ASCII hyphen) when it pages.</summary>
        public static string CounterText(int offset, int total, int capacity)
        {
            if (total <= capacity) return total.ToString();
            int first = offset + 1;
            int last = offset + capacity;
            if (last > total) last = total;
            return first + "-" + last + " of " + total;
        }
    }
}
