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

        [Header("Hand push (deterministic)")]
        [Tooltip("Tracked hand points (e.g. Left/RightHandAnchor). When set, push is driven by their " +
                 "proximity instead of physics trigger events, which don't fire reliably against the " +
                 "kinematic agent on-device.")]
        public Transform[] handPoints;
        [Tooltip("Horizontal distance (m) within which a hand nudges the agent.")]
        public float pushRadius = 0.09f;

        Vector3 _home;
        Vector3 _offset;
        float _contact;   // >0 while a hand is touching this frame-window (trigger fallback)

        void Awake() { _home = transform.localPosition; }
        void OnEnable() { _offset = Vector3.zero; _contact = 0f; }

        // Physics fallback (used only when no handPoints are wired).
        void OnTriggerStay(Collider other)
        {
            if (handPoints != null && handPoints.Length > 0) return;
            if (other.transform.IsChildOf(transform)) return;
            Vector3 contact = other.ClosestPoint(transform.position);
            Vector3 away = transform.position - contact;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) return;
            away.Normalize();
            Vector3 local = transform.parent ? transform.parent.InverseTransformDirection(away) : away;
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

            // Primary path: deterministic proximity from tracked hands.
            if (handPoints != null && handPoints.Length > 0)
            {
                Vector3 pushWorld = Vector3.zero;
                for (int i = 0; i < handPoints.Length; i++)
                {
                    var h = handPoints[i];
                    if (h == null) continue;
                    Vector3 d = transform.position - h.position; d.y = 0f;
                    float dist = d.magnitude;
                    if (dist < pushRadius && dist > 1e-4f)
                        pushWorld += d / dist * ((pushRadius - dist) / pushRadius) * maxOffset;
                }
                Vector3 pushLocal = transform.parent ? transform.parent.InverseTransformDirection(pushWorld) : pushWorld;
                pushLocal = Vector3.ClampMagnitude(pushLocal, maxOffset);
                if (pushLocal.sqrMagnitude > 1e-8f)
                    _offset = Vector3.Lerp(_offset, pushLocal, Time.deltaTime * pushStrength * 12f);
                else
                    _offset = Vector3.Lerp(_offset, Vector3.zero, Time.deltaTime * springback);
                transform.localPosition = _home + _offset;
                return;
            }

            // Fallback path: spring back once the hand trigger has left.
            _contact -= Time.deltaTime;
            if (_contact <= 0f)
                _offset = Vector3.Lerp(_offset, Vector3.zero, Time.deltaTime * springback);
            transform.localPosition = _home + _offset;
        }
    }
}
