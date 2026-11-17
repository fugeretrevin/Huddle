using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Low-risk runtime-cost fixes found by the device log and the static performance audit.
///  - MRUK world lock off: the off-desk workspace never uses scene anchors, and with it on MRUK
///    logged a warning (with a stack trace) EVERY FRAME on the Quest.
///  - No stack traces for Log / Warning in player builds.
///  - Physics at the display rate, and interpolation on the two pushable props (no judder).
///  - Near clip 5 cm so desk-scale miniatures do not clip when the user leans in.
/// Idempotent.  unity command script run --file AgentScripts/PerfQuickWins.cs --entry PerfQuickWins.Apply
/// </summary>
public static class PerfQuickWins
{
    public static string Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();

        foreach (var mb in all.SelectMany(t => t.GetComponents<MonoBehaviour>()).Where(m => m != null && m.GetType().Name == "MRUK"))
        {
            var so = new SerializedObject(mb);
            var p = so.FindProperty("EnableWorldLock");
            if (p != null && p.propertyType == SerializedPropertyType.Boolean)
            {
                sb.AppendLine($"MRUK.EnableWorldLock {p.boolValue} -> False ({mb.name})");
                p.boolValue = false;
                so.ApplyModifiedProperties();
            }
            else sb.AppendLine("MRUK found but EnableWorldLock is not a serialized bool; left alone");
        }

        foreach (var name in new[] { "Prop_Block", "Prop_Mug" })
        {
            var t = all.FirstOrDefault(x => x.name == name);
            var rb = t != null ? t.GetComponent<Rigidbody>() : null;
            if (rb == null) { sb.AppendLine("SKIP " + name); continue; }
            Undo.RecordObject(rb, "interpolate");
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            EditorUtility.SetDirty(rb);
            sb.AppendLine(name + " interpolation -> Interpolate");
        }

        var cam = all.Select(t => t.GetComponent<Camera>()).FirstOrDefault(c => c != null && c.enabled && c.name == "CenterEyeAnchor");
        if (cam != null)
        {
            Undo.RecordObject(cam, "near clip");
            sb.AppendLine($"CenterEyeAnchor near clip {cam.nearClipPlane} -> 0.05");
            cam.nearClipPlane = 0.05f;
            EditorUtility.SetDirty(cam);
        }

        PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        PlayerSettings.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
        sb.AppendLine("stack traces: Log=None, Warning=None (Error/Exception unchanged)");

        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
        var maxStep = tm.FindProperty("Maximum Allowed Timestep");
        if (maxStep != null) { sb.AppendLine($"max timestep {maxStep.floatValue} -> 0.1"); maxStep.floatValue = 0.1f; tm.ApplyModifiedProperties(); }
        sb.AppendLine($"fixed timestep {Time.fixedDeltaTime} -> {1f / 72f}");
        Time.fixedDeltaTime = 1f / 72f;

        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }
}
