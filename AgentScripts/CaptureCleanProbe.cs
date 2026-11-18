using System.Text;
using TMPro;
using UnityEngine;

/// <summary>Capture-clean check: is the switch on, and does any shown text still carry demo wording?</summary>
public static class CaptureCleanProbe
{
    public static string Run()
    {
        var sb = new StringBuilder();
        sb.Append("on=").Append(MuseDesk.DebugTools.CaptureClean.On);
        sb.Append(" comp=").Append(Object.FindAnyObjectByType<MuseDesk.DebugTools.CaptureClean>() != null);
        int total = 0, dirty = 0;
        foreach (var t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            total++;
            string s = t.text ?? "";
            if (s.ToUpperInvariant().Contains("SCRIPTED") || s.Contains("Offline: demo") || s.Contains("No model was used"))
            {
                dirty++;
                if (dirty <= 8) sb.Append(" | ").Append(t.name).Append(": ").Append(s.Replace("\n", "\\n"));
            }
        }
        sb.Append(" texts=").Append(total).Append(" withDemoWords=").Append(dirty);
        return sb.ToString();
    }
}
