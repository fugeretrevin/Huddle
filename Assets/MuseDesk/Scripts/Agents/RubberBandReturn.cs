using System.Collections.Generic;
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
    // Writer ownership of the agent root localPosition: AgentMovement owns it while IsMoving, this component owns it
    // otherwise (it already yields below). Execution order is pinned AFTER AgentMovement (order 0) so on the frame a
    // walk ends this component always sees the final arrival pose and re-homes from it, never a stale one.
    [DefaultExecutionOrder(20)]
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
        [Tooltip("Prefer the real tracked FINGERTIPS (resolved at runtime) so fingers — not the wrist — " +
                 "push the agent. Falls back to handPoints, then to the physics trigger.")]
        public bool useFingertips = true;
        [Tooltip("Fallback tracked hand points (e.g. Left/RightHandAnchor) if fingertips can't resolve.")]
        public Transform[] handPoints;
        [Tooltip("Distance (m) from a fingertip within which the agent is nudged. Small so the finger " +
                 "must actually reach the body.")]
        public float pushRadius = 0.06f;

        Vector3 _home;
        Vector3 _offset;
        float _contact;   // >0 while a hand is touching this frame-window (trigger fallback)
        bool _wasMoving;

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
                _wasMoving = true;
                return;
            }
            if (_wasMoving)
            {
                // The walk ended since our last frame. Take the home from where it actually left
                // the agent: whichever of the two components updated first on the arrival frame,
                // the position we remembered above can be one frame (and one walk bob) stale.
                _wasMoving = false;
                _home = transform.localPosition;
            }

            // Primary path: deterministic proximity from tracked hands. Prefer real fingertips so
            // fingers push the agent, not the wrist; fall back to the wired hand points.
            IReadOnlyList<Transform> points = null;
            if (useFingertips)
            {
                var tips = HandFingertips.Get();
                if (tips != null && tips.Count > 0) points = tips;
            }
            if (points == null && handPoints != null && handPoints.Length > 0) points = handPoints;

            if (points != null)
            {
                // Measure from the agent's chest, not its feet, so a fingertip at body height contacts.
                Vector3 body = transform.position + Vector3.up * 0.12f;
                Vector3 pushWorld = Vector3.zero;
                for (int i = 0; i < points.Count; i++)
                {
                    var h = points[i];
                    if (h == null) continue;
                    Vector3 d = body - h.position;
                    float dist = d.magnitude;
                    if (dist < pushRadius && dist > 1e-4f)
                    {
                        d.y = 0f;
                        if (d.sqrMagnitude < 1e-6f) continue;
                        pushWorld += d.normalized * ((pushRadius - dist) / pushRadius) * maxOffset;
                    }
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
