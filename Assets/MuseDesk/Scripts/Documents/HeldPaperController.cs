using System.Collections;
using System.Reflection;
using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Documents
{
    /// <summary>
    /// Makes the delivered decision paper feel like a real sheet you can pick up: when the hand
    /// grabs it (Oculus.Interaction.Grabbable.SelectingPointsCount > 0) the paper expands to full
    /// detail and we stop fighting the hand — FaceUser billboarding and the one-shot placer are
    /// disabled for the duration of the grab. On release the paper stays where it was put and
    /// (optionally) re-faces the user from its new spot. The grab itself never approves anything;
    /// approvals still go only through the poke buttons -> backend. This reads the grab state by
    /// reflection so the runtime assembly keeps no hard dependency on the Interaction SDK.
    /// </summary>
    public class HeldPaperController : MonoBehaviour
    {
        [Tooltip("The Oculus.Interaction.Grabbable on this paper (assigned by the builder).")]
        public Component grabbable;
        public FaceUser face;
        public PlaceInFrontOnStart placer;
        public DocumentDeliveryController delivery;

        [Tooltip("Re-enable yaw billboard toward the user after the paper is released.")]
        public bool reFaceOnRelease = true;

        [Header("Agent handoff")]
        [Tooltip("If set, the paper is carried (small) in this agent hand point until the user takes it.")]
        public Transform agentHoldPoint;
        [Tooltip("Scale of the paper while the agent holds it out (relative to full world size).")]
        public float heldScale = 0.18f;
        [Tooltip("Local rotation while held by the agent (face the offered side toward the user).")]
        public Vector3 heldLocalEuler = new Vector3(0f, 0f, 0f);
        [Tooltip("Seconds to expand up to full size when taken.")]
        public float expandSeconds = 0.35f;

        [Tooltip("Content (badge/title/text/buttons/details) hidden while the agent holds the blank " +
                 "paper, revealed when the user takes it.")]
        public GameObject[] hideWhileHeld;

        System.Func<int> _selCount;
        bool _selResolved;
        bool _held;
        bool _expandedWhileHeld;
        bool _taken;
        bool _scaleLocked;
        Vector3 _fullScale = Vector3.one;
        bool _capturedScale;

        void Awake()
        {
            if (!_capturedScale) { _fullScale = transform.localScale; _capturedScale = true; }
        }

        void OnEnable()
        {
            // Fresh decision: start collapsed.
            _held = false;
            _expandedWhileHeld = false;
            _taken = false;
            _scaleLocked = false;
            _filing = false;
            _readableSince = float.MaxValue;
            if (agentHoldPoint != null)
            {
                // Carried in the agent's hand: a compact miniature sheet (compensated for rig bone
                // scale, since Blender-exported armature bones have 100x lossyScale), parented,
                // offered toward the user; no billboard/placer until taken.
                transform.SetParent(agentHoldPoint, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.Euler(heldLocalEuler);
                Vector3 p = agentHoldPoint.lossyScale;
                Vector3 targetWorld = _fullScale * heldScale;
                transform.localScale = new Vector3(
                    targetWorld.x / Mathf.Max(1e-5f, Mathf.Abs(p.x)),
                    targetWorld.y / Mathf.Max(1e-5f, Mathf.Abs(p.y)),
                    targetWorld.z / Mathf.Max(1e-5f, Mathf.Abs(p.z)));
                if (face) face.enabled = false;
                if (placer) placer.enabled = false;
                SetContent(false);
            }
            else
            {
                if (face) face.enabled = true;
                if (placer) placer.enabled = true;
                SetContent(true);
                // No agent hand-over configured: the paper is shown full size and readable at once.
                transform.localScale = _fullScale;
                _scaleLocked = true;
                _readableSince = Time.time;
            }
        }

        void SetContent(bool on, bool includeActions = true)
        {
            if (hideWhileHeld == null) return;
            foreach (var go in hideWhileHeld)
            {
                if (go == null) continue;
                if (!includeActions && go.name == "Actions") continue;
                go.SetActive(on);
            }
        }

        IEnumerator ExpandAndEnableActions()
        {
            yield return ScaleTo(_fullScale, expandSeconds);
            SetContent(true, includeActions: true);
            BroadcastMessage("SyncToCurrentScale", SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>True once the user has taken the paper out of the agent's hand (or there is no
        /// agent hand-over configured).</summary>
        public bool IsTaken => _taken || agentHoldPoint == null;

        /// <summary>True while a hand is holding the paper.</summary>
        public bool IsHeld => _held;

        /// <summary>
        /// The paper can be decided on: it has been taken, has finished expanding, and its content has
        /// been in front of the user for a moment. The physical stamps require this, so brushing a
        /// stamp past the blank paper in the agent's hand can never authorise anything.
        /// </summary>
        public bool ReadyForDecision => IsTaken && _scaleLocked && Time.time - _readableSince >= minReadSeconds;

        [Tooltip("Seconds the expanded paper must have been readable before a stamp counts.")]
        public float minReadSeconds = 0.8f;
        float _readableSince = float.MaxValue;
        bool _filing;

        /// <summary>Hand the transform over to the filing animation: stop billboarding, stop holding
        /// the scale, and ignore grabs until the paper is next shown.</summary>
        public void BeginFiling()
        {
            _filing = true;
            _scaleLocked = false;
            StopAllCoroutines();
            if (face) face.enabled = false;
            if (placer) placer.enabled = false;
        }

        /// <summary>Full (expanded) local scale, for restoring after a filing animation.</summary>
        public Vector3 FullScale => _fullScale;

        void LateUpdate()
        {
            // Once fully taken, keep it at full size so nothing shrinks it back.
            if (_scaleLocked && !_filing) transform.localScale = _fullScale;
        }

        void Update()
        {
            if (grabbable == null) return;
            if (!_selResolved)
            {
                _selCount = MuseDesk.Workspace.GrabState.BindSelectingPoints(grabbable);
                _selResolved = true;
                if (_selCount == null)
                {
                    Debug.LogWarning("[MuseDesk] decision paper: grabbable has no SelectingPointsCount; grab-to-expand disabled.");
                    enabled = false; return;
                }
            }

            bool nowHeld = !_filing && _selCount() > 0;

            if (nowHeld && !_held) OnGrabbed();
            else if (!nowHeld && _held) OnReleased();
            _held = nowHeld;
        }

        void OnGrabbed()
        {

            // Taken from the agent's hand: detach to the world (keep world pose) and grow to full size
            // as the user lifts it to read — the physical "pick it up and hold it up" gesture.
            if (agentHoldPoint != null && !_taken)
            {
                _taken = true;
                transform.SetParent(null, true);
                SetContent(true, includeActions: false); // reveal readable sheet while unfolding; enable buttons once full size
                StopAllCoroutines();
                StartCoroutine(ExpandAndEnableActions());
            }
            // Stop fighting the hand.
            if (face) face.enabled = false;
            if (placer) placer.enabled = false;
            // The paper opens on its summary (one line, the recommendation, the keys). The longer
            // explanation stays behind the Details key: summary first, detail on demand.
            _expandedWhileHeld = true;
        }

        IEnumerator ScaleTo(Vector3 target, float seconds)
        {
            Vector3 from = transform.localScale;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                // Ease out with a slight overshoot so the paper "unfolds" instead of inflating linearly.
                float k = Mathf.Clamp01(t / seconds);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                float overshoot = Mathf.Sin(k * Mathf.PI) * 0.04f;
                transform.localScale = Vector3.LerpUnclamped(from, target, e + overshoot);
                yield return null;
            }
            transform.localScale = target;
            _scaleLocked = true;   // from here on, hold it at full size (LateUpdate enforces)
            _readableSince = Time.time;
            BroadcastMessage("SyncToCurrentScale", SendMessageOptions.DontRequireReceiver);

        }

        void OnReleased()
        {
            // Leave it where the user put it; just let it face them again if asked.
            if (reFaceOnRelease && face) face.enabled = true;
        }
    }
}
