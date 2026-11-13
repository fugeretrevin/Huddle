using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Squishy body lean: when the user's hand/fingertips push near or overlap the agent, the
    /// agent's upper torso (Chest / Spine / Bob) TILTS/LEANS smoothly away from the contact, then
    /// springs upright when the hand leaves. Works both via OnTriggerStay (hand capsules) AND
    /// deterministic HandFingertips proximity so Quest hand tracking always feels responsive.
    ///
    /// Uses BoneRotationLayer in LateUpdate after Animator and AgentIdleLife (Order 400) so it
    /// layers cleanly over idle/work clips and never accumulates drift. Yields while walking.
    /// </summary>
    [DefaultExecutionOrder(400)]
    public class AgentTilt : MonoBehaviour
    {
        [Tooltip("Torso bone/transform to lean (defaults to Chest, then Spine, then Bob).")]
        public Transform bob;
        [Tooltip("Max lean angle, degrees.")]
        public float maxTilt = 16f;
        [Tooltip("How fast the lean eases back to upright once the hand leaves.")]
        public float springback = 7f;
        [Tooltip("Lean gained per second of contact.")]
        public float pushStrength = 0.85f;
        [Tooltip("Fingertip proximity radius (m) around chest height for deterministic push.")]
        public float touchRadius = 0.085f;
        [Tooltip("While walking, yield to the walk animation (no lean).")]
        public AgentMovement movement;

        BoneRotationLayer _layer;
        Vector3 _lean;       // world-space horizontal lean direction * magnitude, |.| <= 1
        float _contact;

        void Awake()
        {
            if (movement == null) movement = GetComponent<AgentMovement>();
            if (bob == null)
            {
                var all = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].name == "Chest" || all[i].name == "Bob") { bob = all[i]; break; }
                }
                if (bob == null)
                {
                    for (int i = 0; i < all.Length; i++)
                        if (all[i].name == "Spine") { bob = all[i]; break; }
                }
            }
            if (bob == null) bob = transform;
        }

        void OnEnable() { _lean = Vector3.zero; _contact = 0f; }
        void OnDisable() => _layer.Release(bob);

        void OnTriggerStay(Collider other)
        {
            if (bob == null || other.transform.IsChildOf(transform)) return;
            PushFromPoint(other.ClosestPoint(bob.position));
        }

        void PushFromPoint(Vector3 contact)
        {
            Vector3 away = bob.position - contact;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) return;
            away.Normalize();
            Vector3 want = Vector3.ClampMagnitude(_lean + away, 1f);
            _lean = Vector3.Lerp(_lean, want, Time.deltaTime * pushStrength * 14f);
            _contact = 0.14f;
        }

        void LateUpdate()
        {
            if (bob == null) return;
            if (movement != null && movement.IsMoving)
            {
                _lean = Vector3.zero;
                _contact = 0f;
                _layer.Release(bob);
                return;
            }

            // Deterministic fingertip proximity (works even without physics hand capsules).
            IReadOnlyList<Transform> tips = HandFingertips.Get();
            if (tips != null && tips.Count > 0)
            {
                Vector3 chestPos = bob.position;
                Vector3 accum = Vector3.zero;
                for (int i = 0; i < tips.Count; i++)
                {
                    var h = tips[i];
                    if (h == null) continue;
                    Vector3 d = chestPos - h.position;
                    float dist = d.magnitude;
                    if (dist < touchRadius && dist > 1e-4f)
                    {
                        d.y = 0f;
                        if (d.sqrMagnitude > 1e-6f)
                            accum += d.normalized * ((touchRadius - dist) / touchRadius);
                    }
                }
                if (accum.sqrMagnitude > 1e-6f)
                {
                    Vector3 want = Vector3.ClampMagnitude(accum, 1f);
                    _lean = Vector3.Lerp(_lean, want, Time.deltaTime * pushStrength * 16f);
                    _contact = 0.14f;
                }
            }

            _contact -= Time.deltaTime;
            if (_contact <= 0f)
                _lean = Vector3.Lerp(_lean, Vector3.zero, Time.deltaTime * springback);

            float mag = _lean.magnitude;
            if (mag < 1e-4f)
            {
                _layer.Release(bob);
                return;
            }

            Vector3 dir = _lean / mag;
            Vector3 axis = Vector3.Cross(Vector3.up, dir);
            if (axis.sqrMagnitude < 1e-6f) { _layer.Release(bob); return; }

            Quaternion worldDelta = Quaternion.AngleAxis(Mathf.Clamp01(mag) * maxTilt, axis.normalized);
            _layer.Apply(bob, worldDelta);
        }
    }
}
