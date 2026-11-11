using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Makes an agent face the user ONLY while it is away from its home slot delivering (i.e. it
    /// walked up to hand over a paper and is now standing in front of you). While walking,
    /// AgentMovement drives the facing; back home, the agent faces its station as authored. This
    /// fixes "the one handing me the paper doesn't look at me" without reintroducing the
    /// always-stare-at-user behavior the user disliked.
    /// </summary>
    public class FaceUserWhileDelivering : MonoBehaviour
    {
        public AgentMovement movement;
        public float turnSpeed = 8f;

        Transform _head;

        void LateUpdate()
        {
            if (movement == null) return;
            if (movement.AtHome || movement.IsMoving) return;   // only once arrived at the delivery spot
            if (_head == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _head = cam.transform;
            }
            Vector3 to = _head.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-5f) return;
            // Character front is +Z (eyes/face on +Z), so point forward AT the user.
            var target = Quaternion.LookRotation(to.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * turnSpeed);
        }
    }
}
