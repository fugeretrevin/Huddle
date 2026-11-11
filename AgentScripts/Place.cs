using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using MuseDesk.Bootstrap;

/// <summary>
/// Positions the Meta interaction blocks (pokeable confirm button + grabbable cube)
/// within seated reach under WorkspaceRoot, and wires the pokeable's select event to
/// AppBootstrap.OnConfirmPlacementPressed so a hand-poke confirms manual placement.
/// </summary>
public static class Place
{
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
