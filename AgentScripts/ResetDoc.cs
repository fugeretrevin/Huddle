using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Leave the scene clean for build: the decision paper starts inactive (shown only when a
/// decision arrives) and back at identity so the runtime placer/FaceUser own its pose.</summary>
public static class ResetDoc
{
    public static string Go()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var doc = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "DecisionDocument");
        if (doc == null) return "no doc";
        doc.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return $"doc inactive, scene saved (active={doc.activeSelf})";
    }
}
