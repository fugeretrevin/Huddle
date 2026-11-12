using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Events;
using TMPro;
using MuseDesk.Phase2.Board;
using MuseDesk.Workspace;

/// <summary>
/// Makes the workspace layout consistent with the settled facing: WorkspaceRoot +Z points AT the
/// seated user (agents face the user across their desks). Everything the user touches moves to the
/// user's side within reach; the task board moves off to the side so it is not above the head.
///
/// Coordinates are WorkspaceRoot-local. The user's eye is at about (0, +drop, +depth) looking toward
/// -Z; the user's RIGHT is root -X. Idempotent: re-running re-applies the same poses.
///
///   unity command script run --file AgentScripts/ArrangeForUserSide.cs --entry ArrangeForUserSide.Apply
/// </summary>
public static class ArrangeForUserSide
{
    const float Depth = 0.62f, Drop = 0.30f;
    static readonly Vector3 Eye = new Vector3(0f, Drop, Depth);

    static List<Transform> _all;
    static Transform Find(string name) => _all.FirstOrDefault(t => t != null && t.name == name);
    static Transform FindUnder(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    /// <summary>Local rotation that turns a child's readable/pressable face (local -Z) toward the eye.</summary>
    static Quaternion FaceEye(Vector3 localPos)
    {
        Vector3 away = localPos - Eye; away.y = 0f;
        return Quaternion.LookRotation(away.normalized, Vector3.up);
    }

    static void Place(StringBuilder sb, string name, Vector3 pos, Quaternion? rot = null)
    {
        var t = Find(name);
        if (t == null) { sb.AppendLine($"SKIP (not found): {name}"); return; }
        Undo.RecordObject(t, "Arrange " + name);
        t.localPosition = pos;
        if (rot.HasValue) t.localRotation = rot.Value;
        EditorUtility.SetDirty(t);
        float reach = Vector3.Distance(pos, Eye);
        sb.AppendLine($"{name} -> {pos}  ({reach:F2} m from the eye)");
    }

    public static string Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        _all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();
        var ws = Find("WorkspaceRoot");
        if (ws == null) return "WorkspaceRoot not found";

        // 1. Sit a little further back so the near edge (stamps, buttons) is not in the user's lap.
        var placement = Object.FindAnyObjectByType<WorkspacePlacement>(FindObjectsInactive.Include);
        if (placement != null)
        {
            Undo.RecordObject(placement, "placement depth");
            placement.offDeskDepth = Depth;
            placement.offDeskDrop = Drop;
            placement.offDeskSide = 0f;
            EditorUtility.SetDirty(placement);
            sb.AppendLine($"WorkspacePlacement depth={Depth} drop={Drop}");
        }

        // 2. Task board: off to the user's LEFT (root +X), at eye level, angled toward the user.
        //    It keeps its FaceUser billboard at runtime; the static rotation is the same pose.
        var boardPos = new Vector3(0.46f, 0.36f, 0.38f);
        Place(sb, "TaskBoard", boardPos, FaceEye(boardPos));

        // 3. Artifact panel: user's RIGHT, above the inbox, angled toward the user (no billboard:
        //    it carries poke buttons, which must not rotate under the finger).
        var panelPos = new Vector3(-0.50f, 0.40f, 0.30f);
        Place(sb, "ArtifactPanel", panelPos, FaceEye(panelPos));

        // 4. The user's own approve / reject stamps: front and centre, either hand.
        Place(sb, "StampApprove", new Vector3(-0.13f, 0.02f, 0.40f));
        Place(sb, "StampReject", new Vector3(0.13f, 0.02f, 0.40f));

        // 5. Focus / Explain poke buttons: user's right, in reach, pressable face toward the user.
        foreach (var (name, pos) in new[] { ("FocusToggle", new Vector3(-0.42f, 0.05f, 0.37f)),
                                             ("ExplainToggle", new Vector3(-0.31f, 0.05f, 0.42f)) })
        {
            Place(sb, name, pos, FaceEye(pos));
            var btn = Find(name);
            var label = btn != null ? FindUnder(btn, "ButtonLabel") : null;
            if (label != null)
            {
                Undo.RecordObject(label, "label facing");
                label.localRotation = Quaternion.identity;   // same facing as the button's front (-Z)
                EditorUtility.SetDirty(label);
            }
        }

        // 6. Where an agent stands to hand over a decision paper: on the user's side of the desks,
        //    clear of the factory line, about half a metre from the eye.
        Place(sb, "DeliveryZone", new Vector3(-0.20f, 0f, 0.17f));

        // 7. Clear decor out of the delivery spot and the stamp area.
        Place(sb, "Decor_CoffeeMug", new Vector3(-0.47f, 0f, 0.17f));
        Place(sb, "Collab_PackageBox", new Vector3(0.47f, 0f, 0.20f));
        Place(sb, "Doc_ApprovalStamp", new Vector3(0.25f, 0f, 0.31f));
        Place(sb, "Doc_SignaturePen", new Vector3(0.28f, 0f, 0.29f));

        // 8. Desk-side card controls (Pause/Resume, Priority) next to "+ New task" on the board.
        //    The per-card pokes are 1.8 cm targets on a grabbable card, so they are switched off.
        sb.Append(WireCardConsole());

        // 9. Development leftovers out of the demo: the orange grab-test cube and the grab readout.
        var cube = Find("[BuildingBlock] Grabbable Item");
        if (cube != null && cube.gameObject.activeSelf)
        {
            Undo.RecordObject(cube.gameObject, "hide test cube");
            cube.gameObject.SetActive(false);
            sb.AppendLine("grab-test cube deactivated (re-enable in the hierarchy if needed)");
        }
        foreach (var mb in _all.Where(t => t != null).SelectMany(t => t.GetComponents<MonoBehaviour>()))
            if (mb != null && mb.GetType().Name == "GrabReadout" && mb.enabled)
            {
                Undo.RecordObject(mb, "disable GrabReadout");
                mb.enabled = false; EditorUtility.SetDirty(mb);
                sb.AppendLine("GrabReadout disabled (debug only; it overwrote the status text)");
            }

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        sb.AppendLine("scene saved=" + saved);
        return sb.ToString();
    }

