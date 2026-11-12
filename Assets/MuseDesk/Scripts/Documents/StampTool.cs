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
        [Range(0f, 20f)] public float returnLerp = 8f;

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

        void Awake()
        {
            // Home is PARENT-RELATIVE: WorkspacePlacement moves WorkspaceRoot after Awake, so a world
            // pose captured here would drag the stamp back to the authored spot, off the desk.
            _homePos = transform.localPosition;
            _homeRot = transform.localRotation;
            _homeCaptured = true;
            if (headRenderer != null) _headBaseScale = headRenderer.transform.localScale;

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

        void Update()
        {
            bool held = IsHeld();

            if (!held)
            {
                _dwell = 0f;
                if (returnHome && _homeCaptured)
                {
                    float k = 1f - Mathf.Exp(-returnLerp * Time.deltaTime);
                    transform.localPosition = Vector3.Lerp(transform.localPosition, _homePos, k);
                    transform.localRotation = Quaternion.Slerp(transform.localRotation, _homeRot, k);
                }
                return;
            }

            // Held: look for a press onto the paper zone.
            if (inkHead == null || stampZone == null || controller == null) return;
            if (Time.time < _cooldownUntil) return;

            bool zoneReady = stampZone.gameObject.activeInHierarchy;
            bool contact = zoneReady &&
                (inkHead.position - stampZone.position).sqrMagnitude <= contactRadius * contactRadius;

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

            // The ONLY state change: the real approval path. Never fabricate a result.
            if (action == StampAction.Approve) controller.ApprovePending();
            else controller.RejectPending();

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

        void Play(AudioClip c) { if (c != null && _audio != null) _audio.PlayOneShot(c, volume); }

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
