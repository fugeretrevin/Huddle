using System.Text;
using UnityEngine;
using TMPro;
using MuseDesk.Models;

namespace MuseDesk.Documents
{
    /// <summary>
    /// The readable decision document. Binds a DecisionContent to dark-on-paper text with a clear
    /// hierarchy: a mode band (SCRIPTED DEMO or LIVE, always in words, never colour alone), the one
    /// question, a short summary, the recommendation and where it came from, and, only when the user
    /// asks for it, the longer explanation on a second sheet.
    ///
    /// It owns which part of the lower sheet is showing:
    ///   Ready    the two primary keys (Approve / Reject), the quiet secondary chips and the stamp area
    ///   Pending  the keys are replaced by flat, inert plates and the status line reads "Sending..."
    ///   Resolved the confirmed outcome in words on a coloured plate
    /// and never claims an outcome before the backend confirms it: SetResolved is driven by
    /// decision.resolved. A failed response (SetError) returns to Ready so the keys work again.
    ///
    /// HeldPaperController tells it when the sheet is open and when the keys may be used
    /// (SetSheetState), so nothing on the lower sheet can appear on the small carried paper.
    /// </summary>
    public class DecisionPanel : MonoBehaviour
    {
        [Header("Text fields")]
        public TMP_Text badge;        // mode band, left: "SCRIPTED DEMO" or "LIVE"
        public TMP_Text title;
        public TMP_Text summary;
        public TMP_Text recommendation;
        [Tooltip("Where the recommendation came from, e.g. \"Template recommendation. Not live AI research.\"")]
        public TMP_Text provenance;
        public TMP_Text body;         // rationale + alternatives + risk + impact (Details)
        public TMP_Text status;       // Sending... / error / voice status (one line above the keys)
        public TMP_Text answer;       // Ask-why response (Details)

        [Header("Paper V2 text (all optional)")]
        [Tooltip("Mode band, right: \"Needs your decision\" or \"Revised proposal 2. Needs your decision\". " +
                 "If empty, the words are appended to the badge.")]
        public TMP_Text needsLabel;
        [Tooltip("The confirmed outcome in one or two words (Approved / Rejected / Changes requested).")]
        public TMP_Text outcomeTitle;
        [Tooltip("One short sentence under the outcome.")]
        public TMP_Text outcomeDetail;
        [Tooltip("Label of the Details chip; reads \"Close\" while the details sheet is open.")]
        public TMP_Text detailsChipLabel;
        [Tooltip("Mode in words on the small carried paper's cover.")]
        public TMP_Text coverMode;

        [Header("Provenance band")]
        [Tooltip("Strip behind the badge text, tinted by mode. The text carries the meaning; the colour only reinforces it.")]
        public Renderer modeBand;
        public Color demoBand = new Color(0.86f, 0.55f, 0.16f);
        public Color liveBand = new Color(0.13f, 0.50f, 0.44f);
        [Tooltip("Header band on the small carried paper's cover, tinted like the mode band.")]
        public Renderer coverBand;
        [Tooltip("Text colour on the band in the scripted demo / in live mode.")]
        public Color demoBandText = Color.white;
        public Color liveBandText = Color.white;

        [Header("Action controls (wired by builder)")]
        public GameObject actionsRoot;   // the keys; hidden once a response is pending
        [Tooltip("The longer explanation, shown when the user presses Details.")]
        public GameObject detailsRoot;
        [Tooltip("The physical stamp target. Hidden once a response is pending so a second stamp cannot be attempted.")]
        public GameObject stampZoneRoot;
        [Tooltip("Flat inert plates shown in place of the keys while the response is in flight.")]
        public GameObject pendingRoot;
        [Tooltip("The confirmed outcome (plate + words), shown in place of the keys after decision.resolved.")]
        public GameObject outcomeRoot;
        [Tooltip("Plate behind the outcome words, tinted by outcome.")]
        public Renderer outcomePlate;
        [Tooltip("The Voice chip. Shown only while voice can actually be used; hidden otherwise (never shown disabled).")]
        public GameObject voiceChip;
        [Tooltip("The voice layer whose availability decides whether the Voice chip is shown.")]
        public MuseDesk.Voice.VoiceDecisionController voice;

