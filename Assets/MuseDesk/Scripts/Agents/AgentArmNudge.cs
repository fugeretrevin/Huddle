using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Squishy, nudgeable arm: lives on an arm pivot (Arm_L / Arm_R, a child of Bob). When the user's
    /// hand (physics capsules) overlaps this arm's trigger collider, the arm swings away from the
    /// contact a few degrees (springy) then eases back to its authored rest pose. Purely cosmetic /
    /// kinematic — the pivot only rotates, never translates, and always returns to rest.
    ///
    /// COMPOSITION with AgentView (the key risk):
    ///   AgentView writes this arm's localRotation EVERY frame in Update(). For idle/rest states it
    ///   does so via Slerp(arm.localRotation, target, k) — i.e. it READS BACK the current rotation.
    ///   If we simply post-multiplied a nudge each LateUpdate, AgentView would read our nudged value
    ///   next frame and fold it into its base, and the feedback loop would DIVERGE (base += nudge/k).
    ///
    ///   So we split into two phases around AgentView:
    ///     - Update() [DefaultExecutionOrder(-100), runs BEFORE AgentView's Update(order 0)]:
    ///         STRIP last frame's nudge — restore the clean base pose we captured — so AgentView reads
    ///         a nudge-free rotation and its Slerp cannot accumulate our offset.
    ///     - LateUpdate() [after all Update()]:
    ///         CAPTURE the clean pose AgentView produced this frame as the new base, then apply the
    ///         current nudge on top of it.
    ///   Walk() writes the arm absolutely (no read-back), so the strip is harmless there and we yield
    ///   while moving anyway.
    ///
    /// This arm pivot needs its OWN kinematic Rigidbody so these trigger callbacks are delivered to
    /// THIS GameObject rather than being captured by the agent root's Rigidbody (compound-collider
    /// routing). See BuildInteractiveAgents for the setup + VERIFY notes.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class AgentArmNudge : MonoBehaviour
    {
        [Tooltip("Max swing angle, degrees.")]
        public float maxNudge = 18f;
        [Tooltip("How fast the arm eases back to rest once the hand leaves.")]
        public float springback = 9f;
        [Tooltip("Swing gained per second of contact.")]
        public float pushStrength = 0.7f;
        [Tooltip("While walking, yield to the walk arm-swing (no nudge).")]
        public AgentMovement movement;

        // The clean, AgentView-authored pose captured each LateUpdate (before we add the nudge).
        Quaternion _base = Quaternion.identity;
        bool _hasBase;
        // Accumulated swing as an axis*magnitude in the pivot's LOCAL frame, |.| <= 1.
        Vector3 _nudge;
        float _contact;
        // The whole agent's root — so we ignore this agent's OWN colliders (root push-sphere,
        // the other arm). Those are kinematic-rigidbody triggers too now, so without this guard the
        // arm would be perpetually "nudged" by its own body.
        Transform _agentRoot;

        void Awake()
        {
            var view = GetComponentInParent<AgentView>();
            _agentRoot = view != null ? view.transform : transform.root;
        }

        void OnEnable() { _nudge = Vector3.zero; _contact = 0f; _hasBase = false; }

        // Phase 1: runs before AgentView.Update() — hand the base back so AgentView's Slerp stays clean.
        void Update()
        {
            if (_hasBase) transform.localRotation = _base;
        }

        void OnTriggerStay(Collider other)
        {
            // Ignore any collider belonging to THIS agent (own children, the root push-sphere,
            // the other arm). Only external colliders (the user's hand capsules) should nudge us.
            if (_agentRoot != null && other.transform.IsChildOf(_agentRoot)) return;
            if (other.transform.IsChildOf(transform)) return;

            Vector3 contact = other.ClosestPoint(transform.position);
            Vector3 away = transform.position - contact;
            if (away.sqrMagnitude < 1e-6f) return;
            away.Normalize();

            // The arm hangs along the pivot's local -Y. To swing the hand toward `away`, rotate about
            // axis = down x away (computed in the pivot's local frame).
            Vector3 aLocal = transform.InverseTransformDirection(away);
            Vector3 axis = Vector3.Cross(Vector3.down, aLocal);
            if (axis.sqrMagnitude < 1e-6f) return;
            axis.Normalize();

            Vector3 want = Vector3.ClampMagnitude(_nudge + axis, 1f);
            _nudge = Vector3.Lerp(_nudge, want, Time.deltaTime * pushStrength * 12f);
            _contact = 0.12f;
        }

        // Phase 2: runs after AgentView.Update() — capture the clean base, then add the nudge on top.
        void LateUpdate()
        {
            _base = transform.localRotation;   // AgentView's authored pose this frame (nudge was stripped)
            _hasBase = true;

            if (movement != null && movement.IsMoving) { _nudge = Vector3.zero; _contact = 0f; return; }

            _contact -= Time.deltaTime;
            if (_contact <= 0f)
                _nudge = Vector3.Lerp(_nudge, Vector3.zero, Time.deltaTime * springback);

            float mag = _nudge.magnitude;
            if (mag < 1e-5f) return;

            Quaternion offset = Quaternion.AngleAxis(Mathf.Clamp01(mag) * maxNudge, _nudge / mag);
            // Post-multiply: the swing axis is expressed in the arm's own local frame (same convention
            // AgentView uses: _armHome * Euler(swing,0,0)).
            transform.localRotation = _base * offset;
        }
    }
}
