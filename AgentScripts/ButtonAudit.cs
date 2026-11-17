using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>Read-only: dumps the ISDK poke button building block (hierarchy, components, color fields).</summary>
public static class ButtonAudit
{
    public static string Run()
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var btn = all.FirstOrDefault(t => t.name == "ExplainToggle");
        if (btn == null) return "ExplainToggle not found";
        var sb = new StringBuilder();
        sb.AppendLine($"root le={btn.localEulerAngles} ls={btn.localScale}");
        foreach (var t in btn.GetComponentsInChildren<Transform>(true))
        {
            sb.AppendLine($"- {Path(t, btn)} lp={t.localPosition:F3} ls={t.localScale:F3} active={t.gameObject.activeSelf}");
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                sb.AppendLine($"    [{c.GetType().Name}]");
                var n = c.GetType().Name;
                if (n.Contains("Color") || n.Contains("RoundedBox") || n.Contains("MaterialPropertyBlock") || n.Contains("PokeInteractableVisual"))
                    foreach (var f in c.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        object v = null; try { v = f.GetValue(c); } catch { }
                        string vs = v == null ? "null" : v.ToString();
                        if (v != null && !(v is Object) && !v.GetType().IsPrimitive && !(v is string) && !(v is Color) && !(v is Vector4) && !(v is Vector3))
                            vs = v.GetType().Name + "{" + string.Join("; ", v.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                .Select(ff => { object x = null; try { x = ff.GetValue(v); } catch { } return ff.Name + "=" + (x == null ? "null" : x.ToString()); })) + "}";
                        sb.AppendLine($"        {f.Name} = {vs}");
                    }
                if (c is Renderer r && r.sharedMaterial != null)
                    sb.AppendLine($"        material={r.sharedMaterial.name} shader={r.sharedMaterial.shader.name}");
            }
        }
        return sb.ToString();
    }

    static string Path(Transform t, Transform root)
    {
        string p = t.name;
        while (t != root && t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
