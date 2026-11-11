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
        Transform _parent;
        Transform _dest;
        bool _moving, _returning;
        float _t;
        Action _onArrived;
        Vector3 _startWorld;

        public bool AtHome { get; private set; } = true;

        void Awake()
        {
            _parent = transform.parent;
            _homeLocal = transform.localPosition;
        }

        public void DeliverTo(Transform destination, Action onArrived = null)
        {
            if (destination == null) return;
            _dest = destination; _returning = false; _onArrived = onArrived;
            _startWorld = transform.position; _t = 0f; _moving = true; AtHome = false;
        }

        public void ReturnHome()
        {
            _returning = true; _onArrived = null;
            _startWorld = transform.position; _t = 0f; _moving = true;
        }

        void Update()
        {
            if (!_moving) return;
            _t += Time.deltaTime / Mathf.Max(0.1f, travelSeconds);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));

            Vector3 targetWorld = _returning && _parent != null
                ? _parent.TransformPoint(_homeLocal)
                : (_dest != null ? _dest.position : transform.position);

            Vector3 flat = Vector3.Lerp(_startWorld, targetWorld, k);
            flat.y += Mathf.Abs(Mathf.Sin(_t * Mathf.PI * walkBobSpeed)) * walkBobHeight;
            transform.position = flat;

            Vector3 dir = targetWorld - _startWorld; dir.y = 0f;
            if (dir.sqrMagnitude > 1e-5f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir.normalized, Vector3.up), Time.deltaTime * 6f);

            if (_t >= 1f)
            {
                transform.position = targetWorld;
                _moving = false;
                if (_returning) { if (_parent != null) transform.localPosition = _homeLocal; AtHome = true; }
                var cb = _onArrived; _onArrived = null; cb?.Invoke();
            }
        }
    }
}
