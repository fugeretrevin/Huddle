using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// Keeps an object turned toward the user (yaw only, stays upright) so world-space text
    /// and panels are always readable and characters face the viewer, regardless of how the
    /// workspace ended up oriented. Cheap; runs in LateUpdate after tracking updates.
    /// </summary>
    public class FaceUser : MonoBehaviour
    {
        [Tooltip("If true the text's readable face (+Z) points AT the user; if false it points away (for backs).")]
        public bool forward = true;
        [Tooltip("Blend speed; high = snappy.")]
        public float turnSpeed = 10f;

        Transform _head;

        void LateUpdate()
        {
            if (_head == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _head = cam.transform;
            }
            Vector3 to = _head.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-5f) return;
            Vector3 dir = forward ? to.normalized : -to.normalized;
            var target = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * turnSpeed);
        }
    }
}
