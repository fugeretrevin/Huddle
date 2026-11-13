using System.Collections;
using System.Reflection;
using UnityEngine;
using MuseDesk.Networking;

namespace MuseDesk.Documents
{
    /// <summary>
    /// A physical approval stamp the USER picks up and presses onto the decision paper to approve or
    /// reject — the human-in-the-loop gesture instead of poking a button.
    ///
    /// INVARIANT (product thesis): this NEVER fabricates a result. Pressing the stamp only calls the
    /// real <see cref="SessionController.ApprovePending"/> / <see cref="SessionController.RejectPending"/>,
    /// which route to the backend gateway (source_mode stamped by the gateway, demo or live). It fires
    /// only while there IS a decision awaiting a human (<see cref="SessionController.PendingDecisionId"/>),
    /// at most once per decision (a static guard shared by both stamps), and shows only a NEUTRAL press
    /// cue — the authoritative APPROVED/REJECTED mark is revealed by <see cref="ApprovalStampController"/>
    /// when the backend's decision.resolved arrives, so no outcome is ever shown before it is confirmed.
    ///
    /// Grab state is read by reflection off Oculus.Interaction.Grabbable (SelectingPointsCount), the same
    /// pattern HeldPaperController / TaskCardView use, so this runtime assembly keeps no hard ISDK dep.
    /// Detection is a manual proximity+dwell check (NOT physics triggers: both the stamp and the paper are
    /// kinematic, so OnTriggerEnter is unreliable between them).
    /// </summary>
    [DefaultExecutionOrder(320)]
    public class StampTool : MonoBehaviour
    {
        public enum StampAction { Approve, Reject }

        [Header("Wiring (set by BuildSelfStamp)")]
        [Tooltip("The Oculus.Interaction.Grabbable on this stamp. Read by reflection only.")]
        public Component grabbable;
        [Tooltip("Contact point at the inking face (bottom) of the stamp.")]
        public Transform inkHead;
        [Tooltip("Target zone on the decision paper the ink head presses onto.")]
        public Transform stampZone;
        public SessionController controller;
        public StampAction action = StampAction.Approve;
        [Tooltip("The decision paper. Auto-found from the stamp zone if empty. The stamp only works once the paper is ready (taken and readable).")]
        public HeldPaperController paper;
        bool _paperResolved;
        DocumentDeliveryController _delivery;

        [Header("Press detection")]
        [Tooltip("Ink head must come within this distance of the zone to count as contact (metres).")]
        public float contactRadius = 0.06f;
        [Tooltip("Contact must be held this long before it fires (debounces a brush-past).")]
        public float dwellSeconds = 0.12f;
        [Tooltip("After a stamp (or a 'nothing to approve' cue) this stamp won't re-trigger for a moment.")]
        public float cooldownSeconds = 1.0f;

        [Header("Feel")]
        [Tooltip("Head renderer, briefly scale-popped + flashed on a successful press.")]
        public Renderer headRenderer;
        [Range(0f, 1f)] public float volume = 0.4f;

        [Header("Return-to-rest")]
        [Tooltip("When released, glide back to the desk rest pose so the tools stay tidy.")]
        public bool returnHome = true;
        [Tooltip("Only used when returnSeconds is zero: the old exponential glide.")]
        [Range(0f, 20f)] public float returnLerp = 8f;
        [Tooltip("Seconds of the eased move back to the rest pad after release.")]
        [Range(0f, 1.5f)] public float returnSeconds = 0.4f;

        [Header("Press (Paper V3)")]
        [Tooltip("The stamp's model (a child, never the grabbed root). It dips toward the paper when a stamp lands. Empty: no dip.")]
        public Transform pressVisual;
        [Tooltip("How far the model dips, in metres of this stamp's local space.")]
        public float pressDip = 0.006f;
        [Tooltip("Seconds for the dip down and back.")]
        public float pressSeconds = 0.16f;
        [Tooltip("The stamp target highlights while the ink head of a held stamp that would count is within this distance of the target's centre (metres).")]
        public float highlightRadius = 0.10f;

