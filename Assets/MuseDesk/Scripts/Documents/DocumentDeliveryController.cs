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

        void Awake() { if (paperRoot) paperRoot.SetActive(false); }

        void Update() { if (!_bound && controller != null) Bind(); }

        void OnDisable()
        {
            if (!_bound || controller == null) return;
            controller.Reducer.DecisionRequested -= OnDecisionRequested;
            controller.Reducer.DecisionResolved -= OnDecisionResolved;
            _bound = false;
        }

        void Bind()
        {
            controller.Reducer.DecisionRequested += OnDecisionRequested;
            controller.Reducer.DecisionResolved += OnDecisionResolved;
            _bound = true;
        }

        void OnDecisionRequested(string decisionId, EventEnvelope e)
        {
            if (decisionId == _currentDecisionId) return;   // dedup replay / duplicate
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
            StartCoroutine(DismissAfter(dismissAfterResolve));
        }

        IEnumerator DismissAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (paperRoot) paperRoot.SetActive(false);
            _currentDecisionId = null;
        }

        // ── button hooks (poke buttons call these) ──
        public void OnApprove()        { if (panel) panel.SetResponding(); controller?.ApprovePending(); }
        public void OnReject()         { if (panel) panel.SetResponding(); controller?.RejectPending(); }
        public void OnRequestChanges() { if (panel) panel.SetResponding(); controller?.RequestChangesPending(); }
        public void OnAskWhy()
        {
            controller?.AskPending("Why do you recommend this?",
                ans => { if (panel) panel.SetAnswer(ans); });
        }
    }
}
