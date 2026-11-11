using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using MuseDesk.Config;
using MuseDesk.DebugTools;
using MuseDesk.Networking;

/// <summary>
/// Wires the networking backbone into MainMRScene: SessionController + EventDispatcher on
/// AppServices, and the pokeable button's select event -> SessionController.ApprovePending
/// (so a hand poke approves the pending decision and drives the live/demo round-trip).
/// </summary>
public static class Wire
{
    static Type FindType(string full) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(full)).FirstOrDefault(t => t != null);

    public static string Networking()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var sb = new StringBuilder();

        var services = roots.FirstOrDefault(r => r.name == "AppServices");
        if (services == null) return "ERROR: AppServices not found";

        var settings = AssetDatabase.LoadAssetAtPath<RuntimeSettings>(
            "Assets/MuseDesk/Resources/RuntimeSettings.asset");
        var diag = services.GetComponentInChildren<DiagnosticsOverlay>(true);

        var dispatcher = services.GetComponent<EventDispatcher>() ?? services.AddComponent<EventDispatcher>();
        var controller = services.GetComponent<SessionController>() ?? services.AddComponent<SessionController>();
        controller.settings = settings;
        controller.diagnostics = diag;
        controller.dispatcher = dispatcher;
        sb.AppendLine($"SessionController wired (settings={settings != null}, diag={diag != null})");

        // Wire the pokeable button -> ApprovePending.
        var pokeableType = FindType("Oculus.Interaction.PokeInteractable");
        var wrapperType = FindType("Oculus.Interaction.InteractableUnityEventWrapper");
        if (pokeableType == null || wrapperType == null)
            return sb + "\nWARN: ISDK types not found; skipped poke wiring";

        Component pokeable = null;
        foreach (var r in roots)
        {
            pokeable = r.GetComponentsInChildren(pokeableType, true).FirstOrDefault();
            if (pokeable != null) break;
        }
        if (pokeable == null) { sb.AppendLine("WARN: PokeInteractable not found"); }
        else
        {
            var go = pokeable.gameObject;
            var wrapper = go.GetComponent(wrapperType) ?? go.AddComponent(wrapperType);
            var so = new SerializedObject(wrapper);
            var prop = so.FindProperty("_interactableView");
            if (prop != null) { prop.objectReferenceValue = pokeable; so.ApplyModifiedPropertiesWithoutUndo(); }

            var field = wrapperType.GetField("_whenSelect", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(wrapper) is UnityEvent evt)
            {
                // Remove any prior listeners to our method to avoid duplicates on re-run.
                for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
                    if (evt.GetPersistentTarget(i) == controller) UnityEventTools.RemovePersistentListener(evt, i);
                UnityAction call = controller.ApprovePending;
                UnityEventTools.AddPersistentListener(evt, call);
                EditorUtility.SetDirty(wrapper);
                sb.AppendLine($"poke -> ApprovePending wired on {go.name}");
            }
            else sb.AppendLine("WARN: _whenSelect not found");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("scene saved");
        return sb.ToString();
    }
}