    static string WireCardConsole()
    {
        var sb = new StringBuilder();
        var board = Find("TaskBoard");
        var newTask = board != null ? FindUnder(board, "NewTaskButton") : null;
        var presenter = board != null ? board.GetComponent<BoardPresenter>() : null;
        if (board == null || newTask == null || presenter == null) return "SKIP card console: board/NewTaskButton/presenter missing\n";

        var console = board.GetComponent<CardActionConsole>();
        if (console == null) console = Undo.AddComponent<CardActionConsole>(board.gameObject);
        console.presenter = presenter;

        // Remove any previous copies so re-running does not stack buttons.
        foreach (var n in new[] { "PauseResumeButton", "PriorityButton", "CardConsoleLabel" })
        {
            var old = FindUnder(board, n);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        float step = 0.085f;   // one button pitch along the board's local X
        var specs = new[] { ("PauseResumeButton", "Pause /\nResume", "PauseResume", 1f),
                            ("PriorityButton", "Priority", "CyclePriority", 2f) };
        foreach (var (name, text, method, slot) in specs)
        {
            var go = Object.Instantiate(newTask.gameObject, newTask.parent);
            Undo.RegisterCreatedObjectUndo(go, "card console button");
            go.name = name;
            go.transform.localPosition = newTask.localPosition + new Vector3(slot * step, 0f, 0f);
            go.transform.localRotation = newTask.localRotation;
            go.transform.localScale = newTask.localScale;

            var label = FindUnder(go.transform, "ButtonLabel");
            var tmp = label != null ? label.GetComponent<TMP_Text>() : null;
            if (tmp != null) { tmp.text = text; EditorUtility.SetDirty(tmp); }

            int rewired = 0;
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != "InteractableUnityEventWrapper") continue;
                var so = new SerializedObject(mb);
                var calls = so.FindProperty("_whenSelect.m_PersistentCalls.m_Calls");
                if (calls == null) continue;
                for (int i = 0; i < calls.arraySize; i++)
                {
                    var c = calls.GetArrayElementAtIndex(i);
                    c.FindPropertyRelative("m_Target").objectReferenceValue = console;
                    c.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue =
                        typeof(CardActionConsole).AssemblyQualifiedName;
                    c.FindPropertyRelative("m_MethodName").stringValue = method;
                    c.FindPropertyRelative("m_Mode").intValue = 1;       // Void
                    c.FindPropertyRelative("m_CallState").intValue = 2;  // RuntimeOnly
                    rewired++;
                }
                so.ApplyModifiedProperties();
            }
            sb.AppendLine($"{name} at {go.transform.localPosition} -> CardActionConsole.{method} (calls rewired={rewired})");
        }

        // A line under the buttons that says which card they act on.
        var srcLabel = FindUnder(newTask, "ButtonLabel");
        if (srcLabel != null)
        {
            var lab = Object.Instantiate(srcLabel.gameObject, board);
            Undo.RegisterCreatedObjectUndo(lab, "card console label");
            lab.name = "CardConsoleLabel";
            // Keep the label's world size/orientation as it had under the button, then move it.
            lab.transform.rotation = srcLabel.rotation;
            var ls = srcLabel.lossyScale;
            lab.transform.localScale = new Vector3(ls.x / board.lossyScale.x, ls.y / board.lossyScale.y, ls.z / board.lossyScale.z);
            lab.transform.position = newTask.TransformPoint(Vector3.zero) + board.TransformVector(new Vector3(1.5f * step, -0.05f, 0f));
            var tmp = lab.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = "Grab a card to select it";
                tmp.enableAutoSizing = false;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                console.selectedLabel = tmp;
                EditorUtility.SetDirty(tmp);
            }
            sb.AppendLine("CardConsoleLabel at board-local " + lab.transform.localPosition);
        }
        EditorUtility.SetDirty(console);

        // Per-card pokes off (the console replaces them).
        var template = FindUnder(board, "CardTemplate");
        if (template != null)
            foreach (var n in new[] { "SH_PokePauseResume", "SH_PokePriority" })
            {
                var p = FindUnder(template, n);
                if (p != null && p.gameObject.activeSelf)
                {
                    Undo.RecordObject(p.gameObject, "disable per-card poke");
                    p.gameObject.SetActive(false);
                    sb.AppendLine("per-card " + n + " deactivated");
                }
            }
        return sb.ToString();
    }
}
