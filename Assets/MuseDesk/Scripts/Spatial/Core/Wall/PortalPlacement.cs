using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    public enum PortalProblem { None = 0, NoRoom = 1, OffFloor = 2, NearWall = 3, LowCeiling = 4, HitsFurniture = 5, TooCloseToUser = 6, InFrontOfUser = 7, BehindUser = 8, TooFar = 9, Unverified = 10 }

    [Serializable]
    public sealed class PortalSettings
    {
        [Tooltip("Radius of the floor ring and of the avatar's swept column (m).")]
        public float footprintRadius = 0.30f;
        [Tooltip("Avatar height range (m). The tallest that fits under the ceiling is used.")]
        public float minAvatarHeight = 0.9f;
        public float maxAvatarHeight = 1.2f;
        [Tooltip("Head room kept above the avatar (m).")]
        public float headRoom = 0.12f;
        [Tooltip("Distance kept from walls (floor outline edges) beyond the ring (m).")]
        public float wallMargin = 0.12f;
        [Tooltip("Distance kept from scanned furniture beyond the ring (m).")]
        public float furnitureMargin = 0.10f;
        [Tooltip("The ring stays this far (horizontally) from the user's head (m), beyond its own radius.")]
        public float personalRadius = 0.55f;
        [Tooltip("Not within this horizontal angle of the user's forward (the desk and screen are there) (deg).")]
        public float frontConeDeg = 40f;
        [Tooltip("Not further round than this (behind the user) (deg).")]
        public float maxSideDeg = 135f;
        public float maxDistance = 1.9f;
        public float[] anglesDeg = { 75f, -75f, 95f, -95f, 58f, -58f, 115f, -115f };
        public float[] distances = { 1.15f, 1.35f, 1.0f, 1.55f };
        [Tooltip("Comfort fallback: a small stationary avatar this tall near the user, only when confirmed (m).")]
        public float smallAvatarHeight = 0.35f;
    }

    public struct PortalCandidate
    {
        /// <summary>Centre of the floor ring (on the floor).</summary>
        public Vector3 position;
        /// <summary>Faces the user (yaw only).</summary>
        public Quaternion rotation;
        public float avatarHeight;
        public bool valid;
        public bool roomChecked;
        public PortalProblem problem;
        public string reason;
        public float score;
        public float minClearance;
    }

    /// <summary>
    /// Where a large agent may appear: a clear floor circle beside (not in front of, not behind) the seated
    /// user, with the whole column the avatar occupies while it rises and stands (its swept volume) clear of
    /// walls, furniture and the user's personal space. Never on unscanned floor without the user's explicit
    /// manual confirmation (and then it is labelled unchecked).
    /// </summary>
    public static class PortalPlacementEvaluator
    {
        public static PortalCandidate Evaluate(IRoomGeometry room, Vector3 floorPoint, SeatedUser user, PortalSettings s)
        {
            var c = new PortalCandidate { position = floorPoint, minClearance = float.MaxValue };
            c.rotation = SpatialGeometry.FaceUserYaw(floorPoint, user.head);
            if (room == null || room.Availability != RoomAvailability.Available)
                return Fail(ref c, PortalProblem.NoRoom, room == null ? "No room scan" : SpatialGeometry.ReasonText(room.Availability));
            c.roomChecked = true;

            Vector2 p = SpatialGeometry.XZ(floorPoint);
            int fi = SpatialGeometry.FindFloor(room, p);
            if (fi < 0) return Fail(ref c, PortalProblem.OffFloor, "Not on the scanned floor");
            RoomPlane floor = room.GetFloor(fi);
            c.position.y = floor.height;
            float edge = SpatialGeometry.DistanceToPolygonEdge(floor.polygon, p);
            if (edge < s.footprintRadius + s.wallMargin) return Fail(ref c, PortalProblem.NearWall, "Too close to a wall");
            c.minClearance = edge - s.footprintRadius;

            // Height under the ceiling (no ceiling data: assume the maximum is fine only if nothing says otherwise).
            float height = s.maxAvatarHeight;
            int ci = SpatialGeometry.FindCeiling(room, p);
            if (ci >= 0)
            {
                float clear = room.GetCeiling(ci).height - floor.height - s.headRoom;
                if (clear < s.minAvatarHeight) return Fail(ref c, PortalProblem.LowCeiling, "Not enough head room");
                height = Mathf.Min(height, clear);
            }
            c.avatarHeight = height;

            // The swept column: a square around the ring (conservative), floor to head plus head room.
            OrientedRect column = OrientedRect.FromYaw(p, new Vector2(s.footprintRadius, s.footprintRadius), 0f);
            for (int i = 0; i < room.FurnitureCount; i++)
            {
                FurnitureBox f = room.GetFurniture(i);
                if (!SpatialGeometry.VerticalRangesTouch(floor.height, floor.height + height + s.headRoom, f.minY, f.maxY, 0.02f)) continue;
                float gap = column.DistanceTo(f.Footprint);
                if (gap < s.furnitureMargin) return Fail(ref c, PortalProblem.HitsFurniture, "Furniture is in the way");
                c.minClearance = Mathf.Min(c.minClearance, gap);
            }

            Vector3 d = floorPoint - user.head; d.y = 0f;
            float dist = d.magnitude;
            if (dist < s.personalRadius + s.footprintRadius) return Fail(ref c, PortalProblem.TooCloseToUser, "Too close to you");
            if (dist > s.maxDistance) return Fail(ref c, PortalProblem.TooFar, "Too far away");
            float ang = Vector3.Angle(user.forward, d / Mathf.Max(1e-5f, dist));
            if (ang < s.frontConeDeg) return Fail(ref c, PortalProblem.InFrontOfUser, "In front of your desk");
            if (ang > s.maxSideDeg) return Fail(ref c, PortalProblem.BehindUser, "Behind you");

            c.valid = true;
            c.problem = PortalProblem.None;
            c.reason = "Clear space beside you";
            float sideTerm = 1f - Mathf.Abs(ang - 85f) / 60f;
            float distTerm = 1f - Mathf.Abs(dist - 1.2f) / 0.8f;
            c.score = Mathf.Clamp01(sideTerm) * 0.5f + Mathf.Clamp01(distTerm) * 0.3f + Mathf.Clamp01(c.minClearance / 0.5f) * 0.2f;
            return c;
        }

        static PortalCandidate Fail(ref PortalCandidate c, PortalProblem p, string reason)
        {
            c.valid = false; c.problem = p; c.reason = reason; c.score = 0f;
            return c;
        }

        /// <summary>Valid spots around the user, best first. Empty when none: the caller offers the small near-user avatar.</summary>
        public static List<PortalCandidate> Find(IRoomGeometry room, SeatedUser user, PortalSettings s)
        {
            var list = new List<PortalCandidate>();
            if (room == null || room.Availability != RoomAvailability.Available) return list;
            for (int a = 0; a < s.anglesDeg.Length; a++)
                for (int k = 0; k < s.distances.Length; k++)
                {
                    Vector3 dir = Quaternion.AngleAxis(s.anglesDeg[a], Vector3.up) * user.forward;
                    Vector3 pt = user.head + dir * s.distances[k];
                    PortalCandidate c = Evaluate(room, pt, user, s);
                    if (c.valid) list.Add(c);
                }
            list.Sort((x, y) => y.score.CompareTo(x.score));
            return list;
        }

        /// <summary>No scan: a spot the USER must confirm, labelled unchecked; small avatar only (comfort fallback).</summary>
        public static PortalCandidate ManualSmall(SeatedUser user, float sideDeg, PortalSettings s)
        {
            Vector3 dir = Quaternion.AngleAxis(sideDeg, Vector3.up) * user.forward;
            Vector3 pt = user.head + dir * 0.55f + Vector3.down * 0.35f;
            return new PortalCandidate
            {
                position = pt,
                rotation = SpatialGeometry.FaceUserYaw(pt, user.head),
                avatarHeight = s.smallAvatarHeight,
                valid = true,
                roomChecked = false,
                problem = PortalProblem.Unverified,
                reason = "No room scan: a small visitor beside you. Confirm the space is clear.",
            };
        }
    }

    public enum PortalState { Hidden = 0, Preview = 1, AwaitConfirm = 2, Opening = 3, Emerging = 4, Conversing = 5, Returning = 6, Closing = 7 }

    [Serializable]
    public sealed class PortalTimings
    {
        public float opening = 0.7f, emerging = 1.0f, returning = 0.9f, closing = 0.5f;
        [Tooltip("Comfort mode: animation times are multiplied by this (shorter, calmer).")]
        public float comfortScale = 0.4f;
    }

    /// <summary>
    /// The large-agent portal, one at a time. Preview reserves the agent's presentation (so no desk visit of
    /// the same agent can start); only an explicit Confirm on a valid (or manually accepted) spot opens it.
    /// Cancel, tracking loss, recenter, pause or a mode change close it from any state and free everything.
    /// </summary>
    public sealed class PortalFlow
    {
        readonly AgentPresentationRegistry _registry;
        float _timer;
        public PortalTimings timings = new PortalTimings();
        public bool comfortMode;
        public PortalState State { get; private set; }
        public string Agent { get; private set; }
        public PortalCandidate Spot { get; private set; }
        public string Message { get; private set; }
        public event Action<PortalState> StateChanged;

        public PortalFlow(AgentPresentationRegistry registry) { _registry = registry; }

        /// <summary>Meet Larger: show the ring at the best spot. The agent must be at home or handed over by the desk visit.</summary>
        public bool BeginPreview(string agent, PortalCandidate spot)
        {
            if (State != PortalState.Hidden) { Message = "A portal is already open"; return false; }
            string reason;
            if (!_registry.TryBegin(agent, AgentPresentation.LargePortal, out reason)) { Message = reason; return false; }
            Agent = agent;
            Spot = spot;
            Go(spot.valid ? PortalState.AwaitConfirm : PortalState.Preview);
            Message = spot.valid ? (spot.roomChecked ? "Open the portal here?" : spot.reason) : "No clear space: " + spot.reason;
            return true;
        }

        /// <summary>The ring was moved to another spot (Try another).</summary>
        public void UpdateSpot(PortalCandidate spot)
        {
            if (State != PortalState.Preview && State != PortalState.AwaitConfirm) return;
            Spot = spot;
            Go(spot.valid ? PortalState.AwaitConfirm : PortalState.Preview);
            Message = spot.valid ? "Open the portal here?" : "No clear space: " + spot.reason;
        }

        /// <summary>The user's explicit confirmation. Only on a valid spot.</summary>
        public bool Confirm()
        {
            if (State != PortalState.AwaitConfirm || !Spot.valid) { Message = "Choose a clear spot first"; return false; }
            Go(PortalState.Opening);
            Message = "";
            return true;
        }

        public void SendBack()
        {
            if (State == PortalState.Conversing || State == PortalState.Emerging || State == PortalState.Opening) Go(PortalState.Returning);
            else if (State == PortalState.Preview || State == PortalState.AwaitConfirm) Close();
        }

        /// <summary>Cancel, tracking loss, recenter, pause, mode change: close now, free the reservation.</summary>
        public void Interrupt(string why)
        {
            if (State == PortalState.Hidden) return;
            Message = why;
            if (State == PortalState.Preview || State == PortalState.AwaitConfirm) { Close(); return; }
            Go(PortalState.Closing);
        }

        void Close()
        {
            if (Agent != null) _registry.End(Agent);
            Agent = null;
            Go(PortalState.Hidden);
        }

        float Dur(float t) { return comfortMode ? t * timings.comfortScale : t; }

        public float Progress
        {
            get
            {
                switch (State)
                {
                    case PortalState.Opening: return Mathf.Clamp01(_timer / Dur(timings.opening));
                    case PortalState.Emerging: return Mathf.Clamp01(_timer / Dur(timings.emerging));
                    case PortalState.Returning: return Mathf.Clamp01(_timer / Dur(timings.returning));
                    case PortalState.Closing: return Mathf.Clamp01(_timer / Dur(timings.closing));
                    default: return 0f;
                }
            }
        }

        public void Tick(float dt)
        {
            _timer += Mathf.Max(0f, dt);
            switch (State)
            {
                case PortalState.Opening: if (_timer >= Dur(timings.opening)) Go(PortalState.Emerging); return;
                case PortalState.Emerging: if (_timer >= Dur(timings.emerging)) Go(PortalState.Conversing); return;
                case PortalState.Returning: if (_timer >= Dur(timings.returning)) Go(PortalState.Closing); return;
                case PortalState.Closing: if (_timer >= Dur(timings.closing)) Close(); return;
            }
        }

        void Go(PortalState s)
        {
            _timer = 0f;
            if (State == s) return;
            State = s;
            var h = StateChanged; if (h != null) h(s);
        }
    }
}
