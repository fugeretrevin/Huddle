using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Ui
{
    public enum PlanState { Proposed = 0, Committed = 1 }

    public enum PlanRowKind { Title, State, Section, Text, ItemTitle, ItemDetail, ItemOutput, Risk, More, Provenance }

    /// <summary>Colour roles, mirroring OfficeTokens (Core cannot reference the Workspace assembly).</summary>
    public enum PlanTone { Ink, InkSoft, Teal, Amber, Success, Coral, Lavender, InfoBlue, Neutral }

    public static class PlanPalette
    {
        public static Color32 Of(PlanTone t)
        {
            switch (t)
            {
                case PlanTone.InkSoft: return new Color32(0x4B, 0x55, 0x63, 0xFF);
                case PlanTone.Teal: return new Color32(0x2F, 0xA3, 0x9A, 0xFF);
                case PlanTone.Amber: return new Color32(0xE0, 0xA0, 0x30, 0xFF);
                case PlanTone.Success: return new Color32(0x2E, 0x9B, 0x5F, 0xFF);
                case PlanTone.Coral: return new Color32(0xD9, 0x53, 0x4A, 0xFF);
                case PlanTone.Lavender: return new Color32(0x8C, 0x7B, 0xD6, 0xFF);
                case PlanTone.InfoBlue: return new Color32(0x4C, 0x85, 0xDB, 0xFF);
                case PlanTone.Neutral: return new Color32(0x9A, 0xA0, 0xA8, 0xFF);
                default: return new Color32(0x1F, 0x25, 0x30, 0xFF);
            }
        }
    }

    /// <summary>One work item as the backend sends it. Missing fields stay null/empty.</summary>
    public sealed class PlanItemModel
    {
        public string Id;                    // client_item_id
        public string Title;
        public string Kind;                  // research, code, ...
        public string ProposedAgent;         // researcher, developer, tester
        public List<string> DependsOn;       // ids of earlier items
        public List<string> ExpectedOutputs; // acceptance_criteria
    }

    /// <summary>The plan as the backend sends it (manager.py public plan). Missing fields stay null/empty.</summary>
    public sealed class PlanModel
    {
        public string Title;
        public string UserRequest;           // what the user asked
        public string Objective;
        public List<PlanItemModel> Items;
        public List<string> Assumptions;
        public List<string> Risks;
        public PlanState State;
        public string Source;                // provenance field: SCRIPTED_DEMO, LIVE_MODEL, TEMPLATE_PLAN
    }

    public struct PlanRow
    {
        public PlanRowKind Kind;
        public string Label;       // small caption label, may be null
        public string Text;        // text to display (already truncated if needed)
        public string FullText;    // untruncated text, for a "More" panel
        public float SizeMm;
        public bool Bold;
        public PlanTone Tone;
        public int Lines;
        public float HeightMm;
        public bool ShowMore;
    }

    public sealed class PlanLayoutResult
    {
        public readonly List<PlanRow> Rows = new List<PlanRow>();
        public float TotalHeightMm;
        public int DroppedItems;
        public int DroppedBlocks;
        public bool Overflowed;
        public string ProvenanceText;
    }

    /// <summary>
    /// Pure mapping from a plan model to ordered rows with sizes and token colours. Only real fields are
    /// shown; absent ones are omitted. Proposed is never drawn in the running/teal colour. Provenance comes
    /// only from the plan's source field and is never defaulted to live.
    /// </summary>
    public static class PlanReviewLayout
    {
        public const float RowGapMm = 1.6f;
        public const float BlockGapMm = 2.4f;

        public static string ProvenanceLabel(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "Source not stated";
            switch (source.Trim().ToUpperInvariant())
            {
                case "SCRIPTED_DEMO": return "[SCRIPTED DEMO]";
                case "LIVE_MODEL": return "Model-written";
                case "TEMPLATE_PLAN": return "Template plan (not model-written)";
                default: return "Source: " + source.Trim();
            }
        }

        public static string StateLabel(PlanState s)
        {
            return s == PlanState.Committed ? "COMMITTED - tasks created" : "PROPOSED - nothing has started";
        }

        /// <summary>Colour for the state chip. Proposed is Amber; Committed is InfoBlue. Neither is Teal (running).</summary>
        public static PlanTone StateTone(PlanState s)
        {
            return s == PlanState.Committed ? PlanTone.InfoBlue : PlanTone.Amber;
        }

        public static string AgentLabel(string agent)
        {
            if (string.IsNullOrWhiteSpace(agent)) return null;
            string a = agent.Trim();
            return char.ToUpperInvariant(a[0]) + a.Substring(1);
        }

        /// <summary>
        /// Lay the plan out. <paramref name="widthMm"/> is the text column width, <paramref name="maxHeightMm"/>
        /// the available height (0 = unlimited), <paramref name="viewingDistanceM"/> the surface's viewing distance.
        /// </summary>
        public static PlanLayoutResult Layout(PlanModel plan, float widthMm, float maxHeightMm, float viewingDistanceM)
        {
            var res = new PlanLayoutResult();
            if (plan == null) plan = new PlanModel();
            res.ProvenanceText = ProvenanceLabel(plan.Source);

            // The provenance footer is always kept; reserve its height first.
            PlanRow foot = MakeRow(PlanRowKind.Provenance, null, res.ProvenanceText, widthMm, TypeScale.ProvenanceFloorMm,
                FontWeightKind.Medium, false, PlanTone.InkSoft, 1, viewingDistanceM);

            var blocks = new List<List<PlanRow>>();
            var itemsInBlock = new List<int>();

            var head = new List<PlanRow>();
            if (!string.IsNullOrWhiteSpace(plan.Title))
                head.Add(MakeRow(PlanRowKind.Title, null, plan.Title.Trim(), widthMm, TypeScale.TitleMm, FontWeightKind.Bold, true, PlanTone.Ink, 2, viewingDistanceM));
            head.Add(MakeRow(PlanRowKind.State, null, StateLabel(plan.State), widthMm, TypeScale.CaptionMm, FontWeightKind.Bold, true, StateTone(plan.State), 1, viewingDistanceM));
            blocks.Add(head); itemsInBlock.Add(0);

            if (!string.IsNullOrWhiteSpace(plan.UserRequest))
            {
                blocks.Add(TextBlock("You asked", plan.UserRequest, widthMm, viewingDistanceM, 3)); itemsInBlock.Add(0);
            }
            if (!string.IsNullOrWhiteSpace(plan.Objective) && !SameText(plan.Objective, plan.UserRequest))
            {
                blocks.Add(TextBlock("Objective", plan.Objective, widthMm, viewingDistanceM, 3)); itemsInBlock.Add(0);
            }

            if (plan.Items != null && plan.Items.Count > 0)
            {
                var sec = new List<PlanRow>
                {
                    MakeRow(PlanRowKind.Section, null, "Work items (" + plan.Items.Count + ")", widthMm, TypeScale.HeadingMm, FontWeightKind.Bold, true, PlanTone.Ink, 1, viewingDistanceM)
                };
                blocks.Add(sec); itemsInBlock.Add(0);
                for (int i = 0; i < plan.Items.Count; i++)
                {
                    blocks.Add(ItemBlock(plan, i, widthMm, viewingDistanceM)); itemsInBlock.Add(1);
                }
            }

            if (plan.Assumptions != null && HasAny(plan.Assumptions))
            {
                blocks.Add(ListBlock("Assumptions", plan.Assumptions, PlanRowKind.Text, PlanTone.InkSoft, widthMm, viewingDistanceM)); itemsInBlock.Add(0);
            }
            if (plan.Risks != null && HasAny(plan.Risks))
            {
                blocks.Add(ListBlock("Risks", plan.Risks, PlanRowKind.Risk, PlanTone.Coral, widthMm, viewingDistanceM)); itemsInBlock.Add(0);
            }

            float used = foot.HeightMm + BlockGapMm;
            float moreReserve = TypeScale.CaptionMm * TextFit.LineHeightEm + BlockGapMm;
            int kept = 0;
            for (int b = 0; b < blocks.Count; b++)
            {
                float h = BlockHeight(blocks[b]);
                float reserve = b == blocks.Count - 1 ? 0f : moreReserve;
                if (maxHeightMm > 0f && b > 0 && used + h + reserve > maxHeightMm + 1e-3f) break;
                used += h;
                kept++;
            }

            for (int b = 0; b < kept; b++) res.Rows.AddRange(blocks[b]);
            if (kept < blocks.Count)
            {
                res.Overflowed = true;
                for (int b = kept; b < blocks.Count; b++) { res.DroppedBlocks++; res.DroppedItems += itemsInBlock[b]; }
                string msg = res.DroppedItems > 0
                    ? "+" + res.DroppedItems + " more work item" + (res.DroppedItems == 1 ? "" : "s") + " (tap More)"
                    : "+" + res.DroppedBlocks + " more section" + (res.DroppedBlocks == 1 ? "" : "s") + " (tap More)";
                var more = MakeRow(PlanRowKind.More, null, msg, widthMm, TypeScale.CaptionMm, FontWeightKind.Bold, true, PlanTone.InfoBlue, 1, viewingDistanceM);
                more.ShowMore = true;
                res.Rows.Add(more);
                used += more.HeightMm + BlockGapMm;
            }
            res.Rows.Add(foot);
            res.TotalHeightMm = used;
            return res;
        }

        // ---- blocks ------------------------------------------------------------------------------------------

        static List<PlanRow> TextBlock(string label, string text, float w, float dist, int maxLines)
        {
            var l = new List<PlanRow>();
            l.Add(MakeRow(PlanRowKind.Section, null, label, w, TypeScale.CaptionMm, FontWeightKind.Bold, true, PlanTone.InkSoft, 1, dist));
            l.Add(MakeRow(PlanRowKind.Text, null, text.Trim(), w, TypeScale.BodyMm, FontWeightKind.Medium, false, PlanTone.Ink, maxLines, dist));
            return l;
        }

        static List<PlanRow> ListBlock(string label, List<string> entries, PlanRowKind kind, PlanTone tone, float w, float dist)
        {
            var l = new List<PlanRow>();
            l.Add(MakeRow(PlanRowKind.Section, null, label, w, TypeScale.CaptionMm, FontWeightKind.Bold, true, tone == PlanTone.Coral ? PlanTone.Coral : PlanTone.InkSoft, 1, dist));
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(entries[i])) continue;
                l.Add(MakeRow(kind, null, "- " + entries[i].Trim(), w, TypeScale.CaptionMm, FontWeightKind.Medium, false, PlanTone.Ink, 2, dist));
            }
            return l;
        }

        static List<PlanRow> ItemBlock(PlanModel plan, int index, float w, float dist)
        {
            var it = plan.Items[index] ?? new PlanItemModel();
            var l = new List<PlanRow>();
            string title = string.IsNullOrWhiteSpace(it.Title) ? "Untitled item" : it.Title.Trim();
            l.Add(MakeRow(PlanRowKind.ItemTitle, null, (index + 1) + ". " + title, w, TypeScale.BodyMm, FontWeightKind.Bold, true, PlanTone.Ink, 2, dist));

            string detail = ItemDetail(plan, it);
            if (detail != null)
                l.Add(MakeRow(PlanRowKind.ItemDetail, null, detail, w, TypeScale.CaptionMm, FontWeightKind.Medium, false, PlanTone.InkSoft, 2, dist));

            if (it.ExpectedOutputs != null)
                for (int i = 0; i < it.ExpectedOutputs.Count; i++)
                {
                    string o = it.ExpectedOutputs[i];
                    if (string.IsNullOrWhiteSpace(o)) continue;
                    l.Add(MakeRow(PlanRowKind.ItemOutput, null, "Expect: " + o.Trim(), w, TypeScale.CaptionMm, FontWeightKind.Medium, false, PlanTone.InkSoft, 2, dist));
                }
            return l;
        }

        /// <summary>"Researcher - after 1, 2" built from real fields only; null when none are present.</summary>
        public static string ItemDetail(PlanModel plan, PlanItemModel it)
        {
            string agent = AgentLabel(it.ProposedAgent);
            string deps = null;
            if (it.DependsOn != null && it.DependsOn.Count > 0)
            {
                var parts = new List<string>();
                for (int i = 0; i < it.DependsOn.Count; i++)
                {
                    string id = it.DependsOn[i];
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    int ord = OrdinalOf(plan, id);
                    parts.Add(ord > 0 ? ord.ToString() : id.Trim());
                }
                if (parts.Count > 0) deps = "after " + string.Join(", ", parts.ToArray());
            }
            if (agent == null && deps == null) return null;
            if (agent == null) return deps;
            if (deps == null) return agent;
            return agent + " - " + deps;
        }

        static int OrdinalOf(PlanModel plan, string id)
        {
            if (plan.Items == null) return 0;
            for (int i = 0; i < plan.Items.Count; i++)
                if (plan.Items[i] != null && plan.Items[i].Id == id) return i + 1;
            return 0;
        }

        // ---- row sizing --------------------------------------------------------------------------------------

        static PlanRow MakeRow(PlanRowKind kind, string label, string text, float width, float preferredMm,
                               FontWeightKind weight, bool bold, PlanTone tone, int maxLines, float dist)
        {
            // Wide boxes: height is whatever maxLines needs, so fit is driven by line count and width.
            float floor = TypeScale.MinMmAt(dist);
            float boxH = maxLines * Math.Max(preferredMm, floor) * TextFit.LineHeightEm + 0.01f;
            TextFitResult fit = TextFit.Fit(width, boxH, text.Length, weight, preferredMm, dist, maxLines);
            var row = new PlanRow
            {
                Kind = kind, Label = label, FullText = text, SizeMm = fit.SizeMm, Bold = bold, Tone = tone,
                Text = TextFit.Apply(text, fit), ShowMore = fit.ShowMore
            };
            int lines = fit.Truncated ? fit.Lines : Math.Max(1, fit.Lines);
            row.Lines = Math.Min(lines, maxLines);
            row.HeightMm = row.Lines * row.SizeMm * TextFit.LineHeightEm + RowGapMm;
            return row;
        }

        static float BlockHeight(List<PlanRow> block)
        {
            float h = BlockGapMm;
            for (int i = 0; i < block.Count; i++) h += block[i].HeightMm;
            return h;
        }

        static bool HasAny(List<string> l)
        {
            for (int i = 0; i < l.Count; i++) if (!string.IsNullOrWhiteSpace(l[i])) return true;
            return false;
        }

        static bool SameText(string a, string b)
        {
            return b != null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
