using System;
using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Moves an agent between its home slot and a delivery point along a short, bounded path
    /// (desk-scale, 1â€“3 s) with a simple walk bob and look-at. Triggered by task state, not by
    /// a timer â€” the delivery choreography fires on a real decision.requested / decision.resolved.
    ///
    /// Walks that leave the desk (delivery to the user) can be routed through <see cref="exitWaypoints"/>:
    /// the agent steps out past the end of its desk like a person would instead of cutting through it,
    /// and walks back the same way. Handoffs between neighbouring stations stay on the straight line.
    /// </summary>
    public class AgentMovement : MonoBehaviour
    {
        [Range(0.5f, 3f)] public float travelSeconds = 1.6f;
        public float walkBobHeight = 0.004f;
        public float walkBobSpeed = 10f;

        [Header("Routing around the desk (delivery walks only)")]
        [Tooltip("Points, in order, the agent passes when it walks OUT to the user. The way home reverses them. " +
                 "Empty = straight line.")]
        public Transform[] exitWaypoints;
        [Tooltip("Routed walks are stretched so the ground speed never exceeds this (m/s); the walk clip " +
                 "plays up to 1.4x of 0.15 m/s, so keep it near 0.25.")]
        public float maxRoutedSpeed = 0.25f;

        Vector3 _homeLocal;
        Quaternion _homeLocalRot;
        Transform _parent;
        Transform _dest;
        Vector3 _destPos;
        bool _useDestPos;
        bool _moving, _returning;
        bool _viaExit;
        float _t, _dur = 1.6f;
        Action _onArrived;
        Vector3 _startWorld;

        readonly Vector3[] _pts = new Vector3[AgentWalkPath.MaxPoints];
        readonly Vector3[] _wp = new Vector3[AgentWalkPath.MaxPoints];

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

        /// <summary>World position of this agent's home slot.</summary>
        public Vector3 HomeWorld => _parent != null ? _parent.TransformPoint(_homeLocal) : transform.position;

        public void DeliverTo(Transform destination, Action onArrived = null, bool viaExit = false)
        {
            if (destination == null) return;
            _dest = destination; _useDestPos = false; _returning = false; _onArrived = onArrived;
            _viaExit = viaExit;
            _startWorld = transform.position; _t = 0f; _moving = true; AtHome = false;
            _dur = ComputeDuration();
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
            _destPos = pos; _useDestPos = true; _returning = false; _onArrived = null; _viaExit = true;
            _startWorld = transform.position; _t = 0f; _moving = true; AtHome = false;
            _dur = ComputeDuration();
        }

        public void ReturnHome()
        {
            // Already standing at the home slot: nothing to walk. (Without this the agent "walked"
            // on the spot for a full travel time whenever a caller sent it home twice.)
            if (AtHome && !_moving) return;
            _returning = true; _onArrived = null;
            _startWorld = transform.position; _t = 0f; _moving = true;
            _dur = ComputeDuration();
        }

        Vector3 TargetWorld()
        {
            return _returning && _parent != null
                ? _parent.TransformPoint(_homeLocal)
                : (_useDestPos ? _destPos : (_dest != null ? _dest.position : transform.position));
        }

        /// <summary>Builds the current walk polyline into buf (start, waypoints, target); returns the point count.</summary>
        public int BuildPath(Vector3 start, Vector3 target, bool returning, bool viaExit, Vector3[] buf)
        {
            int wc = 0;
            if (viaExit && exitWaypoints != null)
                for (int i = 0; i < exitWaypoints.Length && wc < _wp.Length; i++)
                    if (exitWaypoints[i] != null) _wp[wc++] = exitWaypoints[i].position;
            return AgentWalkPath.Build(start, target, _wp, wc, returning, buf);
        }

        float ComputeDuration()
        {
            if (!_viaExit || exitWaypoints == null || exitWaypoints.Length == 0) return travelSeconds;
            int n = BuildPath(_startWorld, TargetWorld(), _returning, _viaExit, _pts);
            if (n <= 2) return travelSeconds;
            return AgentWalkPath.Duration(travelSeconds, AgentWalkPath.Length(_pts, n), maxRoutedSpeed);
        }

        void Update()
        {
            if (!_moving) return;
            float dt = Time.deltaTime;
            Vector3 before = transform.position;
            _t += dt / Mathf.Max(0.1f, _dur);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));

            Vector3 targetWorld = TargetWorld();
            int n = BuildPath(_startWorld, targetWorld, _returning, _viaExit, _pts);
            Vector3 flat = AgentWalkPath.Sample(_pts, n, k, out int seg, out float segFrac, out Vector3 segDir);
            flat.y = Mathf.Lerp(_startWorld.y, targetWorld.y, k);
            flat.y += Mathf.Abs(Mathf.Sin(_t * Mathf.PI * walkBobSpeed)) * walkBobHeight;
            transform.position = flat;

            if (dt > 1e-5f)
            {
                float dx = flat.x - before.x, dz = flat.z - before.z;
                PlanarSpeed = Mathf.Sqrt(dx * dx + dz * dz) / dt;
            }

            bool routed = n > 2;
            bool lastStretch = routed ? (seg == n - 2 && segFrac > 0.5f) : k > 0.75f;
            if (_returning && lastStretch)
            {
                // Last stretch of the walk home: turn back to the station pose (toward the desk).
                Quaternion homeRot = _parent != null ? _parent.rotation * _homeLocalRot : _homeLocalRot;
                transform.rotation = Quaternion.Slerp(transform.rotation, homeRot, Time.deltaTime * 10f);
            }
            else if (segDir.sqrMagnitude > 1e-5f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(segDir, Vector3.up), Time.deltaTime * (routed ? 9f : 6f));

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

