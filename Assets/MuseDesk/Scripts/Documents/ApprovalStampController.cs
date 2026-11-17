using System.Collections;
using UnityEngine;
using TMPro;
using MuseDesk.Feedback;
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
    ///
    /// Paper V3 adds two things, both still purely visual:
    ///   the stamp target ("Stamp here") changes tint while a held stamp that would count is close
    ///   (<see cref="NotifyStampNear"/>, called by StampTool every frame it is near), and
    ///   a provisional mark: when a stamp lands, a pale mark in the words of the request (APPROVE /
    ///   REJECT) is printed where the stamp touched. It becomes the dark confirmed mark (APPROVED /
    ///   REJECTED) only on decision.resolved, and disappears if the response fails.
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

        [Header("Stamp target highlight (Paper V3, optional)")]
        [Tooltip("The tinted plate of the stamp target. Tinted through a property block while a held stamp is near.")]
        public Renderer targetPlate;
        [Tooltip("The outline of the stamp target (four thin quads).")]
        public Renderer[] targetOutline;
        public Color targetIdle = new Color32(0xEC, 0xE4, 0xCF, 255);
        public Color targetNear = new Color32(0xCD, 0xE3, 0xD9, 255);
        public Color outlineIdle = new Color32(0x4B, 0x55, 0x63, 255);
        public Color outlineNear = new Color32(0x2F, 0xA3, 0x9A, 255);

        bool _bound;
        Vector3 _stampHome;
        bool _capturedHome;
        bool _confirmed;          // the mark showing is the backend-confirmed one
        bool _provisional;        // the mark showing is the pale, unconfirmed one
        int _nearFrame = -1;      // last frame a stamp reported being near the target
        bool _nearShown;
        MaterialPropertyBlock _block;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        // Darker shades of the success / coral / amber tokens: the mark is printed on cream paper.
        static readonly Color Approve = new Color(0.10f, 0.42f, 0.24f);
        static readonly Color Reject  = new Color(0.62f, 0.16f, 0.13f);
        static readonly Color Changes = new Color(0.60f, 0.36f, 0.05f);
        // The paper token: a provisional mark is the final ink thinned toward the paper.
        static readonly Color PaperTone = new Color32(0xF7, 0xF2, 0xE3, 255);
        const float ProvisionalThin = 0.55f;

        void Awake()
        {
            if (stamp != null && !_capturedHome) { _stampHome = stamp.localPosition; _capturedHome = true; }
            HideAll();
        }

        // The paper is deactivated between decisions, so this component misses the decision.requested
        // that re-activates it; clear the previous decision's mark whenever the paper comes back.
        void OnEnable() { HideAll(); }

        void Update() { if (!_bound && controller != null) Bind(); }

        // Runs after every StampTool.Update of the frame (those are what report "near").
        void LateUpdate()
        {
            bool near = _nearFrame == Time.frameCount;
            if (near != _nearShown) ApplyHighlight(near);
        }

        void OnDisable()
        {
            if (_bound && controller != null)
            {
                controller.Reducer.DecisionResolved -= OnResolved;
                controller.Reducer.DecisionRequested -= OnRequested;
                controller.Reducer.SessionReset -= HideAll;
                controller.RespondFailed -= OnRespondFailed;
            }
            _bound = false;
        }

        void Bind()
        {
            controller.Reducer.DecisionResolved += OnResolved;
            controller.Reducer.DecisionRequested += OnRequested;
            controller.Reducer.SessionReset += HideAll;
            controller.RespondFailed += OnRespondFailed;
            _bound = true;
        }

        void OnRequested(string decisionId, EventEnvelope e) { HideAll(); }

        // The response never reached the backend: the provisional mark was a request that did not
        // happen, so it goes. (StampTool re-arms the stamp on the same event.)
        void OnRespondFailed(string decisionId, string message) { HideProvisional(); }

        void HideAll()
        {
            StopAllCoroutines();
            if (stamp != null) { if (_capturedHome) stamp.localPosition = _stampHome; stamp.gameObject.SetActive(false); }
            if (mark != null) mark.gameObject.SetActive(false);
            _confirmed = false;
            _provisional = false;
            _nearFrame = -1;
            ApplyHighlight(false);
        }

        /// <summary>
        /// A held stamp that would count if pressed is close to the target this frame. Call it every
        /// frame the stamp is near; the highlight ends by itself on the first frame nobody calls.
        /// </summary>
        public void NotifyStampNear()
        {
            _nearFrame = Time.frameCount;
        }

        /// <summary>
        /// A stamp has landed and its response is on the way: print the pale, provisional mark in the
        /// words of the request. Ignored once the confirmed mark is showing. Never claims an outcome.
        /// </summary>
        public void ShowProvisional(bool approve)
        {
            if (_confirmed || mark == null || !isActiveAndEnabled) return;
            mark.gameObject.SetActive(true);
            mark.text = approve ? "APPROVE" : "REJECT";
            mark.color = Color.Lerp(approve ? Approve : Reject, PaperTone, ProvisionalThin);
            _provisional = true;
        }

        /// <summary>Remove the provisional mark (the response failed). A confirmed mark is left alone.</summary>
        public void HideProvisional()
        {
            if (!_provisional || _confirmed) return;
            _provisional = false;
            if (mark != null) mark.gameObject.SetActive(false);
        }

        /// <summary>True while the pale, unconfirmed mark is showing.</summary>
        public bool ProvisionalShown => _provisional && !_confirmed;

        void ApplyHighlight(bool near)
        {
            _nearShown = near;
            if (targetPlate == null && (targetOutline == null || targetOutline.Length == 0)) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            TintOne(targetPlate, near ? targetNear : targetIdle);
            if (targetOutline != null)
                for (int i = 0; i < targetOutline.Length; i++)
                    TintOne(targetOutline[i], near ? outlineNear : outlineIdle);
        }

        void TintOne(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, c);
            _block.SetColor(ColorId, c);
            r.SetPropertyBlock(_block);
        }

        void OnResolved(string decisionId, string state)
        {
            string s = (state ?? "").ToLowerInvariant();
            Color c; string txt; FeedbackCue cue;
            if (s.Contains("approv")) { c = Approve; txt = "APPROVED"; cue = FeedbackCue.Approve; }
            else if (s.Contains("reject")) { c = Reject; txt = "REJECTED"; cue = FeedbackCue.Reject; }
            else { c = Changes; txt = "CHANGES"; cue = FeedbackCue.Changes; }
            // The outcome cue belongs to the confirmed mark. FeedbackController plays the same cue
            // from the same event and drops a repeat within its window, so this is one sound.
            FeedbackController.PlayCue(cue);
            StopAllCoroutines();
            StartCoroutine(Press(c, txt));
        }

        void Confirm(Color c, string txt)
        {
            _confirmed = true;
            _provisional = false;
            if (mark != null) { mark.gameObject.SetActive(true); mark.text = txt; mark.color = c; }
        }

        IEnumerator Press(Color c, string txt)
        {
            if (stamp == null)
            {
                // No agent stamp prop (the USER stamps physically via StampTool): still reveal the
                // authoritative mark — it is driven by the backend's decision.resolved, not the tool.
                Confirm(c, txt);
                yield break;
            }
            if (!_capturedHome) { _stampHome = stamp.localPosition; _capturedHome = true; }
            stamp.gameObject.SetActive(true);
            stamp.localPosition = _stampHome;
            Vector3 down = _stampHome + Vector3.down * pressDepth;

            yield return Move(_stampHome, down, pressSeconds);
            // Contact: reveal the stamped mark.
            Confirm(c, txt);
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