        public bool IsExpanded { get; private set; }

        /// <summary>True while a response is in flight (keys replaced by inert plates).</summary>
        public bool IsPending => _phase == Phase.Pending;
        /// <summary>True once the backend has confirmed an outcome for the bound decision.</summary>
        public bool IsResolved => _phase == Phase.Resolved;
        /// <summary>Revision of the bound proposal: 0 for the first proposal, 1.. for a revised one.</summary>
        public int Revision { get; private set; }

        enum Phase { Ready, Pending, Resolved }

        // Text colours on paper are the darker shades of the state colours, for contrast on cream.
        static readonly Color Ink = new Color32(0x1F, 0x25, 0x30, 255);
        static readonly Color OnPlate = new Color32(0xF3, 0xF4, 0xF6, 255);
        static readonly Color Good = new Color(0.10f, 0.42f, 0.24f);
        static readonly Color Bad = new Color(0.62f, 0.16f, 0.13f);
        static readonly Color Warn = new Color(0.60f, 0.36f, 0.05f);
        // Plate colours (design tokens).
        static readonly Color SuccessPlate = new Color32(0x2E, 0x9B, 0x5F, 255);
        static readonly Color CoralPlate = new Color32(0xD9, 0x53, 0x4A, 255);
        static readonly Color AmberPlate = new Color32(0xE0, 0xA0, 0x30, 255);
        static readonly Color NeutralPlate = new Color32(0x2A, 0x2F, 0x3A, 255);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        MaterialPropertyBlock _block;
        Phase _phase = Phase.Ready;
        // Until a HeldPaperController says otherwise the sheet counts as open and usable, so a paper
        // without a hand-over still works.
        bool _sheetOpen = true;
        bool _keysArmed = true;

        public void Bind(DecisionContent c, string sourceMode, string provenanceLabel = null)
        {
            Bind(c, sourceMode, provenanceLabel, 0);
        }

        /// <summary>Bind a decision. <paramref name="revision"/> is 0 for the first proposal and
        /// 1.. for a revised proposal (shown as "Revised proposal N").</summary>
        public void Bind(DecisionContent c, string sourceMode, string provenanceLabel, int revision)
        {
            if (c == null) return;
            Revision = revision < 0 ? 0 : revision;
            bool live = string.Equals(sourceMode ?? c.SourceMode, SourceModes.Live, System.StringComparison.OrdinalIgnoreCase);
            string mode = live ? "LIVE" : "SCRIPTED DEMO";
            string needs = Revision > 0 ? "Revised proposal " + Revision + ". Needs your decision" : "Needs your decision";
            Color bandText = live ? liveBandText : demoBandText;
            if (badge)
            {
                badge.text = needsLabel ? mode : mode + "    " + needs;
                badge.color = bandText;
            }
            if (needsLabel) { needsLabel.text = needs; needsLabel.color = bandText; }
            if (coverMode) { coverMode.text = mode; coverMode.color = bandText; }
            Tint(modeBand, live ? liveBand : demoBand);
            Tint(coverBand, live ? liveBand : demoBand);

            if (title) title.text = Ascii(c.Title ?? "Approval requested");
            if (summary) summary.text = Ascii(c.Summary ?? "");
            if (recommendation)
            {
                string reco = (c.Recommendation ?? "").Trim();
                recommendation.text = reco.Length == 0 ? "" : "Recommended: " + Ascii(Pretty(reco));
                recommendation.color = reco.StartsWith("reject", System.StringComparison.OrdinalIgnoreCase) ? Bad : Good;
            }
            if (provenance)
            {
                // Say plainly where the recommendation came from. In the demo everything is scripted.
                provenance.text = !string.IsNullOrEmpty(provenanceLabel) ? Ascii(provenanceLabel)
                    : live ? "" : "Scripted sample. Nothing is running on a computer.";
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(c.Rationale)) sb.Append("<b>Why</b>  ").Append(c.Rationale).Append('\n');
            if (c.Alternatives != null && c.Alternatives.Count > 0)
            {
                sb.Append("<b>Options</b>  ");
                for (int i = 0; i < c.Alternatives.Count; i++)
                {
                    var a = c.Alternatives[i];
                    if (a == null) continue;
                    if (i > 0) sb.Append("; ");
                    sb.Append(a.Label).Append(" (").Append(a.Tradeoff).Append(')');
                }
                sb.Append('\n');
            }
            if (!string.IsNullOrEmpty(c.RiskLevel)) sb.Append("<b>Risk</b>  ").Append(c.RiskLevel).Append('\n');
            if (!string.IsNullOrEmpty(c.Impact)) sb.Append("<b>If you wait</b>  ").Append(c.Impact);
            if (body) body.text = sb.Length > 0 ? Ascii(sb.ToString()) : "No further detail was provided.";

            SetStatus("", Ink);
            if (answer) answer.text = "";
            if (outcomeTitle) outcomeTitle.text = "";
            if (outcomeDetail) outcomeDetail.text = "";
            _phase = Phase.Ready;
            RefreshVoiceChip();
            Collapse();   // also applies the visibility of every part
        }

