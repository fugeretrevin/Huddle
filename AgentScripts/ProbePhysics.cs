using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Probe: which ISDK hand-physics types exist, and how the HandVisuals expose their IHand.</summary>
public static class ProbePhysics
{
    public static string Run()
    {
        var sb = new StringBuilder();
        var asms = AppDomain.CurrentDomain.GetAssemblies();
        string[] wanted = { "HandPhysicsCapsules", "HandPokeLimiterVisual", "FingerPhysics", "HandGrabPhysics", "SyntheticHand", "PhysicsHand" };
        sb.AppendLine("=== Candidate physics types ===");
        foreach (var w in wanted)
        {
            var types = asms.SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                             .Where(t => t.Name == w).ToList();
            foreach (var t in types)
                sb.AppendLine($"  FOUND {t.FullName}  (asm {t.Assembly.GetName().Name})");
            if (types.Count == 0) sb.AppendLine($"  (none) {w}");
        }

        // HandVisual IHand field
        sb.AppendLine();
        sb.AppendLine("=== HandVisual fields (to source an IHand) ===");
        var hv = asms.SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                     .FirstOrDefault(t => t.Name == "HandVisual");
        if (hv != null)
        {
            sb.AppendLine($"  {hv.FullName}");
            foreach (var f in hv.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (f.FieldType.Name.Contains("Hand") || f.Name.ToLower().Contains("hand"))
                    sb.AppendLine($"     {f.FieldType.Name} {f.Name}");
            foreach (var p in hv.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                if (p.PropertyType.Name.Contains("Hand"))
                    sb.AppendLine($"     prop {p.PropertyType.Name} {p.Name}");
        }
        else sb.AppendLine("  HandVisual type not found");

        // HandPhysicsCapsules fields if present
        var hpc = asms.SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                      .FirstOrDefault(t => t.Name == "HandPhysicsCapsules");
        if (hpc != null)
        {
            sb.AppendLine();
            sb.AppendLine($"=== {hpc.FullName} serialized fields ===");
            foreach (var f in hpc.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (f.GetCustomAttribute<SerializeField>() != null || f.IsPublic)
                    sb.AppendLine($"     {f.FieldType.Name} {f.Name}");
        }
        return sb.ToString();
    }
}
