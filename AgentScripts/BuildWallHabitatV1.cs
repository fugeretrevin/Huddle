using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Wall;

/// <summary>
/// Phase 3 Wall Habitat scene wiring (idempotent; run with scripts\run_guarded.ps1). Adds ONE object,
/// Spatial/WallHabitat, with WallHabitatController + WallBackendGateway + WallEventBridge, wires it to the
/// existing director, room service, desk office, near-user rig, a ModeConsole key (cloned at run time) and the
/// three desk agents (their MESH is cloned at run time; they are never moved). Creates the proxy material and an
/// empty art manifest. Every Wall flag stays OFF (ReleaseConfig). Revert() removes the object and the director link.
/// </summary>
public static class BuildWallHabitatV1
{
    const string MatPath = "Assets/MuseDesk/Art/Materials/Wall/WallHabitat_Proxy.mat";
    const string ManifestPath = "Assets/MuseDesk/Wall/HabitatArtManifest.asset";

    public static string Apply()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        Transform spatial = all.FirstOrDefault(t => t.parent == null && t.name == "Spatial");
        var director = Object.FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
        var room = Object.FindAnyObjectByType<RoomPlacementService>(FindObjectsInactive.Include);
        var console = Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);
        Transform office = all.FirstOrDefault(t => t.parent == null && t.name == "WorkspaceRoot");
        Transform rig = all.FirstOrDefault(t => t.name == "NearUserRig" && t.parent != null && t.parent.name == "Spatial");
        Transform paper = all.FirstOrDefault(t => t.parent == null && t.name == "DecisionDocument");
        if (spatial == null || director == null || room == null || console == null || office == null || rig == null)
            return "FAILED missing: spatial=" + (spatial != null) + " director=" + (director != null) + " room=" + (room != null) + " console=" + (console != null) + " office=" + (office != null) + " rig=" + (rig != null);

        // Material (URP Simple Lit, opaque, tinted per renderer at run time) and an empty manifest.
        System.IO.Directory.CreateDirectory("Assets/MuseDesk/Art/Materials/Wall");
        System.IO.Directory.CreateDirectory("Assets/MuseDesk/Wall");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(sh) { name = "WallHabitat_Proxy", enableInstancing = true };
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(mat, MatPath);
            sb.AppendLine("created " + MatPath + " shader=" + sh.name);
        }
        var manifest = AssetDatabase.LoadAssetAtPath<HabitatArtManifest>(ManifestPath);
        if (manifest == null)
        {
            manifest = ScriptableObject.CreateInstance<HabitatArtManifest>();
            AssetDatabase.CreateAsset(manifest, ManifestPath);
            sb.AppendLine("created " + ManifestPath + " (empty: proxy art only)");
        }

        Transform wall = spatial.Find("WallHabitat");
        if (wall == null)
        {
            wall = new GameObject("WallHabitat").transform;
            wall.SetParent(spatial, false);
            sb.AppendLine("created Spatial/WallHabitat");
        }
        var gw = wall.GetComponent<WallBackendGateway>() ?? wall.gameObject.AddComponent<WallBackendGateway>();
        var bridge = wall.GetComponent<WallEventBridge>() ?? wall.gameObject.AddComponent<WallEventBridge>();
        var c = wall.GetComponent<WallHabitatController>() ?? wall.gameObject.AddComponent<WallHabitatController>();

        c.director = director;
        c.roomService = room;
        c.officeRoot = office;
        c.nearUserRig = rig;
        c.neverHide = paper != null ? new[] { paper } : new Transform[0];
        c.keyTemplate = console.statusKey != null ? console.statusKey.root : null;
        c.font = console.statusLabel != null ? console.statusLabel.font : null;
        c.baseMaterial = mat;
        c.artManifest = manifest;
        c.backendBehaviour = gw;
        c.eventsBehaviour = bridge;
        string[] names = { "Agent_Researcher", "Agent_Developer", "Agent_Tester" };
        c.agentSources = names.Select(n => { var t = office.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n); return t != null ? t.gameObject : null; }).ToArray();
        c.flags = new MuseDesk.Spatial.Wall.WallFeatureFlags();     // all off; ReleaseConfig decides at run time
        EditorUtility.SetDirty(c);

        director.wallHost = c;
        EditorUtility.SetDirty(director);

        sb.AppendLine("wired: keyTemplate=" + (c.keyTemplate != null ? c.keyTemplate.name : "NULL") + " font=" + (c.font != null ? c.font.name : "NULL")
            + " agents=" + string.Join(",", c.agentSources.Select(a => a != null ? a.name : "NULL")) + " paper=" + (paper != null));
        var cfg = MuseDesk.Config.MuseDeskReleaseConfig.Load();
        sb.AppendLine("ReleaseConfig: " + (cfg != null ? cfg.Summary() : "missing"));

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        sb.AppendLine("saved " + scene.path);
        return sb.ToString();
    }

    public static string Revert()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var director = Object.FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
        if (director != null) { director.wallHost = null; EditorUtility.SetDirty(director); }
        var c = Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);
        if (c != null) Object.DestroyImmediate(c.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "reverted (WallHabitat removed, director.wallHost cleared)";
    }
}