        /// <summary>Forget the bound decision's state (session reset / replay). The next Bind fills it again.</summary>
        public void Clear()
        {
            _phase = Phase.Ready;
            Revision = 0;
            SetStatus("", Ink);
            if (answer) answer.text = "";
            if (outcomeTitle) outcomeTitle.text = "";
            if (outcomeDetail) outcomeDetail.text = "";
            Collapse();
        }

        /// <summary>
        /// Called by the paper: <paramref name="contentVisible"/> is false while the agent carries the
        /// small sheet, true once the user has taken it; <paramref name="keysArmed"/> becomes true only
        /// when the sheet has finished unfolding, and only then are the keys shown.
        /// </summary>
        public void SetSheetState(bool contentVisible, bool keysArmed)
        {
            _sheetOpen = contentVisible;
            _keysArmed = contentVisible && keysArmed;
            if (_keysArmed) RefreshVoiceChip();
            Apply();
        }

        static string Pretty(string action) => action.ToLowerInvariant() switch
        {
            "approve" => "Approve",
            "reject" => "Reject",
            "request_changes" => "Request changes",
            _ => char.ToUpperInvariant(action[0]) + action.Substring(1).Replace('_', ' '),
        };

        // The type system allows ASCII letters, digits and basic punctuation only: typographic
        // quotes, dashes and the ellipsis character are replaced by their plain forms.
        static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            bool plain = true;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '…' || ch == '“' || ch == '”' || ch == '‘' || ch == '’' || ch == '–' || ch == '—')
                { plain = false; break; }
            }
            if (plain) return s;
            return s.Replace("…", "...").Replace('“', '"').Replace('”', '"')
                    .Replace('‘', '\'').Replace('’', '\'').Replace('–', '-').Replace('—', '-');
        }

        void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, c);
            _block.SetColor(ColorId, c);
            r.SetPropertyBlock(_block);
        }

        void SetStatus(string text, Color color)
        {
            if (!status) return;
            status.text = text;
            status.color = color;
        }

        static void Show(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        // One place decides what is visible, from the sheet state and the response phase.
        void Apply()
        {
            bool ready = _phase == Phase.Ready;
            Show(actionsRoot, _sheetOpen && _keysArmed && ready);
            Show(stampZoneRoot, _sheetOpen && ready);
            Show(pendingRoot, _sheetOpen && _phase == Phase.Pending);
            Show(outcomeRoot, _sheetOpen && _phase == Phase.Resolved);
            Show(detailsRoot, _sheetOpen && IsExpanded);
        }

        // The Voice chip is offered only when the voice layer could act on what it hears (the same
        // conditions VoiceDecisionController checks, apart from "a decision is pending").
        void RefreshVoiceChip()
        {
            if (voiceChip == null) return;
            Show(voiceChip, VoiceUsable());
        }

        bool VoiceUsable()
        {
            if (voice == null || !voice.isActiveAndEnabled || !voice.voiceEnabled) return false;
            var source = voice.sourceBehaviour as MuseDesk.Voice.IVoiceCommandSource;
            if (source == null || !source.IsAvailable) return false;
            if (voice.requireLiveMode && (voice.controller == null || !voice.controller.IsLive)) return false;
            return true;
        }

        public void Expand()
        {
            IsExpanded = true;
            if (detailsChipLabel) detailsChipLabel.text = "Close";
            Apply();
        }

        public void Collapse()
        {
            IsExpanded = false;
            if (detailsChipLabel) detailsChipLabel.text = "Details";
            Apply();
        }

        public void ToggleExpand() { if (IsExpanded) Collapse(); else Expand(); }

        public void SetResponding()
        {
            _phase = Phase.Pending;
            SetStatus("Sending...", Ink);
            Apply();
        }

        public void SetResolved(string state)
        {
            string s = (state ?? "").ToLowerInvariant();
            string head, detail; Color plate, onPlate, onPaper;
            switch (s)
            {
                case "approved":
                    head = "Approved"; detail = "The developer is continuing.";
                    plate = SuccessPlate; onPlate = OnPlate; onPaper = Good; break;
                case "rejected":
                    head = "Rejected"; detail = "Nothing was changed.";
                    plate = CoralPlate; onPlate = OnPlate; onPaper = Bad; break;
                case "changes_requested":
                    head = "Changes requested"; detail = "The recommendation goes back for revision.";
                    plate = AmberPlate; onPlate = Ink; onPaper = Warn; break;
                case "expired":
                    head = "Expired"; detail = "The desktop restarted. Nothing was run.";
                    plate = AmberPlate; onPlate = Ink; onPaper = Warn; break;
                default:
                    head = "Resolved"; detail = Ascii(state ?? "");
                    plate = NeutralPlate; onPlate = OnPlate; onPaper = Ink; break;
            }
            _phase = Phase.Resolved;
            if (outcomeRoot != null && outcomeTitle != null)
            {
                // The outcome has its own plate where the keys were; the status line stays clear.
                outcomeTitle.text = head; outcomeTitle.color = onPlate;
                if (outcomeDetail) { outcomeDetail.text = detail; outcomeDetail.color = onPlate; }
                Tint(outcomePlate, plate);
                SetStatus("", Ink);
            }
            else
            {
                SetStatus(detail.Length > 0 ? head + ". " + detail : head + ".", onPaper);
            }
            Apply();
        }

        public void SetError(string message)
        {
            _phase = Phase.Ready;      // let the user retry: the keys and the stamp area come back
            SetStatus(Ascii(message ?? "Couldn't reach the desktop. Try again."), Bad);
            Apply();
        }

        public void SetAnswer(string text)
        {
            if (answer) answer.text = string.IsNullOrEmpty(text) ? "" : "\"" + Ascii(text) + "\"";
        }

        public void SetVoiceStatus(string text)
        {
            if (!status) return;
            // Voice status shares the status line; it never overwrites a confirmed outcome's plate.
            if (string.IsNullOrEmpty(text)) { status.text = ""; status.color = Ink; return; }
            status.text = Ascii(text);
            status.color = text.StartsWith("Voice error") || text.Contains("permission") ? Warn : Ink;
        }
    }
}
