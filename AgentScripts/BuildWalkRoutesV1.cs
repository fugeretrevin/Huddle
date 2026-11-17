using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;

/// <summary>
/// Researcher no longer walks through its desk on the delivery walk. Adds two exit waypoints beside
/// the end of the ResearchStation desk (root-local (-0.47, 0.00) then (-0.47, 0.181), ahead of the
/// delivery zone at (-0.20, 0.17)) and assigns them to the Researcher's AgentMovement. The walk home
/// reverses them. Also moves the mini plant (Phase6/Decor_MiniPlant) behind the agents so it is not
/// on that lane (root-local z centre 0.08 -> -0.12). Idempotent; Revert() undoes it.
/// Run after the other builders (it only touches the Researcher's AgentMovement and the plant).
/// </summary>
public static class BuildWalkRoutesV1
{
    const string W1 = "WalkExit_1", W2 = "WalkExit_2";
    static readonly Vector2 P1 = new Vector2(-0.47f, 0.0f), P2 = new Vector2(-0.47f, 0.181f);
    const float PlantAppliedZ = -0.12f, PlantOriginalZ = 0.08f;

    static Transform Find(string name) =>
        EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == name);

    static Transform EnsureChild(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static float BoundsCenterRootZ(Transform root, Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return float.NaN;
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        return root.InverseTransformPoint(b.center).z;
    }

    static void ShiftPlantTo(Transform root, Transform plant, float targetZ)
    {
        float cur = BoundsCenterRootZ(root, plant);
        if (float.IsNaN(cur)) return;
        Vector3 d = root.TransformVector(new Vector3(0f, 0f, targetZ - cur));
        Undo.RecordObject(plant, "walk lane plant");
        plant.position += d;
        EditorUtility.SetDirty(plant);
    }

    public static string Apply()
    {
        var root = Find("WorkspaceRoot"); var st = Find("ResearchStation");
        if (root == null || st == null) return "missing WorkspaceRoot/ResearchStation";
        var mv = st.GetComponentsInChildren<AgentMovement>(true).FirstOrDefault();
        if (mv == null) return "Researcher AgentMovement missing";
        var sb = new StringBuilder();

        Transform[] wps = new Transform[2];
        Vector2[] pts = { P1, P2 }; string[] names = { W1, W2 };
        for (int i = 0; i < 2; i++)
        {
            var t = EnsureChild(st, names[i]);
            t.gameObject.SetActive(true);
            t.position = root.TransformPoint(new Vector3(pts[i].x, 0f, pts[i].y));
            wps[i] = t;
        }
        mv.exitWaypoints = wps;
        mv.maxRoutedSpeed = 0.25f;
        EditorUtility.SetDirty(mv);
        sb.AppendLine($"Researcher exitWaypoints: {W1}, {W2}; maxRoutedSpeed {mv.maxRoutedSpeed:F2}");

        var plant = Find("Decor_MiniPlant");
        if (plant != null)
        {
            ShiftPlantTo(root, plant, PlantAppliedZ);
            sb.AppendLine($"Decor_MiniPlant centre z -> {BoundsCenterRootZ(root, plant):F3}");
        }
        else sb.AppendLine("Decor_MiniPlant not found (skipped)");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        return sb.ToString();
    }

    public static string Revert()
    {
        var root = Find("WorkspaceRoot"); var st = Find("ResearchStation");
        if (root == null || st == null) return "missing WorkspaceRoot/ResearchStation";
        var mv = st.GetComponentsInChildren<AgentMovement>(true).FirstOrDefault();
        if (mv != null) { mv.exitWaypoints = new Transform[0]; EditorUtility.SetDirty(mv); }
        foreach (var n in new[] { W1, W2 }) { var t = st.Find(n); if (t != null) t.gameObject.SetActive(false); }
        var plant = Find("Decor_MiniPlant");
        if (plant != null) ShiftPlantTo(root, plant, PlantOriginalZ);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        return "walk routes reverted";
    }
}

