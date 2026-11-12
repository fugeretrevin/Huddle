using System.Linq;
using System.Text;
using UnityEngine;
using TMPro;
using MuseDesk.Models;

namespace MuseDesk.Documents
{
    /// <summary>
    /// The readable decision document. Binds a DecisionContent to dark-on-paper text with a clear
    /// hierarchy: a provenance band (SCRIPTED DEMO or LIVE, always in words, never colour alone), the
    /// question as a title, a one-line summary, the recommendation and where it came from, and — only
    /// when the user asks for it — the longer explanation.
    ///
    /// It reflects the response lifecycle (responding / resolved / error) and never claims an outcome
    /// before the backend confirms it: SetResolved is driven by decision.resolved.
    /// </summary>
    public class DecisionPanel : MonoBehaviour
    {
        [Header("Text fields")]
        public TMP_Text badge;        // provenance band: "SCRIPTED DEMO   needs your decision"
        public TMP_Text title;
        public TMP_Text summary;
        public TMP_Text recommendation;
        [Tooltip("Where the recommendation came from, e.g. \"Template recommendation. Not live AI research.\"")]
        public TMP_Text provenance;
        public TMP_Text body;         // rationale + alternatives + risk + impact (Details)
        public TMP_Text status;       // Saving… / Approved / Rejected / error
        public TMP_Text answer;       // Ask-why response (Details)

        [Header("Provenance band")]
        [Tooltip("Strip behind the badge text, tinted by mode. The text carries the meaning; the colour only reinforces it.")]
        public Renderer modeBand;
        public Color demoBand = new Color(0.86f, 0.55f, 0.16f);
        public Color liveBand = new Color(0.13f, 0.50f, 0.44f);

        [Header("Action controls (wired by builder)")]
        public GameObject actionsRoot;   // hidden once a response is pending
        [Tooltip("The longer explanation, shown when the user presses Details.")]
        public GameObject detailsRoot;
        [Tooltip("The physical stamp target. Hidden once a response is pending so a second stamp cannot be attempted.")]
        public GameObject stampZoneRoot;

        public bool IsExpanded { get; private set; }

        static readonly Color Ink = new Color(0.11f, 0.12f, 0.15f);
        static readonly Color Good = new Color(0.10f, 0.42f, 0.24f);
        static readonly Color Bad = new Color(0.62f, 0.16f, 0.13f);
        static readonly Color Warn = new Color(0.60f, 0.36f, 0.05f);

        MaterialPropertyBlock _block;

        public void Bind(DecisionContent c, string sourceMode, string provenanceLabel = null)
        {
            if (c == null) return;
            bool live = string.Equals(sourceMode ?? c.SourceMode, SourceModes.Live, System.StringComparison.OrdinalIgnoreCase);
            if (badge) badge.text = (live ? "LIVE" : "SCRIPTED DEMO") + "    needs your decision";
            TintBand(live ? liveBand : demoBand);

            if (title) title.text = c.Title ?? "Approval requested";
            if (summary) summary.text = c.Summary ?? "";
            if (recommendation)
            {
                string reco = (c.Recommendation ?? "").Trim();
                recommendation.text = reco.Length == 0 ? "" : "Recommended: " + Pretty(reco);
                recommendation.color = reco.StartsWith("reject", System.StringComparison.OrdinalIgnoreCase) ? Bad : Good;
            }
            if (provenance)
            {
                // Say plainly where the recommendation came from. In the demo everything is scripted.
                provenance.text = !string.IsNullOrEmpty(provenanceLabel) ? provenanceLabel
                    : live ? "" : "Scripted sample. Nothing is running on a computer.";
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(c.Rationale)) sb.Append("<b>Why</b>  ").Append(c.Rationale).Append('\n');
            if (c.Alternatives != null && c.Alternatives.Count > 0)
                sb.Append("<b>Options</b>  ")
                  .Append(string.Join("; ", c.Alternatives.Select(a => $"{a.Label} ({a.Tradeoff})")))
                  .Append('\n');
            if (!string.IsNullOrEmpty(c.RiskLevel)) sb.Append("<b>Risk</b>  ").Append(c.RiskLevel).Append('\n');
            if (!string.IsNullOrEmpty(c.Impact)) sb.Append("<b>If you wait</b>  ").Append(c.Impact);
            if (body) body.text = sb.Length > 0 ? sb.ToString() : "No further detail was provided.";

            SetStatus("", Ink);
            if (answer) answer.text = "";
            SetActionsVisible(true);
            Collapse();
        }

        static string Pretty(string action) => action.ToLowerInvariant() switch
        {
            "approve" => "Approve",
            "reject" => "Reject",
            "request_changes" => "Request changes",
            _ => char.ToUpperInvariant(action[0]) + action.Substring(1).Replace('_', ' '),
        };

        void TintBand(Color c)
        {
            if (modeBand == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            modeBand.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", c);
            _block.SetColor("_Color", c);
            modeBand.SetPropertyBlock(_block);
        }

        void SetStatus(string text, Color color)
        {
            if (!status) return;
            status.text = text;
            status.color = color;
        }

        public void Expand()
        {
            IsExpanded = true;
            if (detailsRoot) detailsRoot.SetActive(true);
        }

        public void Collapse()
        {
            IsExpanded = false;
            if (detailsRoot) detailsRoot.SetActive(false);
        }

        public void ToggleExpand() { if (IsExpanded) Collapse(); else Expand(); }

        public void SetResponding()
        {
            SetStatus("Saving your decision…", Ink);
            SetActionsVisible(false);
        }

        public void SetResolved(string state)
        {
            string s = (state ?? "").ToLowerInvariant();
            switch (s)
            {
                case "approved": SetStatus("Approved. The developer is continuing.", Good); break;
                case "rejected": SetStatus("Rejected. Nothing was changed.", Bad); break;
                case "changes_requested": SetStatus("Changes requested. The run has stopped.", Warn); break;
                case "expired": SetStatus("This request expired (the desktop restarted). Nothing was run.", Warn); break;
                default: SetStatus($"Resolved: {state}", Ink); break;
            }
            SetActionsVisible(false);
        }

        public void SetError(string message)
        {
            SetStatus(message ?? "Couldn't reach the desktop. Try again.", Bad);
            SetActionsVisible(true);   // let the user retry
        }

        public void SetAnswer(string text)
        {
            if (answer) answer.text = string.IsNullOrEmpty(text) ? "" : "“" + text + "”";
        }

        void SetActionsVisible(bool v)
        {
            if (actionsRoot) actionsRoot.SetActive(v);
            if (stampZoneRoot) stampZoneRoot.SetActive(v);
        }
    }
}
