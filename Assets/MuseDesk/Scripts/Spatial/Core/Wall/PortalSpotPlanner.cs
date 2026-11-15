using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>
    /// Axis-aligned box in world XZ (x = X, y = Z). The room-bounds input of the portal spot planner is
    /// abstracted to these so the MRUK adapter (view side) only has to flatten walls / furniture / screens
    /// to boxes. Engine-free, no allocation per query.
    /// </summary>
    public struct PortalBox
    {
        public Vector2 min, max;
        public PortalBox(Vector2 min, Vector2 max) { this.min = min; this.max = max; }
        public static PortalBox FromCenter(Vector2 c, Vector2 half) { return new PortalBox(c - half, c + half); }

        /// <summary>Distance from p to the box; 0 when inside.</summary>
        public float DistanceTo(Vector2 p)
        {
            float dx = Mathf.Max(0f, Mathf.Max(min.x - p.x, p.x - max.x));
            float dy = Mathf.Max(0f, Mathf.Max(min.y - p.y, p.y - max.y));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Distance from p (inside) to the nearest edge; negative when outside.</summary>
        public float InsideMargin(Vector2 p)
        {
            return Mathf.Min(Mathf.Min(p.x - min.x, max.x - p.x), Mathf.Min(p.y - min.y, max.y - p.y));
        }
    }

    /// <summary>Room input for the portal spot planner, as boxes. Fill from MRUK (view side) or by hand (tests).</summary>
    public sealed class PortalRoomBounds
    {
        /// <summary>True when a floor rectangle is known. When false the planner only returns the fallback.</summary>
        public bool hasFloor;
        public PortalBox floor;
        public float floorY;
        /// <summary>Ceiling height (world). &lt;= floorY means unknown (no head-room check).</summary>
        public float ceilingY;
        /// <summary>Furniture and other solid things the avatar must not touch.</summary>
        public readonly List<PortalBox> obstacles = new List<PortalBox>();
        /// <summary>Screens, the desk front, anything the user looks at: bigger margin than obstacles.</summary>
        public readonly List<PortalBox> keepOut = new List<PortalBox>();
        /// <summary>Bump when the underlying room changed so the tracker re-evaluates.</summary>
        public int revision;
    }

    [Serializable]
    public sealed class PortalSpotSettings
    {
        [Tooltip("Radius of the avatar's body footprint at the planned scale (m).")]
        public float bodyRadius = 0.30f;
        [Tooltip("Conservative margin kept from the floor outline (walls) beyond the body (m).")]
        public float wallMargin = 0.20f;
        public float obstacleMargin = 0.15f;
        [Tooltip("Margin from screens / desk-front zones beyond the body (m).")]
        public float keepOutMargin = 0.35f;
        [Tooltip("The user comfort envelope: the body stays this far (horizontally) from the head, beyond its own radius (m).")]
        public float personalRadius = 0.60f;
        [Tooltip("Never within this angle of the user's forward (deg).")]
        public float frontConeDeg = 45f;
        [Tooltip("Never further round than this (behind the user) (deg).")]
        public float maxSideDeg = 135f;
        public float maxDistance = 2.0f;
        public float[] sideAnglesDeg = { 75f, 95f, 60f, 115f };
        public float[] distances = { 1.15f, 1.35f, 1.0f, 1.6f };
        [Tooltip("Head room required above the avatar when the ceiling is known (m).")]
        public float headRoom = 0.12f;
        public float avatarHeight = 1.0f;
        [Tooltip("Fallback sits this far beyond the envelope, on the user's right (m).")]
        public float fallbackExtra = 0.10f;

        [Header("Hysteresis")]
        public float minRecheckSeconds = 0.75f;
        public float moveThreshold = 0.30f;
        public float turnThresholdDeg = 25f;
        [Tooltip("A still-valid current spot is only replaced by a spot scoring this much better.")]
        public float switchMargin = 0.15f;
    }

    public struct PortalSpot
    {
        /// <summary>Body centre on the floor plane.</summary>
        public Vector3 position;
        /// <summary>Faces the user (yaw only).</summary>
        public Quaternion rotation;
        /// <summary>True when the spot passed every check against the supplied bounds.</summary>
        public bool valid;
        /// <summary>True when no clear area was found: this is the stable comfort position, unverified.
        /// The UI must say so ("No clear space found").</summary>
        public bool isFallback;
        public float score;
        public string reason;
    }

    /// <summary>
    /// Pure placement of the large portal avatar: never in the user's forward cone or over a screen / desk-front
    /// zone, side-space candidates around the comfort envelope, conservative margins to room boxes, and a stable
    /// flagged fallback when nothing is clear. <see cref="PortalSpotTracker"/> adds hysteresis.
    /// </summary>
    public static class PortalSpotPlanner
    {
        static Vector2 XZ(Vector3 v) { return new Vector2(v.x, v.z); }

        /// <summary>Checks one floor point (XZ) against everything. Fills valid/score/reason.</summary>
        public static PortalSpot Evaluate(SeatedUser user, PortalRoomBounds room, PortalSpotSettings s, Vector2 xz)
        {
            float floorY = room != null ? room.floorY : user.head.y - 1.2f;
            var spot = new PortalSpot { position = new Vector3(xz.x, floorY, xz.y) };
            spot.rotation = FaceUser(spot.position, user);
            if (room == null || !room.hasFloor) { spot.reason = "No room bounds"; return spot; }

            float need = s.bodyRadius;
            if (room.floor.InsideMargin(xz) < need + s.wallMargin) { spot.reason = "Too close to a wall"; return spot; }
            if (room.ceilingY > room.floorY && room.ceilingY - room.floorY - s.headRoom < s.avatarHeight) { spot.reason = "Not enough head room"; return spot; }

            float clearance = float.MaxValue;
            for (int i = 0; i < room.keepOut.Count; i++)
            {
                float gap = room.keepOut[i].DistanceTo(xz) - need;
                if (gap < s.keepOutMargin) { spot.reason = "Over a screen or desk zone"; return spot; }
                clearance = Mathf.Min(clearance, gap);
            }
            for (int i = 0; i < room.obstacles.Count; i++)
            {
                float gap = room.obstacles[i].DistanceTo(xz) - need;
                if (gap < s.obstacleMargin) { spot.reason = "Furniture is in the way"; return spot; }
                clearance = Mathf.Min(clearance, gap);
            }
            clearance = Mathf.Min(clearance, room.floor.InsideMargin(xz) - need);

            Vector2 d = xz - XZ(user.head);
            float dist = d.magnitude;
            if (dist < s.personalRadius + need) { spot.reason = "Too close to you"; return spot; }
            if (dist > s.maxDistance) { spot.reason = "Too far away"; return spot; }
            float ang = Vector2.Angle(XZ(user.forward), d);
            if (ang < s.frontConeDeg) { spot.reason = "In front of you"; return spot; }
            if (ang > s.maxSideDeg) { spot.reason = "Behind you"; return spot; }

            spot.valid = true;
            spot.reason = "Clear space beside you";
            float sideTerm = Mathf.Clamp01(1f - Mathf.Abs(ang - 85f) / 60f);
            float distTerm = Mathf.Clamp01(1f - Mathf.Abs(dist - 1.2f) / 0.8f);
            spot.score = sideTerm * 0.5f + distTerm * 0.3f + Mathf.Clamp01(clearance / 0.5f) * 0.2f;
            return spot;
        }

        /// <summary>Best valid spot, or the stable fallback (<c>isFallback</c> true, <c>valid</c> false).</summary>
        public static PortalSpot Plan(SeatedUser user, PortalRoomBounds room, PortalSpotSettings s)
        {
            PortalSpot best = default(PortalSpot);
            bool have = false;
            if (room != null && room.hasFloor)
            {
                Vector2 fwd = XZ(user.forward);
                for (int side = 0; side < 2; side++)
                    for (int a = 0; a < s.sideAnglesDeg.Length; a++)
                        for (int k = 0; k < s.distances.Length; k++)
                        {
                            float deg = side == 0 ? s.sideAnglesDeg[a] : -s.sideAnglesDeg[a];   // positive = user's right; right side first (tie-break)
                            Vector2 dir = Rotate(fwd, deg);
                            Vector2 p = XZ(user.head) + dir * s.distances[k];
                            PortalSpot c = Evaluate(user, room, s, p);
                            if (c.valid && (!have || c.score > best.score + 1e-5f)) { best = c; have = true; }
                        }
            }
            if (have) return best;
            return Fallback(user, room, s);
        }

        /// <summary>
        /// The comfort position used when nothing is clear: depends only on the user pose and the settings
        /// (and the floor height), so it does not jitter as geometry changes. Flagged, never "valid".
        /// </summary>
        public static PortalSpot Fallback(SeatedUser user, PortalRoomBounds room, PortalSpotSettings s)
        {
            float floorY = room != null && room.hasFloor ? room.floorY : user.head.y - 1.2f;
            Vector3 right = user.Right;
            float r = s.personalRadius + s.bodyRadius + s.fallbackExtra;
            var pos = new Vector3(user.head.x + right.x * r, floorY, user.head.z + right.z * r);
            return new PortalSpot
            {
                position = pos,
                rotation = FaceUser(pos, user),
                valid = false,
                isFallback = true,
                score = 0f,
                reason = room != null && room.hasFloor ? "No clear space found: showing the nearest comfortable spot" : "No room scan: showing a comfortable spot, unchecked",
            };
        }

        public static Quaternion FaceUser(Vector3 from, SeatedUser user)
        {
            Vector3 d = user.head - from; d.y = 0f;
            return d.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        // Same sense as Quaternion.AngleAxis(deg, up) * forward on the XZ plane.
        static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), sn = Mathf.Sin(r);
            return new Vector2(v.x * c + v.y * sn, -v.x * sn + v.y * c);
        }
    }

    /// <summary>
    /// Holds the chosen spot between frames. It re-evaluates at most every <c>minRecheckSeconds</c>, only after the
    /// user moved or turned beyond a threshold or the room changed, and a still-valid spot is replaced only by a
    /// clearly better one. Never evaluates every frame.
    /// </summary>
    public sealed class PortalSpotTracker
    {
        readonly PortalSpotSettings _s;
        float _lastEval = float.NegativeInfinity;
        Vector3 _lastHead;
        Vector3 _lastForward;
        int _lastRevision;
        public int EvaluationCount { get; private set; }
        public bool HasSpot { get; private set; }
        public PortalSpot Current { get; private set; }

        public PortalSpotTracker(PortalSpotSettings settings) { _s = settings ?? new PortalSpotSettings(); }

        public void Reset() { HasSpot = false; _lastEval = float.NegativeInfinity; EvaluationCount = 0; }

        public PortalSpot Update(float now, SeatedUser user, PortalRoomBounds room)
        {
            int rev = room != null ? room.revision : 0;
            if (!HasSpot) return Adopt(now, user, rev, PortalSpotPlanner.Plan(user, room, _s));
            if (now - _lastEval < _s.minRecheckSeconds) return Current;

            Vector3 dh = user.head - _lastHead; dh.y = 0f;
            bool moved = dh.magnitude > _s.moveThreshold;
            bool turned = Vector3.Angle(user.forward, _lastForward) > _s.turnThresholdDeg;
            PortalSpot now0 = Current.isFallback
                ? Current
                : PortalSpotPlanner.Evaluate(user, room, _s, new Vector2(Current.position.x, Current.position.z));
            bool changedRoom = rev != _lastRevision;
            if (!moved && !turned && !changedRoom && (Current.isFallback || now0.valid)) { _lastEval = now; return Current; }

            PortalSpot fresh = PortalSpotPlanner.Plan(user, room, _s);
            if (!Current.isFallback && now0.valid && fresh.valid && fresh.score < now0.score + _s.switchMargin)
            {
                // Keep the old spot, but follow the user's facing and refresh the stored reference pose.
                var keep = now0;
                return Adopt(now, user, rev, keep);
            }
            return Adopt(now, user, rev, fresh);
        }

        PortalSpot Adopt(float now, SeatedUser user, int rev, PortalSpot spot)
        {
            Current = spot; HasSpot = true;
            _lastEval = now; _lastHead = user.head; _lastForward = user.forward; _lastRevision = rev;
            EvaluationCount++;
            return spot;
        }
    }
}
