using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using Oculus.Interaction;
using MuseDesk.Spatial;
using MuseDesk.Workspace;

/// <summary>
/// rc9 owner report: "the agents also never used the portal and there was no way to access that as an option". Adds ONE key,
/// Console_Meet, to the Spaces dock's context row (Spatial/NearUserRig/Panel_Mode/ModeConsole), in the slot of the Wall
/// preview's Full / Compact key (Console_Layout) — the fourth column, right of Manager. Layout is shown only while a wall
/// spot is previewed and Meet only while the habitat is at rest on the wall, so the two never show together and no key
/// moves: the row stays Move | Select | Manager | Meet at rest, Next spot | Place | Cancel | Full at preview.
///
/// What it does, idempotently:
///   - clones Console_Manager into Console_Meet at Console_Layout's local position (saved hidden by scale, never SetActive;
///     the ModeConsole shows it by scale and switches its PokeInteractable), label "Meet" on one line (auto-sized down to
///     5.5 mm, never an ellipsis), its InteractableUnityEventWrapper._whenSelect persistent call wired to ModeConsole.PressMeet;
///   - sets ModeConsole.meetKey. Feature flags are untouched (the key shows only with largeAgentPortal on).
/// Needs UnifiedModeDockV1 applied first. Saves the scene once.
///
///   unity command script run --file AgentScripts/MeetKeyV1.cs --entry MeetKeyV1.Apply
///   (MeetKeyV1.Revert removes Console_Meet and clears ModeConsole.meetKey.)
/// Play Mode: AgentScripts/MeetDrive.cs.
/// </summary>
public static class MeetKeyV1
{
    const string KeyName = "Console_Meet";
    const float Hidden = 0.001f;
    const float LabelMinMm = 5.5f;

    static string P(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";

    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t != root && t.name == name);

