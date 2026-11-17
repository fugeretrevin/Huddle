using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Squishy, nudgeable arm: lives on UpperArm_L / UpperArm_R (or Arm_L / Arm_R). When the
    /// user's hand capsules or tracked fingertips touch/push near the arm, it swings away springily
    /// then eases back to its animated pose. Uses BoneRotationLayer at LateUpdate Order 410 so it
    /// layers cleanly over Animator + AgentIdleLife without fighting keyframes or accumulating drift.
    /// </summary>
    [DefaultExecutionOrder(410)]
    public class AgentArmNudge : MonoBehaviour
    {
        [Tooltip("Max swing angle, degrees.")]
        public float maxNudge = 28f;
        [Tooltip("How fast the arm eases back to rest once the hand leaves.")]
        public float springback = 8f;
        [Tooltip("Swing gained per second of contact.")]
        public float pushStrength = 0.9f;
        [Tooltip("Fingertip proximity radius (m) along the arm.")]
        public float touchRadius = 0.06f;
        [Tooltip("While walking, yield to the walk arm-swing (no nudge).")]
        public AgentMovement movement;

        BoneRotationLayer _layer;
        Vector3 _nudgeWorldAxisMag;   // world rotation axis * magnitude, |.| <= 1
        float _contact;
        Transform _agentRoot;

        void Awake()
        {
            var view = GetComponentInParent<AgentView>();
            if (movement == null && view != null) movement = view.GetComponent<AgentMovement>();
            _agentRoot = view != null ? view.transform : transform.root;
        }

        void OnEnable() { _nudgeWorldAxisMag = Vector3.zero; _contact = 0f; }
        void OnDisable() => _layer.Release(transform);

        void OnTriggerStay(Collider other)
        {
            if (_agentRoot != null && other.transform.IsChildOf(_agentRoot)) return;
            if (other.transform.IsChildOf(transform)) return;
            NudgeFromPoint(other.ClosestPoint(transform.position));
        }

        void NudgeFromPoint(Vector3 contact)
        {
            Vector3 armCenter = transform.position + Vector3.down * 0.025f;
            Vector3 away = armCenter - contact;
            if (away.sqrMagnitude < 1e-6f) return;
            away.Normalize();

            Vector3 axis = Vector3.Cross(Vector3.down, away);
            if (axis.sqrMagnitude < 1e-6f) return;
            axis.Normalize();

            Vector3 want = Vector3.ClampMagnitude(_nudgeWorldAxisMag + axis, 1f);
            _nudgeWorldAxisMag = Vector3.Lerp(_nudgeWorldAxisMag, want, Time.deltaTime * pushStrength * 14f);
            _contact = 0.14f;
        }

        void LateUpdate()
        {
            if (movement != null && movement.IsMoving)
            {
                _nudgeWorldAxisMag = Vector3.zero;
                _contact = 0f;
                _layer.Release(transform);
                return;
            }

            IReadOnlyList<Transform> tips = HandFingertips.Get();
            if (tips != null && tips.Count > 0)
            {
                Vector3 armCenter = transform.position + Vector3.down * 0.025f;
                Vector3 bestAway = Vector3.zero;
                float bestStrength = 0f;
                for (int i = 0; i < tips.Count; i++)
                {
                    var h = tips[i];
                    if (h == null) continue;
                    Vector3 d = armCenter - h.position;
                    float dist = d.magnitude;
                    if (dist < touchRadius && dist > 1e-4f)
                    {
                        float s = (touchRadius - dist) / touchRadius;
                        if (s > bestStrength) { bestStrength = s; bestAway = d.normalized; }
                    }
                }
                if (bestStrength > 0f)
                {
                    Vector3 axis = Vector3.Cross(Vector3.down, bestAway);
                    if (axis.sqrMagnitude > 1e-6f)
                    {
                        Vector3 want = axis.normalized * Mathf.Clamp01(bestStrength * 1.25f);
                        _nudgeWorldAxisMag = Vector3.Lerp(_nudgeWorldAxisMag, want, Time.deltaTime * pushStrength * 16f);
                        _contact = 0.14f;
                    }
                }
            }

            _contact -= Time.deltaTime;
            if (_contact <= 0f)
                _nudgeWorldAxisMag = Vector3.Lerp(_nudgeWorldAxisMag, Vector3.zero, Time.deltaTime * springback);

            float mag = _nudgeWorldAxisMag.magnitude;
            if (mag < 1e-4f)
            {
                _layer.Release(transform);
                return;
            }

            Quaternion worldDelta = Quaternion.AngleAxis(Mathf.Clamp01(mag) * maxNudge, _nudgeWorldAxisMag / mag);
            _layer.Apply(transform, worldDelta);
        }
    }
}
