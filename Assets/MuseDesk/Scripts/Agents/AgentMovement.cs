using System;
using MuseDesk.Motion;
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
        float _dur = 1.6f;
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
            BeginProfile();
            _startWorld = transform.position; _moving = true; AtHome = false;
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
            BeginProfile();
            _startWorld = transform.position; _moving = true; AtHome = false;
            _dur = ComputeDuration();
        }

        public void ReturnHome()
        {
            // Already standing at the home slot: nothing to walk. (Without this the agent "walked"
            // on the spot for a full travel time whenever a caller sent it home twice.)
            if (AtHome && !_moving) return;
            _returning = true; _onArrived = null;
            BeginProfile();
            _startWorld = transform.position; _moving = true;
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

        // ---- Polish: speed profile, turn-in-place, damped facing (waypoints and paths are unchanged) ----
        // Position advances along the path DISTANCE with a ProfiledMover (accelerate over startSeconds, brake to rest
        // exactly on the destination over stopSeconds), so there is no end snap. Facing is a critically damped yaw
        // (Damped.FollowAngle). A walk that starts facing more than walkTurnLimit away turns on the spot first.
        ProfiledMover _mover;
        float _yawVel;
        float _walkTime, _turnWait;
        bool _startTurn;          // from rest: turn in place before the first step if the heading error is large
        bool _settling;           // returned home: the final turn to the station pose is still finishing
        const float MaxTurnWait = 1.0f;   // never hold a walk longer than this for the start turn

        static float YawOf(Vector3 dir) { return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; }

        /// <summary>Resets the profile for a new walk. A walk re-targeted mid-stride keeps its current speed
        /// (no instant stop) and skips the start turn.</summary>
        void BeginProfile()
        {
            _mover.position = 0f;
            if (!_moving) _mover.velocity = 0f;
            _startTurn = !_moving;
            _walkTime = 0f; _turnWait = 0f;
            _settling = false;
        }

        void Update()
        {
            if (!_moving)
            {
                if (_settling) SettleHome();
                return;
            }
            float dt = Time.deltaTime;
            if (dt <= 1e-6f) return;
            var prof = MuseMotionProfile.Active;
            _walkTime += dt;

            Vector3 before = transform.position;
            Vector3 targetWorld = TargetWorld();
            int n = BuildPath(_startWorld, targetWorld, _returning, _viaExit, _pts);
            float total = AgentWalkPath.Length(_pts, n);

            // Heading wanted along the first part of the path, for the start turn.
            if (_startTurn)
            {
                AgentWalkPath.Sample(_pts, n, 0f, out _, out _, out Vector3 d0);
                if (d0.sqrMagnitude > 1e-5f)
                {
                    float cur = transform.eulerAngles.y;
                    float err = Mathf.Abs(Mathf.DeltaAngle(cur, YawOf(d0)));
                    _turnWait += dt;
                    if (err > prof.walkTurnLimit && _turnWait < MaxTurnWait)
                    {
                        float y = Damped.FollowAngle(cur, YawOf(d0), ref _yawVel, prof.turnSmoothTime, dt);
                        transform.rotation = Quaternion.Euler(0f, y, 0f);
                        PlanarSpeed = 0f;
                        return;   // still on the spot: the walk clip steps, the body does not travel
                    }
                }
                _startTurn = false;
            }

            // Cruise speed so the walk still takes about _dur including the ramps.
            float ramps = 0.5f * (prof.startSeconds + prof.stopSeconds);
            float cruiseTime = Mathf.Max(0.3f * _dur, _dur - ramps);
            float vMax = total / cruiseTime;
            float accel = MuseMotionProfile.AccelFor(vMax, prof.startSeconds);
            float decel = MuseMotionProfile.AccelFor(vMax, prof.stopSeconds);
            bool arrived = total < 1e-5f || _mover.Step(total, vMax, accel, decel, dt);
            float s = arrived ? total : _mover.position;
            float k = total > 1e-5f ? Mathf.Clamp01(s / total) : 1f;

            Vector3 flat = AgentWalkPath.Sample(_pts, n, k, out int seg, out float segFrac, out Vector3 segDir);
            flat.y = Mathf.Lerp(_startWorld.y, targetWorld.y, k);
            float speed01 = vMax > 1e-5f ? Mathf.Clamp01(Mathf.Abs(_mover.velocity) / vMax) : 0f;
            if (!arrived) flat.y += Mathf.Abs(Mathf.Sin(k * Mathf.PI * walkBobSpeed)) * walkBobHeight * speed01;
            transform.position = arrived ? targetWorld : flat;

            {
                float dx = transform.position.x - before.x, dz = transform.position.z - before.z;
                PlanarSpeed = Mathf.Sqrt(dx * dx + dz * dz) / dt;
            }

            bool routed = n > 2;
            bool lastStretch = routed ? (seg == n - 2 && segFrac > 0.5f) : k > 0.75f;
            float curYaw = transform.eulerAngles.y;
            if (_returning && lastStretch)
            {
                // Last stretch of the walk home: turn back to the station pose (toward the desk).
                Quaternion homeRot = _parent != null ? _parent.rotation * _homeLocalRot : _homeLocalRot;
                float y = Damped.FollowAngle(curYaw, homeRot.eulerAngles.y, ref _yawVel, prof.turnSmoothTime, dt);
                transform.rotation = Quaternion.Euler(0f, y, 0f);
            }
            else if (segDir.sqrMagnitude > 1e-5f)
            {
                float y = Damped.FollowAngle(curYaw, YawOf(segDir), ref _yawVel, prof.turnSmoothTime, dt);
                transform.rotation = Quaternion.Euler(0f, y, 0f);
            }

            if (arrived)
            {
                _moving = false;
                PlanarSpeed = 0f;
                if (_returning)
                {
                    if (_parent != null) transform.localPosition = _homeLocal;
                    AtHome = true;
                    _settling = true;      // the station pose is reached by a damped turn, not a snap
                    _turnWait = 0f;
                    SettleHome();
                }
                var cb = _onArrived; _onArrived = null; cb?.Invoke();
            }
        }

        /// <summary>After a walk home: finish the turn to the station pose with the same damped follower, then land
        /// exactly on it (the remaining error is below a twentieth of a degree by then).</summary>
        void SettleHome()
        {
            float dt = Time.deltaTime;
            Quaternion homeRot = _parent != null ? _parent.rotation * _homeLocalRot : _homeLocalRot;
            float target = homeRot.eulerAngles.y;
            float cur = transform.eulerAngles.y;
            _turnWait += dt;
            float y = Damped.FollowAngle(cur, target, ref _yawVel, MuseMotionProfile.Active.turnSmoothTime, dt);
            if (Mathf.Abs(Mathf.DeltaAngle(y, target)) < 0.05f || _turnWait > MaxTurnWait)
            {
                transform.localRotation = _homeLocalRot;
                _settling = false;
                _yawVel = 0f;
            }
            else transform.rotation = Quaternion.Euler(0f, y, 0f);
        }
    }
}

