// =============================================================================
// MuseDesk · EditMode tests · Wall Habitat placement (WallPlacementEvaluator, WallMath, WallPlacementCodec,
// WallModeFlow). Pure logic against synthetic rooms. Not proven here: a real MRUK scan, real hands.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class WallPlacementTests
    {
        // A 4 x 4 m room centred on the origin, floor at 0, ceiling 2.5. The user sits at the centre facing +Z
        // (toward wall-N, where the monitor is), eye 1.2 m.
        static SyntheticWallRoom Room(float w = 4f, float d = 4f, float ceiling = 2.5f) { return SyntheticWallRoom.Rectangle(0f, 0f, w, d, 0f, ceiling); }
        static SeatedUser User(Vector3? head = null, Vector3? fwd = null) { return new SeatedUser(head ?? new Vector3(0f, 1.2f, 0f), fwd ?? Vector3.forward); }
        static readonly WallPlacementSettings S = new WallPlacementSettings();

        static WallCandidate Eval(SyntheticWallRoom r, string wall, float u, float y, WallLayoutKind k = WallLayoutKind.Full, SeatedUser? user = null)
        {
            return WallPlacementEvaluator.Evaluate(r.room, r, r.IndexOf(wall), u, y, k, user ?? User(), S);
        }

        [Test]
        public void GoodSideWall_IsValid_AndFacesIntoTheRoom()
        {
            var r = Room();
            WallCandidate c = Eval(r, "wall-E", 0.6f, 1.35f);
            Assert.IsTrue(c.valid, c.reason);
            Assert.IsTrue(c.roomChecked);
            Vector3 fwd = c.rotation * Vector3.forward;
            Assert.Greater(Vector3.Dot(fwd, Vector3.left), 0.99f, "habitat +Z must point out of wall-E into the room (-X)");
            Assert.AreEqual(1f, Vector3.Dot(c.rotation * Vector3.up, Vector3.up), 1e-4f, "no tilt");
            Assert.AreEqual(2f - S.wallStandoff, c.position.x, 1e-3f, "backplate sits on the wall surface");
        }

        [Test]
        public void WorldLocalRoundTrip_VolumeCentreIsHalfDepthOut()
        {
            var r = Room();
            WallCandidate c = Eval(r, "wall-W", -0.4f, 1.3f);
            Assert.IsTrue(c.valid, c.reason);
            Vector3 local = new Vector3(0.1f, -0.2f, 0.05f);
            Vector3 world = c.position + c.rotation * local;
            Vector3 back = Quaternion.Inverse(c.rotation) * (world - c.position);
            Assert.Less((back - local).magnitude, 1e-5f);
            Assert.Less((c.VolumeCenter - (c.position + c.rotation * new Vector3(0, 0, c.size.z * 0.5f))).magnitude, 1e-5f);
            Assert.Greater(c.VolumeCenter.x, c.position.x, "wall-W normal is +X; the volume is in the room");
        }

        [Test]
        public void StraightAhead_IsRefused_AsOverTheMonitor()
        {
            var r = Room();
            WallCandidate c = Eval(r, "wall-N", 0f, 1.35f);
            Assert.IsFalse(c.valid);
            Assert.AreEqual(WallProblem.OverMonitor, c.problem);
        }

        [Test]
        public void FindCandidates_PrefersSideWalls_AndAllAreValidAndSpaced()
        {
            var r = Room();
            List<WallCandidate> list = WallPlacementEvaluator.FindCandidates(r.room, r, User(), S);
            Assert.Greater(list.Count, 0);
            Assert.That(list[0].wallId == "wall-E" || list[0].wallId == "wall-W", "best is a side wall, got " + list[0].wallId);
            for (int i = 0; i < list.Count; i++)
            {
                Assert.IsTrue(list[i].valid);
                for (int j = i + 1; j < list.Count; j++)
                    if (list[i].wallIndex == list[j].wallIndex)
                        Assert.GreaterOrEqual((list[i].VolumeCenter - list[j].VolumeCenter).magnitude, S.minCandidateSpacing - 1e-4f);
            }
        }

        [Test]
        public void ShortWall_TooSmallForFull_CompactMayFit()
        {
            var r = Room(4f, 4f, 0.95f);                    // a very low wall (0.95 m)
            Assert.AreEqual(WallProblem.WallTooSmall, Eval(r, "wall-E", 0f, 0.5f).problem);
        }

        [Test]
        public void NarrowCorner_WallTooSmall()
        {
            var r = Room(1.0f, 4f);
            WallCandidate c = Eval(r, "wall-N", 0f, 1.35f, WallLayoutKind.Full);
            Assert.AreEqual(WallProblem.WallTooSmall, c.problem);
        }

        [Test]
        public void DoorAndWindow_AreAvoided()
        {
            var r = Room();
            r.AddOpening("wall-E", WallOpeningKind.Door, 0.6f, 1.0f, 0.9f, 2.0f);
            Assert.AreEqual(WallProblem.OverlapsDoor, Eval(r, "wall-E", 0.6f, 1.35f).problem);
            r.AddOpening("wall-W", WallOpeningKind.Window, -0.5f, 1.4f, 1.0f, 1.0f);
            Assert.AreEqual(WallProblem.OverlapsWindow, Eval(r, "wall-W", -0.5f, 1.35f).problem);
            // Far enough along the wall the spot is fine again.
            Assert.IsTrue(Eval(r, "wall-E", -0.9f, 1.35f).valid);
            foreach (var c in WallPlacementEvaluator.FindCandidates(r.room, r, User(), S))
                Assert.IsFalse(c.problem == WallProblem.OverlapsDoor || c.problem == WallProblem.OverlapsWindow);
        }

        [Test]
        public void TallFurniture_Blocks_DeskBelow_DoesNot()
        {
            var r = Room();
            r.room.AddFurniture("STORAGE", new Vector3(1.8f, 1.0f, 0.6f), new Vector3(0.4f, 2.0f, 0.5f));   // tall shelf against wall-E
            WallCandidate blocked = Eval(r, "wall-E", 0.6f, 1.35f);
            Assert.AreEqual(WallProblem.HitsFurniture, blocked.problem);
            var r2 = Room();
            r2.room.AddFurniture("TABLE", new Vector3(1.6f, 0.37f, 0.6f), new Vector3(0.8f, 0.74f, 1.2f));  // a desk below the spot
            Assert.IsTrue(Eval(r2, "wall-E", 0.6f, 1.35f).valid, "a desk under the habitat is not in its volume");
        }

        [Test]
        public void FacingAway_WhenUserIsBehindTheWall()
        {
            var r = Room();
            var outside = User(new Vector3(3f, 1.2f, 0f), Vector3.left);
            Assert.AreEqual(WallProblem.FacingAway, Eval(r, "wall-E", 0f, 1.35f, WallLayoutKind.Full, outside).problem);
        }

        [Test]
        public void TooCloseToTheUser_IsRefused()
        {
            var r = Room();
            var nearWall = User(new Vector3(1.6f, 1.2f, 0f), Vector3.forward);
            Assert.AreEqual(WallProblem.TooCloseToUser, Eval(r, "wall-E", 0.0f, 1.35f, WallLayoutKind.Full, nearWall).problem);
        }

        [Test]
        public void TooHighOrLowForTheSeatedEye()
        {
            var r = Room();
            Assert.AreEqual(WallProblem.TooHigh, Eval(r, "wall-E", 0.6f, 1.2f + 0.8f).problem);
            Assert.AreEqual(WallProblem.TooLow, Eval(r, "wall-E", 0.6f, 0.6f).problem);
        }

        [Test]
        public void RotatedRoom_NormalsPointInward_AndCandidatesExist()
        {
            // A 4 x 4 room rotated 30 degrees about the origin.
            var geo = new SyntheticRoomGeometry();
            Quaternion q = Quaternion.Euler(0, 30, 0);
            var corners = new Vector2[4];
            Vector3[] local = { new Vector3(-2, 0, -2), new Vector3(2, 0, -2), new Vector3(2, 0, 2), new Vector3(-2, 0, 2) };
            for (int i = 0; i < 4; i++) { Vector3 w = q * local[i]; corners[i] = new Vector2(w.x, w.z); }
            geo.AddFloor(corners, 0f);
            geo.AddCeiling(corners, 2.5f);
            var r = new SyntheticWallRoom(geo);
            for (int i = 0; i < 4; i++) r.AddWall(RoomWall.FromEnds("w" + i, corners[i], corners[(i + 1) % 4], 0f, 2.5f, Vector2.zero));
            for (int i = 0; i < r.WallCount; i++)
            {
                RoomWall w = r.GetWall(i);
                Assert.Greater(Vector3.Dot(w.normal, -new Vector3(w.center.x, 0, w.center.z).normalized), 0.99f, "normal points into the room");
            }
            var list = WallPlacementEvaluator.FindCandidates(geo, r, User(null, q * Vector3.forward), S);
            Assert.Greater(list.Count, 0);
        }

        [Test]
        public void PartialScan_TwoWallsOnly_StillWorks_NoFurnitureChecksRun()
        {
            var r = Room();
            var partial = new SyntheticWallRoom(r.room);
            partial.AddWall(r.GetWall(r.IndexOf("wall-E")));
            partial.AddWall(r.GetWall(r.IndexOf("wall-N")));
            var list = WallPlacementEvaluator.FindCandidates(r.room, partial, User(), S);
            Assert.Greater(list.Count, 0);
            Assert.AreEqual("wall-E", list[0].wallId);
        }

        [Test]
        public void NoPermission_NoRoom_NoCandidates_AndManualIsUnverified()
        {
            var r = Room();
            r.forcedAvailability = RoomAvailability.PermissionDenied;
            Assert.AreEqual(0, WallPlacementEvaluator.FindCandidates(r.room, r, User(), S).Count);
            Assert.AreEqual(WallProblem.NoRoom, WallPlacementEvaluator.Evaluate(r.room, r, 0, 0f, 1.3f, WallLayoutKind.Full, User(), S).problem);
            WallCandidate m = WallPlacementEvaluator.Manual(User(), 65f, 1.5f, 0.15f, WallLayoutKind.Full, S);
            Assert.IsTrue(m.valid);
            Assert.IsFalse(m.roomChecked);
            Assert.IsTrue(m.needsReview);
            Assert.AreEqual(WallProblem.Unverified, m.problem);
            StringAssert.Contains("nothing", m.reason.ToLowerInvariant());
            Assert.IsFalse(WallPlacementEvaluator.Manual(User(), 0f, 1.5f, 0.15f, WallLayoutKind.Full, S).valid, "manual spot over the monitor is refused");
        }

        [Test]
        public void AnotherWall_CuttingThroughTheVolume_IsRefused()
        {
            var r = Room();
            // A short return wall sticking out of wall-E at z = 0.6, 0.3 m deep.
            r.AddWall(RoomWall.FromEnds("stub", new Vector2(2f, 0.6f), new Vector2(1.7f, 0.6f), 0f, 2.5f, new Vector2(0f, 0f)));
            Assert.AreEqual(WallProblem.HitsOtherWall, Eval(r, "wall-E", 0.6f, 1.35f).problem);
        }

        [Test]
        public void Ray_HitsTheWallInFront_AndFromRayEvaluatesThere()
        {
            var r = Room();
            int wi; float u, y, d;
            Assert.IsTrue(WallMath.Raycast(r, new Vector3(0, 1.3f, 0), new Vector3(1, 0.05f, 0.3f), 10f, out wi, out u, out y, out d));
            Assert.AreEqual("wall-E", r.GetWall(wi).id);
            WallCandidate c;
            Assert.IsTrue(WallPlacementEvaluator.FromRay(r.room, r, new Vector3(0, 1.3f, 0), new Vector3(1, 0.05f, 0.3f), WallLayoutKind.Full, User(), S, out c));
            Assert.AreEqual("wall-E", c.wallId);
            Assert.IsFalse(WallMath.Raycast(r, new Vector3(0, 1.3f, 0), Vector3.up, 10f, out wi, out u, out y, out d), "straight up hits no wall");
        }

        // ── persistence ──

        [Test]
        public void SaveRestore_SameRoom_Restores()
        {
            var r = Room();
            WallCandidate c = Eval(r, "wall-E", 0.6f, 1.35f);
            Pose reference; r.room.TryGetReferencePose(out reference);
            string json = WallPlacementCodec.Encode(WallPlacementCodec.Build(c, r.room.RoomId, true, reference, "b1"));
            SavedWallPlacement s; Assert.IsTrue(WallPlacementCodec.TryDecode(json, out s));
            WallCandidate back; string why;
            Assert.IsTrue(WallPlacementCodec.TryRestore(s, r.room, r, User(), S, out back, out why), why);
            Assert.Less((back.position - c.position).magnitude, 1e-4f);
        }

        [Test]
        public void Restore_WallIdChanged_FoundByReferencePose()
        {
            var r = Room();
            WallCandidate c = Eval(r, "wall-E", 0.6f, 1.35f);
            Pose reference; r.room.TryGetReferencePose(out reference);
            SavedWallPlacement s = WallPlacementCodec.Build(c, r.room.RoomId, true, reference, "b1");
            s.wallId = "old-uuid";
            WallCandidate back; string why;
            Assert.IsTrue(WallPlacementCodec.TryRestore(s, r.room, r, User(), S, out back, out why), why);
            Assert.AreEqual("wall-E", back.wallId);
        }

        [Test]
        public void Restore_Refused_DifferentRoom_LostWall_NowBlocked_Garbage()
        {
            var r = Room();
            WallCandidate c = Eval(r, "wall-E", 0.6f, 1.35f);
            Pose reference; r.room.TryGetReferencePose(out reference);
            SavedWallPlacement s = WallPlacementCodec.Build(c, r.room.RoomId, true, reference, "b1");
            WallCandidate back; string why;

            var other = Room(); other.room.roomId = "another-room";
            Assert.IsFalse(WallPlacementCodec.TryRestore(s, other.room, other, User(), S, out back, out why));
            StringAssert.Contains("different room", why);

            var noWall = new SyntheticWallRoom(r.room);
            noWall.AddWall(r.GetWall(r.IndexOf("wall-N")));
            s.wallId = "gone"; s.hasReference = false;
            Assert.IsFalse(WallPlacementCodec.TryRestore(s, r.room, noWall, User(), S, out back, out why));

            s = WallPlacementCodec.Build(c, r.room.RoomId, true, reference, "b1");
            r.room.AddFurniture("STORAGE", new Vector3(1.8f, 1.0f, 0.6f), new Vector3(0.4f, 2.0f, 0.5f));
            Assert.IsFalse(WallPlacementCodec.TryRestore(s, r.room, r, User(), S, out back, out why));
            StringAssert.Contains("no longer fits", why);

            SavedWallPlacement junk;
            Assert.IsFalse(WallPlacementCodec.TryDecode("not json", out junk));
            Assert.IsFalse(WallPlacementCodec.TryDecode("", out junk));
        }

        // ── the mode flow ──

        static WallModeFlow Flow(SyntheticWallRoom r, bool enabled = true)
        {
            return new WallModeFlow(() => r.room, () => r, () => User(), new WallPlacementSettings(), new WallFeatureFlags { wallHabitatEnabled = enabled });
        }

        [Test]
        public void Flow_FlagOff_RefusesEverything()
        {
            var f = Flow(Room(), false);
            Assert.IsFalse(f.RequestEnter(true, false));
            Assert.AreEqual(WallModeState.Off, f.State);
        }

        [Test]
        public void Flow_PlaceHere_IsExplicit_AndRefusedWhileHolding()
        {
            var f = Flow(Room());
            Assert.IsTrue(f.RequestEnter(true, false));
            Assert.AreEqual(WallModeState.Previewing, f.State);
            Assert.IsTrue(f.HasShown && f.Shown.valid);
            Assert.IsFalse(f.PlaceHere(true), "holding something");
            Assert.AreEqual(WallModeState.Previewing, f.State);
            Assert.IsTrue(f.PlaceHere(false));
            Assert.AreEqual(WallModeState.Active, f.State);
            Assert.IsTrue(f.HasPlaced);
        }

        [Test]
        public void Flow_InvalidSpot_CannotBePlaced()
        {
            var f = Flow(Room());
            f.RequestEnter(true, false);
            Assert.IsTrue(f.Aim(new Vector3(0, 1.3f, 0), Vector3.forward));     // straight ahead: over the monitor
            Assert.IsFalse(f.Shown.valid);
            Assert.IsFalse(f.PlaceHere(false));
            Assert.AreEqual(WallModeState.Previewing, f.State);
        }

        [Test]
        public void Flow_Relocate_ThenCancel_KeepsOldSpot()
        {
            var f = Flow(Room());
            f.RequestEnter(true, false); f.PlaceHere(false);
            Vector3 old = f.Placed.position;
            Assert.IsTrue(f.Relocate(false));
            Assert.AreEqual(WallModeState.Relocating, f.State);
            f.TryAnother();
            f.Cancel();
            Assert.AreEqual(WallModeState.Active, f.State);
            Assert.Less((f.Placed.position - old).magnitude, 1e-6f);
        }

        [Test]
        public void Flow_NoWalls_OffersManualAmber_WithWarning()
        {
            var r = Room();
            r.forcedAvailability = RoomAvailability.NoRoomSetup;
            var f = Flow(r);
            Assert.IsTrue(f.RequestEnter(true, false));
            Assert.IsTrue(f.ManualSpot);
            Assert.IsTrue(f.Shown.needsReview);
            StringAssert.Contains("nothing", f.Status.ToLowerInvariant());
        }

        [Test]
        public void Flow_RoomChange_Suspends_NeverMoves()
        {
            var r = Room();
            var f = Flow(r);
            f.RequestEnter(true, false); f.PlaceHere(false);
            Vector3 at = f.Placed.position;
            r.room.AddFurniture("STORAGE", f.Placed.VolumeCenter, new Vector3(0.3f, 0.3f, 0.3f));
            f.OnRoomChanged();
            Assert.IsTrue(f.Suspended);
            Assert.AreEqual(WallModeState.Active, f.State);
            Assert.Less((f.Placed.position - at).magnitude, 1e-6f);
        }

        [Test]
        public void Flow_ToggleLayout_ProducesCompact()
        {
            var f = Flow(Room());
            f.RequestEnter(true, false);
            f.ToggleLayout();
            Assert.AreEqual(WallLayoutKind.Compact, f.Layout);
            Assert.IsTrue(f.HasShown);
            Assert.AreEqual(WallLayoutKind.Compact, f.Shown.layout);
        }
    }
}
#endif
