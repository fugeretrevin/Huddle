using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// Keeps a foundation smoke-test prop reliably in front of the user. For the first few
    /// seconds it re-places every frame (so it lands correctly once head tracking settles,
    /// instead of freezing at the tracking-space origin), then locks in place. Rigidbodies
    /// are made kinematic so the prop can't fall away. Call Recall() to bring it back.
    /// </summary>
    public class PlaceInFrontOnStart : MonoBehaviour
    {
        [Tooltip("Metres in front of the user.")]
        [Range(0.3f, 0.7f)] public float distance = 0.45f;
        [Tooltip("Sideways offset in head space (+ = right), metres.")]
        public float sideOffset = 0f;
        [Tooltip("Vertical offset below eye level, metres.")]
        public float dropBelowEye = 0.15f;
        public bool faceUser = true;
        [Tooltip("Seconds to keep re-placing (following the head) before locking.")]
        [Range(0.5f, 5f)] public float followSeconds = 2.0f;

        Rigidbody _rb;
        float _elapsed;
        bool _locked;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb != null) { _rb.useGravity = false; _rb.isKinematic = true; }
        }

        void OnEnable() { _elapsed = 0f; _locked = false; }

        void LateUpdate()
        {
            if (_locked) return;
            _elapsed += Time.deltaTime;
            Reposition();
            if (_elapsed >= followSeconds) _locked = true;
        }

        /// <summary>Re-acquire a spot in front of the user (e.g. from a reset button).</summary>
        public void Recall() { _elapsed = 0f; _locked = false; }

        void Reposition()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var head = cam.transform;
            // Ignore the first frames where the rig may still sit at the origin.
            if (head.position.sqrMagnitude < 0.0004f && _elapsed < followSeconds) return;

            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 pos = head.position + fwd * distance + right * sideOffset + Vector3.down * dropBelowEye;

            transform.position = pos;
            if (_rb != null) _rb.position = pos;
            if (faceUser)
            {
                Vector3 toUser = head.position - pos; toUser.y = 0f;
                if (toUser.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(toUser.normalized, Vector3.up);
            }
        }
    }
}
