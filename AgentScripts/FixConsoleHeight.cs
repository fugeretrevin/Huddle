using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Raises ModeConsole so its lowest visible mesh rests 1 mm above the desk plane. Idempotent.</summary>
public static class FixConsoleHeight
{
    public static string Apply()
    {
        var all = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var root = all.First(t => t.name == "WorkspaceRoot");
        var console = all.First(t => t.name == "ModeConsole");
        var sb = new StringBuilder();
        var list = console.GetComponentsInChildren<MeshRenderer>(false)
            .Where(r => r.enabled && r.transform.lossyScale.magnitude > 0.005f && r.GetComponent<TMPro.TMP_Text>() == null)
            .Select(r => new { r, y = MinY(r, root) }).OrderBy(x => x.y).ToArray();
        foreach (var x in list.Take(4)) sb.Append($"{x.r.name}@{x.y * 1000f:F1}mm ");
        float low = list[0].y;
        float lift = 0.001f - low;
        if (Mathf.Abs(lift) > 0.0005f)
        {
            console.position += root.up * lift;
            EditorUtility.SetDirty(console);
            EditorSceneManager.MarkSceneDirty(console.gameObject.scene);
            EditorSceneManager.SaveScene(console.gameObject.scene);
        }
        return $"lowest: {sb} lift={lift * 1000f:F1}mm";
    }

    static float MinY(MeshRenderer r, Transform root)
    {
        var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return 99f;
        var b = mf.sharedMesh.bounds; float min = 99f;
        for (int i = 0; i < 8; i++)
        {
            var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            min = Mathf.Min(min, root.InverseTransformPoint(r.transform.TransformPoint(c)).y);
        }
        return min;
    }
}
