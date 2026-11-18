using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// rc22 polish (owner: "the agents' arms clip through themselves when idle, full size, as well as other aspects of their
    /// clothing"). The rig's idle clips hang the upper arms almost straight down, so at full size the sleeves cut into the
    /// coat and the hips. After the Animator (LateUpdate), a HANGING upper arm is rolled a few degrees away from the body,
    /// fading in with how straight down it hangs: a raised arm (a wave, a held card, typing) is left alone.
    ///
    /// Works in world space from the bone positions (no assumption about the bones' local axes). Each arm bone remembers the
    /// pose it was given and the pose it was left in: if the Animator did not rewrite it this frame, the base is put back
    /// first, so the roll can never accumulate. Presentation only; allocation-free per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AgentArmClearance : MonoBehaviour
    {
        [Tooltip("How far a hanging upper arm is rolled away from the body (degrees).")]
        [Range(0f, 25f)] public float degrees = 10f;
        [Tooltip("The arm counts as hanging once it points down at least this much (dot with the body's down).")]
        [Range(0f, 1f)] public float hangFrom = 0.55f;
        [Tooltip("Fully hanging (the whole roll) from this much.")]
        [Range(0f, 1f)] public float hangFull = 0.9f;

        struct Arm { public Transform upper, fore; public Quaternion baseRot, left; public bool has; }
        Arm _l, _r;
        Transform _chest, _hips;
        bool _found;

        void Awake() { Find(); }

        void Find()
        {
            _found = true;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "UpperArm_L": if (_l.upper == null) _l.upper = t; break;
                    case "UpperArm_R": if (_r.upper == null) _r.upper = t; break;
                    case "Forearm_L": if (_l.fore == null) _l.fore = t; break;
                    case "Forearm_R": if (_r.fore == null) _r.fore = t; break;
                    case "Chest": if (_chest == null) _chest = t; break;
                    case "Hips": if (_hips == null) _hips = t; break;
                }
            }
        }

        /// <summary>For drivers: the roll applied last frame to the left and right arm (degrees).</summary>
        public float LastLeft { get; private set; }
        public float LastRight { get; private set; }

        void LateUpdate()
        {
            if (!_found) Find();
            if (_chest == null || _hips == null) return;
            Vector3 bodyUp = _chest.position - _hips.position;
            if (bodyUp.sqrMagnitude < 1e-10f) return;
            bodyUp.Normalize();
            LastLeft = Clear(ref _l, bodyUp);
            LastRight = Clear(ref _r, bodyUp);
        }

        float Clear(ref Arm a, Vector3 bodyUp)
        {
            if (a.upper == null || a.fore == null) return 0f;
            Quaternion now = a.upper.localRotation;
            if (a.has && Quaternion.Angle(now, a.left) < 0.01f) { a.upper.localRotation = a.baseRot; now = a.baseRot; }
            a.baseRot = now;
            float applied = 0f;
            Vector3 arm = a.fore.position - a.upper.position;
            if (arm.sqrMagnitude > 1e-12f && degrees > 0f)
            {
                arm.Normalize();
                float hang = Vector3.Dot(arm, -bodyUp);
                float w = Mathf.InverseLerp(hangFrom, Mathf.Max(hangFrom + 0.01f, hangFull), hang);
                if (w > 0f)
                {
                    Vector3 outward = a.upper.position - _chest.position;
                    outward -= bodyUp * Vector3.Dot(outward, bodyUp);
                    if (outward.sqrMagnitude > 1e-12f)
                    {
                        Vector3 axis = Vector3.Cross(arm, outward.normalized);   // rotating arm toward outward
                        if (axis.sqrMagnitude > 1e-10f)
                        {
                            applied = degrees * w;
                            a.upper.rotation = Quaternion.AngleAxis(applied, axis.normalized) * a.upper.rotation;
                        }
                    }
                }
            }
            a.left = a.upper.localRotation;
            a.has = true;
            return applied;
        }
    }
}
