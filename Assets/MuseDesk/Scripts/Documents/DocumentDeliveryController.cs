using System.Collections;
using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Documents
{
    /// <summary>
    /// Turns backend decision events into a single physical document. Shows exactly one paper
    /// at a time (dedup by decision id, so replayed events never spawn duplicates), binds its
    /// content, and reflects responding/resolved from the reducer — not from the button press.
    /// The poke buttons call OnApprove/OnReject/OnRequestChanges/OnAskWhy.
    /// </summary>
    public class DocumentDeliveryController : MonoBehaviour
    {
        [Header("Wiring")]
        public SessionController controller;
        public GameObject paperRoot;     // the whole document; inactive until a decision arrives
        public DecisionPanel panel;

        [Tooltip("Seconds to keep the resolved paper visible before dismissing it.")]
        [Range(0.5f, 6f)] public float dismissAfterResolve = 3f;

        string _currentDecisionId;
        bool _bound;
        Coroutine _dismiss;

        void Awake() { if (paperRoot) paperRoot.SetActive(false); }

        void Update() { if (!_bound && controller != null) Bind(); }

        void OnDisable()
        {
            if (!_bound || controller == null) return;
            controller.Reducer.DecisionRequested -= OnDecisionRequested;
            controller.Reducer.DecisionResolved -= OnDecisionResolved;
            controller.RespondFailed -= OnRespondFailed;
            controller.Reducer.SnapshotLoaded -= ShowPendingFromSnapshot;
            _bound = false;
        }

        void Bind()
        {
            controller.Reducer.DecisionRequested += OnDecisionRequested;
            controller.Reducer.DecisionResolved += OnDecisionResolved;
            controller.RespondFailed += OnRespondFailed;
            controller.Reducer.SnapshotLoaded += ShowPendingFromSnapshot;
            _bound = true;
            ShowPendingFromSnapshot();
        }

        // Restart / resume: a decision that was already awaiting a human has no decision.requested
        // event coming, so present it from the authoritative snapshot (summary-level detail only).
        void ShowPendingFromSnapshot()
        {
            var d = controller.Reducer.FirstPendingDecision();
            if (d == null || d.DecisionId == _currentDecisionId) return;
            if (_dismiss != null) { StopCoroutine(_dismiss); _dismiss = null; }
            _currentDecisionId = d.DecisionId;
            var content = new DecisionContent
            {
                Title = "Approval requested",
                Summary = d.Summary,
                Recommendation = d.Recommendation,
                SourceMode = controller.IsLive ? SourceModes.Live : SourceModes.Demo,
            };
            if (panel) panel.Bind(content, content.SourceMode);
            if (paperRoot) { paperRoot.SetActive(false); paperRoot.SetActive(true); }
        }

        // The response never reached the backend: say so on the paper and give the buttons back.
        void OnRespondFailed(string decisionId, string message)
        {
            if (decisionId != _currentDecisionId) return;
            if (panel) panel.SetError(message);
        }

        void OnDecisionRequested(string decisionId, EventEnvelope e)
        {
            if (decisionId == _currentDecisionId) return;   // dedup replay / duplicate
            // A previous paper may still be counting down to dismissal; that timer must not hide
            // (and orphan) the NEW decision's paper.
            if (_dismiss != null) { StopCoroutine(_dismiss); _dismiss = null; }
            _currentDecisionId = decisionId;

            DecisionContent content = null;
            try { content = e.Payload != null ? e.Payload.ToObject<DecisionContent>() : null; }
            catch { /* fall through to a minimal panel */ }
            content ??= new DecisionContent { Title = "Approval requested", Summary = e.PayloadString("summary") };

            if (panel) panel.Bind(content, content.SourceMode);
            if (paperRoot)
            {
                paperRoot.SetActive(false);   // re-trigger OnEnable placement
                paperRoot.SetActive(true);
            }
        }

        void OnDecisionResolved(string decisionId, string state)
        {
            if (decisionId != _currentDecisionId) return;
            if (panel) panel.SetResolved(state);
            if (_dismiss != null) StopCoroutine(_dismiss);
            _dismiss = StartCoroutine(DismissAfter(dismissAfterResolve));
        }

        IEnumerator DismissAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (paperRoot) paperRoot.SetActive(false);
            _currentDecisionId = null;
            _dismiss = null;
        }

        /// <summary>Called when the user grabs or pokes the paper — reveal the full detail.</summary>
        public void OnExpand() { if (panel) panel.Expand(); }

        /// <summary>Details button: show/hide the extra rationale panel. Never approves anything.</summary>
        public void OnToggleDetails() { if (panel) panel.ToggleExpand(); }

        // ── button hooks (poke buttons call these) ──
        public void OnApprove()        { if (panel) panel.SetResponding(); if (controller != null) controller.ApprovePending(); }
        public void OnReject()         { if (panel) panel.SetResponding(); if (controller != null) controller.RejectPending(); }
        public void OnRequestChanges() { if (panel) panel.SetResponding(); if (controller != null) controller.RequestChangesPending(); }
        public void OnAskWhy()
        {
            if (controller == null) return;
            if (panel) { panel.Expand(); panel.SetAnswer("Asking…"); }
            controller.AskPending("Why do you recommend this?",
                ans => { if (panel) panel.SetAnswer(ans); });
        }
    }
}
