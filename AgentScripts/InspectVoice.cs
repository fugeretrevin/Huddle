using System;
using System.Linq;
using System.Reflection;
using System.Text;

/// <summary>Find how to assign a WitConfiguration onto AppVoiceExperience (runtime config path).</summary>
public static class InspectVoice
{
    static Type Find(string n) => AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
        .FirstOrDefault(t => t.Name == n);

    public static string Dump()
    {
        var sb = new StringBuilder();
        var ave = Find("AppVoiceExperience");
        sb.Append("AppVoiceExperience: ").Append(ave?.FullName).Append('\n');
        var t = ave;
        while (t != null && t.Name != "Object")
        {
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         .Where(f => f.Name.ToLower().Contains("config") || f.Name.ToLower().Contains("runtime")))
                sb.Append("  [").Append(t.Name).Append("] field ").Append(f.FieldType.Name).Append(' ').Append(f.Name).Append('\n');
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         .Where(p => p.Name.ToLower().Contains("config") || p.Name.ToLower().Contains("runtime")))
                sb.Append("  [").Append(t.Name).Append("] prop ").Append(p.PropertyType.Name).Append(' ').Append(p.Name).Append(p.CanWrite ? " {set}" : " {get}").Append('\n');
            t = t.BaseType;
        }

        // WitRuntimeConfiguration.witConfiguration field name
        var wrc = Find("WitRuntimeConfiguration");
        sb.Append("\nWitRuntimeConfiguration: ").Append(wrc?.FullName).Append('\n');
        if (wrc != null)
            foreach (var f in wrc.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(f => f.Name.ToLower().Contains("config")))
                sb.Append("  field ").Append(f.FieldType.Name).Append(' ').Append(f.Name).Append('\n');
        return sb.ToString();
    }
}
