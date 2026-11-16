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
        return "begin: " + PlayDrive.Start() + " (timeScale 4; wait for completion, then Check)";
    }

    public static string Check()
    {
        if (!Application.isPlaying) return "not playing";
        var sb = new StringBuilder();
        var visible = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<Renderer>(false))
            .Where(r => r.enabled && r.gameObject.activeInHierarchy && UnderBad(r.transform)).Select(r => r.name).ToList();
        sb.AppendLine("visible factory/belt/conveyor renderers: " + visible.Count + (visible.Count > 0 ? " [" + string.Join(", ", visible.Take(8)) + "]" : ""));
        sb.AppendLine("receipt/paper state: " + PlayDrive.State().Split('\n').FirstOrDefault(l => l.StartsWith("CompletionReceipt")));
        sb.AppendLine(visible.Count == 0 ? "PASS (no visible factory; check the receipt line shows completion and read the console for errors)" : "FAIL");
        return sb.ToString();
    }

    public static string End()
    {
        Time.timeScale = 1f;
        return "ended";
    }
}
