using UnityEngine;

namespace MuseDesk.Wall
{
    /// <summary>
    /// rc21: a small nod and tilt of the head bone while an agent talks (owner: "a small head bob animation while they talk").
    /// Added in LateUpdate on top of the animation (and after PortalVisitorLife, which also adds to the head). Because
    /// PortalVisitorLife's bone guard treats an unchanged bone as un-animated and restores its remembered pose, this component
    /// takes its own rotation OFF again in Update (before the Animator and the guard run), so nothing accumulates.
    /// Set <see cref="Talking"/> each frame; the strength eases in and out.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public class WallTalkBob : MonoBehaviour
    {
        public bool Talking;
        /// <summary>Peak pitch in degrees (smaller in comfort mode).</summary>
        public float amplitudeDeg = 3.5f;
        public float hz = 3.5f;

        Transform _head;
        float _k, _phase, _t;
        bool _applied;
        Quaternion _pre, _out;

        /// <summary>Angle in degrees added to the head this frame (probe evidence).</summary>
        public float LastAngle { get; private set; }
        public bool HasHead => _head != null;
        public float Strength => _k;

        void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Head" || t.name == "head" || t.name == "mixamorig:Head") { _head = t; break; }
        }

        void Update()
        {
            // Take last frame's addition off if the bone is still exactly as we left it (the Animator did not rewrite it).
            if (_applied && _head != null && Quaternion.Angle(_head.localRotation, _out) < 0.001f) _head.localRotation = _pre;
            _applied = false;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            _k = Mathf.MoveTowards(_k, Talking ? 1f : 0f, dt / (Talking ? 0.3f : 0.4f));
            LastAngle = 0f;
            if (_head == null || _k <= 0.001f) return;
            _t += dt;
            // 3-4 Hz with slow variation.
            _phase += dt * (hz * (1f + 0.18f * Mathf.Sin(_t * 0.9f) + 0.08f * Mathf.Sin(_t * 2.3f))) * (Mathf.PI * 2f);
            float e = _k * _k * (3f - 2f * _k);
            float pitch = e * amplitudeDeg * (0.55f + 0.45f * Mathf.Sin(_phase)) * Mathf.Sin(_phase * 0.5f + 0.7f * Mathf.Sin(_t * 0.6f));
            // A nod mostly down (never a big back-tilt), plus a slight sideways tilt at a different rate.
            pitch = Mathf.Abs(pitch) * 0.8f + pitch * 0.2f;
            float roll = e * amplitudeDeg * 0.35f * Mathf.Sin(_phase * 0.37f + 1.1f);
            Quaternion add = Quaternion.AngleAxis(pitch, transform.right) * Quaternion.AngleAxis(roll, transform.forward);
            _pre = _head.localRotation;
            _head.rotation = add * _head.rotation;
            _out = _head.localRotation;
            _applied = true;
            LastAngle = Quaternion.Angle(_pre, _out);
        }

        void OnDisable()
        {
            if (_applied && _head != null && Quaternion.Angle(_head.localRotation, _out) < 0.001f) _head.localRotation = _pre;
            _applied = false;
        }
    }
}
