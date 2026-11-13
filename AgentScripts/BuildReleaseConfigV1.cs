using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Config;

/// <summary>
/// One explicit release configuration. Creates Assets/MuseDesk/Resources/ReleaseConfig.asset when missing
/// (defaults: everything on, buildLabel "rc6-dev") and adds ONE object, "Spatial/ReleaseConfig", with the
/// ReleaseConfigApplier (execution order -10000) to the open scene. Idempotent; saves the scene.
///   scripts\run_guarded.ps1 -File AgentScripts\BuildReleaseConfigV1.cs -Entry BuildReleaseConfigV1.Apply
/// Flip a release (each is a parameterless entry; edits the asset only and saves it):
///   -Entry BuildReleaseConfigV1.Preset_All          Desk + Floor + Ceiling, every extra on
///   -Entry BuildReleaseConfigV1.Preset_DeskFloor    Floor on; Ceiling, pull, particles, cone off
///   -Entry BuildReleaseConfigV1.Preset_DeskOnly     Floor and Ceiling off (extras left as they are)
///   -Entry BuildReleaseConfigV1.Show                prints the current values
/// or in code: BuildReleaseConfigV1.SetFlags("floor=1 ceiling=0 voice=0 grids=1 label=rc6-floor").
/// Keys: floor ceiling pull particles cone grids voice label. Revert() deactivates the applier object
/// (nothing destroyed; the asset stays); Apply() switches it back on.
/// </summary>
public static class BuildReleaseConfigV1
{
    const string Dir = "Assets/MuseDesk/Resources";
    const string AssetPath = Dir + "/ReleaseConfig.asset";

    static MuseDeskReleaseConfig LoadOrCreate(out bool created)
    {
        created = false;
        var c = AssetDatabase.LoadAssetAtPath<MuseDeskReleaseConfig>(AssetPath);
        if (c != null) return c;
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/MuseDesk", "Resources");
        c = ScriptableObject.CreateInstance<MuseDeskReleaseConfig>();
        AssetDatabase.CreateAsset(c, AssetPath);
        AssetDatabase.SaveAssets();
        created = true;
        return c;
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildReleaseConfigV1: stop Play Mode first.";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildReleaseConfigV1: no open scene.";
        var cfg = LoadOrCreate(out bool created);

        Transform spatial = scene.GetRootGameObjects().Select(g => g.transform).FirstOrDefault(t => t.name == "Spatial");
        if (spatial == null) return "BuildReleaseConfigV1: scene root object \"Spatial\" not found; asset ready, scene unchanged.";
        Transform holder = spatial.Find("ReleaseConfig");
        if (holder == null)
        {
            var go = new GameObject("ReleaseConfig");
            Undo.RegisterCreatedObjectUndo(go, "ReleaseConfig");
            go.transform.SetParent(spatial, false);
            holder = go.transform;
        }
        holder.gameObject.SetActive(true);
        var applier = holder.GetComponent<ReleaseConfigApplier>();
        if (applier == null) applier = Undo.AddComponent<ReleaseConfigApplier>(holder.gameObject);
        applier.config = cfg;
        EditorUtility.SetDirty(applier);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        return "BuildReleaseConfigV1: asset " + (created ? "created" : "found") + " (" + cfg.Summary() + "); applier on Spatial/ReleaseConfig; scene saved=" + saved;
    }

    public static string Revert()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var t = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ReleaseConfigApplier>(true)).FirstOrDefault();
        if (t == null) return "BuildReleaseConfigV1.Revert: nothing to revert.";
        t.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        return "BuildReleaseConfigV1.Revert: applier deactivated; scene saved=" + EditorSceneManager.SaveScene(scene);
    }

    public static string Show() { return LoadOrCreate(out _).Summary(); }

    public static string Preset_All() { return SetFlags("floor=1 ceiling=1 pull=1 particles=1 cone=1 grids=1"); }
    public static string Preset_DeskFloor() { return SetFlags("floor=1 ceiling=0 pull=0 particles=0 cone=0"); }
    public static string Preset_DeskOnly() { return SetFlags("floor=0 ceiling=0"); }

    /// <summary>"key=value" tokens separated by spaces; value 1/0/true/false (label: text without spaces).</summary>
    public static string SetFlags(string spec)
    {
        var c = LoadOrCreate(out _);
        foreach (var tok in (spec ?? "").Split(new[] { ' ', ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = tok.IndexOf('=');
            if (eq <= 0) return "SetFlags: bad token '" + tok + "' (use key=value)";
            string k = tok.Substring(0, eq).ToLowerInvariant(), v = tok.Substring(eq + 1);
            bool b = v == "1" || v.ToLowerInvariant() == "true" || v.ToLowerInvariant() == "on";
            switch (k)
            {
                case "floor": c.floorMode = b; break;
                case "ceiling": c.ceilingMode = b; break;
                case "pull": c.ceilingPullGesture = b; break;
                case "particles": c.ceilingParticles = b; break;
                case "cone": c.ceilingPortalCone = b; break;
                case "grids": c.placementScanGrids = b; break;
                case "voice": c.voice = b; break;
                case "label": c.buildLabel = v; break;
                default: return "SetFlags: unknown key '" + k + "'";
            }
        }
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssets();
        return c.Summary();
    }
}
