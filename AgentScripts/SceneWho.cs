using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SceneWho
{
    public static string Run()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var s = EditorSceneManager.GetSceneAt(i);
            sb.AppendLine($"scene[{i}] {s.path} loaded={s.isLoaded} dirty={s.isDirty} roots={s.rootCount}");
            foreach (var r in s.GetRootGameObjects()) sb.AppendLine($"   root {r.name} active={r.activeSelf} children={r.GetComponentsInChildren<Transform>(true).Length}");
        }
        var all = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.gameObject.scene.IsValid());
        foreach (var n in new[] { "DecisionDocument", "WorkspaceRoot", "TaskDock", "CompletionReceipt" })
            sb.AppendLine($"{n}: {string.Join(" | ", all.Where(t => t.name.StartsWith(n)).Select(t => t.name + "@" + (t.parent != null ? t.parent.name : "<root>") + " in " + System.IO.Path.GetFileNameWithoutExtension(t.gameObject.scene.path)).Take(6))}");
        sb.AppendLine("playing=" + EditorApplication.isPlaying + " prefabStage=" + (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null));
        return sb.ToString();
    }
}
