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
        AgentGaze _gaze;
        float _nextCamTry;

        void Awake()
        {
            _gaze = GetComponent<AgentGaze>();   // optional
        }

        void LateUpdate()
        {
            if (movement == null) return;
            if (movement.AtHome || movement.IsMoving) return;   // only once arrived at the delivery spot
            // Standing next to a colleague in an agent-to-agent handoff: keep facing them (the walk
            // left the body pointing that way) instead of turning to the user mid-exchange.
            if (AgentHandoffController.AnyBusy && _gaze != null && _gaze.HasNearbyAgent) return;
            if (_head == null)
            {
                if (Time.unscaledTime < _nextCamTry) return;     // Camera.main is a scene search
                _nextCamTry = Time.unscaledTime + 0.5f;
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
