// =============================================================================
// MuseDesk · EditMode tests · rc21 "present facing" (owner, worn rc20: "they walk to the elevator and then stand there and
// then just turn around, it's not clear what they're doing").
// While a walker dwells at its destination it now turns IN PLACE to whom the trip is for: the user at the dispatch spot
// (yaw 0, out of the wall), the colleague's desk at a corridor door, the Manager's desk at the hand-off spot. These tests
// record every pose the walker writes during the dwell: the position never changes (rotation only), the yaw ends on the
// target, and the walker still walks home afterwards. Pure logic; not proven here: the animation, the Quest.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class Rc21PresentFacingTests
    {
        const float Dt = 1f / 72f;

        static float[] FloorYs(HabitatLayout L) { var y = new float[L.floors.Count]; for (int i = 0; i < y.Length; i++) y[i] = L.floors[i].floorY; return y; }

        struct Pose { public Vector3 p; public float yaw; public WalkerState s; }

        /// <summary>Walk to <paramref name="node"/>, dwell, come home. Returns every pose written during the dwell, the yaw on
        /// arrival, and whether the walker got home.</summary>
        static List<Pose> RunTrip(HabitatLayout L, HabitatRole role, string node, float dwell, out float arrivalYaw, out bool home)
        {
            var res = new HabitatReservations(L);
            var elev = new HabitatElevatorMachine(FloorYs(L));
            var w = new HabitatWalker(role.ToString().ToLowerInvariant(), role, L, res, elev);
            bool backHome = false;
            w.ReturnedHome += () => backHome = true;
            Assert.IsTrue(w.GoTo(node, dwell, true), "a route to " + node);
            var dwellPoses = new List<Pose>();
            arrivalYaw = float.NaN;
            for (int i = 0; i < 72 * 120 && !backHome; i++)
            {
                elev.Tick(Dt); w.Tick(Dt);
                if (w.State == WalkerState.Dwelling)
                {
                    if (dwellPoses.Count == 0) arrivalYaw = w.Yaw;
                    dwellPoses.Add(new Pose { p = w.Position, yaw = w.Yaw, s = w.State });
                }
            }
            home = backHome && w.AtHome;
            return dwellPoses;
        }

        static void AssertStillDuringDwell(List<Pose> poses)
        {
            Assert.Greater(poses.Count, 10, "the walker dwelled");
            for (int i = 1; i < poses.Count; i++)
                Assert.AreEqual(0f, (poses[i].p - poses[0].p).magnitude, 1e-6f, "no translation while presenting (tick " + i + ")");
        }

        [Test]
        public void Question_AtTheDispatchSpot_TurnsInPlaceToFaceTheUser_ThenWalksHome([Values(HabitatRole.Researcher, HabitatRole.Developer, HabitatRole.Tester)] HabitatRole role)
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(WallLayoutKind.Full);
            float arrival; bool home;
            List<Pose> poses = RunTrip(L, role, HabitatLayout.DispatchNode, 2f, out arrival, out home);
            AssertStillDuringDwell(poses);
            Assert.AreEqual(L.nav.Node(HabitatLayout.DispatchNode).position.x, poses[0].p.x, 1e-4f, "stands on the dispatch node");
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(arrival, 0f)), 45f, "arrives facing along the corridor (the old pose)");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(poses[poses.Count - 1].yaw, 0f)), 3f, "ends the dwell facing the user (+Z)");
            Assert.IsTrue(home, "walks home after presenting");
        }

        [Test]
        public void HandoffAtAColleaguesDoor_FacesThatColleaguesDesk_NotTheLift()
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(WallLayoutKind.Full);
            float arrival; bool home;
            List<Pose> poses = RunTrip(L, HabitatRole.Developer, HabitatLayout.DoorNode(HabitatRole.Tester), 2f, out arrival, out home);
            AssertStillDuringDwell(poses);
            Vector3 d = L.nav.Node(HabitatLayout.WorkNode(HabitatRole.Tester)).position - poses[0].p; d.y = 0f;
            float want = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(poses[poses.Count - 1].yaw, want)), 3f, "faces the Tester's desk");
            // The lift is at +X (yaw 90): the old pose looked at it; the new one does not.
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(poses[poses.Count - 1].yaw, 90f)), 60f, "not facing the lift");
            Assert.IsTrue(home);
        }

        [Test]
        public void AtTheManagersHandoffSpot_FacesTheManagersDesk()
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(WallLayoutKind.Full);
            float arrival; bool home;
            List<Pose> poses = RunTrip(L, HabitatRole.Researcher, HabitatLayout.HandoffAtManager, 2f, out arrival, out home);
            AssertStillDuringDwell(poses);
            Vector3 d = L.nav.Node(HabitatLayout.WorkNode(HabitatRole.Manager)).position - poses[0].p; d.y = 0f;
            float want = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(poses[poses.Count - 1].yaw, want)), 3f, "faces the Manager's desk");
            Assert.IsTrue(home, "rides back home");
        }

        [Test]
        public void PresentYaw_IsOnlyForPeople_NotForTheLiftOrTheCabin()
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(WallLayoutKind.Full);
            var w = new HabitatWalker("developer", HabitatRole.Developer, L, new HabitatReservations(L), new HabitatElevatorMachine(FloorYs(L)));
            float yaw;
            Assert.IsFalse(w.PresentYaw(out yaw), "at its own desk there is no one to present to");
        }
    }
}
#endif
