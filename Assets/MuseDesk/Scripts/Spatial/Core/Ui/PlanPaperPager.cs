using System.Collections.Generic;

namespace MuseDesk.Spatial.Ui
{
    /// <summary>One page of the plan-review paper: rows with their top offsets (mm from the top of the text area).</summary>
    public sealed class PlanPage
    {
        public readonly List<PlanRow> Rows = new List<PlanRow>();
        public readonly List<float> TopMm = new List<float>();
        public float UsedMm;
    }

    /// <summary>
    /// Splits a full plan layout (<see cref="PlanReviewLayout.Layout"/> with no height limit) into pages that fit the
    /// paper, so "More" turns the page instead of hiding items. Pure.
    ///  - Nothing is dropped and nothing is shrunk: every row of the layout appears on exactly one page, in order.
    ///  - The provenance footer is not placed here: the paper draws it on EVERY page (it is the last row of the layout).
    ///  - A page breaks before a block (title, section head, item title) when it can, so an item's detail lines stay
    ///    with its title; a block taller than a whole page is split between rows.
    /// </summary>
    public static class PlanPaperPager
    {
        public static bool IsBlockStart(PlanRowKind k)
        {
            return k == PlanRowKind.Title || k == PlanRowKind.Section || k == PlanRowKind.ItemTitle || k == PlanRowKind.More;
        }

        /// <summary><paramref name="pageHeightMm"/> is the height available for rows above the footer.</summary>
        public static List<PlanPage> Paginate(PlanLayoutResult layout, float pageHeightMm)
        {
            var pages = new List<PlanPage>();
            if (layout == null) return pages;
            var blocks = new List<List<PlanRow>>();
            for (int i = 0; i < layout.Rows.Count; i++)
            {
                PlanRow r = layout.Rows[i];
                if (r.Kind == PlanRowKind.Provenance) continue;
                // A section head followed by its first item/text stays with it: "Work items (3)" is a block of its own
                // in the layout, so glue a lone section head to the block that follows.
                if (blocks.Count == 0 || IsBlockStart(r.Kind)) blocks.Add(new List<PlanRow>());
                blocks[blocks.Count - 1].Add(r);
            }
            for (int b = blocks.Count - 2; b >= 0; b--)
                if (blocks[b].Count == 1 && blocks[b][0].Kind == PlanRowKind.Section) { blocks[b].AddRange(blocks[b + 1]); blocks.RemoveAt(b + 1); }

            var page = new PlanPage();
            for (int b = 0; b < blocks.Count; b++)
            {
                float h = Height(blocks[b]);
                float gap = page.Rows.Count > 0 ? PlanReviewLayout.BlockGapMm : 0f;
                if (page.Rows.Count > 0 && pageHeightMm > 0f && page.UsedMm + gap + h > pageHeightMm + 1e-3f)
                {
                    pages.Add(page);
                    page = new PlanPage();
                    gap = 0f;
                }
                page.UsedMm += gap;
                for (int i = 0; i < blocks[b].Count; i++)
                {
                    PlanRow r = blocks[b][i];
                    if (page.Rows.Count > 0 && pageHeightMm > 0f && page.UsedMm + r.HeightMm > pageHeightMm + 1e-3f)
                    {
                        pages.Add(page);                 // a block taller than the page: split between rows
                        page = new PlanPage();
                    }
                    page.Rows.Add(r);
                    page.TopMm.Add(page.UsedMm);
                    page.UsedMm += r.HeightMm;
                }
            }
            if (page.Rows.Count > 0 || pages.Count == 0) pages.Add(page);
            return pages;
        }

        /// <summary>
        /// One row sized by the same rules as the plan layout (type scale, legibility floor for the distance, truncation
        /// with an ellipsis instead of shrinking). For pages that are not a plan, such as the plan status page.
        /// </summary>
        public static PlanRow Row(PlanRowKind kind, string text, float widthMm, float preferredMm, FontWeightKind weight,
                                  PlanTone tone, int maxLines, float viewingDistanceM)
        {
            text = text ?? "";
            float floor = TypeScale.MinMmAt(viewingDistanceM);
            float boxH = maxLines * System.Math.Max(preferredMm, floor) * TextFit.LineHeightEm + 0.01f;
            TextFitResult fit = TextFit.Fit(widthMm, boxH, text.Length, weight, preferredMm, viewingDistanceM, maxLines);
            var row = new PlanRow
            {
                Kind = kind, FullText = text, SizeMm = fit.SizeMm, Bold = weight == FontWeightKind.Bold, Tone = tone,
                Text = TextFit.Apply(text, fit), ShowMore = fit.ShowMore,
            };
            int lines = fit.Truncated ? fit.Lines : System.Math.Max(1, fit.Lines);
            row.Lines = System.Math.Min(lines, maxLines);
            row.HeightMm = row.Lines * row.SizeMm * TextFit.LineHeightEm + PlanReviewLayout.RowGapMm;
            return row;
        }

        static float Height(List<PlanRow> rows)
        {
            float h = 0f;
            for (int i = 0; i < rows.Count; i++) h += rows[i].HeightMm;
            return h;
        }
    }
}