        // One decision = one stamp, across BOTH stamps (main-thread only, so a static is safe).
        static string s_handledDecision;

        System.Func<int> _selCount;
        bool _selResolved;
        float _dwell;
        float _cooldownUntil;
        Vector3 _homePos;
        Quaternion _homeRot;
        bool _homeCaptured;
        Vector3 _headBaseScale = Vector3.one;
        AudioSource _audio;
        AudioClip _thunk, _nope;

        // Eased return to the rest pad.
        bool _wasHeld;
        bool _returning;
        float _returnT;
        Vector3 _returnFromPos;
        Quaternion _returnFromRot;

        // Press dip of the model.
        bool _dipping;
        float _dipT;
        Vector3 _visualBasePos;

        // The paper's mark and target highlight (on the same object as the HeldPaperController).
        ApprovalStampController _marks;
        bool _marksResolved;

        void Awake()
        {
            // Home is PARENT-RELATIVE: WorkspacePlacement moves WorkspaceRoot after Awake, so a world
            // pose captured here would drag the stamp back to the authored spot, off the desk.
            _homePos = transform.localPosition;
            _homeRot = transform.localRotation;
            _homeCaptured = true;
            if (headRenderer != null) _headBaseScale = headRenderer.transform.localScale;
            // The dip moves a child of the grabbed root, and only one that is really a child.
            if (pressVisual != null && (pressVisual == transform || !pressVisual.IsChildOf(transform))) pressVisual = null;
            if (pressVisual != null) _visualBasePos = pressVisual.localPosition;

            _audio = GetComponent<AudioSource>();
            if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _thunk = Thunk();
            _nope = Nope();
        }

        void OnEnable() { if (controller != null) controller.RespondFailed += OnRespondFailed; }
        void OnDisable() { if (controller != null) controller.RespondFailed -= OnRespondFailed; }

        // The stamped response never reached the backend: re-arm so the user can stamp again.
        void OnRespondFailed(string decisionId, string message)
        {
            if (s_handledDecision == decisionId) s_handledDecision = null;
            Play(_nope);
        }

        // ── used by StampPoof (the stamp leaving after a decision and coming back for the next) ──

        /// <summary>True while a hand is holding this stamp.</summary>
        public bool Held => IsHeld();

        /// <summary>While true the released stamp stays where it is (no glide back to the pad).
        /// StampPoof sets it while the stamp is leaving, so it vanishes from where it was let go.</summary>
        public bool HoldInPlace { get; set; }

        /// <summary>Put the stamp on its rest pose at once and end any glide. Clears <see cref="HoldInPlace"/>.</summary>
        public void SnapHome()
        {
            HoldInPlace = false;
            _returning = false;
            _wasHeld = false;
            if (!_homeCaptured) return;
            transform.localPosition = _homePos;
            transform.localRotation = _homeRot;
        }

        /// <summary>A quiet puff of air for the stamp leaving or arriving. Synthesised once and shared;
        /// two stamps asking in the same moment make one sound. No audio asset.</summary>
        public void PlayPoof(bool arriving)
        {
            if (_audio == null) return;
            if (Time.unscaledTime - s_lastPoofAt < 0.08f) return;
            s_lastPoofAt = Time.unscaledTime;
            if (s_poof == null) s_poof = Poof();
            _audio.pitch = arriving ? 1.25f : 1f;
            _audio.PlayOneShot(s_poof, volume * (arriving ? 0.30f : 0.45f));
        }

        static AudioClip s_poof;
        static float s_lastPoofAt = -10f;

