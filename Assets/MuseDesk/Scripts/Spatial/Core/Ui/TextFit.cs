using System;

namespace MuseDesk.Spatial.Ui
{
    public enum FontWeightKind { Regular = 0, Medium = 1, Bold = 2 }

    /// <summary>Outcome of fitting text into a box. Pure data.</summary>
    public struct TextFitResult
    {
        public float SizeMm;
        public int Lines;          // lines actually shown
        public int MaxLines;       // lines the box holds at SizeMm
        public int VisibleChars;   // characters shown (before the ellipsis when truncated)
        public bool Truncated;
        public bool UseEllipsis;
        /// <summary>Show a "More" affordance: the full text does not fit at a legible size.</summary>
        public bool ShowMore;
        public bool FitsFully;
        /// <summary>True when the surface minimum exceeded every ladder size and was used directly.</summary>
        public bool ClampedToMinimum;
    }

    /// <summary>
    /// Pure text fitter. Chooses a size from the type scale, never below the surface's legible minimum.
    /// Overflow becomes truncation plus a "More" flag, never an illegible shrink.
    /// Width estimates are averages for Inter; wrapping is approximated with a slop factor.
    /// </summary>
    public static class TextFit
    {
        public const float LineHeightEm = 1.25f;
        /// <summary>Fraction of each line actually usable after word-wrap raggedness.</summary>
        public const float WrapEfficiency = 0.92f;

        public static float AvgCharWidthEm(FontWeightKind w)
        {
            switch (w)
            {
                case FontWeightKind.Bold: return 0.58f;
                case FontWeightKind.Medium: return 0.55f;
                default: return 0.53f;
            }
        }

        public static int CharsPerLine(float widthMm, float sizeMm, FontWeightKind w)
        {
            if (widthMm <= 0f || sizeMm <= 0f) return 0;
            float perChar = sizeMm * AvgCharWidthEm(w);
            return (int)Math.Floor(widthMm * WrapEfficiency / perChar);
        }

        public static int MaxLinesFor(float heightMm, float sizeMm)
        {
            if (heightMm <= 0f || sizeMm <= 0f) return 0;
            return (int)Math.Floor(heightMm / (sizeMm * LineHeightEm) + 1e-4f);
        }

        public static int LinesNeeded(int textLength, int charsPerLine)
        {
            if (textLength <= 0) return 0;
            if (charsPerLine <= 0) return int.MaxValue;
            return (textLength + charsPerLine - 1) / charsPerLine;
        }

        /// <summary>
        /// Fit text of <paramref name="textLength"/> characters into the box.
        /// <paramref name="preferredMm"/> is the largest size to consider; <paramref name="viewingDistanceM"/>
        /// sets the legibility floor. <paramref name="maxLinesCap"/> (0 = none) limits lines, e.g. 1 for a title.
        /// </summary>
        public static TextFitResult Fit(float widthMm, float heightMm, int textLength, FontWeightKind weight,
                                        float preferredMm, float viewingDistanceM, int maxLinesCap = 0)
        {
            float floor = TypeScale.MinMmAt(viewingDistanceM);
            var r = new TextFitResult();
            if (textLength <= 0)
            {
                r.SizeMm = Math.Max(floor, Math.Min(preferredMm, TypeScale.TitleMm));
                r.FitsFully = true;
                return r;
            }

            // Candidates: ladder sizes within [floor, preferred].
            float best = -1f;
            var ladder = TypeScale.Ladder;
            for (int i = 0; i < ladder.Length; i++)
            {
                float s = ladder[i];
                if (s + 1e-4f < floor || s > preferredMm + 1e-4f) continue;
                if (FitsAt(widthMm, heightMm, textLength, weight, s, maxLinesCap)) { best = s; break; }
            }

            if (best > 0f)
            {
                int cpl = CharsPerLine(widthMm, best, weight);
                r.SizeMm = best;
                r.MaxLines = LimitLines(MaxLinesFor(heightMm, best), maxLinesCap);
                r.Lines = LinesNeeded(textLength, cpl);
                r.VisibleChars = textLength;
                r.FitsFully = true;
                return r;
            }

            // Does not fit at any legible size: use the smallest legible size at or below preferred, then truncate.
            float size = SmallestLegible(floor, preferredMm, out bool clamped);
            if (clamped && FitsAt(widthMm, heightMm, textLength, weight, size, maxLinesCap))
            {
                r.SizeMm = size;
                r.ClampedToMinimum = true;
                r.MaxLines = LimitLines(MaxLinesFor(heightMm, size), maxLinesCap);
                r.Lines = LinesNeeded(textLength, CharsPerLine(widthMm, size, weight));
                r.VisibleChars = textLength;
                r.FitsFully = true;
                return r;
            }
            int lines = LimitLines(MaxLinesFor(heightMm, size), maxLinesCap);
            int perLine = CharsPerLine(widthMm, size, weight);
            r.SizeMm = size;
            r.ClampedToMinimum = clamped;
            r.MaxLines = lines;
            r.Truncated = true;
            r.ShowMore = true;
            if (lines <= 0 || perLine <= 0)
            {
                r.Lines = 0; r.VisibleChars = 0; r.UseEllipsis = false;
                return r;
            }
            int capacity = lines * perLine;
            r.Lines = lines;
            r.UseEllipsis = true;
            r.VisibleChars = Math.Max(0, Math.Min(textLength - 1, capacity - 1));
            return r;
        }

        /// <summary>Truncate to the plan with an ellipsis; returns the input when no truncation is planned.</summary>
        public static string Apply(string text, TextFitResult plan)
        {
            if (text == null) return string.Empty;
            if (!plan.Truncated || plan.VisibleChars >= text.Length) return text;
            int n = Math.Max(0, Math.Min(plan.VisibleChars, text.Length));
            string head = text.Substring(0, n).TrimEnd();
            return plan.UseEllipsis ? head + "…" : head;
        }

        static bool FitsAt(float w, float h, int len, FontWeightKind weight, float size, int cap)
        {
            int lines = LimitLines(MaxLinesFor(h, size), cap);
            int cpl = CharsPerLine(w, size, weight);
            if (lines <= 0 || cpl <= 0) return false;
            return LinesNeeded(len, cpl) <= lines;
        }

        static int LimitLines(int lines, int cap) { return cap > 0 ? Math.Min(lines, cap) : lines; }

        static float SmallestLegible(float floor, float preferred, out bool clamped)
        {
            clamped = false;
            var ladder = TypeScale.Ladder;
            float pick = -1f;
            for (int i = 0; i < ladder.Length; i++)
            {
                float s = ladder[i];
                if (s + 1e-4f >= floor && s <= preferred + 1e-4f) pick = s; // ladder is descending: last wins = smallest
            }
            if (pick > 0f) return pick;
            clamped = true;
            return floor;
        }
    }
}
