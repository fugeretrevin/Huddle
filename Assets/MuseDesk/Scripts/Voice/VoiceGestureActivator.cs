using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Spatial;

namespace MuseDesk.Voice
{
    /// <summary>
    /// Optional hands-only way to open the microphone without reaching for the key: pinch thumb and
    /// index together in front of the mouth and hold for half a second. It reads the fingertips of
    /// the existing hand visuals (HandFingertips; no new rig, no second interaction framework).
    ///
    /// OFF by default (<see cref="gestureEnabled"/>). Reasons: the headset tracks hands poorly this
    /// close to the face, and a pinch is also how a paper is held up to read, so until it has been
    /// tried on the head it must not be able to open a microphone by accident. It only ever OPENS
    /// the microphone (VoiceCommandCenter.BeginFromGesture); it never closes it and never sends a
    /// command by itself.
    /// </summary>
    public class VoiceGestureActivator : MonoBehaviour
    {
        public VoiceCommandCenter center;
        [Tooltip("Master switch. Leave off until the gesture was validated on the headset.")]
        public bool gestureEnabled = false;

        [Tooltip("Thumb tip to index tip distance that counts as a pinch (m).")]
        public float pinchDistance = 0.02f;
        [Tooltip("Seconds the pinch must be held in the zone.")]
        public float holdSeconds = 0.5f;
        [Tooltip("Seconds before the gesture can fire again.")]
        public float cooldownSeconds = 2.5f;
        [Tooltip("Zone in front of the mouth, in head space: x half-width, y below the eyes (min, max), z ahead (min, max), metres.")]
        public float zoneHalfWidth = 0.12f;
        public Vector2 zoneBelowEyes = new Vector2(0.04f, 0.22f);
        public Vector2 zoneAhead = new Vector2(0.04f, 0.28f);

        float _held, _readyAt;

        /// <summary>0..1 progress of the hold (for a driver or a visual).</summary>
        public float Progress => holdSeconds > 0f ? Mathf.Clamp01(_held / holdSeconds) : 0f;

        void Update()
        {
            if (!gestureEnabled || center == null) { _held = 0f; return; }
            if (center.IsListening || Time.unscaledTime < _readyAt) { _held = 0f; return; }
            Transform head = RemoteNearUser.Head();
            if (head == null) { _held = 0f; return; }

            // Five tips per hand, in the order thumb, index, middle, ring, little.
            IReadOnlyList<Transform> tips = HandFingertips.Get();
            bool pinching = false;
            for (int i = 0; i + 1 < tips.Count; i += 5)
            {
                Transform thumb = tips[i], index = tips[i + 1];
                if (thumb == null || index == null) continue;
                if ((thumb.position - index.position).sqrMagnitude > pinchDistance * pinchDistance) continue;
                Vector3 local = head.InverseTransformPoint((thumb.position + index.position) * 0.5f);
                if (Mathf.Abs(local.x) > zoneHalfWidth) continue;
                if (-local.y < zoneBelowEyes.x || -local.y > zoneBelowEyes.y) continue;
                if (local.z < zoneAhead.x || local.z > zoneAhead.y) continue;
                pinching = true;
                break;
            }

            if (!pinching) { _held = 0f; return; }
            _held += Time.unscaledDeltaTime;
            if (_held < holdSeconds) return;
            _held = 0f;
            _readyAt = Time.unscaledTime + cooldownSeconds;
            center.BeginFromGesture();
        }
    }
}
