using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>Play Mode driver: reads the redesigned UI state and presses things the way a poke would
/// (by invoking the same public methods the keys' UnityEvents call).</summary>
public static class PlayDrive
{
    static Transform Find(string n) => Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
        .SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == n);

    static string Texts(string root)
    {
        var t = Find(root); if (t == null) return root + ": MISSING";
        var sb = new StringBuilder($"{root} active={t.gameObject.activeInHierarchy} scale={t.lossyScale.x:F3}: ");
        foreach (var x in t.GetComponentsInChildren<TMP_Text>(false))
            if (!string.IsNullOrEmpty(x.text)) sb.Append($"[{x.name}{(x.transform.lossyScale.x < 0.001f ? "(hidden)" : "")}='{x.text.Replace("\n", " / ")}'] ");
        return sb.ToString();
    }

    public static string State()
    {
        if (!Application.isPlaying) return "not playing";
        var sb = new StringBuilder();
        foreach (var n in new[] { "TaskDock", "ModePlaque", "Nameplate_Researcher", "Nameplate_Developer", "Nameplate_Tester", "CompletionReceipt", "FiledPoke", "DecisionDocument", "CardToolbar", "AssignDock", "ArchiveCard", "ViewerSheetRoot" })
            sb.AppendLine(Texts(n));
        var b = Find("TaskBoard");
        sb.AppendLine($"TaskBoard scale={b.localScale.x:F3} cards={b.GetComponentsInChildren<Transform>(false).Count(t => t.GetComponent("TaskCardView") != null)}");
        var sk = Find("DockStartKey"); if (sk != null) sb.AppendLine($"DockStartKey scale={sk.localScale.x:F4}");
        foreach (var a in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" }) { var t = Find(a); sb.AppendLine($"{a} pos={t.parent.parent.InverseTransformPoint(t.position)}"); }
        return sb.ToString();
    }

    static string Call(string obj, string comp, string method)
    {
        var t = Find(obj); if (t == null) return "missing " + obj;
        var c = t.GetComponent(comp) ?? t.GetComponentInChildren(System.Type.GetType(comp) ?? typeof(MonoBehaviour), true);
        var target = t.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == comp);
        if (target == null) return $"no {comp} on {obj}";
        var mi = target.GetType().GetMethod(method, System.Type.EmptyTypes);
        if (mi == null) return $"no {method} on {comp}";
        mi.Invoke(target, null);
        return $"called {comp}.{method}";
    }

    public static string Start() => Call("TaskDock", "TaskDock", "StartReadyTask");
    public static string ToggleBoard() => Call("TaskDock", "TaskDock", "ToggleBoard");
    public static string Approve() => Call("AppServices", "DocumentDeliveryController", "OnApprove");
    public static string Reject() => Call("AppServices", "DocumentDeliveryController", "OnReject");
    public static string Why() => Call("AppServices", "DocumentDeliveryController", "OnAskWhy");
    public static string Details() => Call("AppServices", "DocumentDeliveryController", "OnToggleDetails");
    public static string RunAgain() => Call("CompletionReceipt", "CompletionReceipt", "RunAgain");
    public static string Filed() => Call("FiledPoke", "ArchiveStackPoke", "Poke");
    public static string TakePaper() => Call("DecisionDocument", "HeldPaperController", "TakeNow");
    public static string View() => Call("CompletionReceipt", "CompletionReceipt", "View");
    public static string ViewerNext() => Call("ArtifactPanel", "ArtifactViewer", "NextPage");
    public static string ViewerClose() => Call("ArtifactPanel", "ArtifactViewer", "Close");
    public static string SelectCard() => Call("TaskBoard", "CardActionConsole", "SelectFirstCard");
    public static string HoldCard() => Call("TaskBoard", "CardActionConsole", "SimulateHoldOverPad");
    public static string CancelHold() => Call("TaskBoard", "CardActionConsole", "CancelSimulatedHold");
    public static string PreviewSuccess() => Call("AppServices", "CompletionSequence", "PreviewSuccess");
    public static string PreviewFailure() => Call("AppServices", "CompletionSequence", "PreviewFailure");
    /// <summary>Puts the taken paper where a user would hold it to read (capture aid only).</summary>
    public static string LiftPaper()
    {
        var d = Find("DecisionDocument"); var ws = Find("WorkspaceRoot");
        Vector3 eye = ws.TransformPoint(new Vector3(0, 0.30f, 0.62f));
        Vector3 pos = ws.TransformPoint(new Vector3(0, 0.34f, 0.22f));
        foreach (var mb in d.GetComponents<MonoBehaviour>()) if (mb != null && mb.GetType().Name == "FaceUser") mb.enabled = false;
        d.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - eye, Vector3.up));
        return "paper lifted to reading pose";
    }
    public static string Fast() { Time.timeScale = 4f; return "timeScale 4"; }
    public static string Normal() { Time.timeScale = 1f; return "timeScale 1"; }
}
