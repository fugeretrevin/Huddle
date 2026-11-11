using System.Linq;
using System.Text;
using UnityEngine;
using TMPro;
using MuseDesk.Models;

namespace MuseDesk.Documents
{
    /// <summary>
    /// Readable expanded decision document: binds a DecisionContent to high-contrast text
    /// and reflects the response lifecycle (responding / resolved / error). It never claims
    /// an approval before the backend confirms it — SetResolved is driven by decision.resolved.
    /// </summary>
    public class DecisionPanel : MonoBehaviour
    {
        [Header("Text fields")]
        public TMP_Text badge;        // [BLOCKING] [LIVE/DEMO]
        public TMP_Text title;
        public TMP_Text summary;
        public TMP_Text recommendation;
        public TMP_Text body;         // rationale + alternatives + risk + impact
        public TMP_Text status;       // Saving… / Approved / Rejected / error
        public TMP_Text answer;       // Ask-why response

        [Header("Action controls (wired by builder)")]
        public GameObject actionsRoot;   // hidden once a response is pending
        [Tooltip("Extra detail (rationale/alternatives/risk/answer) shown when the paper is grabbed/expanded.")]
        public GameObject detailsRoot;   // hidden until Expand()

        public bool IsExpanded { get; private set; }

        public void Bind(DecisionContent c, string sourceMode)
        {
            if (c == null) return;
            string mode = (sourceMode ?? c.SourceMode ?? "demo").ToUpperInvariant();
            if (badge) badge.text = $"[{(c.Priority ?? "blocking").ToUpperInvariant()}]  [{mode}]";
            if (title) title.text = c.Title ?? "Approval requested";
            if (summary) summary.text = c.Summary ?? "";
            if (recommendation)
                recommendation.text = string.IsNullOrEmpty(c.Recommendation)
                    ? "" : $"Recommended: {c.Recommendation.ToUpperInvariant()}";

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(c.Rationale)) sb.Append("Why: ").Append(c.Rationale).Append('\n');
            if (c.Alternatives != null && c.Alternatives.Count > 0)
                sb.Append("Options: ")
                  .Append(string.Join("  •  ", c.Alternatives.Select(a => $"{a.Label} ({a.Tradeoff})")))
                  .Append('\n');
            if (!string.IsNullOrEmpty(c.RiskLevel)) sb.Append("Risk: ").Append(c.RiskLevel).Append('\n');
            if (!string.IsNullOrEmpty(c.Impact)) sb.Append("Impact: ").Append(c.Impact);
            if (body) body.text = sb.ToString();

            if (status) status.text = "";
            if (answer) answer.text = "";
            SetActionsVisible(true);
            Collapse();
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
            if (status) status.text = "Saving your decision…";
            SetActionsVisible(false);
        }

        public void SetResolved(string state)
        {
            string s = (state ?? "").ToLowerInvariant();
            if (status)
                status.text = s switch
                {
                    "approved" => "✓ Approved. Developer is continuing.",
                    "rejected" => "✗ Rejected. Agent will reconsider.",
                    "changes_requested" => "↻ Changes requested.",
                    _ => $"Resolved: {state}",
                };
            SetActionsVisible(false);
        }

        public void SetError(string message)
        {
            if (status) status.text = message ?? "Couldn't reach the desktop. Retry or use Demo Mode.";
            SetActionsVisible(true);   // let the user retry
        }

        public void SetAnswer(string text)
        {
            if (answer) answer.text = string.IsNullOrEmpty(text) ? "" : "“" + text + "”";
        }

        void SetActionsVisible(bool v)
        {
            if (actionsRoot) actionsRoot.SetActive(v);
        }
    }
}
