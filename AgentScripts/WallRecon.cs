using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>Read-only reconnaissance for the Wall Habitat work: scene roots, WorkspaceRoot children
/// (depth 2) with local pose and renderer bounds, and where the key components live.</summary>
public static class WallRecon
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder();
        sb.AppendLine("scene=" + scene.path);
        foreach (var r in scene.GetRootGameObjects())
            sb.AppendLine("ROOT " + r.name + " [" + (r.activeSelf ? "on" : "off") + "] children=" + r.transform.childCount);
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var ws = all.FirstOrDefault(t => t.name == "WorkspaceRoot");
        if (ws != null)
        {
            sb.AppendLine("WorkspaceRoot parent=" + (ws.parent ? ws.parent.name : "-") + " w=" + V(ws.position) + " rotY=" + ws.eulerAngles.y.ToString("F0") + " s=" + ws.lossyScale.x.ToString("F2"));
            foreach (Transform c in ws)
            {
                sb.AppendLine("  " + c.name + " [" + (c.gameObject.activeSelf ? "on" : "off") + "] l=" + V(c.localPosition) + B(c));
                foreach (Transform g in c)
                    sb.AppendLine("    " + g.name + " [" + (g.gameObject.activeSelf ? "on" : "off") + "] l=" + V(g.localPosition) + B(g));
            }
        }
        string[] types = { "SessionController", "Phase2Bootstrap", "BoardPresenter", "SpatialWorkspaceDirector", "ModeConsole",
            "SpatialModeStrip", "NearUserKit", "RemoteDocumentDeliveryController", "AttentionBeacon", "AttentionController",
            "AgentController", "AgentMovement", "AgentAnimatorBridge", "HeldPaperController", "DocumentDeliveryController",
            "HandInteractionProbe", "FloorAimController", "CeilingPullGesture", "ReleaseConfigApplier", "MovablePanel",
            "VoiceCommandCenter", "RoomPlacementService", "MrukRoomGeometry", "FocusModeController", "AgentCharacterBinder" };
        foreach (var tn in types)
        {
            var objs = all.Where(t => t.GetComponents<MonoBehaviour>().Any(m => m != null && m.GetType().Name == tn)).ToArray();
            sb.AppendLine("T " + tn + ": " + string.Join(" | ", objs.Select(Path)));
        }
        return sb.ToString();
    }

    static string Path(Transform t)
    {
        string p = t.name;
        int n = 0;
        while (t.parent != null && n++ < 6) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    static string B(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return "";
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return " size=" + V(b.size) + " rend=" + rs.Length;
    }

    static string V(Vector3 v) => "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")";
}
