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
        /// <summary>Floor must fill at least this fraction of its bounding box, else the box would over-claim space.</summary>
        public const float RectangleFill = 0.9f;

        /// <summary>
        /// Fills <paramref name="into"/> (reused: no allocation once the lists are grown). Returns false (hasFloor = false) for a
        /// missing room or a floor that is not close to a rectangle; the caller then uses the exact legacy evaluator.
        /// Furniture oriented rects become their (conservative) world AABBs.
        /// </summary>
        public static bool Build(IRoomGeometry room, SeatedUser user, PortalRoomBounds into)
        {
            into.obstacles.Clear(); into.keepOut.Clear();
            into.hasFloor = false;
            into.revision = 0;
            if (room == null || room.Availability != RoomAvailability.Available || room.FloorCount == 0) return false;

            int fi = SpatialGeometry.FindFloor(room, new Vector2(user.head.x, user.head.z));
            if (fi < 0) fi = 0;
            RoomPlane floor = room.GetFloor(fi);
            if (floor.polygon == null || floor.polygon.Length < 3) return false;

            Vector2 mn = floor.polygon[0], mx = floor.polygon[0];
            float area2 = 0f;
            for (int i = 0; i < floor.polygon.Length; i++)
            {
                Vector2 a = floor.polygon[i], b = floor.polygon[(i + 1) % floor.polygon.Length];
                mn = Vector2.Min(mn, a); mx = Vector2.Max(mx, a);
                area2 += a.x * b.y - b.x * a.y;
            }
            float boxArea = (mx.x - mn.x) * (mx.y - mn.y);
            if (boxArea < 1e-4f || Mathf.Abs(area2) * 0.5f < RectangleFill * boxArea) return false;

            into.floor = new PortalBox(mn, mx);
            into.floorY = floor.height;
            into.ceilingY = float.NegativeInfinity;
            int ci = SpatialGeometry.FindCeiling(room, new Vector2(user.head.x, user.head.z));
            if (ci >= 0) into.ceilingY = room.GetCeiling(ci).height;
            into.hasFloor = true;

            for (int i = 0; i < room.FurnitureCount; i++)
            {
                FurnitureBox f = room.GetFurniture(i);
                if (f.maxY < floor.height + 0.05f || f.minY > floor.height + 2.5f) continue;   // not in the avatar's column
                float yaw = f.yawDeg * Mathf.Deg2Rad;
                float c = Mathf.Abs(Mathf.Cos(yaw)), s = Mathf.Abs(Mathf.Sin(yaw));
                Vector2 half = new Vector2(f.halfSize.x * c + f.halfSize.y * s, f.halfSize.x * s + f.halfSize.y * c);
                into.obstacles.Add(PortalBox.FromCenter(f.center, half));
            }
            into.revision = room.FurnitureCount * 31 + room.FloorCount;
            return true;
        }

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
            if (r.IndexOf("Furniture", StringComparison.OrdinalIgnoreCase) >= 0 || r.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0) return PortalProblem.HitsFurniture;
            if (r.IndexOf("close to you", StringComparison.OrdinalIgnoreCase) >= 0) return PortalProblem.TooCloseToUser;
            if (r.IndexOf("front", StringComparison.OrdinalIgnoreCase) >= 0) return PortalProblem.InFrontOfUser;
            if (r.IndexOf("Behind", StringComparison.OrdinalIgnoreCase) >= 0) return PortalProblem.BehindUser;
            if (r.IndexOf("far", StringComparison.OrdinalIgnoreCase) >= 0) return PortalProblem.TooFar;
            return s.isFallback ? PortalProblem.NoRoom : PortalProblem.OffFloor;
        }

        public const string NotEnoughSpace = "Not enough space here";
    }
}
