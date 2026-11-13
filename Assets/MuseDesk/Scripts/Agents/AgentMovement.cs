using System;
using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Moves an agent between its home slot and a delivery point along a short, bounded path
    /// (desk-scale, 1–3 s) with a simple walk bob and look-at. Triggered by task state, not by
    /// a timer — the delivery choreography fires on a real decision.requested / decision.resolved.
    /// </summary>
    public class AgentMovement : MonoBehaviour
    {
        [Range(0.5f, 3f)] public float travelSeconds = 1.6f;
        public float walkBobHeight = 0.004f;
        public float walkBobSpeed = 10f;

        Vector3 _homeLocal;
        Quaternion _homeLocalRot;
        Transform _parent;
        Transform _dest;
        Vector3 _destPos;
        bool _useDestPos;
        bool _moving, _returning;
        float _t;
        Action _onArrived;
        Vector3 _startWorld;

        public bool AtHome { get; private set; } = true;

        /// <summary>True whenever the agent is physically translating (walking out or home),
        /// so the view can play a walk cycle instead of its idle/working motion.</summary>
        public bool IsMoving => _moving;

        /// <summary>Measured ground speed of the walk this frame, metres per second (0 when standing).
        /// The animator bridge reads it to match the walk clip's playback rate to the real speed.</summary>
        public float PlanarSpeed { get; private set; }

        void Awake()
        {
            _parent = transform.parent;
            _homeLocal = transform.localPosition;
            _homeLocalRot = transform.localRotation;
        }

        public void DeliverTo(Transform destination, Action onArrived = null)
        {
            if (destination == null) return;
            _dest = destination; _useDestPos = false; _returning = false; _onArrived = onArrived;
            _startWorld = transform.position; _t = 0f; _moving = true; AtHome = false;
        }

        /// <summary>Walk to a reachable point in front of the user (stays at the agent's height).</summary>
        public void DeliverInFrontOfUser(float distance = 0.4f, float dropBelowEye = 0.28f)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var head = cam.transform;
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 pos = head.position + fwd * distance + Vector3.down * dropBelowEye;
            pos.y = transform.position.y;   // keep the walk on the desk plane
            _destPos = pos; _useDestPos = true; _returning = false; _onArrived = null;
            _startWorld = transform.position; _t = 0f; _moving = true; AtHome = false;
        }

        public void ReturnHome()
        {
            // Already standing at the home slot: nothing to walk. (Without this the agent "walked"
            // on the spot for a full travel time whenever a caller sent it home twice.)
            if (AtHome && !_moving) return;
            _returning = true; _onArrived = null;
            _startWorld = transform.position; _t = 0f; _moving = true;
        }

        void Update()
        {
            if (!_moving) return;
            float dt = Time.deltaTime;
            Vector3 before = transform.position;
            _t += dt / Mathf.Max(0.1f, travelSeconds);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));

            Vector3 targetWorld = _returning && _parent != null
                ? _parent.TransformPoint(_homeLocal)
                : (_useDestPos ? _destPos : (_dest != null ? _dest.position : transform.position));

            Vector3 flat = Vector3.Lerp(_startWorld, targetWorld, k);
            flat.y += Mathf.Abs(Mathf.Sin(_t * Mathf.PI * walkBobSpeed)) * walkBobHeight;
            transform.position = flat;

            if (dt > 1e-5f)
            {
                float dx = flat.x - before.x, dz = flat.z - before.z;
                PlanarSpeed = Mathf.Sqrt(dx * dx + dz * dz) / dt;
            }

            Vector3 dir = targetWorld - _startWorld; dir.y = 0f;
            if (_returning && k > 0.75f)
            {
                // Last stretch of the walk home: turn back to the station pose (toward the desk).
                Quaternion homeRot = _parent != null ? _parent.rotation * _homeLocalRot : _homeLocalRot;
                transform.rotation = Quaternion.Slerp(transform.rotation, homeRot, Time.deltaTime * 10f);
            }
            else if (dir.sqrMagnitude > 1e-5f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir.normalized, Vector3.up), Time.deltaTime * 6f);

            if (_t >= 1f)
            {
                transform.position = targetWorld;
                _moving = false;
                PlanarSpeed = 0f;
                if (_returning)
                {
                    if (_parent != null) transform.localPosition = _homeLocal;
                    transform.localRotation = _homeLocalRot;
                    AtHome = true;
                }
                var cb = _onArrived; _onArrived = null; cb?.Invoke();
            }
        }
    }
}
