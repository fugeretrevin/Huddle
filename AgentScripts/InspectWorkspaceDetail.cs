using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Workspace;
using MuseDesk.Diagrams;
using MuseDesk.Art;

public static class InspectWorkspaceDetail
{
    public static string Run()
    {
        EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();
        var ws = GameObject.Find("WorkspaceRoot");
        if (ws == null) return "NO WorkspaceRoot";

        Dump(ws.transform, 0, 3, sb);

        var app = GameObject.Find("AppServices");
        if (app != null)
        {
            sb.AppendLine("\n== AppServices Components ==");
            foreach (var c in app.GetComponents<Component>())
                if (c != null) sb.AppendLine("  " + c.GetType().FullName);
        }
        return sb.ToString();
    }

    static void Dump(Transform t, int depth, int maxDepth, StringBuilder sb)
    {
        if (depth > maxDepth) return;
        var comps = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
        sb.AppendLine($"{new string(' ', depth * 2)}{t.name} active={t.gameObject.activeSelf} pos={F(t.localPosition)} rot={F(t.localEulerAngles)} scale={F(t.localScale)} [{comps}]");
        foreach (Transform c in t) Dump(c, depth + 1, maxDepth, sb);
    }

    static string F(Vector3 v) => $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";
}

