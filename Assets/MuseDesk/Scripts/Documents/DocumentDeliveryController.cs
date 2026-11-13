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

        [Tooltip("Where a decided paper is filed (e.g. the out-tray). If empty the paper is simply hidden.")]
        public Transform fileTarget;
        [Range(0.2f, 1.5f)] public float fileSeconds = 0.55f;

        string _currentDecisionId;
        bool _bound;
        bool _responding;
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
            controller.Reducer.SessionReset -= OnSessionReset;
            _bound = false;
        }

        void Bind()
        {
            controller.Reducer.DecisionRequested += OnDecisionRequested;
            controller.Reducer.DecisionResolved += OnDecisionResolved;
            controller.RespondFailed += OnRespondFailed;
            controller.Reducer.SnapshotLoaded += ShowPendingFromSnapshot;
            controller.Reducer.SessionReset += OnSessionReset;
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
            _responding = false;
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

            _responding = false;
            // Where the recommendation came from, in the backend's own words when it says so
            // ("Template recommendation. Not live AI research.").
            // A revised proposal (after "Changes") says so: revision 0 is the first proposal.
            int revision = e.PayloadValue<int>("decision_revision", e.PayloadValue<int>("revision", 0));
            if (panel) panel.Bind(content, content.SourceMode, e.PayloadString("research_provenance_label", null), revision);
            if (paperRoot)
            {
                paperRoot.SetActive(false);   // re-trigger OnEnable placement
                paperRoot.SetActive(true);
            }
        }

        void OnDecisionResolved(string decisionId, string state)
        {
            if (decisionId != _currentDecisionId) return;
            _responding = false;
            if (panel) panel.SetResolved(state);
            if (_dismiss != null) StopCoroutine(_dismiss);
            _dismiss = StartCoroutine(DismissAfter(dismissAfterResolve));
        }

        IEnumerator DismissAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);

            // File the paper away instead of blinking it out: it shrinks and glides to the filing
            // spot (the out-tray), so the user sees where a decided document goes.
            var held = paperRoot != null ? paperRoot.GetComponent<HeldPaperController>() : null;
            if (paperRoot != null && fileTarget != null && held != null && paperRoot.activeInHierarchy && held.IsTaken)
            {
                // Don't pull it out of the user's hand: wait (bounded) until they let go.
                float waited = 0f;
                while (held.IsHeld && waited < 4f) { waited += Time.deltaTime; yield return null; }

                held.BeginFiling();
                var t = paperRoot.transform;
                Vector3 fromPos = t.position, fromScale = t.localScale;
                Quaternion fromRot = t.rotation;
                float dur = Mathf.Max(0.2f, fileSeconds), e = 0f;
                while (e < dur)
                {
                    e += Time.deltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / dur));
                    Vector3 p = Vector3.Lerp(fromPos, fileTarget.position, k);
                    p.y += Mathf.Sin(k * Mathf.PI) * 0.05f;                 // a small arc
                    t.SetPositionAndRotation(p, Quaternion.Slerp(fromRot, fileTarget.rotation, k));
                    t.localScale = Vector3.Lerp(fromScale, fromScale * 0.12f, k);
                    yield return null;
                }
                t.localScale = held.FullScale;      // restored for the next decision
            }

            if (paperRoot) paperRoot.SetActive(false);
            _currentDecisionId = null;
            _dismiss = null;
        }

        // A session reset (replay) clears whatever paper is showing.
        void OnSessionReset()
        {
            if (_dismiss != null) { StopCoroutine(_dismiss); _dismiss = null; }
            if (paperRoot) paperRoot.SetActive(false);
            _currentDecisionId = null;
            _responding = false;
            if (panel) panel.Clear();
        }

        bool BeginResponse()
        {
            // One response per decision: a second poke (or a stamp landing while a poke is in flight)
            // is ignored until the backend answers or the request fails.
            if (_responding || controller == null || string.IsNullOrEmpty(controller.PendingDecisionId)) return false;
            _responding = true;
            if (panel) panel.SetResponding();
            return true;
        }

        /// <summary>Called when the user grabs or pokes the paper — reveal the full detail.</summary>
        public void OnExpand() { if (panel) panel.Expand(); }

        /// <summary>Details button: show/hide the extra rationale panel. Never approves anything.</summary>
        public void OnToggleDetails() { if (panel) panel.ToggleExpand(); }

        // ── button hooks (poke buttons call these) ──
        // Buttons and the physical stamps both come through here, so there is exactly one response
        // in flight per decision whichever way the user chose to answer.
        public void OnApprove()        { if (BeginResponse()) controller.ApprovePending(); }
        public void OnReject()         { if (BeginResponse()) controller.RejectPending(); }
        public void OnRequestChanges() { if (BeginResponse()) controller.RequestChangesPending(); }

        /// <summary>True while a response has been sent and the backend has not yet answered.</summary>
        public bool ResponseInFlight => _responding;
        public void OnAskWhy()
        {
            if (controller == null) return;
            if (panel) { panel.Expand(); panel.SetAnswer("Asking..."); }
            controller.AskPending("Why do you recommend this?",
                ans => { if (panel) panel.SetAnswer(ans); });
        }
    }
}
