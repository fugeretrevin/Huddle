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
        /// <summary>The floor's bounding box (always set with <see cref="hasFloor"/>).</summary>
        public PortalBox floor;
        /// <summary>Exact floor outline in world XZ. Null: the floor is the box. Set: the body must be inside it, clear of every edge.</summary>
        public Vector2[] floorPolygon;
        public float floorY;
        /// <summary>Ceiling height (world). &lt;= floorY means unknown (no head-room check).</summary>
        public float ceilingY;
        /// <summary>Furniture and other solid things the avatar must not touch.</summary>
        public readonly List<PortalBox> obstacles = new List<PortalBox>();
        /// <summary>Screens, the desk front, anything the user looks at: bigger margin than obstacles.</summary>
        public readonly List<PortalBox> keepOut = new List<PortalBox>();
        /// <summary>Labelled keep-out volumes (scanned screens and their line of sight, the desk front, doors, the
        /// portal's controls), oriented, each with its own margin and vertical range.</summary>
        public readonly List<PortalKeepOutVolume> volumes = new List<PortalKeepOutVolume>();
        /// <summary>How many SCREEN / TABLE / door volumes came from the scan. 0 screens does NOT mean there is no monitor:
        /// the default forward work area is always applied.</summary>
        public int screensFromScan, desksFromScan, doorsFromScan;
        /// <summary>Bump when the underlying room changed so the tracker re-evaluates.</summary>
        public int revision;
    }

    [Serializable]
    public sealed class PortalSpotSettings
    {
        [Tooltip("Radius of the avatar's body footprint at the planned scale (m). A taller avatar is wider (comfort.radiusPerHeight).")]
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
        public float[] sideAnglesDeg = { 75f, 95f, 60f, 105f };
        public float[] distances = { 1.15f, 1.35f, 1.0f, 1.6f };
        [Tooltip("Head room required above the avatar when the ceiling is known (m).")]
        public float headRoom = 0.12f;
        public float avatarHeight = 1.0f;
        [Tooltip("Fallback sits this far beyond the envelope, on the user's right (m).")]
        public float fallbackExtra = 0.10f;

        [Tooltip("Seated user keep-out (head/torso, hands, forward work area), labelled volume margins, view limit and the swept body model.")]
        public SeatedComfortSettings comfort = new SeatedComfortSettings();

        [Header("Hysteresis")]
        public float minRecheckSeconds = 0.75f;
        public float moveThreshold = 0.30f;
        public float turnThresholdDeg = 25f;
        [Tooltip("A turn re-seats the frame only after the user has looked away from the spot this long (s). Looking AT the spot never moves it.")]
        public float reseatTurnSeconds = 2.5f;
        [Tooltip("Within this angle of the spot the user is looking at it (deg).")]
        public float lookAtSpotDeg = 35f;
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
    /// Pure placement of the large portal avatar. A spot is valid only when the avatar's whole appearance (its body
    /// swept from the first grow frame to full size, leaning toward the user) keeps clear of: the floor edge, the
    /// seated user's head/torso column, hand-comfort zone and default forward work area, every scanned screen (and
    /// the line of sight to it), the user's desk front, doors, furniture and the legacy keep-out boxes; and the spot is
    /// beside the user (outside the front cone, inside the comfortable view angle). Nothing clear: a stable flagged
    /// fallback that the UI must present as "not enough space". <see cref="PortalSpotTracker"/> adds hysteresis.
    /// </summary>
    public static class PortalSpotPlanner
    {
        static Vector2 XZ(Vector3 v) { return new Vector2(v.x, v.z); }

        /// <summary>Full-size body radius for these settings (grows with the avatar height).</summary>
        public static float BodyRadius(PortalSpotSettings s)
        {
            return SeatedUserKeepOut.BodyRadius(s.avatarHeight, s.bodyRadius, s.comfort);
        }

        /// <summary>Checks one floor point (XZ) against everything. Fills valid/score/reason.</summary>
        public static PortalSpot Evaluate(SeatedUser user, PortalRoomBounds room, PortalSpotSettings s, Vector2 xz)
        {
            float floorY = room != null ? room.floorY : user.head.y - 1.2f;
            var spot = new PortalSpot { position = new Vector3(xz.x, floorY, xz.y) };
            spot.rotation = FaceUser(spot.position, user);
            if (room == null || !room.hasFloor) { spot.reason = "No room bounds"; return spot; }
            SeatedComfortSettings comfort = s.comfort ?? (s.comfort = new SeatedComfortSettings());
            if (room.ceilingY > room.floorY && room.ceilingY - room.floorY - s.headRoom < s.avatarHeight) { spot.reason = "Not enough head room"; return spot; }

            float need = BodyRadius(s);
            Vector2 d = xz - XZ(user.head);
            float dist = d.magnitude;
            if (dist < s.personalRadius + need) { spot.reason = "Too close to you"; return spot; }
            if (dist > s.maxDistance) { spot.reason = "Too far away"; return spot; }
            float ang = Vector2.Angle(XZ(user.forward), d);
            if (ang < s.frontConeDeg) { spot.reason = "In front of you"; return spot; }
            if (ang > s.maxSideDeg) { spot.reason = "Behind you"; return spot; }
            if (ang > comfort.maxViewDeg) { spot.reason = "Behind you: too far round to see comfortably"; return spot; }

            // The whole appearance, not the final centre: the body is swept from the first grow frame to full size.
            float clearance;
            string why;
            if (!SweepClear(user, room, s, xz, comfort.emergeGrow, xz, 1f, out clearance, out why)) { spot.reason = why; return spot; }

            spot.valid = true;
            spot.reason = "Clear space beside you";
            float sideTerm = Mathf.Clamp01(1f - Mathf.Abs(ang - 85f) / 60f);
            float distTerm = Mathf.Clamp01(1f - Mathf.Abs(dist - 1.3f) / 0.8f);
            spot.score = sideTerm * 0.5f + distTerm * 0.3f + Mathf.Clamp01(clearance / 0.5f) * 0.2f;
            return spot;
        }

        /// <summary>
        /// Swept-volume check of the avatar body from (fromXZ, fromGrow) to (toXZ, toGrow), grow = fraction of full size.
        /// The body at grow g is the circle enclosing a capsule from its floor centre leaning g * reach toward the user, radius
        /// g * bodyRadius, column floor .. floor + g * height + headRoom. Samples are at most comfort.sweepStep apart (boundary
        /// travel) and each is padded by half the actual spacing, so the gaps between samples are covered (fail closed).
        /// <paramref name="record"/> (tests, drivers) receives every sample as (x, z, radius, top).
        /// </summary>
        public static bool SweepClear(SeatedUser user, PortalRoomBounds room, PortalSpotSettings s,
                                      Vector2 fromXZ, float fromGrow, Vector2 toXZ, float toGrow,
                                      out float clearance, out string reason, List<Vector4> record = null)
        {
            clearance = float.MaxValue;
            reason = null;
            if (room == null || !room.hasFloor) { clearance = 0f; reason = "No room bounds"; return false; }

            SeatedComfortSettings c = s.comfort ?? (s.comfort = new SeatedComfortSettings());
            float body = BodyRadius(s);
            float reach = SeatedUserKeepOut.ReachTowardUser(s.avatarHeight, c);
            float g0 = Mathf.Clamp01(fromGrow), g1 = Mathf.Clamp01(toGrow);
            float travel = (toXZ - fromXZ).magnitude + Mathf.Abs(g1 - g0) * Mathf.Max(body + reach, s.avatarHeight);
            int n = Mathf.Clamp(Mathf.CeilToInt(travel / Mathf.Max(0.01f, c.sweepStep)), 1, 400);
            float pad = travel / n * 0.5f;

            // The user's own zones, from the seated frame given (never from the instantaneous gaze).
            PortalKeepOutVolume hands = SeatedUserKeepOut.HandZone(user, c);
            PortalKeepOutVolume work = SeatedUserKeepOut.WorkArea(user, c);
            float envelope = SeatedUserKeepOut.HeadTorsoEnvelope(c);
            Vector2 head = XZ(user.head);

            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                Vector2 p = Vector2.Lerp(fromXZ, toXZ, t);
                float g = Mathf.Lerp(g0, g1, t);
                Vector2 toUser = head - p;
                float len = toUser.magnitude;
                toUser = len > 1e-5f ? toUser / len : Vector2.zero;
                Vector2 centre = p + toUser * (reach * g * 0.5f);
                float r = body * g + reach * g * 0.5f + pad;
                float top = room.floorY + s.avatarHeight * g + s.headRoom;
                if (record != null) record.Add(new Vector4(centre.x, centre.y, r, top));

                // Floor outline (walls).
                float edge;
                if (room.floorPolygon != null && room.floorPolygon.Length >= 3)
                {
                    if (!SpatialGeometry.PointInPolygon(room.floorPolygon, centre)) { reason = "Off the scanned floor"; return Fail(out clearance); }
                    edge = SpatialGeometry.DistanceToPolygonEdge(room.floorPolygon, centre);
                }
                else edge = room.floor.InsideMargin(centre);
                if (edge - r < s.wallMargin) { reason = "Too close to a wall"; return Fail(out clearance); }
                clearance = Mathf.Min(clearance, edge - r);

                // The seated user: head and torso (ClearsUserEnvelope), hands, the default forward work area.
                if (!PortalAvatarScale.ClearsUserEnvelope(new Vector3(centre.x, user.head.y, centre.y), r, user, envelope)) { reason = "Too close to you"; return Fail(out clearance); }
                if (!VolumeClear(hands, centre, r, room.floorY, top, ref clearance)) { reason = hands.Reason; return Fail(out clearance); }
                if (!VolumeClear(work, centre, r, room.floorY, top, ref clearance)) { reason = work.Reason; return Fail(out clearance); }

                // Labelled volumes from the scan (and the controls, when the caller gave them).
                for (int k = 0; k < room.volumes.Count; k++)
                    if (!VolumeClear(room.volumes[k], centre, r, room.floorY, top, ref clearance)) { reason = room.volumes[k].Reason; return Fail(out clearance); }

                for (int k = 0; k < room.keepOut.Count; k++)
                {
                    float gap = room.keepOut[k].DistanceTo(centre) - r;
                    if (gap < s.keepOutMargin) { reason = "Over a screen or desk zone"; return Fail(out clearance); }
                    clearance = Mathf.Min(clearance, gap);
                }
                for (int k = 0; k < room.obstacles.Count; k++)
                {
                    float gap = room.obstacles[k].DistanceTo(centre) - r;
                    if (gap < s.obstacleMargin) { reason = "Furniture is in the way"; return Fail(out clearance); }
                    clearance = Mathf.Min(clearance, gap);
                }
            }
            return true;
        }

        static bool Fail(out float clearance) { clearance = 0f; return false; }

        static bool VolumeClear(PortalKeepOutVolume v, Vector2 centre, float r, float bottom, float top, ref float clearance)
        {
            if (!SpatialGeometry.VerticalRangesTouch(bottom, top, v.minY, v.maxY, 0.02f)) return true;
            float gap = v.footprint.DistanceToPoint(centre) - r;
            if (gap < v.margin) return false;
            clearance = Mathf.Min(clearance, gap);
            return true;
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
        /// (and the floor height), so it does not jitter as geometry changes. Flagged, never "valid": the large
        /// avatar never opens here. It sits outside the head and hand zones.
        /// </summary>
        public static PortalSpot Fallback(SeatedUser user, PortalRoomBounds room, PortalSpotSettings s)
        {
            float floorY = room != null && room.hasFloor ? room.floorY : user.head.y - 1.2f;
            Vector3 right = user.Right;
            float body = BodyRadius(s);
            SeatedComfortSettings c = s.comfort ?? new SeatedComfortSettings();
            float r = Mathf.Max(s.personalRadius, c.handReachSide + c.handMargin) + body + s.fallbackExtra;
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
    /// user moved beyond a threshold, the room changed, or the user has looked AWAY from the spot (not at it) for
    /// <c>reseatTurnSeconds</c>; a still-valid spot is replaced only by a clearly better one. The seated frame (forward)
    /// is held between re-seats, so turning the head to look at the ring never moves it. Never evaluates every frame.
    /// </summary>
    public sealed class PortalSpotTracker
    {
        readonly PortalSpotSettings _s;
        float _lastEval = float.NegativeInfinity;
        Vector3 _lastHead;
        Vector3 _seatForward = Vector3.forward;
        float _turnedSince = -1f;
        int _lastRevision;
        public int EvaluationCount { get; private set; }
        public bool HasSpot { get; private set; }
        public PortalSpot Current { get; private set; }
        /// <summary>The seated forward the spots are planned against.</summary>
        public Vector3 SeatForward => _seatForward;

        public PortalSpotTracker(PortalSpotSettings settings) { _s = settings ?? new PortalSpotSettings(); }

        public void Reset() { HasSpot = false; _lastEval = float.NegativeInfinity; EvaluationCount = 0; _turnedSince = -1f; }

        public PortalSpot Update(float now, SeatedUser user, PortalRoomBounds room)
        {
            int rev = room != null ? room.revision : 0;
            if (!HasSpot)
            {
                _seatForward = user.forward;
                _turnedSince = -1f;
                SeatedUser seat0 = new SeatedUser(user.head, _seatForward);
                return Adopt(now, seat0, rev, PortalSpotPlanner.Plan(seat0, room, _s));
            }
            if (now - _lastEval < _s.minRecheckSeconds) return Current;

            Vector3 dh = user.head - _lastHead; dh.y = 0f;
            bool moved = dh.magnitude > _s.moveThreshold;
            bool turned = false;
            if (moved) { _seatForward = user.forward; _turnedSince = -1f; }        // the user moved: re-seat
            else if (LookingAwayLongEnough(now, user)) { _seatForward = user.forward; _turnedSince = -1f; turned = true; }
            SeatedUser seat = new SeatedUser(user.head, _seatForward);

            PortalSpot now0 = Current.isFallback
                ? Current
                : PortalSpotPlanner.Evaluate(seat, room, _s, new Vector2(Current.position.x, Current.position.z));
            bool changedRoom = rev != _lastRevision;
            if (!moved && !turned && !changedRoom && (Current.isFallback || now0.valid)) { _lastEval = now; return Current; }

            PortalSpot fresh = PortalSpotPlanner.Plan(seat, room, _s);
            if (!Current.isFallback && now0.valid && fresh.valid && fresh.score < now0.score + _s.switchMargin)
            {
                // Keep the old spot, but refresh the stored reference pose.
                return Adopt(now, seat, rev, now0);
            }
            return Adopt(now, seat, rev, fresh);
        }

        // A turn re-seats the frame only when it is sustained and NOT toward the spot (looking at the ring is not a turn).
        bool LookingAwayLongEnough(float now, SeatedUser user)
        {
            bool away = Vector3.Angle(user.forward, _seatForward) > _s.turnThresholdDeg;
            if (away && HasSpot)
            {
                Vector3 toSpot = Current.position - user.head; toSpot.y = 0f;
                if (toSpot.sqrMagnitude > 1e-6f && Vector3.Angle(user.forward, toSpot) <= _s.lookAtSpotDeg) away = false;
            }
            if (!away) { _turnedSince = -1f; return false; }
            if (_turnedSince < 0f) { _turnedSince = now; return false; }
            return now - _turnedSince >= _s.reseatTurnSeconds;
        }

        PortalSpot Adopt(float now, SeatedUser seat, int rev, PortalSpot spot)
        {
            Current = spot; HasSpot = true;
            _lastEval = now; _lastHead = seat.head; _lastRevision = rev;
            EvaluationCount++;
            return spot;
        }
    }
}
