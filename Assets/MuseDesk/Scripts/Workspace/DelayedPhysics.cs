using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// Keeps a prop's Rigidbody kinematic for a short settle window after load, then releases it to
    /// gravity. This avoids the physics "explosion" that happens when the workspace root is moved
    /// into place (follow-then-lock) while dynamic bodies are parented under it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class DelayedPhysics : MonoBehaviour
    {
        public float delay = 3f;

        Rigidbody _rb;
        Vector3 _home;
        Quaternion _homeRot;
        float _t;
        bool _released;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _home = transform.localPosition;
            _homeRot = transform.localRotation;
            if (_rb) _rb.isKinematic = true;
        }

        void Update()
        {
            if (_released || _rb == null) return;
            _t += Time.deltaTime;
            if (_t >= delay)
            {
                _rb.isKinematic = false;
                _rb.WakeUp();
                _released = true;
            }
        }

        /// <summary>Put the prop back on the tray (call from a reset button if it rolls away).</summary>
        public void ResetToHome()
        {
            if (_rb) { _rb.isKinematic = true; _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
            transform.localPosition = _home;
            transform.localRotation = _homeRot;
            _t = 0f; _released = false;
        }
    }
}
