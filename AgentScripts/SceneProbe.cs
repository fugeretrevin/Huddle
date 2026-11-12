using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Read-only probe of specific scene values needed before wiring. Changes nothing.</summary>
public static class SceneProbe
{
    static Transform Find(string name) => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == name);

    static string V(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

    static void Tree(Transform t, StringBuilder sb, int depth, int maxDepth)
    {
        var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name);
        sb.AppendLine($"{new string(' ', depth * 2)}{t.name} active={t.gameObject.activeSelf} lp={V(t.localPosition)} le={V(t.localEulerAngles)} ls={V(t.localScale)} [{string.Join(",", comps)}]");
        if (depth >= maxDepth) return;
        foreach (Transform c in t) Tree(c, sb, depth + 1, maxDepth);
    }

    static void Dump(Component c, StringBuilder sb)
    {
        var so = new SerializedObject(c);
        var it = so.GetIterator();
        bool enter = true;
        sb.Append($"{c.GetType().Name}@{c.name}: ");
        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.name == "m_Script") continue;
            string val = it.propertyType switch
            {
                SerializedPropertyType.Boolean => it.boolValue.ToString(),
                SerializedPropertyType.Float => it.floatValue.ToString("F3"),
                SerializedPropertyType.Integer => it.intValue.ToString(),
                SerializedPropertyType.String => "\"" + it.stringValue + "\"",
                SerializedPropertyType.ObjectReference => it.objectReferenceValue != null ? it.objectReferenceValue.name : "null",
                SerializedPropertyType.Vector3 => V(it.vector3Value),
                SerializedPropertyType.Enum => it.enumNames.Length > it.enumValueIndex && it.enumValueIndex >= 0 ? it.enumNames[it.enumValueIndex] : it.intValue.ToString(),
                _ => null,
            };
            if (val != null) sb.Append($"{it.name}={val}; ");
        }
        sb.AppendLine();
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

        sb.AppendLine("--- FaceUser ---");
        foreach (var t in all)
            foreach (var mb in t.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetType().Name == "FaceUser") Dump(mb, sb);

        sb.AppendLine("--- key components ---");
        foreach (var name in new[] { "WorkspacePlacement", "SessionController", "DiagnosticsOverlay", "DemoDirector",
                     "Phase2Bootstrap", "BoardPresenter", "AgentHandoffController", "ProjectShelfController",
                     "VoiceDecisionController", "FocusModeController", "ArtifactViewer", "AgentRegistry", "WorkspaceLayoutStore" })
            foreach (var t in all)
                foreach (var mb in t.GetComponents<MonoBehaviour>())
                    if (mb != null && mb.GetType().Name == name) Dump(mb, sb);

        var settings = AssetDatabase.FindAssets("t:RuntimeSettings").Select(AssetDatabase.GUIDToAssetPath).ToList();
        foreach (var p in settings)
        {
            var o = AssetDatabase.LoadMainAssetAtPath(p);
            sb.Append(p + " -> ");
            var so = new SerializedObject(o); var it = so.GetIterator(); bool e = true;
            while (it.NextVisible(e)) { e = false; if (it.name == "m_Script") continue;
                sb.Append(it.name + "=" + (it.propertyType == SerializedPropertyType.String ? it.stringValue :
                    it.propertyType == SerializedPropertyType.Boolean ? it.boolValue.ToString() :
                    it.propertyType == SerializedPropertyType.Float ? it.floatValue.ToString() :
                    it.propertyType == SerializedPropertyType.Integer ? it.intValue.ToString() : "?") + "; "); }
            sb.AppendLine();
        }

        sb.AppendLine("--- TaskBoard tree ---");
        var board = Find("TaskBoard");
        if (board != null) Tree(board, sb, 0, 3);
        sb.AppendLine("--- WorkspaceRoot children ---");
        var root = Find("WorkspaceRoot");
        if (root != null) Tree(root, sb, 0, 1);
        sb.AppendLine("--- Phase6 ---");
        var p6 = Find("Phase6");
        if (p6 != null) Tree(p6, sb, 0, 2);
        return sb.ToString();
    }
}
