using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;

/// <summary>Tally the Editor console by message (first line), errors and warnings separately: what floods the log?
///   unity command script run --file AgentScripts/LogTally.cs --entry LogTally.Run</summary>
public static class LogTally
{
    // Editor-only Meta XR SDK messages (no headset session over Link): never seen in the Quest's own log.
    static readonly string[] EditorOnly = {
        "XrStructureNode", "XrStructureChain", "HandExtensibilityManager", "Insight Passthrough",
        "Layout update failed to stabilize", "[OVRPlugin]", "xrGetInstanceProcAddr", "MRUK couldn't load",
    };

    /// <summary>"errors=N editorOnly=M": console errors that are NOT known Editor-only SDK noise, and those that are.</summary>
    public static string Errors()
    {
        int real = 0, editorOnly = 0;
        string firstReal = "";
        foreach (var line in Run().Split('\n'))
        {
            int x = line.IndexOf("x E ");
            if (x < 0) continue;
            int n = int.Parse(line.Substring(0, x));
            string msg = line.Substring(x + 4);
            if (EditorOnly.Any(p => msg.Contains(p))) editorOnly += n;
            else { real += n; if (firstReal.Length == 0) firstReal = msg; }
        }
        return "errors=" + real + " editorOnly=" + editorOnly + (firstReal.Length > 0 ? " first='" + firstReal + "'" : "");
    }

    public static string Run()
    {
        var asm = typeof(EditorWindow).Assembly;
        var logEntries = asm.GetType("UnityEditor.LogEntries");
        var logEntry = asm.GetType("UnityEditor.LogEntry");
        int count = (int)logEntries.GetMethod("StartGettingEntries").Invoke(null, null);
        var entry = System.Activator.CreateInstance(logEntry);
        var get = logEntries.GetMethod("GetEntryInternal");
        var msgField = logEntry.GetField("message");
        var modeField = logEntry.GetField("mode");
        var tally = new Dictionary<string, int>();
        try
        {
            for (int i = 0; i < count; i++)
            {
                get.Invoke(null, new object[] { i, entry });
                string msg = (string)msgField.GetValue(entry) ?? "";
                int mode = (int)modeField.GetValue(entry);
                bool err = (mode & (1 | 2 | 16 | 64 | 256 | 2048 | 131072 | 1048576 | 2097152)) != 0;   // error-ish flags
                bool warn = (mode & (128 | 512 | 4096 | 524288)) != 0;
                string kind = err ? "E" : warn ? "W" : "L";
                if (kind == "L") continue;
                string first = msg.Split('\n')[0];
                if (first.Length > 160) first = first.Substring(0, 160);
                string k = kind + " " + first;
                tally[k] = tally.TryGetValue(k, out int n) ? n + 1 : 1;
            }
        }
        finally { logEntries.GetMethod("EndGettingEntries").Invoke(null, null); }
        var sb = new StringBuilder("entries=" + count + "\n");
        foreach (var kv in tally.OrderByDescending(x => x.Value).Take(25)) sb.AppendLine(kv.Value + "x " + kv.Key);
        return sb.ToString();
    }
}