        // Soft low-passed noise with a quick swell and decay: air, not a tone.
        static AudioClip Poof()
        {
            int rate = 44100, n = (int)(rate * 0.20f);
            var d = new float[n];
            uint seed = 0x9E3779B9u;
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                float white = ((seed >> 8) & 0xFFFF) / 32767.5f - 1f;
                float t = (float)i / rate;
                // The filter closes as the sound dies, so it ends dull, not hissy.
                float a = Mathf.Lerp(0.22f, 0.04f, t / 0.20f);
                lp += a * (white - lp);
                float env = (1f - Mathf.Exp(-t / 0.008f)) * Mathf.Exp(-18f * t);
                d[i] = 1.6f * env * lp;
            }
            var c = AudioClip.Create("stamp_poof", n, 1, rate, false); c.SetData(d, 0); return c;
        }

        bool IsHeld()
        {
            if (grabbable == null) return false;
            if (!_selResolved)
            {
                _selCount = MuseDesk.Workspace.GrabState.BindSelectingPoints(grabbable);
                _selResolved = true;
            }
            return _selCount != null && _selCount() > 0;
        }

        // The model's dip toward the paper and back. It moves only the model child, so it never
        // fights the hand that is holding the root.
        void TickDip()
        {
            if (!_dipping) return;
            if (pressVisual == null) { _dipping = false; return; }
            _dipT += Time.deltaTime / Mathf.Max(0.02f, pressSeconds);
            if (_dipT >= 1f)
            {
                _dipping = false;
                pressVisual.localPosition = _visualBasePos;
                return;
            }
            // Down and back in one smooth arc (the ink face is the model's underside: local -Y).
            pressVisual.localPosition = _visualBasePos + Vector3.down * (pressDip * Mathf.Sin(_dipT * Mathf.PI));
        }

