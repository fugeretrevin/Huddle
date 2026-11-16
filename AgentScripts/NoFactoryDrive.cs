using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Play Mode driver for RemoveFactoryV1. Begin() starts the offline desk demo task at 4x and counts
/// console errors; Check() reports whether the run completed and whether any factory/belt/conveyor object is
/// still active. Call Begin, wait ~60 s, then Check.</summary>
public static class NoFactoryDrive
{
    static int _errors;
    static readonly List<string> _errorTexts = new List<string>();
    static bool _hooked;

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        { _errors++; if (_errorTexts.Count < 5) _errorTexts.Add(msg); }
    }

    static bool Bad(string n) =>
        n.IndexOf("Factory", System.StringComparison.OrdinalIgnoreCase) >= 0
        || n.IndexOf("Belt", System.StringComparison.OrdinalIgnoreCase) >= 0
        || n.IndexOf("Conveyor", System.StringComparison.OrdinalIgnoreCase) >= 0;

    public static string Begin()
    {
        if (!Application.isPlaying) return "not playing";
        if (!_hooked) { Application.logMessageReceived += OnLog; _hooked = true; }
        _errors = 0; _errorTexts.Clear();
        Time.timeScale = 4f;
        return "begin: " + PlayDrive.Start() + " (timeScale 4; now wait for completion, then Check)";
    }

    public static string Check()
    {
        if (!Application.isPlaying) return "not playing";
        var sb = new StringBuilder();
        var active = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<Transform>(false))
            .Where(t => t.gameObject.activeInHierarchy && Bad(t.name)).Select(t => t.name).ToList();
        sb.AppendLine("active factory/belt/conveyor objects: " + active.Count + (active.Count > 0 ? " [" + string.Join(", ", active.Take(8)) + "]" : ""));
        sb.AppendLine("console errors since Begin: " + _errors + (_errors > 0 ? " first: " + string.Join(" | ", _errorTexts) : ""));
        sb.AppendLine("receipt/paper state: " + PlayDrive.State().Split('\n').FirstOrDefault(l => l.StartsWith("CompletionReceipt")));
        bool ok = active.Count == 0 && _errors == 0;
        sb.AppendLine(ok ? "PASS (verify the receipt line above shows the run completed)" : "FAIL");
        return sb.ToString();
    }

    public static string End()
    {
        if (_hooked) { Application.logMessageReceived -= OnLog; _hooked = false; }
        Time.timeScale = 1f;
        return "ended";
    }
}
