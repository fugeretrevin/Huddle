using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Optional developer view of what the placement code sees: floor and ceiling outlines,
    /// furniture boxes, the ranked floor candidates and the ceiling dock with its descent path.
    /// Drawn with Gizmos (Scene view, or Game view with Gizmos on), so it costs nothing in a build.
    /// Off by default.
    /// </summary>
    public class WorkspacePlacementDebugView : MonoBehaviour
    {
        public SpatialWorkspaceDirector director;
        [Tooltip("Draw the debug view. Off by default.")]
        public bool show;

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!show || director == null) return;
            SpatialDirectorCore core = director.Core;
            if (core == null) return;
            IRoomGeometry room = core.Room;
            if (room != null && room.HasRoom)
            {
                Gizmos.color = new Color(0.30f, 0.52f, 0.86f);
                for (int i = 0; i < room.FloorCount; i++) DrawPlane(room.GetFloor(i));
                Gizmos.color = new Color(0.55f, 0.48f, 0.84f);
                for (int i = 0; i < room.CeilingCount; i++) DrawPlane(room.GetCeiling(i));
                Gizmos.color = new Color(0.85f, 0.33f, 0.29f);
                for (int i = 0; i < room.FurnitureCount; i++) DrawBox(room.GetFurniture(i));
            }

            Vector2 fp = core.footprint;
            for (int i = 0; i < core.CandidateCount; i++)
            {
                FloorCandidate c = core.GetCandidate(i);
                Gizmos.color = Color.Lerp(new Color(0.88f, 0.63f, 0.19f), new Color(0.18f, 0.64f, 0.60f), Mathf.Clamp01(c.score));
                DrawRect(SpatialGeometry.Footprint(c.position, c.rotation, c.scale, fp, core.floorSettings.footprintCenterOffset), c.position.y + 0.01f);
                UnityEditor.Handles.Label(c.position + Vector3.up * 0.1f, (i + 1) + ": " + c.explanation);
            }

            CeilingPlacement ceiling = core.LastCeiling;
            if (ceiling.valid)
            {
                Gizmos.color = new Color(0.18f, 0.64f, 0.60f);
                Gizmos.DrawWireSphere(ceiling.dockPosition, 0.05f);
                Gizmos.DrawWireSphere(ceiling.inspectionPosition, 0.05f);
                Gizmos.DrawLine(ceiling.dockPosition, ceiling.inspectionPosition);
            }
        }

        static void DrawPlane(RoomPlane plane)
        {
            Vector2[] p = plane.polygon;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                Gizmos.DrawLine(new Vector3(p[j].x, plane.height, p[j].y), new Vector3(p[i].x, plane.height, p[i].y));
        }

        static void DrawRect(OrientedRect r, float y)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = r.Corner(i), b = r.Corner((i + 1) % 4);
                Gizmos.DrawLine(new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y));
            }
        }

        static void DrawBox(FurnitureBox box)
        {
            OrientedRect r = box.Footprint;
            DrawRect(r, box.minY);
            DrawRect(r, box.maxY);
            for (int i = 0; i < 4; i++)
            {
                Vector2 c = r.Corner(i);
                Gizmos.DrawLine(new Vector3(c.x, box.minY, c.y), new Vector3(c.x, box.maxY, c.y));
            }
        }
#endif
    }
}
