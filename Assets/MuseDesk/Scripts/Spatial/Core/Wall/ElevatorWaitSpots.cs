using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>
    /// Builds the validated wait spots for <see cref="ElevatorDispatcher"/>: the floor's queue node first, then extra
    /// corridor spots further from the shaft. A spot is kept only when it is inside the habitat, its standing capsule
    /// keeps the layout clearance from every box, and it is outside the doorway (door half width + radius + clearance
    /// from the landing), so a waiting rider never blocks the door. Spots are spaced two capsules apart.
    /// </summary>
    public static class ElevatorWaitSpots
    {
        public static List<WaitSpot> ForLayout(HabitatLayout L, int extraPerFloor = 2)
        {
            var list = new List<WaitSpot>();
            float r = L.agentRadius, c = L.clearance;
            float spacing = 2f * r + 2f * c;
            for (int f = 0; f < L.floors.Count; f++)
            {
                HabitatNavNode q = L.nav.Node(HabitatLayout.QueueNode(f));
                HabitatNavNode land = L.nav.Node(HabitatLayout.LandingNode(f));
                if (q == null || land == null) continue;
                var first = new WaitSpot { floor = f, position = q.position, nodeId = q.id };
                if (IsValid(L, first, land.position)) list.Add(first);
                for (int k = 1; k <= extraPerFloor; k++)
                {
                    // Away from the shaft (the shaft is at +X), along the corridor line.
                    var s = new WaitSpot { floor = f, position = q.position + new Vector3(-spacing * k, 0f, 0f), nodeId = "" };
                    if (!IsValid(L, s, land.position)) break;
                    list.Add(s);
                }
            }
            return list;
        }

        public static bool IsValid(HabitatLayout L, WaitSpot s, Vector3 landing)
        {
            if (s.position.x < -L.size.x * 0.5f || s.position.x > L.size.x * 0.5f) return false;
            float doorway = L.doorWidth * 0.5f + L.agentRadius + L.clearance;
            if (Mathf.Abs(s.position.x - landing.x) < doorway) return false;
            string hit;
            float gap = HabitatLayoutValidator.CapsuleGap(L, s.position, s.position, s.floor, out hit);
            if (gap < L.clearance - 1e-4f) return false;
            return true;
        }
    }
}
