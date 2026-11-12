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
        [Tooltip("Scale of the paper while the agent holds it out (relative to full).")]
        public float heldScale = 0.42f;
        [Tooltip("Local rotation while held by the agent (face the offered side toward the user).")]
        public Vector3 heldLocalEuler = new Vector3(0f, 0f, 0f);
        [Tooltip("Seconds to expand up to full size when taken.")]
        public float expandSeconds = 0.4f;

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
            if (agentHoldPoint != null)
            {
                // Carried in the agent's hand: a small blank paper (no content/buttons), parented,
                // offered toward the user at an angle; no billboard/placer until taken.
                transform.SetParent(agentHoldPoint, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.Euler(heldLocalEuler);
                transform.localScale = _fullScale * heldScale;
                if (face) face.enabled = false;
                if (placer) placer.enabled = false;
                SetContent(false);
            }
            else
            {
                if (face) face.enabled = true;
                if (placer) placer.enabled = true;
                SetContent(true);
            }
        }

        void SetContent(bool on)
        {
            if (hideWhileHeld == null) return;
            foreach (var go in hideWhileHeld) if (go != null) go.SetActive(on);
        }

        void LateUpdate()
        {
            // Once fully taken, keep it at full size so nothing shrinks it back.
            if (_scaleLocked) transform.localScale = _fullScale;
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

            bool nowHeld = _selCount() > 0;

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
                SetContent(true);          // reveal title/info/buttons now that it's taken
                StopAllCoroutines();
                StartCoroutine(ScaleTo(_fullScale, expandSeconds));
            }
            // Stop fighting the hand.
            if (face) face.enabled = false;
            if (placer) placer.enabled = false;
            // Pulling the paper up to read is the natural "show me the details" gesture.
            if (!_expandedWhileHeld && delivery != null)
            {
                delivery.OnExpand();
                _expandedWhileHeld = true;
            }
        }

        IEnumerator ScaleTo(Vector3 target, float seconds)
        {
            Vector3 from = transform.localScale;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                transform.localScale = Vector3.Lerp(from, target, t / seconds);
                yield return null;
            }
            transform.localScale = target;
            _scaleLocked = true;   // from here on, hold it at full size (LateUpdate enforces)

        }

        void OnReleased()
        {
            // Leave it where the user put it; just let it face them again if asked.
            if (reFaceOnRelease && face) face.enabled = true;
        }
    }
}
