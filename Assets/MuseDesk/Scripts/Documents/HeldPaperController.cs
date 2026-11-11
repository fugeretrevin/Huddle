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

        PropertyInfo _selCount;
        bool _held;
        bool _expandedWhileHeld;

        void OnEnable()
        {
            // Fresh decision: start collapsed, facing the user, and placed normally.
            _held = false;
            _expandedWhileHeld = false;
            if (face) face.enabled = true;
            if (placer) placer.enabled = true;
        }

        void Update()
        {
            if (grabbable == null) return;
            if (_selCount == null)
            {
                _selCount = grabbable.GetType().GetProperty("SelectingPointsCount",
                    BindingFlags.Instance | BindingFlags.Public);
                if (_selCount == null) { enabled = false; return; }
            }

            bool nowHeld;
            try { nowHeld = (int)_selCount.GetValue(grabbable) > 0; }
            catch { return; }

            if (nowHeld && !_held) OnGrabbed();
            else if (!nowHeld && _held) OnReleased();
            _held = nowHeld;
        }

        void OnGrabbed()
        {
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

        void OnReleased()
        {
            // Leave it where the user put it; just let it face them again if asked.
            if (reFaceOnRelease && face) face.enabled = true;
        }
    }
}
