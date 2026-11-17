using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// Readability / honesty pass over the poke buttons and the task board:
///  - button labels no longer wrap one letter per line, and use glyphs the font actually has
///  - the artifact panel's Prev/Next/Close buttons live under the panel root, so they are hidden
///    until an artifact is open
///  - decorative "preview" cards and dependency links on the board are switched off: they looked
///    like tasks but were not driven by any task data
/// Idempotent.  unity command script run --file AgentScripts/PolishBoardAndButtons.cs --entry PolishBoardAndButtons.Apply
/// </summary>
public static class PolishBoardAndButtons
{
    public static string Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();

        var rename = new Dictionary<string, string>
        {
            { "＋ New task", "+ New task" }, { "◀ Prev", "< Prev" }, { "Next ▶", "Next >" },
            { "Pause /\nResume", "Pause" }, { "Pause / Resume", "Pause" }, { "+ New task", "+ New" },
        };

        foreach (var t in all)
        {
            if (!t.GetComponents<MonoBehaviour>().Any(m => m != null && m.GetType().Name == "PokeInteractable")) continue;
            foreach (var tmp in t.GetComponentsInChildren<TMP_Text>(true))
            {
                Undo.RecordObject(tmp, "button label");
                if (rename.TryGetValue(tmp.text, out var better)) tmp.text = better;
                if (rename.TryGetValue(tmp.text, out var better2)) tmp.text = better2;   // second hop (＋ New task -> + New)
                // Labels were either ~3 cm tall and overlapping their neighbours, or (Focus / Explain)
                // scaled to 0.01 and invisible. One size for all: about 2 cm tall, parent-scaled.
                tmp.enableAutoSizing = false;
                tmp.fontSize = 4f;
                tmp.transform.localScale = Vector3.one;
                if (tmp.transform is RectTransform rt) rt.sizeDelta = new Vector2(0.09f, 0.03f);
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.alignment = TextAlignmentOptions.Center;
                EditorUtility.SetDirty(tmp);
                sb.AppendLine($"label '{tmp.text}' on {t.name}: single line");
            }
            // A press visual that is not centred on its surface (PrevPoke was offset sideways).
            var vis = t.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == "ButtonVisual");
            if (vis != null && (Mathf.Abs(vis.localPosition.x) > 1e-4f || Mathf.Abs(vis.localPosition.y) > 1e-4f))
            {
                Undo.RecordObject(vis, "centre visual");
                vis.localPosition = new Vector3(0f, 0f, vis.localPosition.z);
                EditorUtility.SetDirty(vis);
                sb.AppendLine($"re-centred press visual on {t.name}");
            }
        }

        var consoleLabel = all.FirstOrDefault(x => x != null && x.name == "CardConsoleLabel");
        if (consoleLabel != null)
        {
            var ctmp = consoleLabel.GetComponent<TMP_Text>();
            if (ctmp != null) { Undo.RecordObject(ctmp, "console label"); ctmp.fontSize = 3f; EditorUtility.SetDirty(ctmp); }
        }

        // Artifact panel buttons follow the panel's visibility.
        var panel = all.FirstOrDefault(t => t.name == "ArtifactPanel");
        var root = panel != null ? panel.Find("Root") : null;
        if (root != null)
            foreach (var n in new[] { "PrevPoke", "NextPoke", "ClosePoke" })
            {
                var b = panel.Find(n);
                if (b == null) continue;
                Undo.SetTransformParent(b, root, "artifact button under root");
                sb.AppendLine($"{n} now under ArtifactPanel/Root (hidden until an artifact opens)");
            }
        else sb.AppendLine("SKIP artifact buttons: ArtifactPanel/Root not found");

        // Fake task visuals on the board.
        var board = all.FirstOrDefault(t => t.name == "TaskBoard");
        if (board != null)
            foreach (Transform c in board)
                if ((c.name.StartsWith("PreviewCard_") || c.name.StartsWith("DependencyLink_")) && c.gameObject.activeSelf)
                {
                    Undo.RecordObject(c.gameObject, "hide placeholder");
                    c.gameObject.SetActive(false);
                    sb.AppendLine("placeholder " + c.name + " deactivated");
                }

        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }
}
