using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Squishy body lean: when the user's hand (physics capsules) pushes the agent's trigger volume,
    /// the agent not only displaces (that is <see cref="RubberBandReturn"/>'s job, on the root
    /// localPosition) but also TILTS/LEANS away from the contact, then springs back upright when the
    /// hand leaves. Purely cosmetic / kinematic: we lean the Bob child a few degrees, never real
    /// physics, and never touch task/agent state. The agent can never fall, drift, or leave its spot.
    ///
    /// COMPOSITION with AgentView (the key risk):
    ///   AgentView.Update() OVERWRITES bob.localRotation from scratch every frame (bob/idle/walk pose).
    ///   This component runs its lean in LateUpdate (which always runs AFTER every Update), reads the
    ///   pose AgentView just wrote, and PRE-MULTIPLIES a lean rotation on top of it. Because AgentView
    ///   rebuilds bob.localRotation absolutely each frame (it never reads back the previous value for
    ///   the rotation), there is no feedback/accumulation: our lean is a pure additive layer that
    ///   AgentView wipes and we re-apply each frame. [DefaultExecutionOrder] is set high so, if any
    ///   other LateUpdate ever also touched Bob, ours would compose last.
    ///
    /// This lives on the agent ROOT (same GameObject as RubberBandReturn + the trigger SphereCollider),
    /// so it receives the SAME OnTriggerStay events as RubberBandReturn — no extra collider needed for
    /// the body lean.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class AgentTilt : MonoBehaviour
    {
        [Tooltip("Transform to lean. Defaults to AgentView.bob, else this transform.")]
        public Transform bob;
        [Tooltip("Max lean angle, degrees.")]
        public float maxTilt = 12f;
        [Tooltip("How fast the lean eases back to upright once the hand leaves.")]
        public float springback = 7f;
        [Tooltip("Lean gained per second of contact.")]
        public float pushStrength = 0.6f;
        [Tooltip("While walking, yield to the walk animation (no lean).")]
        public AgentMovement movement;

        // Accumulated lean as a HORIZONTAL direction*magnitude in Bob's PARENT-local frame, |.| <= 1.
        Vector3 _lean;
        float _contact;   // >0 while a hand is touching this frame-window

        void Awake()
        {
            if (bob == null)
            {
                var view = GetComponent<AgentView>();
                if (view != null && view.bob != null) bob = view.bob;
            }
            if (bob == null) bob = transform;
        }

        void OnEnable() { _lean = Vector3.zero; _contact = 0f; }

        void OnTriggerStay(Collider other)
        {
            if (bob == null) return;
            // Ignore self / children (our own body/arm colliders).
            if (other.transform.IsChildOf(transform)) return;

            Vector3 contact = other.ClosestPoint(bob.position);
            Vector3 away = bob.position - contact;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) return;
            away.Normalize();

            // Express the lean direction in Bob's parent-local frame so the lean is independent of the
            // agent's world facing (which FaceUserWhileDelivering / AgentMovement may be turning).
            Vector3 local = bob.parent != null ? bob.parent.InverseTransformDirection(away) : away;
            local.y = 0f;
            if (local.sqrMagnitude < 1e-6f) return;
            local.Normalize();

            Vector3 want = Vector3.ClampMagnitude(_lean + local, 1f);
            _lean = Vector3.Lerp(_lean, want, Time.deltaTime * pushStrength * 12f);
            _contact = 0.12f;
        }

        void LateUpdate()
        {
            if (bob == null) return;

            // Walking is driven by AgentView.Walk() (bob.localRotation) + AgentMovement (root). Don't
            // fight it: drop the lean and let the walk read cleanly.
            if (movement != null && movement.IsMoving) { _lean = Vector3.zero; _contact = 0f; return; }

            // Only spring back once the hand has LEFT; holding during contact kills jitter.
            _contact -= Time.deltaTime;
            if (_contact <= 0f)
                _lean = Vector3.Lerp(_lean, Vector3.zero, Time.deltaTime * springback);

            float mag = _lean.magnitude;
            if (mag < 1e-5f) return;

            Vector3 dir = _lean / mag;                       // horizontal lean direction (parent-local)
            Vector3 axis = Vector3.Cross(Vector3.up, dir);   // tipping the top toward `dir` rotates about this
            if (axis.sqrMagnitude < 1e-6f) return;

            float angle = Mathf.Clamp01(mag) * maxTilt;
            Quaternion lean = Quaternion.AngleAxis(angle, axis.normalized);

            // AgentView already wrote bob.localRotation THIS frame (in Update). Compose our lean in the
            // parent frame on top of it. AgentView rebuilds the base next frame, so no accumulation.
            bob.localRotation = lean * bob.localRotation;
        }
    }
}