        // Released: one eased move back to the rest pad (no physics; the stamp is kinematic).
        void TickReturn()
        {
            if (!returnHome || !_homeCaptured || HoldInPlace) return;
            if (returnSeconds <= 0f)
            {
                float k = 1f - Mathf.Exp(-returnLerp * Time.deltaTime);
                transform.localPosition = Vector3.Lerp(transform.localPosition, _homePos, k);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, _homeRot, k);
                return;
            }
            if (!_returning) return;
            _returnT += Time.deltaTime / returnSeconds;
            float t = Mathf.Clamp01(_returnT);
            float e = t * t * (3f - 2f * t);                    // ease in and out
            transform.localPosition = Vector3.Lerp(_returnFromPos, _homePos, e);
            transform.localRotation = Quaternion.Slerp(_returnFromRot, _homeRot, e);
            if (t >= 1f) _returning = false;
        }

        void Update()
        {
            TickDip();
            bool held = IsHeld();

            if (!held)
            {
                _dwell = 0f;
                if (_wasHeld)
                {
                    _wasHeld = false;
                    _returning = true;
                    _returnT = 0f;
                    _returnFromPos = transform.localPosition;
                    _returnFromRot = transform.localRotation;
                }
                TickReturn();
                return;
            }
            _wasHeld = true;
            _returning = false;

            // Held: look for a press onto the paper zone.
            if (inkHead == null || stampZone == null || controller == null) return;
            if (Time.time < _cooldownUntil) return;

            bool zoneReady = stampZone.gameObject.activeInHierarchy;
            float distSqr = (inkHead.position - stampZone.position).sqrMagnitude;
            bool contact = zoneReady && distSqr <= contactRadius * contactRadius;
            bool near = zoneReady && distSqr <= highlightRadius * highlightRadius;

            if (!near && !contact) { _dwell = 0f; return; }

            // A stamp only counts on a paper the user has taken and had in front of them: never on
            // the blank sheet still in the agent's hand, and never in the instant it is unfolding.
            if (!_paperResolved)
            {
                if (paper == null) paper = stampZone.GetComponentInParent<HeldPaperController>(true);
                _paperResolved = true;
            }
            if (paper != null && !paper.ReadyForDecision) { _dwell = 0f; return; }

            // The target lights up only when pressing this stamp now would really send a response.
            if (!_marksResolved)
            {
                _marksResolved = true;
                _marks = paper != null ? paper.GetComponent<ApprovalStampController>() : stampZone.GetComponentInParent<ApprovalStampController>(true);
            }
            if (_marks != null)
            {
                string open = controller.PendingDecisionId;
                if (!string.IsNullOrEmpty(open) && open != s_handledDecision) _marks.NotifyStampNear();
            }

            if (!contact) { _dwell = 0f; return; }

            string pending = controller.PendingDecisionId;
            if (string.IsNullOrEmpty(pending))
            {
                // Pressed with nothing to approve — soft "nope", then cool down (no backend call).
                Play(_nope);
                _cooldownUntil = Time.time + cooldownSeconds;
                _dwell = 0f;
                return;
            }

            if (pending == s_handledDecision) { _dwell = 0f; return; } // already acted this decision

            _dwell += Time.deltaTime;
            if (_dwell >= dwellSeconds)
                Fire(pending);
        }

        void Fire(string decisionId)
        {
            s_handledDecision = decisionId;          // block the other stamp for this decision
            _cooldownUntil = Time.time + cooldownSeconds;
            _dwell = 0f;

            // The ONLY state change: the real approval path. Never fabricate a result. Go through the
            // paper's own response hooks when there is one, so a stamp and a button press share a
            // single in-flight response (no double submission) and the paper shows "Saving…".
            if (_delivery == null) _delivery = FindAnyObjectByType<DocumentDeliveryController>();
            bool inFlight = true;
            if (_delivery != null)
            {
                if (_delivery.ResponseInFlight) return;
                if (action == StampAction.Approve) _delivery.OnApprove(); else _delivery.OnReject();
                inFlight = _delivery.ResponseInFlight;
            }
            else if (action == StampAction.Approve) controller.ApprovePending();
            else controller.RejectPending();

            // The model dips onto the paper, and the paper takes a pale provisional mark in the words
            // of the request. Neither claims an outcome: the dark mark and the outcome cue come from
            // ApprovalStampController on decision.resolved, and a failed response removes the pale one.
            if (pressVisual != null) { _dipping = true; _dipT = 0f; }
            if (inFlight && _marks != null) _marks.ShowProvisional(action == StampAction.Approve);

            Play(_thunk);
            StopAllCoroutines();
            StartCoroutine(Pop());
            Debug.Log($"[MuseDesk] self-stamp {action} -> ApprovePending/RejectPending for {decisionId}");
        }

        IEnumerator Pop()
        {
            if (headRenderer == null) yield break;
            var tr = headRenderer.transform;
            float t = 0f, dur = 0.18f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float s = 1f + 0.25f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
                tr.localScale = _headBaseScale * s;
                yield return null;
            }
            tr.localScale = _headBaseScale;
        }

        void Play(AudioClip c) { if (c != null && _audio != null) { _audio.pitch = 1f; _audio.PlayOneShot(c, volume); } }

        // ── procedural cues (self-contained; FeedbackController's Play is private + event-bound) ──
        static AudioClip Thunk()
        {
            int rate = 44100, n = (int)(rate * 0.14f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate, env = Mathf.Exp(-16f * t);
                d[i] = 0.9f * env * (Mathf.Sin(2f * Mathf.PI * 150f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 90f * t));
            }
            var c = AudioClip.Create("stamp_thunk", n, 1, rate, false); c.SetData(d, 0); return c;
        }

        static AudioClip Nope()
        {
            int rate = 44100, per = (int)(rate * 0.1f), n = per * 2;
            var d = new float[n];
            float[] notes = { 330f, 247f };
            for (int k = 0; k < 2; k++)
                for (int i = 0; i < per; i++)
                {
                    float t = (float)i / rate, env = Mathf.Exp(-6f * t);
                    d[k * per + i] = 0.5f * env * Mathf.Sin(2f * Mathf.PI * notes[k] * t);
                }
            var c = AudioClip.Create("stamp_nope", n, 1, rate, false); c.SetData(d, 0); return c;
        }
    }
}
