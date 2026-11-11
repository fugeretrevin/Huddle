using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using MuseDesk.Config;
using MuseDesk.Workspace;
using MuseDesk.DebugTools;
using MuseDesk.Bootstrap;

/// <summary>
/// Headless builder for the MuseDesk scene scaffold (TASK-01). Reliable, Meta-rig-
/// independent parts only: scene, WorkspaceRoot + station/delivery anchors, world-space
/// diagnostics text, RuntimeSettings asset, AppBootstrap wiring, build-settings entry.
/// The Meta XR rig/passthrough/hands/MRUK blocks are added by MetaBlocks.cs.
/// </summary>
public static class BuildScene
{
    const string ScenePath = "Assets/MuseDesk/Scenes/MainMRScene.unity";
    const string SettingsDir = "Assets/MuseDesk/Resources";
    const string SettingsPath = "Assets/MuseDesk/Resources/RuntimeSettings.asset";

    public static string Scaffold()
    {
        EnsureFolders();
        var settings = EnsureSettings();

        // Fresh empty scene (the Meta blocks will add the single rig next).
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── AppServices root ──
        var services = new GameObject("AppServices");

        // ── WorkspaceRoot tray (starts inactive until placed) ──
        var workspaceRoot = new GameObject("WorkspaceRoot");
        workspaceRoot.transform.position = new Vector3(0f, 1.0f, 0.5f);
        MakeAnchor(workspaceRoot.transform, "ResearchStation", "researcher", new Vector3(-0.30f, 0f, 0f));
        MakeAnchor(workspaceRoot.transform, "DevStation", "developer", new Vector3(0.0f, 0f, 0.05f));
        MakeAnchor(workspaceRoot.transform, "QAStation", "tester", new Vector3(0.30f, 0f, 0f));
        MakeAnchor(workspaceRoot.transform, "DeliveryZone", "delivery", new Vector3(0.0f, 0f, -0.18f));

        // A small visible marker cube so placement is verifiable on-device even before art exists.
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "PlacementMarker";
        marker.transform.SetParent(workspaceRoot.transform, false);
        marker.transform.localScale = Vector3.one * 0.05f;
        marker.transform.localPosition = new Vector3(0f, 0.025f, 0f);

        // ── Placement guide ghost (manual fallback) ──
        var guide = GameObject.CreatePrimitive(PrimitiveType.Quad);
        guide.name = "PlacementGuide";
        guide.transform.localScale = new Vector3(0.8f, 0.3f, 1f);
        guide.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        guide.SetActive(false);

        // ── World-space diagnostics text (TextMeshPro 3D) ──
        var diagGo = new GameObject("DiagnosticsText");
        diagGo.transform.SetParent(services.transform, false);
        diagGo.transform.position = new Vector3(0f, 1.3f, 0.5f);
        diagGo.transform.localScale = Vector3.one * 0.02f;
        var tmp = diagGo.AddComponent<TextMeshPro>();
        tmp.text = "MuseDesk diagnostics";
        tmp.fontSize = 8f;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.rectTransform.sizeDelta = new Vector2(30f, 18f);

        // ── Components + wiring ──
        var placement = services.AddComponent<WorkspacePlacement>();
        placement.settings = settings;
        placement.workspaceRoot = workspaceRoot.transform;
        placement.placementGuide = guide;

        var diag = services.AddComponent<DiagnosticsOverlay>();
        diag.text = tmp;
        diag.placement = placement;
        diag.modeLabel = "DEMO · Simulated work";

        var bootstrap = services.AddComponent<AppBootstrap>();
        bootstrap.settings = settings;
        bootstrap.placement = placement;
        bootstrap.diagnostics = diag;

        // Keep WorkspaceRoot active (placement toggles visibility logic itself).
        workspaceRoot.SetActive(true);

        // ── Save + register in build settings ──
        bool ok = EditorSceneManager.SaveScene(scene, ScenePath);
        RegisterInBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int anchorCount = Object.FindObjectsByType<WorkstationAnchor>(
            FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        return $"scaffold saved={ok} path={ScenePath} anchors={anchorCount} settings={SettingsPath}";
    }

    static void MakeAnchor(Transform parent, string name, string role, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var a = go.AddComponent<WorkstationAnchor>();
        a.role = role;
    }

    static void EnsureFolders()
    {
        foreach (var dir in new[] { "Assets/MuseDesk", "Assets/MuseDesk/Scenes",
                                     "Assets/MuseDesk/Resources", "Assets/MuseDesk/Prefabs" })
        {
            if (!AssetDatabase.IsValidFolder(dir))
            {
                var parent = Path.GetDirectoryName(dir).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
            }
        }
    }

    static RuntimeSettings EnsureSettings()
    {
        var existing = AssetDatabase.LoadAssetAtPath<RuntimeSettings>(SettingsPath);
        if (existing != null) return existing;
        var s = ScriptableObject.CreateInstance<RuntimeSettings>();
        AssetDatabase.CreateAsset(s, SettingsPath);
        AssetDatabase.SaveAssets();
        return s;
    }

    static void RegisterInBuildSettings(string path)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == path)) return;
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
