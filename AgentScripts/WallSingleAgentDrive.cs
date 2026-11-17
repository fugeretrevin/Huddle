using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;
using MuseDesk.Wall;

/// <summary>
/// Play Mode driver: exactly ONE visible agent per role in Wall mode (offline). Evidence level: PLAY, never on-head.
///   Setup()  -> turns the Wall flags on (driver only, synthetic room) and presses Wall.
///   Place()  -> presses Place here (call after a second of frames).
///   Count()  -> counts ENABLED, active SkinnedMeshRenderers per role (Researcher / Developer / Tester / Manager), grouped by
///               character root, lists every one with its lossyScale and height, and flags duplicates.
///   Desk()   -> back to the desk.
/// "PASS" means exactly one visible character root per role under the habitat and none left on the desk office.
/// </summary>
public static class WallSingleAgentDrive
{
    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static WallHabitatController C => One<WallHabitatController>();

    public static string Setup()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C;
        if (c == null) return "no WallHabitatController";
        c.SetFlags(new WallFeatureFlags { wallHabitatEnabled = true, wallManagerEnabled = true, agentSelectEnabled = true, agentBtwEnabled = true, largeAgentPortalEnabled = true, wallElevatorVfxEnabled = false });
        var d = One<SpatialWorkspaceDirector>();
        if (d != null) d.UseSyntheticTestRoom();
        c.PressWall();
        return "wall requested; call Place in a second";
    }

    public static string Place() { if (!Application.isPlaying) return "not playing"; C.PressPlaceHere(); return "place pressed; call Count after the habitat shows"; }
    public static string Desk() { if (!Application.isPlaying) return "not playing"; C.PressDesk(); return "desk pressed"; }

    sealed class Entry { public Transform root; public bool habitat; public int smrs; public float height; public Vector3 lossy; public string path; }

    public static string Count()
    {
        if (!Application.isPlaying) return "not playing";
        var habitatRoot = C != null ? C.transform.Find("WallHabitatRoot") : null;
        var smrs = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include);
        var byRole = new Dictionary<string, Dictionary<Transform, Entry>>();
        int totalVisible = 0;
        foreach (var s in smrs)
        {
            if (s == null || !s.enabled || !s.gameObject.activeInHierarchy || s.sharedMesh == null) continue;
            // Characters only: the agent meshes are Agent_<Role>_Body. Animated props (task printer, elevator) are skinned too.
            if (!s.sharedMesh.name.StartsWith("Agent_")) continue;
            Transform t = s.transform;
            string role = null; Transform charRoot = null;
            for (Transform p = t; p != null; p = p.parent)
            {
                string r = HabitatArtRules.RoleWordOf(p.name);
                if (r == null) { if (charRoot != null) break; continue; }
                if (role == null) role = r;
                if (r == role) charRoot = p;
            }
            if (role == null) continue;
            totalVisible++;
            if (!byRole.TryGetValue(role, out var map)) byRole[role] = map = new Dictionary<Transform, Entry>();
            if (!map.TryGetValue(charRoot, out var e))
            {
                map[charRoot] = e = new Entry
                {
                    root = charRoot, habitat = habitatRoot != null && charRoot.IsChildOf(habitatRoot), lossy = charRoot.lossyScale, path = Path(charRoot), height = 0f
                };
            }
            e.smrs++;
            e.height = Mathf.Max(e.height, s.bounds.size.y);
        }

        var sb = new StringBuilder();
        int dup = 0;
        foreach (string role in new[] { "Researcher", "Developer", "Tester", "Manager" })
        {
            Dictionary<Transform, Entry> map;
            byRole.TryGetValue(role, out map);
            int n = map == null ? 0 : map.Count;
            sb.AppendLine(role + ": " + n + " visible character root(s)" + (n > 1 ? "  <-- DUPLICATE" : n == 0 ? "  (none visible)" : ""));
            if (n > 1) dup++;
            if (map != null)
                foreach (var e in map.Values)
                    sb.AppendLine("    " + (e.habitat ? "[habitat] " : "[desk/other] ") + e.path + "  smr=" + e.smrs + "  lossyScale=" + e.lossy.ToString("F3") + "  worldHeight=" + e.height.ToString("F3") + " m");
        }
        bool habitatOn = habitatRoot != null && habitatRoot.gameObject.activeInHierarchy;
        int deskLeft = byRole.Values.Sum(m => m.Values.Count(e => !e.habitat));
        sb.Insert(0, "habitat active=" + habitatOn + " | bakedCharacterRenderersHidden=" + HabitatArtManifest.LastHiddenCharacterRenderers
                     + " | visible role smrs=" + totalVisible + " | desk/other roots still visible=" + deskLeft + " | roles with duplicates=" + dup + "\n");
        sb.Insert(0, habitatOn && dup == 0 && deskLeft == 0 ? "PASS\n" : habitatOn ? "FAIL\n" : "NOT IN WALL (place the habitat first)\n");
        return sb.ToString();
    }

    static string Path(Transform t) { string p = t.name; while (t.parent != null) { t = t.parent; p = t.name + "/" + p; } return p; }
}
