using System;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>What <see cref="PortalComfortGuard.Tick"/> did this frame.</summary>
    public enum PortalGuardAction
    {
        None = 0,
        /// <summary>The previewed spot was made not-confirmable ("Open here" disabled, ring coral).</summary>
        BlockedConfirm = 1,
        /// <summary>A spot this guard had blocked is confirmable again.</summary>
        Restored = 2,
        /// <summary>The portal was interrupted (it closes and frees the agent's presentation).</summary>
        Closed = 3,
    }

    [Serializable]
    public sealed class PortalGuardSettings
    {
        [Tooltip("Geometry re-check interval while a portal exists (s). The hands-busy check runs every frame.")]
        public float checkInterval = 0.2f;
        [Tooltip("Room bounds are rebuilt (room changes, doors, labels) at most this often (s).")]
        public float rebuildInterval = 0.5f;
        [Tooltip("While the avatar is emerging or conversing, this many failed checks in a row close it (debounce). " +
                 "Before anything is visible (preview, opening) one failed check is enough.")]
        public int failChecksToClose = 2;
        [Tooltip("The head moved this far (horizontally) from the seat: the seated frame is re-taken and the spot re-checked now " +
                 "(the user moved, or a recenter/relocalization moved the world).")]
        public float reseatDistance = 0.35f;
        [Tooltip("A candidate this tall or less is the small desk visitor (the one fallback): no large-avatar geometry check.")]
        public float smallVisitorMaxHeight = 0.5f;
    }

    /// <summary>
    /// The one runtime gate between the large-agent portal and the user's comfort keep-out. Call <see cref="Tick"/> every
    /// frame with the portal flow; it never moves anything, it only refuses or closes:
    ///  - Hands busy (a paper, stamp or card held): no portal opens. In Preview/AwaitConfirm the spot is made
    ///    not-confirmable (restored when the hands are free); in Opening (nothing visible yet) the portal is closed.
    ///    While the avatar is already out, holding something is fine.
    ///  - Geometry: a large candidate is re-checked with <see cref="PortalSpotPlanner.Evaluate"/> (the full swept
    ///    appearance against the seated head/torso/hand zones, the default forward work area, scanned screens and their
    ///    line of sight, the desk front, doors, furniture, walls, head room, and the controls when given) against bounds
    ///    rebuilt from the live room at most every <c>rebuildInterval</c>. A candidate from any planner (including the legacy
    ///    evaluator) is held to the same rules. No room data: a large candidate never passes (fail closed).
    ///  - The seated frame (forward) is taken when the portal session starts and re-taken only when the head moves more
    ///    than <c>reseatDistance</c>: turning to look at the avatar never closes it; a recenter or the user moving re-checks it.
    ///  - Failure mid-interaction: Interrupt, so the flow closes cleanly and frees the presentation (exactly once).
    /// The small desk visitor (height &lt;= smallVisitorMaxHeight) is the single fallback and is only subject to the hands check.
    /// Pure logic, no allocation per tick once the bounds lists have grown.
    /// </summary>
    public sealed class PortalComfortGuard
    {
        public const string HandsBusyReason = "Hands busy: put down the paper, stamp or card first";

        public readonly PortalSpotSettings spot = new PortalSpotSettings();
        public readonly PortalGuardSettings settings = new PortalGuardSettings();
        readonly PortalRoomBounds _bounds = new PortalRoomBounds();

        bool _session;
        Vector3 _seatHead, _seatForward;
        float _nextCheck, _nextBuild;
        int _fails;
        bool _parked;
        PortalCandidate _parkedSpot;
        Vector3 _blockedAt;

        public string LastReason { get; private set; }
        public PortalGuardAction LastAction { get; private set; }
        public int Checks { get; private set; }
        public int Closures { get; private set; }
        public int Blocks { get; private set; }
        public bool InSession => _session;
        public Vector3 SeatForward => _seatForward;
        /// <summary>The bounds of the last rebuild (drivers, tests).</summary>
        public PortalRoomBounds Bounds => _bounds;
        /// <summary>The last full check of the current candidate.</summary>
        public PortalSpot LastCheck { get; private set; }

        /// <summary>
        /// One frame. <paramref name="room"/> may be null (unknown room). <paramref name="userHolding"/>: anything held by either
        /// hand or a paper's transition hold. <paramref name="controls"/>: world position of the portal's controls (the visit
        /// card), or null. Returns what it did; <see cref="LastReason"/> has the words for the user.
        /// </summary>
        public PortalGuardAction Tick(PortalFlow portal, float now, SeatedUser user, IRoomGeometry room, bool userHolding, Vector3? controls = null)
        {
            LastAction = PortalGuardAction.None;
            if (portal == null || portal.State == PortalState.Hidden) { EndSession(); return PortalGuardAction.None; }
            if (!_session)
            {
                _session = true;
                _seatHead = user.head; _seatForward = user.forward;
                _nextCheck = _nextBuild = now;
                _fails = 0; _parked = false;
            }
            Vector3 dh = user.head - _seatHead; dh.y = 0f;
            if (dh.magnitude > settings.reseatDistance)
            {
                _seatHead = user.head; _seatForward = user.forward;
                _nextCheck = _nextBuild = now;           // re-check now against the new seat
            }

            PortalState st = portal.State;
            PortalCandidate c = portal.Spot;
            bool preview = st == PortalState.Preview || st == PortalState.AwaitConfirm;
            // Somebody else replaced the spot we blocked (Other spot, the tracker): forget the parked one.
            if (_parked && (c.valid || (c.position - _blockedAt).sqrMagnitude > 1e-8f)) _parked = false;

            // 1. Hands busy: no portal opens. Every frame (fail closed), before the throttle.
            if (userHolding)
            {
                if (preview && c.valid) return Block(portal, c, HandsBusyReason);
                if (st == PortalState.Opening) return Close(portal, HandsBusyReason);
            }

            if (now < _nextCheck) return PortalGuardAction.None;
            _nextCheck = now + Mathf.Max(0.02f, settings.checkInterval);

            PortalCandidate subject = _parked ? _parkedSpot : c;
            bool large = subject.avatarHeight > settings.smallVisitorMaxHeight;
            bool geometryOk = true;
            string why = null;
            if (large && (preview ? subject.valid : true))
            {
                if (now >= _nextBuild)
                {
                    _nextBuild = now + Mathf.Max(0.05f, settings.rebuildInterval);
                    SeatedUser buildSeat = new SeatedUser(user.head, _seatForward);
                    PortalSpotAdapter.Build(room, room as IWallGeometry, buildSeat, _bounds, spot.comfort);
                    if (controls.HasValue) PortalSpotAdapter.AddControls(_bounds, buildSeat, controls.Value, spot.comfort);
                }
                spot.avatarHeight = subject.avatarHeight;
                SeatedUser seat = new SeatedUser(user.head, _seatForward);
                PortalSpot e = PortalSpotPlanner.Evaluate(seat, _bounds, spot, new Vector2(subject.position.x, subject.position.z));
                LastCheck = e;
                Checks++;
                geometryOk = e.valid;
                why = e.reason;
            }

            switch (st)
            {
                case PortalState.Preview:
                case PortalState.AwaitConfirm:
                    if (!geometryOk) return c.valid ? Block(portal, c, why) : PortalGuardAction.None;
                    if (_parked && !userHolding)
                    {
                        _parked = false;
                        portal.UpdateSpot(_parkedSpot);
                        LastReason = "";
                        LastAction = PortalGuardAction.Restored;
                        return LastAction;
                    }
                    return PortalGuardAction.None;
                case PortalState.Opening:
                    return geometryOk ? PortalGuardAction.None : Close(portal, why);       // nothing visible yet: no debounce
                case PortalState.Emerging:
                case PortalState.Conversing:
                    if (geometryOk) { _fails = 0; return PortalGuardAction.None; }
                    return ++_fails >= Mathf.Max(1, settings.failChecksToClose) ? Close(portal, why) : PortalGuardAction.None;
                default:
                    return PortalGuardAction.None;                                          // Returning / Closing: already leaving
            }
        }

        /// <summary>Make the previewed spot not-confirmable. The view redraws the ring when the spot moves, so the blocked copy
        /// sits 2 mm lower: the ring re-tints (coral) without any view change. Restored exactly on <see cref="PortalGuardAction.Restored"/>.</summary>
        PortalGuardAction Block(PortalFlow portal, PortalCandidate c, string reason)
        {
            _parked = true;
            _parkedSpot = c;
            PortalCandidate blocked = c;
            blocked.valid = false;
            blocked.reason = reason;
            blocked.problem = reason == HandsBusyReason ? PortalProblem.Unverified : PortalProblem.HitsFurniture;
            blocked.position = c.position + Vector3.down * 0.002f;
            _blockedAt = blocked.position;
            portal.UpdateSpot(blocked);
            Blocks++;
            LastReason = reason;
            LastAction = PortalGuardAction.BlockedConfirm;
            return LastAction;
        }

        PortalGuardAction Close(PortalFlow portal, string reason)
        {
            _parked = false;
            _fails = 0;
            portal.Interrupt("Portal closed: " + reason);
            Closures++;
            LastReason = reason;
            LastAction = PortalGuardAction.Closed;
            return LastAction;
        }

        void EndSession()
        {
            _session = false;
            _parked = false;
            _fails = 0;
        }
    }
}
