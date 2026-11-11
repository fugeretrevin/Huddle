using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Dump button UnityEvent wiring, agent walk/arm fields, and the diagnostic text setup.</summary>
public static class DiagWire
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToList();
        var sb = new StringBuilder();

        sb.AppendLine("=== BUTTON EVENT WIRING ===");
        foreach (var go in all)
        {
            var wrap = go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "InteractableUnityEventWrapper");
            if (wrap == null) continue;
            sb.AppendLine($"  '{Path(go)}'");
            var so = new SerializedObject(wrap);
            foreach (var ev in new[] { "_whenSelect", "_whenUnselect", "_whenHover", "_whenUnhover" })
            {
                var calls = so.FindProperty(ev + ".m_PersistentCalls.m_Calls");
                if (calls == null) { continue; }
                if (calls.arraySize == 0) { sb.AppendLine($"      {ev}: (none)"); continue; }
                for (int i = 0; i < calls.arraySize; i++)
                {
                    var call = calls.GetArrayElementAtIndex(i);
                    var tgt = call.FindPropertyRelative("m_Target").objectReferenceValue;
                    var method = call.FindPropertyRelative("m_MethodName").stringValue;
                    var state = call.FindPropertyRelative("m_CallState").enumDisplayNames.ElementAtOrDefault(call.FindPropertyRelative("m_CallState").enumValueIndex);
                    sb.AppendLine($"      {ev}[{i}] -> {(tgt ? tgt.GetType().Name + "@" + (tgt as Component)?.gameObject.name : "NULL")} . {method}()  [{state}]");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== AGENT walk/arm fields (AgentView) ===");
        foreach (var go in all)
        {
            var av = go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "AgentView");
            if (av == null) continue;
            sb.AppendLine($"  AgentView on '{go.name}'");
            var so = new SerializedObject(av);
            var p = so.GetIterator();
            bool enter = true;
            while (p.NextVisible(enter))
            {
                enter = false;
                if (p.propertyType == SerializedPropertyType.ObjectReference)
                    sb.AppendLine($"      {p.name} = {(p.objectReferenceValue ? p.objectReferenceValue.name : "NULL")}");
                else if (p.propertyType == SerializedPropertyType.Float || p.propertyType == SerializedPropertyType.Boolean || p.propertyType == SerializedPropertyType.Integer)
                    sb.AppendLine($"      {p.name} = {GetVal(p)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== DiagnosticsText ===");
        var dt = all.FirstOrDefault(g => g.name == "DiagnosticsText");
        if (dt != null)
        {
            sb.AppendLine($"  active={dt.activeInHierarchy} localRot={dt.transform.localEulerAngles} worldFwd={dt.transform.forward}");
            foreach (var c in dt.GetComponents<Component>())
                if (c != null) sb.AppendLine($"      {c.GetType().Name}");
        }
        else sb.AppendLine("  not found");

        return sb.ToString();
    }

    static string GetVal(SerializedProperty p)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Float: return p.floatValue.ToString();
            case SerializedPropertyType.Boolean: return p.boolValue.ToString();
            case SerializedPropertyType.Integer: return p.intValue.ToString();
            default: return "?";
        }
    }

    static string Path(GameObject go)
    {
        var p = go.name; var t = go.transform.parent; int d = 0;
        while (t != null && d < 4) { p = t.name + "/" + p; t = t.parent; d++; }
        return p;
    }
}
