using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Feedback;
using MuseDesk.Networking;

public static class WireFeedback
{
    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var services = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "AppServices");
        if (services == null) return "no AppServices";
        var controller = services.GetComponent<SessionController>();

        var fb = services.GetComponent<FeedbackController>() ?? services.AddComponent<FeedbackController>();
        fb.controller = controller;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return $"FeedbackController wired (controller={controller != null})";
    }
}
