using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Which shaders draw the scene (by renderer count), with one example each, for the look pass.</summary>
public static class ShaderCensus
{
    public static string Run()
    {
        var count = new Dictionary<string, int>(); var example = new Dictionary<string, string>();
        foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>().Where(r => r.gameObject.scene.IsValid() && r.gameObject.activeInHierarchy && r.enabled))
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                string s = m.shader.name;
                count[s] = count.TryGetValue(s, out int n) ? n + 1 : 1;
                if (!example.ContainsKey(s)) example[s] = r.name + " / " + m.name;
            }
        var sb = new StringBuilder();
        foreach (var kv in count.OrderByDescending(k => k.Value)) sb.AppendLine(kv.Value + "  " + kv.Key + "   e.g. " + example[kv.Key]);
        return sb.ToString();
    }
}
