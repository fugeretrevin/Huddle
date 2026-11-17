using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>Read-only measurements for the fourth on-head pass: key depth, hand sockets, card
/// footprint vs slot pitch, and the board's header blocks vs their labels.</summary>
public static class FeedbackProbe4
{
    static string V(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

    static bool MeshBounds(Transform root, Transform space, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity; bool any = false;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.GetComponent<TMP_Text>() != null) continue;
            var f = r.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null) continue;
            var b = f.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var l = space.InverseTransformPoint(r.transform.TransformPoint(p));
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l); any = true;
            }
        }
        return any;
    }

    public static string Run()
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();

        sb.AppendLine("--- key parts in KEY-local units (surface plane z=0, user on -z) ---");
        foreach (var name in new[] { "StartKey", "NewTaskButton" })
        {
            var key = all.FirstOrDefault(t => t.name == name); if (key == null) continue;
            foreach (var part in new[] { "Cap3D", "Housing3D", "ButtonVisual" })
            {
                var p = key.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == part);
                if (p == null) continue;
                var own = p.GetComponent<MeshRenderer>();
                Vector3 mn = Vector3.zero, mx = Vector3.zero; bool ok = false;
                if (own != null && own.GetComponent<MeshFilter>() != null && own.GetComponent<MeshFilter>().sharedMesh != null)
                {
                    var b = own.GetComponent<MeshFilter>().sharedMesh.bounds; mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity; ok = true;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var l = key.InverseTransformPoint(p.TransformPoint(c)); mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
                    }
                }
                sb.AppendLine($"{name}/{part}: lp={V(p.localPosition)} ls={V(p.localScale)} parent={p.parent.name} rendOn={(own != null && own.enabled)}" + (ok ? $" x[{mn.x:F2}..{mx.x:F2}] y[{mn.y:F2}..{mx.y:F2}] z[{mn.z:F3}..{mx.z:F3}]" : ""));
            }
            var label = key.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
            if (label != null) sb.AppendLine($"{name}/label z(key-local)={key.InverseTransformPoint(label.transform.position).z:F3} parent={label.transform.parent.name}");
        }

        sb.AppendLine("--- researcher sockets (relative to agent root, metres) ---");
        var agent = all.First(t => t.name == "Agent_Researcher");
        foreach (var t in agent.GetComponentsInChildren<Transform>(true))
            if (t.name.Contains("Attach") || t.name.StartsWith("Hand") || t.name == "PaperHoldPoint")
                sb.AppendLine($"{t.name}: rel={V(agent.InverseTransformPoint(t.position))} lossy={t.lossyScale.x:F1} parent={t.parent.name}");

        sb.AppendLine("--- board (BOARD-local metres) ---");
        var board = all.First(t => t.name == "TaskBoard");
        var so = new SerializedObject(board.GetComponent("BoardPresenter"));
        sb.AppendLine($"presenter cardSpacing={so.FindProperty("cardSpacing").vector2Value} columns={so.FindProperty("columns").intValue}");
        var card = board.Find("CardTemplate");
        if (MeshBounds(card, card, out var cmn, out var cmx))
            sb.AppendLine($"card template scale={card.localScale.x:F2}; mesh footprint in board units: w={(cmx.x - cmn.x) * card.localScale.x:F3} h={(cmx.y - cmn.y) * card.localScale.y:F3} (x[{cmn.x * card.localScale.x:F3}..{cmx.x * card.localScale.x:F3}] y[{cmn.y * card.localScale.y:F3}..{cmx.y * card.localScale.y:F3}]) z[{cmn.z * card.localScale.z:F4}..{cmx.z * card.localScale.z:F4}]");
        foreach (Transform c in board)
        {
            if (c.name.StartsWith("Region_") || c.name.StartsWith("Label_") || c.name == "Title" || c.name == "Backing" || c.name == "CardConsoleLabel" || c.name.EndsWith("Button") || c.name == "AssignDock")
            {
                var tmp = c.GetComponent<TMP_Text>();
                sb.AppendLine($"{c.name}: lp={V(c.localPosition)} ls={c.localScale.x:F3}" + (tmp != null ? $" TEXT='{tmp.text}' font={tmp.fontSize} box={tmp.rectTransform.sizeDelta} align={tmp.alignment}" : ""));
            }
        }
        var frame = board.Find("KanbanFrame_Visual");
        if (frame != null)
        {
            sb.AppendLine($"KanbanFrame_Visual lp={V(frame.localPosition)} le={V(frame.localEulerAngles)} ls={V(frame.localScale)}");
            foreach (var r in frame.GetComponentsInChildren<MeshRenderer>(true))
            {
                var f = r.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null) continue;
                var b = f.sharedMesh.bounds; Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    var p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var l = board.InverseTransformPoint(r.transform.TransformPoint(p)); mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
                }
                sb.AppendLine($"  frame mesh {r.name}: x[{mn.x:F3}..{mx.x:F3}] y[{mn.y:F3}..{mx.y:F3}] front z={mn.z:F4} back z={mx.z:F4} mat={(r.sharedMaterial != null ? r.sharedMaterial.name : "none")} submeshes={f.sharedMesh.subMeshCount}");
            }
            foreach (var t in frame.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<MeshRenderer>() == null && (t.name.StartsWith("Header") || t.name.StartsWith("Socket_") && t.name.EndsWith("_1") || t.name.StartsWith("ColumnDropZone") || t.name.Contains("Title")))
                    sb.AppendLine($"  frame anchor {t.name}: {V(board.InverseTransformPoint(t.position))}");
        }
        return sb.ToString();
    }
}
