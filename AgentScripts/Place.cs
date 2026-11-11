using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using MuseDesk.Bootstrap;
using MuseDesk.Workspace;

/// <summary>
/// Positions the Meta interaction blocks (pokeable confirm button + grabbable cube)
/// within seated reach under WorkspaceRoot, and wires the pokeable's select event to
/// AppBootstrap.OnConfirmPlacementPressed so a hand-poke confirms manual placement.
/// </summary>
public static class Place
{
    /// <summary>
    /// Reliability pass: lift the pokeable button and grabbable cube OUT of the moving
    /// WorkspaceRoot, size the cube sensibly, and attach PlaceInFrontOnStart so both park
    /// in front of the user (facing them); the cube becomes kinematic so it can't fall.
    /// Removes the placement marker to avoid confusing it with the grabbable.
    /// </summary>
    public static string FixProps()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToList();
        var sb = new StringBuilder();

        var poke = all.FirstOrDefault(g => g.name.Contains("Poke Interaction"));
        var grab = all.FirstOrDefault(g => g.name.Contains("Grabbable Item"));
        var marker = all.FirstOrDefault(g => g.name == "PlacementMarker");

        if (poke != null)
        {
            poke.transform.SetParent(null, true);
            var p = poke.GetComponent<PlaceInFrontOnStart>() ?? poke.AddComponent<PlaceInFrontOnStart>();
            p.sideOffset = 0.12f; p.distance = 0.45f; p.faceUser = true;
            sb.AppendLine($"poke -> front (+0.12): {poke.name}");
        }
        else sb.AppendLine("poke NOT FOUND");

        if (grab != null)
        {
            grab.transform.SetParent(null, true);
            grab.transform.localScale = Vector3.one * 0.06f;
            var p = grab.GetComponent<PlaceInFrontOnStart>() ?? grab.AddComponent<PlaceInFrontOnStart>();
            p.sideOffset = -0.12f; p.distance = 0.45f; p.faceUser = true;
            sb.AppendLine($"grab -> front (-0.12), scale 0.06, kinematic: {grab.name}");
        }
        else sb.AppendLine("grab NOT FOUND");

        if (marker != null) { Object.DestroyImmediate(marker); sb.AppendLine("removed PlacementMarker"); }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("scene saved");
        return sb.ToString();
    }

    public static string InteractionProps()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var sb = new StringBuilder();

        var workspace = roots.FirstOrDefault(r => r.name == "WorkspaceRoot");
        var poke = roots.FirstOrDefault(r => r.name.Contains("PokeInteraction") || r.name.Contains("Poke Interaction"));
        var grab = roots.FirstOrDefault(r => r.name.Contains("Grabbable"));
        var bootstrap = Object.FindAnyObjectByType<AppBootstrap>();

        if (workspace == null) return "ERROR: WorkspaceRoot not found";

        if (poke != null)
        {
            poke.transform.SetParent(workspace.transform, false);
            poke.transform.localPosition = new Vector3(0.12f, 0.06f, -0.14f);
            poke.transform.localRotation = Quaternion.identity;
            sb.AppendLine($"poke parented+placed: {poke.name}");

            // Wire WhenSelect → AppBootstrap.OnConfirmPlacementPressed (best-effort).
            var wrapperType = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Oculus.Interaction.InteractableUnityEventWrapper"))
                .FirstOrDefault(t => t != null);
            if (wrapperType != null && bootstrap != null)
            {
                var wrapper = poke.GetComponentInChildren(wrapperType, true);
                if (wrapper != null)
                {
                    var field = wrapperType.GetField("_whenSelect",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field?.GetValue(wrapper) is UnityEvent evt)
                    {
                        UnityAction action = bootstrap.OnConfirmPlacementPressed;
                        UnityEventTools.AddPersistentListener(evt, action);
                        sb.AppendLine("poke WhenSelect -> AppBootstrap.OnConfirmPlacementPressed wired");
                    }
                    else sb.AppendLine("poke: _whenSelect event not found (skipped wiring)");
                }
                else sb.AppendLine("poke: InteractableUnityEventWrapper not found (skipped wiring)");
            }
            else sb.AppendLine("poke: wrapper type or bootstrap missing (skipped wiring)");
        }
        else sb.AppendLine("poke block not found");

        if (grab != null)
        {
            grab.transform.SetParent(workspace.transform, false);
            grab.transform.localPosition = new Vector3(-0.12f, 0.06f, -0.14f);
            grab.transform.localScale = Vector3.one * 0.04f;
            sb.AppendLine($"grab parented+placed: {grab.name}");
        }
        else sb.AppendLine("grabbable block not found");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("scene saved");
        return sb.ToString();
    }
}
