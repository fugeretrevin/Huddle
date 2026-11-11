using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// REVERSIBLE fix for "hands look weird": the scene has two hand-visual systems that can render
/// overlapping meshes — the Meta ISDK synthetic hands (OculusHand_L/R, used by the interactors)
/// and the OpenXR Hands visualizer (OpenXRLeftHand/RightHand). This disables the OpenXR visualizer
/// so only the ISDK hands draw. ON-HEAD TEST REQUIRED. If your hands vanish or interaction breaks,
/// run Revert() to turn them back on. Does not touch tracking data sources, only the visual meshes.
/// </summary>
public static class FixDoubleHands
{
    static readonly string[] OpenXrHands = { "OpenXRLeftHand", "OpenXRRightHand" };

    public static string Disable() => SetActive(false);
    public static string Revert()  => SetActive(true);

    static string SetActive(bool on)
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();
        int n = 0;
        foreach (var name in OpenXrHands)
        {
            foreach (var t in all.Where(t => t.name == name))
            {
                t.gameObject.SetActive(on);
                sb.AppendLine($"{name} active={on}");
                n++;
            }
        }
        if (n == 0) sb.AppendLine("no OpenXR hand visualizers found (name may differ)");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved");
        return sb.ToString();
    }
}
