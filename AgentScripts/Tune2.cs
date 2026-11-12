using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Board text was ballooning (autosize grew to max). Disable autosize + fixed small fonts,
/// shrink + lower the board. Also tighten the agent push capsule.</summary>
public static class Tune2
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();

        // ---- Board ----
        var board = all.FirstOrDefault(t => t.name == "TaskBoard");
        if (board != null)
        {
            board.localScale = Vector3.one * 0.55f;
            board.position = new Vector3(0f, 1.32f, 0.6f);
            foreach (var c in board.GetComponentsInChildren<Component>(true).Where(x => x != null && x.GetType().Name.StartsWith("TextMeshPro")))
            {
                var ty = c.GetType();
                var nm = (c as Component).transform.name;
                float size = nm == "Title" ? 0.05f : nm.StartsWith("Label_") ? 0.042f : 0.03f; // card title etc.
                var auto = ty.GetProperty("enableAutoSizing"); if (auto != null) auto.SetValue(c, false);
                var fsz = ty.GetProperty("fontSize"); if (fsz != null) fsz.SetValue(c, size);
                var ov = ty.GetProperty("overflowMode"); if (ov != null) { try { ov.SetValue(c, System.Enum.ToObject(ov.PropertyType, 0)); } catch { } }
                // Force the TMP mesh to rebuild so the new size actually takes (edit-mode).
                ty.GetMethod("ForceMeshUpdate", System.Type.EmptyTypes)?.Invoke(c, null);
                EditorUtility.SetDirty(c);
            }
            sb.AppendLine($"board: scale 0.55, pos {board.position}, fixed small fonts (autosize off)");
        }

        // ---- Hitbox: tighter capsule around the body core ----
        foreach (var agent in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" })
        {
            var a = all.FirstOrDefault(t => t.name == agent);
            if (a == null) continue;
            var cap = a.GetComponents<CapsuleCollider>().FirstOrDefault(c => c.isTrigger);
            if (cap != null)
            {
                cap.radius = 0.016f; cap.height = 0.085f; cap.center = new Vector3(0f, 0.06f, 0f);
                sb.AppendLine($"{agent}: capsule r=0.016 h=0.085 (tighter)");
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("SAVED.");
        return sb.ToString();
    }
}
