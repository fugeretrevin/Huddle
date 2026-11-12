using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Editor-only visual checks. Temporarily activates runtime-hidden objects and frames the
/// SceneView on them from the user's side (-Z looking +Z). NEVER saves the scene; call Restore()
/// (or let the next builder re-open the scene from disk) afterwards.</summary>
public static class Shot
{
    static Transform Find(string name) => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == name);

    static string Frame(Vector3 pivot, float size)
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return "no scene view";
        sv.orthographic = false;
        sv.pivot = pivot;
        sv.rotation = Quaternion.identity;   // looking +Z, i.e. at the content's -Z fronts
        sv.size = size;
        sv.Repaint();
        return $"framed {pivot} size {size}";
    }

    public static string ShowPaper()
    {
        var doc = Find("DecisionDocument");
        if (doc == null) return "DecisionDocument not found";
        doc.gameObject.SetActive(true);
        foreach (Transform c in doc) c.gameObject.SetActive(true);
        var kids = string.Join(", ", doc.Cast<Transform>().Select(c => c.name));
        return Frame(doc.position, 0.28f) + " | children: " + kids;
    }

    public static string ShowBoard()
    {
        var board = Find("TaskBoard");
        if (board == null) return "TaskBoard not found";
        return Frame(board.position, 0.75f * board.lossyScale.x);
    }

    public static string ShowWorkspace()
    {
        var ws = Find("WorkspaceRoot");
        if (ws == null) return "WorkspaceRoot not found";
        return Frame(ws.position + new Vector3(0f, 0.35f, 0.2f), 1.1f);
    }

    public static string ShowDesk()
    {
        var ws = Find("WorkspaceRoot");
        if (ws == null) return "WorkspaceRoot not found";
        var sv = SceneView.lastActiveSceneView;
        sv.pivot = ws.position + new Vector3(0f, 0.05f, 0.05f);
        sv.rotation = Quaternion.Euler(38f, 0f, 0f);   // looking down from the user's side
        sv.size = 0.55f;
        sv.Repaint();
        return "framed desk from above/user side";
    }

    public static string Restore()
    {
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);   // reload from disk, discard temp state
        return "reloaded " + scene.path;
    }
}
