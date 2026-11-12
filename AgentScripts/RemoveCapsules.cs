using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Remove the HandPhysicsCapsules push rig: the solid finger capsules were squeezing the dynamic
/// props on pinch and flinging them. Grab stability beats hand-push; the dynamic finger-collision
/// visual (FingerProxy triggers) is independent and stays. Deletes the 'HandPhysicsPush' children.
/// </summary>
public static class RemoveCapsules
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();
        int n = 0;
        foreach (var t in all.Where(t => t.name == "HandPhysicsPush").ToList())
        {
            sb.AppendLine($"removed {t.name} under {t.parent?.name}");
            Object.DestroyImmediate(t.gameObject);
            n++;
        }
        // Also strip any stray HandPhysicsCapsules components left anywhere.
        foreach (var go in all)
        {
            if (go == null) continue;
            foreach (var c in go.GetComponents<Component>().Where(c => c != null && c.GetType().Name == "HandPhysicsCapsules").ToList())
            { Object.DestroyImmediate(c); sb.AppendLine($"removed stray HandPhysicsCapsules on {go.name}"); n++; }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine($"done ({n}) + SAVED");
        return sb.ToString();
    }
}
