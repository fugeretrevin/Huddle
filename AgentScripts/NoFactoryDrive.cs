using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Play Mode driver for RemoveFactoryV1. Begin() starts the offline desk demo at 4x; wait ~60 s, then
/// Check() reports any VISIBLE factory/belt/conveyor renderer and the receipt line. The SoftwareFactory root stays
/// active on purpose (its FactoryEventController feeds test evidence to CompletionSequence), so this checks
/// renderers, not objects. Each script run compiles a fresh assembly, so no state is kept between calls:
/// read console errors with the editor console command, not from here.</summary>
public static class NoFactoryDrive
{
    static bool Bad(string n) =>
        n.IndexOf("Factory", System.StringComparison.OrdinalIgnoreCase) >= 0
        || n.IndexOf("Belt", System.StringComparison.OrdinalIgnoreCase) >= 0
        || n.IndexOf("Conveyor", System.StringComparison.OrdinalIgnoreCase) >= 0;

    static bool UnderBad(Transform t)
    {
        for (var p = t; p != null; p = p.parent) if (Bad(p.name)) return true;
        return false;
    }

    public static string Begin()
    {
        if (!Application.isPlaying) return "not playing";
        Time.timeScale = 4f;
        // Same start as PlayDrive.Start (TaskDock.StartReadyTask), inlined: a driver file is compiled on its own.
        var dock = GameObject.Find("TaskDock");
        var comp = dock != null ? dock.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "TaskDock") : null;
        var mi = comp != null ? comp.GetType().GetMethod("StartReadyTask", System.Type.EmptyTypes) : null;
        if (mi != null) mi.Invoke(comp, null);
        return "begin: " + (mi != null ? "started the offline demo task" : "TaskDock.StartReadyTask not found") + " (timeScale 4; wait for completion, then Check)";
    }

    static string ReceiptLine()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.gameObject.scene.IsValid() && t.name.IndexOf("Receipt", System.StringComparison.OrdinalIgnoreCase) >= 0 && t.parent != null && t.parent.name.IndexOf("Receipt", System.StringComparison.OrdinalIgnoreCase) < 0).ToList();
        return all.Count == 0 ? "no receipt object" : string.Join(", ", all.Take(4).Select(t => t.name + (t.gameObject.activeInHierarchy ? " (shown)" : " (hidden)")));
    }

    public static string Check()
    {
        if (!Application.isPlaying) return "not playing";
        var sb = new StringBuilder();
        var visible = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<Renderer>(false))
            .Where(r => r.enabled && r.gameObject.activeInHierarchy && UnderBad(r.transform)).Select(r => r.name).ToList();
        sb.AppendLine("visible factory/belt/conveyor renderers: " + visible.Count + (visible.Count > 0 ? " [" + string.Join(", ", visible.Take(8)) + "]" : ""));
        sb.AppendLine("receipt objects: " + ReceiptLine());
        sb.AppendLine(visible.Count == 0 ? "PASS (no visible factory; check the receipt line shows completion and read the console for errors)" : "FAIL");
        return sb.ToString();
    }

    public static string End()
    {
        Time.timeScale = 1f;
        return "ended";
    }
}
