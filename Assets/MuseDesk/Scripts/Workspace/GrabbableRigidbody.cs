using System.Reflection;
using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// Lets a physics prop also be *grabbed* (pinch or cup) like the decision paper, not only pushed.
    /// A copied Oculus Grabbable + HandGrabInteractable drives selection; while selected we make the
    /// Rigidbody kinematic so it follows the hand cleanly, and on release we restore dynamics and
    /// hand the prop the hand's last velocity so it can be set down or tossed. Grab state is read by
    /// reflection so this runtime assembly keeps no hard ISDK dependency. Co-exists with
    /// DelayedPhysics (that only acts during the first few seconds, then leaves the body dynamic).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GrabbableRigidbody : MonoBehaviour
    {
        [Tooltip("The Oculus.Interaction.Grabbable on this prop (set by the builder).")]
        public Component grabbable;

        Rigidbody _rb;
        PropertyInfo _sel;
        bool _held;
        Vector3 _lastPos;
        Vector3 _vel;

        void Awake() { _rb = GetComponent<Rigidbody>(); _lastPos = transform.position; }

        void Update()
        {
            if (grabbable == null || _rb == null) return;
            if (_sel == null)
            {
                _sel = grabbable.GetType().GetProperty("SelectingPointsCount",
                    BindingFlags.Instance | BindingFlags.Public);
                if (_sel == null) { enabled = false; return; }
            }

            bool held;
            try { held = (int)_sel.GetValue(grabbable) > 0; }
            catch { return; }

            if (held && !_held) { _rb.isKinematic = true; }
            else if (!held && _held)
            {
                _rb.isKinematic = false;
                _rb.linearVelocity = Vector3.ClampMagnitude(_vel, 2.5f);
            }

            if (held)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                _vel = (transform.position - _lastPos) / dt;
            }
            _lastPos = transform.position;
            _held = held;
        }
    }
}
