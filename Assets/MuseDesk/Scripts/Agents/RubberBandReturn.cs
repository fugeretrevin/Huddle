using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Gives an agent soft, springy collision: when the user's hand (physics capsules) overlaps the
    /// agent's trigger volume, the agent is nudged away from the contact and then eases back to its
    /// home spot — so hands don't phase straight through, but the agent never gets knocked out of
    /// place permanently. Purely cosmetic: we move our own localPosition (kinematic), never real
    /// physics, and never touch task/agent state.
    /// </summary>
    public class RubberBandReturn : MonoBehaviour
    {
        [Tooltip("Max displacement from home, metres.")]
        public float maxOffset = 0.05f;
        [Tooltip("How fast the agent eases back to home.")]
        public float springback = 7f;
        [Tooltip("Push gained per second of contact.")]
        public float pushStrength = 0.6f;

        [Tooltip("While this agent is walking, yield to the walk animation (no springback).")]
        public AgentMovement movement;

        Vector3 _home;
        Vector3 _offset;
        float _contact;   // >0 while a hand is touching this frame-window

        void Awake() { _home = transform.localPosition; }
        void OnEnable() { _offset = Vector3.zero; _contact = 0f; }

        void OnTriggerStay(Collider other)
        {
            // Ignore self / children.
            if (other.transform.IsChildOf(transform)) return;
            Vector3 contact = other.ClosestPoint(transform.position);
            Vector3 away = transform.position - contact;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) return;
            away.Normalize();
            Vector3 local = transform.parent ? transform.parent.InverseTransformDirection(away) : away;
            // Ease toward the pushed spot (smooth, not additive-jittery), and hold while touched.
            Vector3 want = Vector3.ClampMagnitude(_offset + local * maxOffset, maxOffset);
            _offset = Vector3.Lerp(_offset, want, Time.deltaTime * pushStrength * 12f);
            _contact = 0.12f;
        }

        void Update()
        {
            // Walking is driven by AgentMovement on this same transform — don't fight it.
            if (movement != null && movement.IsMoving)
            {
                _home = transform.localPosition;   // re-home wherever the walk leaves us
                _offset = Vector3.zero;
                _contact = 0f;
                return;
            }
            // Only spring back once the hand has LEFT — holding during contact kills the jitter.
            _contact -= Time.deltaTime;
            if (_contact <= 0f)
                _offset = Vector3.Lerp(_offset, Vector3.zero, Time.deltaTime * springback);
            transform.localPosition = _home + _offset;
        }
    }
}
