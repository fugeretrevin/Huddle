using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fixes the decision paper's grab: the HandGrabInteractable was copy-pasted from the proven cube,
/// which carried over its base-class _pointableElement reference pointing at the SOURCE cube's
/// Grabbable. Grabs therefore routed to the cube, never the paper, so the paper never moved and
/// HeldPaperController never saw SelectingPointsCount &gt; 0. Repoint it at the paper's own Grabbable.
/// </summary>
public static class FixGrabWire
{
    public static string Go()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var doc = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "DecisionDocument");
        if (doc == null) return "no DecisionDocument";

        var grab = doc.GetComponents<Component>().FirstOrDefault(c => c.GetType().FullName == "Oculus.Interaction.Grabbable");
        var hand = doc.GetComponents<Component>().FirstOrDefault(c => c.GetType().FullName == "Oculus.Interaction.HandGrab.HandGrabInteractable");
        if (grab == null || hand == null) return "missing Grabbable/HandGrabInteractable on paper";

        var sb = new StringBuilder();
        var f = FindField(hand.GetType(), "_pointableElement");
        if (f == null) return "ERROR: _pointableElement field not found";
        var before = f.GetValue(hand) as UnityEngine.Object;
        f.SetValue(hand, grab);   // Grabbable implements IPointableElement; stored as UnityEngine.Object
        sb.AppendLine($"_pointableElement: {(before ? before.name : "null")} -> {grab.name} (self)");

        // Also clear the runtime backing field so it re-resolves from the serialized ref (defensive).
        var bf = FindField(hand.GetType(), "<PointableElement>k__BackingField");
        if (bf != null) bf.SetValue(hand, null);

        EditorUtility.SetDirty(hand);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved");
        return sb.ToString();
    }

    static FieldInfo FindField(Type t, string name)
    {
        for (; t != null; t = t.BaseType)
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }
}
