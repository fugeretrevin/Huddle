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
            // All displacement is script-driven; an Animator that applies root motion would fight it.
            foreach (var an in GetComponentsInChildren<Animator>(true))
                if (an != null && an.applyRootMotion) an.applyRootMotion = false;
            _parent = transform.parent;
            _homeLocal = transform.localPosition;
            _homeLocalRot = transform.localRotation;
        }

        /// <summary>World position of this agent's home slot.</summary>
        public Vector3 HomeWorld => _parent != null ? _parent.TransformPoint(_homeLocal) : transform.position;

        // ---- Routes as data (desk choreography layout) ----
        // Every walk follows a list of authored waypoints (scene transforms, set by a builder): the delivery walk uses
        // exitWaypoints, a handoff uses the lane the AgentHandoffController was given. The way home is remembered when
        // the walk out starts, so ReturnHome needs no arguments. A walk that starts ON its route (re-targeted part-way)
        // continues from where it stands instead of going back to the first corner (AgentWalkPath.WaypointsBehind).
        Transform[] _via;            // waypoints of the walk out, in travel order
        Transform[] _homeVia;        // waypoints of the walk home from where the walk out ends
        bool _homeViaReversed;       // true: _homeVia is listed in the walk-out order and is walked backwards
        float _durOverride;          // seconds for this walk; 0 = the usual rule

        public void DeliverTo(Transform destination, Action onArrived = null, bool viaExit = false)
        {
            if (destination == null) return;
            _dest = destination; _useDestPos = false; _returning = false; _onArrived = onArrived;
            _viaExit = viaExit;
            _via = viaExit ? exitWaypoints : null;
            _homeVia = _via; _homeViaReversed = true;
            _durOverride = 0f;
            BeginProfile();
            _startWorld = transform.position; _moving = true; AtHome = false;
            _dur = ComputeDuration();
        }

        /// <summary>
        /// Walk to <paramref name="destination"/> through <paramref name="via"/> (in order; may be null or empty), and
        /// remember <paramref name="homeVia"/> (in order from the destination back to the home slot; may be null) as the
        /// way home, so a later <see cref="ReturnHome"/> keeps to an authored lane too.
        /// <paramref name="seconds"/> above zero sets how long the walk takes; zero keeps the usual rule.
        /// </summary>
        public void WalkRoute(Transform destination, Transform[] via, Transform[] homeVia, Action onArrived = null, float seconds = 0f, float minSeconds = 0f)
        {
            WalkRouteCore(destination, via, homeVia, onArrived, seconds);
            // A floor on the walk time (never faster than the usual rule would make it).
            if (_moving && minSeconds > _dur) _dur = minSeconds;
        }

        void WalkRouteCore(Transform destination, Transform[] via, Transform[] homeVia, Action onArrived, float seconds)
        {
            if (destination == null) return;
            _dest = destination; _useDestPos = false; _returning = false; _onArrived = onArrived;
            _viaExit = via != null && via.Length > 0;
            _via = via;
            _homeVia = homeVia; _homeViaReversed = false;
            _durOverride = Mathf.Max(0f, seconds);
            BeginProfile();
            _startWorld = transform.position; _moving = true; AtHome = false;
            _dur = ComputeDuration();
        }

        /// <summary>True while the agent is walking back to its home slot.</summary>
        public bool IsReturning => _moving && _returning;

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
            _via = exitWaypoints; _homeVia = _via; _homeViaReversed = true; _durOverride = 0f;
            BeginProfile();
            _startWorld = transform.position; _moving = true; AtHome = false;
            _dur = ComputeDuration();
        }

        public void ReturnHome()
        {
            // Already standing at the home slot: nothing to walk. (Without this the agent "walked"
            // on the spot for a full travel time whenever a caller sent it home twice.)
            if (AtHome && !_moving) return;
            // Already on the way home: keep walking. Starting the walk again from here would only reset its speed.
            if (_moving && _returning) return;
            _returning = true; _onArrived = null;
            _durOverride = 0f;
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

        /// <summary>Builds the delivery walk polyline into buf (start, the exit waypoints, target); returns the point
        /// count. Kept for the Play Mode drivers; the walk itself uses <see cref="BuildRoute"/>.</summary>
        public int BuildPath(Vector3 start, Vector3 target, bool returning, bool viaExit, Vector3[] buf)
        {
            return BuildRoute(start, target, viaExit ? exitWaypoints : null, returning, buf);
        }

        /// <summary>Builds a walk polyline into buf: start, the waypoints still ahead (in order, or reversed), target.
        /// Waypoints the start has already passed (it stands on the route) are left out. Returns the point count.</summary>
        public int BuildRoute(Vector3 start, Vector3 target, Transform[] via, bool reverse, Vector3[] buf)
        {
            int wc = 0;
            if (via != null)
                for (int i = 0; i < via.Length && wc < _wp.Length; i++)
                    if (via[i] != null) _wp[wc++] = via[i].position;
            int skip = AgentWalkPath.WaypointsBehind(start, _wp, wc, reverse, target);
            return AgentWalkPath.BuildFrom(start, target, _wp, wc, reverse, skip, buf);
        }

        // The polyline of the walk in progress.
        int BuildActive(Vector3[] buf)
        {
            return _returning
                ? BuildRoute(_startWorld, TargetWorld(), _homeVia, _homeViaReversed, buf)
                : BuildRoute(_startWorld, TargetWorld(), _via, false, buf);
        }

        float ComputeDuration()
        {
            if (_durOverride > 0f) return _durOverride;
            var via = _returning ? _homeVia : _via;
            if (via == null || via.Length == 0) return travelSeconds;
            int n = BuildActive(_pts);
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
            _facing = false;
            _cornerTurn = false;
            SetTurnDir(0);
        }

        // ---- Facing a colleague while standing (handoffs) ----
        // Heading only, on the spot, with the same damped follower as the walk. Never while walking: a new walk ends it.
        bool _facing;
        Vector3 _facePoint;

        /// <summary>While standing, turn on the spot to face a world point and keep facing it until
        /// <see cref="StopFacing"/> or the next walk. Ignored while walking.</summary>
        public void FacePoint(Vector3 worldPoint)
        {
            if (_moving) return;
            _facing = true; _facePoint = worldPoint;
            _settling = false;
        }

        /// <summary>Ends <see cref="FacePoint"/>. At the home slot the agent turns back to its station pose (damped,
        /// then exact); anywhere else it simply stops turning.</summary>
        public void StopFacing()
        {
            if (!_facing) return;
            _facing = false;
            if (AtHome && !_moving) { _settling = true; _turnWait = 0f; }
        }

        /// <summary>True while <see cref="FacePoint"/> is in force.</summary>
        public bool IsFacingPoint => _facing;

        void FaceStep()
        {
            Vector3 to = _facePoint - transform.position; to.y = 0f;
            if (to.sqrMagnitude < 1e-6f) return;
            float y = Damped.FollowAngle(transform.eulerAngles.y, YawOf(to), ref _yawVel, MuseMotionProfile.Active.turnSmoothTime, Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, y, 0f);
        }

        // ---- Polish S9a: sharp routed corners brake, turn on the spot, then continue (same rule as HabitatWalker) ----
        bool _cornerTurn;         // standing at a sharp corner, turning toward the next segment
        float _cornerYaw;         // heading wanted after the corner
        float _cornerElapsed;
        const float CornerTurnDone = 2f;   // degrees: close enough to resume walking (facing then follows damped)

        /// <summary>-1 while turning in place to the left, +1 to the right, 0 when not turning on the spot. Covers both
        /// the turn before the first step and the turn at a sharp routed corner. A later task can play Turn_Left /
        /// Turn_Right from this; nothing here drives an animator.</summary>
        public int TurnInPlaceDirection { get; private set; }

        /// <summary>True while the agent is standing and turning (see <see cref="TurnInPlaceDirection"/>).</summary>
        public bool IsTurningInPlace => TurnInPlaceDirection != 0;

        /// <summary>Raised when the turn-in-place direction changes: argument is the new direction (-1 left, +1 right,
        /// 0 = turn finished or abandoned). Fires on the main thread inside Update.</summary>
        public event Action<int> TurnInPlaceChanged;

        void SetTurnDir(int dir)
        {
            if (TurnInPlaceDirection == dir) return;
            TurnInPlaceDirection = dir;
            TurnInPlaceChanged?.Invoke(dir);
        }

        void OnDisable()
        {
            _cornerTurn = false;
            SetTurnDir(0);
        }

        void Update()
        {
            if (!_moving)
            {
                if (TurnInPlaceDirection != 0) { _cornerTurn = false; SetTurnDir(0); }
                if (_facing) FaceStep();
                else if (_settling) SettleHome();
                return;
            }
            float dt = Time.deltaTime;
            if (dt <= 1e-6f) return;
            var prof = MuseMotionProfile.Active;
            _walkTime += dt;

            Vector3 before = transform.position;
            Vector3 targetWorld = TargetWorld();
            int n = BuildActive(_pts);
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
                        SetTurnDir(AgentWalkCorners.TurnSign(cur, YawOf(d0), 0f));
                        float y = Damped.FollowAngle(cur, YawOf(d0), ref _yawVel, prof.turnSmoothTime, dt);
                        transform.rotation = Quaternion.Euler(0f, y, 0f);
                        PlanarSpeed = 0f;
                        return;   // still on the spot: the walk clip steps, the body does not travel
                    }
                }
                _startTurn = false;
                SetTurnDir(0);
            }

            // Turning on the spot at a sharp corner: position is held exactly on the corner, only heading changes.
            if (_cornerTurn)
            {
                float cur = transform.eulerAngles.y;
                _cornerElapsed += dt;
                float y = Damped.FollowAngle(cur, _cornerYaw, ref _yawVel, prof.turnSmoothTime, dt);
                transform.rotation = Quaternion.Euler(0f, y, 0f);
                PlanarSpeed = 0f;
                if (Mathf.Abs(Mathf.DeltaAngle(y, _cornerYaw)) <= CornerTurnDone || _cornerElapsed >= MaxTurnWait)
                {
                    _cornerTurn = false;
                    SetTurnDir(0);
                }
                return;
            }

            // Cruise speed so the walk still takes about _dur including the ramps.
            float ramps = 0.5f * (prof.startSeconds + prof.stopSeconds);
            float cruiseTime = Mathf.Max(0.3f * _dur, _dur - ramps);
            float vMax = total / cruiseTime;
            float accel = MuseMotionProfile.AccelFor(vMax, prof.startSeconds);
            float decel = MuseMotionProfile.AccelFor(vMax, prof.stopSeconds);
            // The mover's target is the next SHARP corner (sharper than walkTurnLimit) or the end. Reaching a corner
            // means braking to rest exactly on that waypoint; then the agent turns on the spot and walks on.
            float stop = AgentWalkCorners.NextStopDistance(_pts, n, _mover.position, prof.walkTurnLimit, out int cornerIdx);
            bool atCorner = false;
            bool arrived;
            if (total < 1e-5f) arrived = true;
            else
            {
                bool reached = _mover.Step(stop, vMax, accel, decel, dt);
                arrived = reached && cornerIdx < 0;
                atCorner = reached && cornerIdx >= 0;
            }
            float s = arrived ? total : (atCorner ? stop : _mover.position);
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

            if (atCorner)
            {
                _cornerYaw = AgentWalkCorners.OutYaw(_pts, n, cornerIdx, transform.eulerAngles.y);
                int sign = AgentWalkCorners.TurnSign(transform.eulerAngles.y, _cornerYaw, CornerTurnDone);
                if (sign != 0) { _cornerTurn = true; _cornerElapsed = 0f; SetTurnDir(sign); }
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

