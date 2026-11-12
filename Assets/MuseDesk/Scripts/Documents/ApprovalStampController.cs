using System.Collections;
using UnityEngine;
using TMPro;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Documents
{
    /// <summary>
    /// Physical approval-stamp feedback for the decision paper. Listens to the reducer's
    /// decision.resolved (NOT the local button press — AGENTS invariant #2) and, only once the
    /// backend has confirmed the outcome, presses the real Doc_ApprovalStamp prop onto the card
    /// and reveals a colored APPROVED / REJECTED / CHANGES mark. Purely visual; it never approves
    /// anything. Resets on each new decision.requested.
    /// </summary>
    public class ApprovalStampController : MonoBehaviour
    {
        public SessionController controller;
        [Tooltip("The stamp prop root that drops down to press the paper.")]
        public Transform stamp;
        [Tooltip("TMP mark stamped onto the card (APPROVED/REJECTED/CHANGES).")]
        public TMP_Text mark;

        [Tooltip("How far (local units) the stamp drops to touch the paper.")]
        public float pressDepth = 0.10f;
        public float pressSeconds = 0.16f;

        bool _bound;
        Vector3 _stampHome;
        bool _capturedHome;

        static readonly Color Approve = new Color(0.22f, 0.76f, 0.45f);
        static readonly Color Reject  = new Color(0.89f, 0.20f, 0.18f);
        static readonly Color Changes = new Color(0.96f, 0.60f, 0.25f);

        void Awake()
        {
            if (stamp != null && !_capturedHome) { _stampHome = stamp.localPosition; _capturedHome = true; }
            HideAll();
        }

        void Update() { if (!_bound && controller != null) Bind(); }

        void OnDisable()
        {
            if (_bound && controller != null)
            {
                controller.Reducer.DecisionResolved -= OnResolved;
                controller.Reducer.DecisionRequested -= OnRequested;
            }
            _bound = false;
        }

        void Bind()
        {
            controller.Reducer.DecisionResolved += OnResolved;
            controller.Reducer.DecisionRequested += OnRequested;
            _bound = true;
        }

        void OnRequested(string decisionId, EventEnvelope e) { HideAll(); }

        void HideAll()
        {
            StopAllCoroutines();
            if (stamp != null) { if (_capturedHome) stamp.localPosition = _stampHome; stamp.gameObject.SetActive(false); }
            if (mark != null) mark.gameObject.SetActive(false);
        }

        void OnResolved(string decisionId, string state)
        {
            string s = (state ?? "").ToLowerInvariant();
            Color c; string txt;
            if (s.Contains("approv")) { c = Approve; txt = "APPROVED"; }
            else if (s.Contains("reject")) { c = Reject; txt = "REJECTED"; }
            else { c = Changes; txt = "CHANGES"; }
            StopAllCoroutines();
            StartCoroutine(Press(c, txt));
        }

        IEnumerator Press(Color c, string txt)
        {
            if (stamp == null)
            {
                // No agent stamp prop (the USER stamps physically via StampTool): still reveal the
                // authoritative mark — it is driven by the backend's decision.resolved, not the tool.
                if (mark != null) { mark.gameObject.SetActive(true); mark.text = txt; mark.color = c; }
                yield break;
            }
            if (!_capturedHome) { _stampHome = stamp.localPosition; _capturedHome = true; }
            stamp.gameObject.SetActive(true);
            stamp.localPosition = _stampHome;
            Vector3 down = _stampHome + Vector3.down * pressDepth;

            yield return Move(_stampHome, down, pressSeconds);
            // Contact: reveal the stamped mark.
            if (mark != null) { mark.gameObject.SetActive(true); mark.text = txt; mark.color = c; }
            yield return new WaitForSeconds(0.08f);
            yield return Move(down, _stampHome, pressSeconds * 1.4f);
        }

        IEnumerator Move(Vector3 a, Vector3 b, float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                stamp.localPosition = Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, t / seconds));
                yield return null;
            }
            stamp.localPosition = b;
        }
    }
}
