using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// rc18 (owner worn rc17) scene values:
///  - the per-agent "!" (Agent_*/Alert) sat 0.40 above the feet on 0.22-0.24 tall agents: "a little high up". Now 0.315.
///  - handoff pacing: exchangeHold 1.8 s, Researcher lane walk from the delivery spot 1.1 -> 2.2 s, the success sequence waits
///    up to 24 s for the handoff chain (it waited 15).
///  - ReleaseConfig: the Calm motion key is removed (reducedMotionToggle off; reduced motion stays off).
///   unity command script run --file AgentScripts/SceneTweaksRc18.cs --entry SceneTweaksRc18.Apply --args '[true]'
/// </summary>
public static class SceneTweaksRc18
{
    const float AlertY = 0.315f;

    public static string Apply(bool save)
    {
        var sb = new StringBuilder();
        Transform ws = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "WorkspaceRoot" && t.gameObject.scene.IsValid());
        if (ws == null) return "no WorkspaceRoot";

        foreach (var view in ws.GetComponentsInChildren<MuseDesk.Agents.AgentView>(true))
        {
            if (view.alert == null) continue;
            var a = view.alert.transform;
            Undo.RecordObject(a, "Alert");
            float before = a.localPosition.y;
            a.localPosition = new Vector3(a.localPosition.x, AlertY, a.localPosition.z);
            sb.AppendLine(view.name + " alert y " + before.ToString("F3") + " -> " + AlertY.ToString("F3"));
        }

        foreach (var h in Resources.FindObjectsOfTypeAll<MuseDesk.Agents.AgentHandoffController>().Where(x => x.gameObject.scene.IsValid()))
        {
            Undo.RecordObject(h, "Handoff pacing");
            h.exchangeHold = 1.8f; h.minApproachSeconds = 2.2f; h.readyBeat = 0.4f;
            if (h.lanes != null)
                foreach (var l in h.lanes)
                    if (l != null && l.fromDeliverySeconds > 0f && l.fromDeliverySeconds < 2.2f) { sb.AppendLine("lane " + l.sourceId + "->" + l.targetId + " fromDeliverySeconds " + l.fromDeliverySeconds + " -> 2.2"); l.fromDeliverySeconds = 2.2f; }
            EditorUtility.SetDirty(h);
            sb.AppendLine("handoff " + h.name + ": hold 1.8, minApproach 2.2, ready 0.4");
        }
        foreach (var c in Resources.FindObjectsOfTypeAll<MuseDesk.Agents.CompletionSequence>().Where(x => x.gameObject.scene.IsValid()))
        {
            Undo.RecordObject(c, "Chain wait"); c.handoffChainWaitMax = 24f; EditorUtility.SetDirty(c);
            sb.AppendLine("completion " + c.name + ": chain wait 24");
        }

        var cfg = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/MuseDesk/Resources/ReleaseConfig.asset");
        if (cfg != null)
        {
            var so = new SerializedObject(cfg);
            var p = so.FindProperty("reducedMotionToggle");
            if (p != null) { sb.AppendLine("ReleaseConfig.reducedMotionToggle " + p.boolValue + " -> False"); p.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(cfg); }
        }

        if (save)
        {
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(ws.gameObject.scene);
            EditorSceneManager.SaveScene(ws.gameObject.scene);
            sb.AppendLine("saved");
        }
        return sb.ToString();
    }
}
