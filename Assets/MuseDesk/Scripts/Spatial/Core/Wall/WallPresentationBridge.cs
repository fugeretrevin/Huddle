using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>
    /// Adapter logic between the Wall's existing flows (AgentVisitFlow / PortalFlow / registry) and the A9
    /// <see cref="PresentationLifecycle"/>. Pure, engine-light, EditMode-testable. The legacy flows keep owning the
    /// animation and the states; this mirror guarantees: one presentation per logical agent (shared identity map),
    /// exactly-once ReturnHome + Closed per session, Cancel at any time and BackendLost cleanup.
    /// </summary>
    public sealed class PresentationMirror
    {
        public readonly PresentationIdentityMap map = new PresentationIdentityMap();
        public readonly PresentationLifecycle visit;
        public readonly PresentationLifecycle portal;
        int _visitSeq, _portalSeq;
        public string LastRejection { get; private set; }
        public int RejectedCount { get; private set; }

        public PresentationMirror()
        {
            visit = new PresentationLifecycle(PresentationKind.DeskVisit, map);
            portal = new PresentationLifecycle(PresentationKind.Portal, map);
        }

        /// <summary>
        /// Mirror the legacy flows. Closes first (so a visit-to-portal handover of the same agent can register), then opens.
        /// A presentation opened here is immediately Presenting: the legacy flow owns the opening animation.
        /// </summary>
        public void Sync(bool visitActive, string visitAgent, Vector3 visitHome, bool portalActive, string portalAgent, Vector3 portalHome)
        {
            CloseIfStale(visit, visitActive, visitAgent);
            CloseIfStale(portal, portalActive, portalAgent);
            if (visitActive) OpenIfIdle(visit, "visit-" + (_visitSeq + 1), visitAgent, visitHome, ref _visitSeq);
            if (portalActive) OpenIfIdle(portal, "portal-" + (_portalSeq + 1), portalAgent, portalHome, ref _portalSeq);
        }

        void CloseIfStale(PresentationLifecycle l, bool active, string agent)
        {
            if (l.Phase == PresentationPhase.Idle) return;
            if (active && agent == l.AgentId) return;
            l.RequestClose();
            l.Tick(10f);                                   // the legacy flow already animated the return
        }

        void OpenIfIdle(PresentationLifecycle l, string id, string agent, Vector3 home, ref int seq)
        {
            if (l.Phase != PresentationPhase.Idle || string.IsNullOrEmpty(agent)) return;
            PresentationOpenResult r = l.Open(id, agent, home);
            if (r == PresentationOpenResult.Started) { seq++; l.MarkReady(); }
            else { RejectedCount++; LastRejection = l.Message; }
        }

        /// <summary>Cancel both at any point: ReturnHome and Closed(Cancelled) fire once each per open session.</summary>
        public void CancelAll()
        {
            CancelOne(visit);
            CancelOne(portal);
        }

        static void CancelOne(PresentationLifecycle l)
        {
            l.Cancel();
            l.Tick(10f);
        }

        /// <summary>Backend lost: immediate cleanup, no animation.</summary>
        public void BackendLost() { visit.BackendLost(); portal.BackendLost(); }

        public bool AnyOpen => visit.Phase != PresentationPhase.Idle || portal.Phase != PresentationPhase.Idle;

        /// <summary>Tether for a presentation that is open; inactive otherwise. <paramref name="phase"/> and
        /// <paramref name="progress"/> come from the legacy flow (it owns the animation).</summary>
        public static PresentationTether TetherFor(PresentationPhase phase, float progress, Vector3 home, Vector3 point)
        {
            return PresentationTether.Compute(phase, progress, home, point);
        }
    }

    /// <summary>Tracks the backend live/lost edge: a loss is only a transition from live to not live (offline demo is never a loss).</summary>
    public struct BackendLossDetector
    {
        bool _wasLive;
        public bool Update(bool isLive)
        {
            bool lost = _wasLive && !isLive;
            _wasLive = isLive;
            return lost;
        }
    }

    /// <summary>Flattens an <see cref="IRoomGeometry"/> into <see cref="PortalRoomBounds"/> and converts spots to legacy candidates.</summary>
    public static class PortalSpotAdapter
    {
        /// <summary>Legacy: the floor used to have to fill this fraction of its bounding box. Any floor outline is now planned
        /// exactly (<see cref="PortalRoomBounds.floorPolygon"/>), so the keep-out-aware planner covers every scanned room.</summary>
        public const float RectangleFill = 0.9f;

        static readonly SeatedComfortSettings DefaultComfort = new SeatedComfortSettings();

        /// <summary>
        /// Fills <paramref name="into"/> (reused: no allocation once the lists are grown). Returns false (hasFloor = false) only
        /// for a missing room or a floor without an outline; the planner then offers no large spot (fallback). The floor is the
        /// exact outline (any shape) plus its box. Furniture oriented rects become their (conservative) world AABBs. Keep-out
        /// volumes from labels: SCREEN (and the line of sight to it), the user's TABLE front, and doors when the room also
        /// implements <see cref="IWallGeometry"/> (MRUK does).
        /// </summary>
        public static bool Build(IRoomGeometry room, SeatedUser user, PortalRoomBounds into)
        {
            return Build(room, room as IWallGeometry, user, into, null);
        }

        /// <summary>Same, with walls (doors) given explicitly and the comfort settings that size the label volumes.</summary>
        public static bool Build(IRoomGeometry room, IWallGeometry walls, SeatedUser user, PortalRoomBounds into, SeatedComfortSettings comfort)
        {
            SeatedComfortSettings c = comfort ?? DefaultComfort;
            into.obstacles.Clear(); into.keepOut.Clear(); into.volumes.Clear();
            into.hasFloor = false;
            into.floorPolygon = null;
            into.screensFromScan = into.desksFromScan = into.doorsFromScan = 0;
            into.revision = 0;
            if (room == null || room.Availability != RoomAvailability.Available || room.FloorCount == 0) return false;

            Vector2 head = new Vector2(user.head.x, user.head.z);
            int fi = SpatialGeometry.FindFloor(room, head);
            if (fi < 0) fi = 0;
            RoomPlane floor = room.GetFloor(fi);
            if (floor.polygon == null || floor.polygon.Length < 3) return false;

            Vector2 mn = floor.polygon[0], mx = floor.polygon[0];
            for (int i = 0; i < floor.polygon.Length; i++) { mn = Vector2.Min(mn, floor.polygon[i]); mx = Vector2.Max(mx, floor.polygon[i]); }
            if ((mx.x - mn.x) * (mx.y - mn.y) < 1e-4f) return false;

            into.floor = new PortalBox(mn, mx);
            into.floorPolygon = floor.polygon;
            into.floorY = floor.height;
            into.ceilingY = float.NegativeInfinity;
            int ci = SpatialGeometry.FindCeiling(room, head);
            if (ci >= 0) into.ceilingY = room.GetCeiling(ci).height;
            into.hasFloor = true;

            int hash = 17;
            hash = Mix(hash, floor.polygon.Length); hash = Mix(hash, Q(floor.height)); hash = Mix(hash, Q(mn.x)); hash = Mix(hash, Q(mx.y));
            hash = Mix(hash, ci >= 0 ? Q(into.ceilingY) : -1);
            float userTop = user.head.y + c.aboveHead;

            for (int i = 0; i < room.FurnitureCount; i++)
            {
                FurnitureBox f = room.GetFurniture(i);
                hash = Mix(hash, Q(f.center.x)); hash = Mix(hash, Q(f.center.y)); hash = Mix(hash, Q(f.halfSize.x + f.halfSize.y)); hash = Mix(hash, Q(f.yawDeg));
                if (f.maxY < floor.height + 0.05f || f.minY > floor.height + 2.5f) continue;   // not in the avatar's column
                float yaw = f.yawDeg * Mathf.Deg2Rad;
                float cs = Mathf.Abs(Mathf.Cos(yaw)), sn = Mathf.Abs(Mathf.Sin(yaw));
                Vector2 half = new Vector2(f.halfSize.x * cs + f.halfSize.y * sn, f.halfSize.x * sn + f.halfSize.y * cs);
                into.obstacles.Add(PortalBox.FromCenter(f.center, half));

                OrientedRect fp = f.Footprint;
                if (IsScreen(f.label))
                {
                    // The screen itself (bigger margin than furniture) and the line of sight from the head to it.
                    into.volumes.Add(PortalKeepOutVolume.Make(fp, f.minY, f.maxY, DefaultSpotSettings.keepOutMargin, PortalKeepOutKind.Screen));
                    float w = Mathf.Max(f.halfSize.x, f.halfSize.y) + c.sightlinePad;
                    into.volumes.Add(PortalKeepOutVolume.Make(SeatedUserKeepOut.RectBetween(head, f.center, w), floor.height, Mathf.Max(f.maxY, userTop), c.sightlineMargin, PortalKeepOutKind.ScreenSightline));
                    into.screensFromScan++;
                }
                else if (IsDesk(f.label) && fp.DistanceToPoint(head) <= c.deskNearUser)
                {
                    into.volumes.Add(PortalKeepOutVolume.Make(DeskFront(fp, head, c.deskFrontDepth), floor.height, userTop, c.deskFrontMargin, PortalKeepOutKind.DeskFront));
                    into.desksFromScan++;
                }
            }

            if (walls != null && walls.WallAvailability == RoomAvailability.Available)
            {
                for (int i = 0; i < walls.OpeningCount; i++)
                {
                    WallOpening o = walls.GetOpening(i);
                    if (o.kind != WallOpeningKind.Door) continue;
                    RoomWall w; if (!FindWall(walls, o.wallId, out w)) continue;
                    float u = (o.uMin + o.uMax) * 0.5f;
                    Vector3 sill = w.PointAt(u, floor.height);
                    Vector2 n = new Vector2(w.normal.x, w.normal.z);
                    float depth = Mathf.Max(0.05f, c.doorSwing);
                    var rect = new OrientedRect
                    {
                        center = new Vector2(sill.x, sill.z) + n * (depth * 0.5f),
                        half = new Vector2((o.uMax - o.uMin) * 0.5f + c.doorSidePad, depth * 0.5f),
                        axisY = n,
                        axisX = new Vector2(n.y, -n.x),
                    };
                    into.volumes.Add(PortalKeepOutVolume.Make(rect, Mathf.Min(o.yMin, floor.height), o.yMax, c.doorMargin, PortalKeepOutKind.Door));
                    into.doorsFromScan++;
                    hash = Mix(hash, Q(sill.x)); hash = Mix(hash, Q(sill.z)); hash = Mix(hash, Q(o.uMax - o.uMin));
                }
            }
            into.revision = hash == 0 ? 1 : hash;
            return true;
        }

        static readonly PortalSpotSettings DefaultSpotSettings = new PortalSpotSettings();

        /// <summary>
        /// The portal's controls (the visit card): the avatar keeps clear of the card and of the line of sight from the head to
        /// it, so the user can always see and reach Send back. Call after <see cref="Build"/> (which clears the volumes).
        /// </summary>
        public static void AddControls(PortalRoomBounds into, SeatedUser user, Vector3 controls, SeatedComfortSettings comfort)
        {
            SeatedComfortSettings c = comfort ?? DefaultComfort;
            Vector2 head = new Vector2(user.head.x, user.head.z), at = new Vector2(controls.x, controls.z);
            float lo = Mathf.Min(controls.y, user.head.y) - 0.6f, hi = Mathf.Max(controls.y, user.head.y) + 0.2f;
            into.volumes.Add(PortalKeepOutVolume.Make(SeatedUserKeepOut.RectBetween(head, at, c.controlsSightlineHalfWidth), lo, hi, 0f, PortalKeepOutKind.Controls));
            into.volumes.Add(PortalKeepOutVolume.Make(OrientedRect.FromYaw(at, new Vector2(c.controlsRadius, c.controlsRadius), 0f), lo, hi, 0f, PortalKeepOutKind.Controls));
        }

        /// <summary>MRUK labels arrive as the SceneLabels enum's text, possibly several ("TABLE, SCREEN").</summary>
        public static bool IsScreen(string label)
        {
            return !string.IsNullOrEmpty(label) && (label.IndexOf("SCREEN", StringComparison.OrdinalIgnoreCase) >= 0 || label.IndexOf("MONITOR", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static bool IsDesk(string label)
        {
            return !string.IsNullOrEmpty(label) && (label.IndexOf("TABLE", StringComparison.OrdinalIgnoreCase) >= 0 || label.IndexOf("DESK", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>The zone between a desk's user-facing edge and the user (knees, chair, forearms): the full desk width, depth toward the user.</summary>
        public static OrientedRect DeskFront(OrientedRect desk, Vector2 head, float depth)
        {
            Vector2 toUser = head - desk.center;
            float ax = Vector2.Dot(toUser, desk.axisX), ay = Vector2.Dot(toUser, desk.axisY);
            bool alongX = Mathf.Abs(ax) - desk.half.x > Mathf.Abs(ay) - desk.half.y;   // the face the user is further outside of
            Vector2 n = alongX ? desk.axisX * Mathf.Sign(ax == 0f ? 1f : ax) : desk.axisY * Mathf.Sign(ay == 0f ? 1f : ay);
            float halfAlong = alongX ? desk.half.x : desk.half.y;
            float halfAcross = alongX ? desk.half.y : desk.half.x;
            float d = Mathf.Max(0.05f, depth);
            return new OrientedRect
            {
                center = desk.center + n * (halfAlong + d * 0.5f),
                half = new Vector2(halfAcross, d * 0.5f),
                axisY = n,
                axisX = new Vector2(n.y, -n.x),
            };
        }

        static bool FindWall(IWallGeometry walls, string id, out RoomWall wall)
        {
            for (int i = 0; i < walls.WallCount; i++)
            {
                RoomWall w = walls.GetWall(i);
                if (w.id == id) { wall = w; return true; }
            }
            wall = default(RoomWall);
            return false;
        }

        static int Q(float v) { return float.IsInfinity(v) || float.IsNaN(v) ? 0 : Mathf.RoundToInt(v * 100f); }
        static int Mix(int h, int v) { unchecked { return h * 31 + v; } }

        /// <summary>All valid spots around the user, best first (the "Other spot" cycle). Empty when none.</summary>
        public static List<PortalSpot> ListSpots(SeatedUser user, PortalRoomBounds room, PortalSpotSettings s)
        {
            var list = new List<PortalSpot>();
            if (room == null || !room.hasFloor) return list;
            Vector2 head = new Vector2(user.head.x, user.head.z);
            Vector2 fwd = new Vector2(user.forward.x, user.forward.z);
            for (int side = 0; side < 2; side++)
                for (int a = 0; a < s.sideAnglesDeg.Length; a++)
                    for (int k = 0; k < s.distances.Length; k++)
                    {
                        float deg = side == 0 ? s.sideAnglesDeg[a] : -s.sideAnglesDeg[a];
                        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), sn = Mathf.Sin(r);
                        Vector2 dir = new Vector2(fwd.x * c + fwd.y * sn, -fwd.x * sn + fwd.y * c);
                        PortalSpot p = PortalSpotPlanner.Evaluate(user, room, s, head + dir * s.distances[k]);
                        if (p.valid) list.Add(p);
                    }
            list.Sort((x, y) => y.score.CompareTo(x.score));
            return list;
        }

        /// <summary>Largest avatar height that fits under the known ceiling (or the wanted height).</summary>
        public static float FitHeight(float wantHeight, float floorY, float ceilingY, float headRoom, PortalScaleSettings tmp)
        {
            var s = tmp ?? new PortalScaleSettings();
            s.minScale = 0.001f; s.maxScale = 1000f; s.headRoom = headRoom;
            return PortalAvatarScale.Compute(1f, 0f, 0.3f, wantHeight, floorY, ceilingY, s).height;
        }

        /// <summary>Legacy candidate for the existing PortalFlow. A fallback is never valid (the UI says there is not enough space).</summary>
        public static PortalCandidate ToCandidate(PortalSpot spot, bool roomChecked, float avatarHeight)
        {
            return new PortalCandidate
            {
                position = spot.position,
                rotation = spot.rotation,
                avatarHeight = avatarHeight,
                valid = spot.valid && !spot.isFallback,
                roomChecked = roomChecked,
                problem = spot.valid ? PortalProblem.None : ProblemOf(spot),
                reason = spot.reason,
                score = spot.score,
                minClearance = 0f,
            };
        }

        static PortalProblem ProblemOf(PortalSpot s)
        {
            string r = s.reason ?? "";
            if (r.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0) return PortalProblem.NearWall;
            if (r.IndexOf("head room", StringComparison.OrdinalIgnoreCase) >= 0) return PortalProblem.LowCeiling;
            if (r.IndexOf("Furniture", StringComparison.OrdinalIgnoreCase) >= 0 || r.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0
                || r.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0 || r.IndexOf("controls", StringComparison.OrdinalIgnoreCase) >= 0) return PortalProblem.HitsFurniture;
            if (r.IndexOf("close to you", StringComparison.OrdinalIgnoreCase) >= 0) return PortalProblem.TooCloseToUser;
            if (r.IndexOf("front", StringComparison.OrdinalIgnoreCase) >= 0) return PortalProblem.InFrontOfUser;
            if (r.IndexOf("Behind", StringComparison.OrdinalIgnoreCase) >= 0) return PortalProblem.BehindUser;
            if (r.IndexOf("far", StringComparison.OrdinalIgnoreCase) >= 0) return PortalProblem.TooFar;
            return s.isFallback ? PortalProblem.NoRoom : PortalProblem.OffFloor;
        }

        public const string NotEnoughSpace = "Not enough space here";
    }
}
