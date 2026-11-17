using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Coordinator fix-ups after the three redesign builders. Idempotent; saves the scene.</summary>
public static class IntegrateV2
{
    static Transform Find(string n) => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == n);

    /// <summary>Moves the first TMP label named <paramref name="label"/> under <paramref name="owner"/>
    /// back toward its surface (along its own +Z, away from the reader) by metres.</summary>
    static void Push(string owner, string label, float metres, StringBuilder sb)
    {
        var o = Find(owner); if (o == null) { sb.AppendLine("push: missing " + owner); return; }
        var t = o.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault(x => x.name == label);
        if (t == null) { sb.AppendLine($"push: no {label} under {owner}"); return; }
        t.transform.position += t.transform.forward * metres;
        sb.AppendLine($"pushed {owner}/{label} {metres * 1000f:F2} mm");
    }

    public static string Apply()
    {
        var sb = new StringBuilder();
        var receipt = Find("CompletionReceipt");
        if (receipt != null)
        {
            var c = receipt.GetComponent("CompletionReceipt");
            if (c != null)
            {
                var so = new SerializedObject(c);
                var p = so.FindProperty("startAfterReset");
                if (p != null) { p.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); sb.AppendLine("receipt: Run again resets and starts"); }
            }
        }
        // Nameplates: sit on the measured desktop front edge (desktops are 7.5 to 8 cm high), not in
        // front of the agents' faces. Root-local; OfficeV2 is an identity child of WorkspaceRoot.
        var ws = Find("WorkspaceRoot");
        var plates = new (string n, Vector3 p)[] {
            ("Nameplate_Researcher", new Vector3(-0.300f, 0.091f, 0.108f)),
            ("Nameplate_Developer",  new Vector3( 0.000f, 0.103f, 0.150f)),
            ("Nameplate_Tester",     new Vector3( 0.300f, 0.096f, 0.108f)) };
        foreach (var (n, p) in plates)
        {
            var t = Find(n); if (t == null) { sb.AppendLine("missing " + n); continue; }
            t.position = ws.TransformPoint(p);
            // 1.4x so the role word is about 9 mm and the activity about 7.7 mm at 0.6 m; side plates yaw to the eye.
            t.localScale = Vector3.one * 1.4f;
            Vector3 eye = new Vector3(0, 0.30f, 0.62f);
            Vector3 away = p - eye; away.y = 0;
            t.rotation = ws.rotation * Quaternion.LookRotation(away.normalized, Vector3.up);   // upright: on head the 30 degree lean read as tilted down
            var np = t.GetComponent("AgentNameplate");
            if (np != null) { var so = new SerializedObject(np); var sp = so.FindProperty("shownScale"); if (sp != null) { sp.vector3Value = Vector3.one * 1.4f; so.ApplyModifiedPropertiesWithoutUndo(); } }
            sb.AppendLine($"{n} -> {p} euler {t.localEulerAngles}");
        }

        // One-time depth corrections measured by XrVisualQa (text was 1.2 to 3 mm proud; target 0.5 mm).
        if (Find("V2_DepthFixed") == null)
        {
            Push("TaskBoard", "Title", 0.0024f, sb);
            foreach (var d in new[] { "Dock_Researcher", "Dock_Developer", "Dock_Tester" }) Push(d, "Name", 0.0025f, sb);
            foreach (var k in new[] { "PauseResumeButton", "PriorityButton", "DockStartKey", "DockBoardKey" }) Push(k, "ButtonLabel", 0.00066f, sb);
            Push("FocusToggle", "ButtonLabel", 0.00095f, sb);
            var marker = new GameObject("V2_DepthFixed"); marker.transform.SetParent(ws, false);
        }

        // New-task key was 0.72 m from the eye on the top rail: bring it to the bottom row with the toolbar.
        var nt = Find("NewTaskButton");
        if (nt != null) { nt.localPosition = new Vector3(-0.085f, -0.262f, -0.012f); sb.AppendLine("NewTaskButton -> bottom row"); }

        // Focus-mode inbox readout was 3.6 mm text.
        var inbox = Find("InboxReadout");
        if (inbox != null && Find("V2_InboxScaled") == null)
        {
            inbox.localScale *= 1.6f;
            new GameObject("V2_InboxScaled").transform.SetParent(ws, false);
        }
        var dock = Find("TaskDock");
        if (dock != null)
        {
            var mode = dock.Find("Mode");
            var tmp = mode != null ? mode.GetComponent<TMPro.TMP_Text>() : null;
            if (tmp != null) { var sd = tmp.rectTransform.sizeDelta; if (sd.x < 5.6f) { tmp.rectTransform.sizeDelta = new Vector2(5.6f, sd.y); tmp.enableAutoSizing = true; tmp.fontSizeMin = 5.5f; } sb.AppendLine($"dock Mode rect {tmp.rectTransform.sizeDelta} font {tmp.fontSize}"); }
        }
        var ccl = Find("CardConsoleLabel");
        if (ccl != null) { var tmp = ccl.GetComponent<TMPro.TMP_Text>(); if (tmp != null) sb.AppendLine($"CardConsoleLabel font {tmp.fontSize} min {tmp.fontSizeMin} max {tmp.fontSizeMax} auto {tmp.enableAutoSizing} rect {tmp.rectTransform.sizeDelta} scale {ccl.lossyScale.x}"); }

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sb.ToString();
    }
}
