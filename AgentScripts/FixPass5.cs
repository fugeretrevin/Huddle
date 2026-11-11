using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Pass 5: bring arms in to the shoulders and shorten them so hands sit at mid-body.</summary>
public static class FixPass5
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        int n = 0;
        foreach (var agent in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" })
        {
            var a = all.FirstOrDefault(t => t.name == agent);
            if (a == null) continue;
            foreach (var arm in a.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Arm_L" || t.name == "Arm_R"))
            {
                float x = arm.name == "Arm_L" ? -0.026f : 0.026f;
                arm.localPosition = new Vector3(x, 0.075f, 0.006f);   // in at the shoulder
                var mesh = arm.Find("ArmMesh");
                if (mesh != null) { mesh.localPosition = new Vector3(0, -0.016f, 0); mesh.localScale = new Vector3(0.010f, 0.020f, 0.010f); }
                var hand = arm.Find("Hand");
                if (hand != null) hand.localPosition = new Vector3(0, -0.034f, 0);
                n++;
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return $"reshaped {n} arms (shoulder in/up, shorter) + SAVED";
    }
}