    static int Rewire(Transform key, Object target, string method)
    {
        int wired = 0;
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "InteractableUnityEventWrapper") continue;
            var so = new SerializedObject(mb);
            var calls = so.FindProperty("_whenSelect.m_PersistentCalls.m_Calls");
            if (calls == null) continue;
            if (calls.arraySize == 0) calls.InsertArrayElementAtIndex(0);
            while (calls.arraySize > 1) calls.DeleteArrayElementAtIndex(calls.arraySize - 1);   // exactly one call: PressMeet
            var c = calls.GetArrayElementAtIndex(0);
            c.FindPropertyRelative("m_Target").objectReferenceValue = target;
            c.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = target.GetType().AssemblyQualifiedName;
            c.FindPropertyRelative("m_MethodName").stringValue = method;
            c.FindPropertyRelative("m_Mode").intValue = 1;
            c.FindPropertyRelative("m_CallState").intValue = 2;
            so.ApplyModifiedProperties();
            wired++;
        }
        return wired;
    }

    /// <summary>One line, auto-sized down to 5.5 mm, never an ellipsis (UnifiedModeDockV1's rule).</summary>
    static void Words(TMP_Text label, string words)
    {
        if (label == null) return;
        Undo.RecordObject(label, "label");
        label.text = words;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        float s = Mathf.Abs(label.transform.lossyScale.x);
        if (s > 1e-7f)
        {
            float min = LabelMinMm * 0.01f / s;
            if (label.fontSizeMax < min) label.fontSizeMax = min;
            label.fontSizeMin = Mathf.Min(min, label.fontSizeMax);
            label.enableAutoSizing = label.fontSizeMin < label.fontSizeMax;
        }
        EditorUtility.SetDirty(label);
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "MeetKeyV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "MeetKeyV1: no open scene.\n";
        var sb = new StringBuilder("== MeetKeyV1.Apply ==\n");

        var console = Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);
        if (console == null) return "MeetKeyV1: no ModeConsole; nothing changed.\n";
        if (!console.unifiedApplied || console.wallKey == null || console.wallKey.root == null)
            return "MeetKeyV1: the Spaces dock is not unified yet (run UnifiedModeDockV1 first); nothing changed.\n";
        if (console.managerKey == null || console.managerKey.root == null || console.layoutKey == null || console.layoutKey.root == null)
            return "MeetKeyV1: Console_Manager or Console_Layout is missing (re-run UnifiedModeDockV1); nothing changed.\n";
        Transform ct = console.transform;
        ModeConsole.ConsoleKey template = console.managerKey;
        Vector3 slot = console.layoutKey.root.localPosition;

        // The slot must be the fourth column of the context row: same row as Manager, one key pitch to its side.
        Vector3 mgr = template.root.localPosition;
        float pitch = console.floorKey.root.localPosition.x - console.deskKey.root.localPosition.x;
        if (Mathf.Abs(slot.y - mgr.y) > 0.002f || Mathf.Abs(Mathf.Abs(slot.x - mgr.x) - Mathf.Abs(pitch)) > 0.003f)
            sb.AppendLine($"  NOTE: Console_Layout {P(slot)} is not one pitch ({pitch * 1000f:F1} mm) beside Console_Manager {P(mgr)}: check the row in the Scene view");

        Undo.RecordObject(console, "MeetKeyV1");
        Transform k = ct.Find(KeyName);
        bool created = k == null;
        if (created)
        {
            var go = Object.Instantiate(template.root.gameObject, ct);
            Undo.RegisterCreatedObjectUndo(go, KeyName);
            go.name = KeyName;
            k = go.transform;
        }
        k.gameObject.SetActive(true);                       // the GameObject stays active; hidden by scale only
        k.SetSiblingIndex(template.root.GetSiblingIndex() + 1);
        k.localPosition = slot;
        k.localRotation = template.root.localRotation;
        k.localScale = template.shownScale * Hidden;        // saved hidden; ModeConsole shows it by scale
        Transform capT = Under(k, "Cap3D");
        var capR = capT != null ? capT.GetComponent<MeshRenderer>() : null;
        if (capR != null && console.segmentIdle != null) { capR.sharedMaterial = console.segmentIdle; EditorUtility.SetDirty(capR); }
        var label = k.GetComponentInChildren<TMP_Text>(true);
        Words(label, SpacesDockLogic.Meet);
        var ksv = k.GetComponent<KeyStateVisual>();
        if (ksv != null) { Undo.RecordObject(ksv, "visual"); ksv.capRenderer = capR; ksv.label = label; EditorUtility.SetDirty(ksv); }
        int wired = Rewire(k, console, nameof(ModeConsole.PressMeet));
        var poke = k.GetComponentInChildren<PokeInteractable>(true);
        EditorUtility.SetDirty(k.gameObject);

        console.meetKey = new ModeConsole.ConsoleKey
        {
            root = k, shownScale = template.shownScale, label = label, visual = ksv, poke = poke, cap = capR,
        };
        EditorUtility.SetDirty(console);
        sb.AppendLine($"  {(created ? "created" : "updated")} {KeyName} \"{SpacesDockLogic.Meet}\" at {P(slot)} (Console_Layout's slot, right of Manager) -> ModeConsole.PressMeet"
                      + $" ({wired} wrapper(s) wired){(poke == null ? "  WARNING: no PokeInteractable" : "")}{(label == null ? "  WARNING: no label" : "")}");
        sb.AppendLine("  " + (SpacesDockLogic.FitsOnKey(SpacesDockLogic.Meet) ? "\"Meet\" fits one line of a 36 mm key at 5.5 mm" : "WARNING: \"Meet\" may not fit the key"));

        var wall = Object.FindAnyObjectByType<MuseDesk.Wall.WallHabitatController>(FindObjectsInactive.Include);
        sb.AppendLine(wall != null
            ? $"  wall host {wall.name}: largeAgentPortal flag in the scene = {wall.flags.largeAgentPortalEnabled} (ReleaseConfigApplier sets it at run time; untouched here)"
            : "  NOTE: no WallHabitatController: the Meet key never shows");
        if (console.wallHostBehaviour == null && wall != null) sb.AppendLine("  NOTE: ModeConsole.wallHostBehaviour is empty: the director's wallHost is used");

        // Reach from the seated eye (rig-local), as UnifiedModeDockV1 reports it.
        var kit = Object.FindAnyObjectByType<NearUserKit>(FindObjectsInactive.Include);
        var desk = Object.FindAnyObjectByType<WorkspacePlacement>(FindObjectsInactive.Include);
        Transform rig = kit != null && kit.rig != null ? kit.rig : ct.parent != null ? ct.parent.parent : null;
        Vector3 eye = desk != null ? new Vector3(desk.offDeskSide, desk.offDeskDrop, desk.offDeskDepth) : new Vector3(0f, 0.39f, 0.62f);
        if (rig != null)
        {
            float dMeet = (rig.InverseTransformPoint(k.position) - eye).magnitude;
            float dMgr = (rig.InverseTransformPoint(template.root.position) - eye).magnitude;
            sb.AppendLine($"  reach from the seated eye {P(eye)} (rig-local): Meet {dMeet:F3} m, Manager {dMgr:F3} m{(dMeet > 0.55f ? "  NOTE: beyond 0.55 m; the start-up reach keeps it under 0.8 m" : "")}");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }

    public static string Revert()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "MeetKeyV1.Revert: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder("== MeetKeyV1.Revert ==\n");
        var console = Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);
        if (console == null) return "MeetKeyV1.Revert: no ModeConsole.\n";
        Undo.RecordObject(console, "MeetKeyV1 revert");
        Transform t = console.transform.Find(KeyName);
        if (t != null) { Undo.DestroyObjectImmediate(t.gameObject); sb.AppendLine("  removed " + KeyName); }
        else sb.AppendLine("  no " + KeyName + " to remove");
        console.meetKey = new ModeConsole.ConsoleKey();
        EditorUtility.SetDirty(console);
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }
}
