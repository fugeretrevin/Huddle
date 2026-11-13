using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Documents;

/// <summary>Read-only probe for the 2026-11-12 night on-head feedback: carried paper, key press
/// parts, board position, card text vs card face.</summary>
public static class FeedbackProbe2
{
    static string V(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

    public static string Run()
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var ws = all.First(t => t.name == "WorkspaceRoot");
        var sb = new StringBuilder();

        // 1. carried paper
        var doc = all.First(t => t.name == "DecisionDocument");
        var held = doc.GetComponent<HeldPaperController>();
        var hp = held.agentHoldPoint;
        sb.AppendLine("--- paper ---");
        sb.AppendLine($"heldScale={held.heldScale} heldLocalEuler={V(held.heldLocalEuler)} holdPoint={(hp != null ? hp.name : "null")}");
        if (hp != null)
        {
            var agent = hp.GetComponentInParent<MuseDesk.Agents.AgentView>(true);
            sb.AppendLine($"holdPoint lossy={V(hp.lossyScale)} rootLocal={V(ws.InverseTransformPoint(hp.position))} agent={(agent != null ? agent.name : "?")} agentRootLocal={(agent != null ? V(ws.InverseTransformPoint(agent.transform.position)) : "?")} relToAgent={(agent != null ? V(agent.transform.InverseTransformPoint(hp.position)) : "?")}");
            string chain = ""; var t = hp; int n = 0;
            while (t != null && n++ < 9) { chain = t.name + "(" + t.localScale.x.ToString("F2") + ")/" + chain; t = t.parent; }
            sb.AppendLine("chain: " + chain);
        }
        sb.AppendLine("hideWhileHeld: " + string.Join(",", (held.hideWhileHeld ?? new GameObject[0]).Select(g => g != null ? g.name : "null")));
        foreach (Transform c in doc)
        {
            var rs = c.GetComponentsInChildren<Renderer>(true);
            sb.AppendLine($"  doc child {c.name} active={c.gameObject.activeSelf} renderers={rs.Length} enabled={rs.Count(r => r.enabled)} lp={V(c.localPosition)} ls={V(c.localScale)}");
        }

        // 2. a key's parts
        sb.AppendLine("--- key (StartKey) ---");
        var key = all.FirstOrDefault(t => t.name == "StartKey") ?? all.First(t => t.name == "FocusToggle");
        foreach (var c in key.GetComponentsInChildren<Transform>(true))
        {
            var comps = string.Join(",", c.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name + ((x is Behaviour b && !b.enabled) || (x is Renderer r && !r.enabled) ? "[off]" : "")));
            sb.AppendLine($"  {c.name} active={c.gameObject.activeSelf} parent={c.parent.name} lp={V(c.localPosition)} ls={V(c.localScale)} [{comps}]");
        }

        // 3. board
        sb.AppendLine("--- board ---");
        var board = all.First(t => t.name == "TaskBoard");
        sb.AppendLine($"TaskBoard rootLocal={V(ws.InverseTransformPoint(board.position))} le={V(board.localEulerAngles)} ls={V(board.localScale)}");
        foreach (var n in new[] { "ResearchStation", "DevStation", "QAStation", "AssignDock", "StampApprove", "StampReject", "PhysicsTray", "OutboxTray", "FiledKey", "ArchiveReadout" })
        {
            var t = all.FirstOrDefault(x => x.name == n);
            if (t != null) sb.AppendLine($"  {n} rootLocal={V(ws.InverseTransformPoint(t.position))}");
        }
        var brs = board.GetComponentsInChildren<Renderer>(false);
        Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
        foreach (var r in brs) { var b = r.bounds; mn = Vector3.Min(mn, ws.InverseTransformPoint(b.min)); mx = Vector3.Max(mx, ws.InverseTransformPoint(b.max)); mn = Vector3.Min(mn, ws.InverseTransformPoint(b.max)); mx = Vector3.Max(mx, ws.InverseTransformPoint(b.min)); }
        sb.AppendLine($"  board bounds (root-local, approx) x[{mn.x:F2}..{mx.x:F2}] y[{mn.y:F2}..{mx.y:F2}] z[{mn.z:F2}..{mx.z:F2}]");

        // 4. card
        sb.AppendLine("--- card template ---");
        var card = all.First(t => t.name == "CardTemplate");
        sb.AppendLine($"card ls={V(card.localScale)}");
        foreach (Transform c in card)
        {
            var tmp = c.GetComponent<TMP_Text>();
            var mf = c.GetComponent<MeshFilter>();
            string extra = tmp != null ? $" TEXT box={tmp.rectTransform.sizeDelta} font={tmp.fontSize:F1} auto={tmp.enableAutoSizing}[{tmp.fontSizeMin:F1}-{tmp.fontSizeMax:F1}] pivot={tmp.rectTransform.pivot} align={tmp.alignment}" : "";
            var rs = c.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.GetComponent<TMP_Text>() == null).ToList();
            if (rs.Count > 0)
            {
                Vector3 a = Vector3.positiveInfinity, z = Vector3.negativeInfinity;
                foreach (var r in rs)
                {
                    var f = r.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null) continue;
                    var b = f.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var l = card.InverseTransformPoint(r.transform.TransformPoint(p));
                        a = Vector3.Min(a, l); z = Vector3.Max(z, l);
                    }
                }
                extra += $" MESH x[{a.x * 1000:F0}..{z.x * 1000:F0}] y[{a.y * 1000:F0}..{z.y * 1000:F0}] z[{a.z * 1000:F0}..{z.z * 1000:F0}]mm rend={rs.Count} on={rs.Count(r => r.enabled && r.gameObject.activeInHierarchy)}";
            }
            sb.AppendLine($"  {c.name} active={c.gameObject.activeSelf} lp=({c.localPosition.x * 1000:F1},{c.localPosition.y * 1000:F1},{c.localPosition.z * 1000:F1})mm ls={c.localScale.x:F3} le={V(c.localEulerAngles)}{extra}");
        }

        // 5. distance grab
        sb.AppendLine("--- distance grab on ---");
        foreach (var t in all)
            if (t.GetComponents<MonoBehaviour>().Any(m => m != null && m.GetType().Name == "DistanceHandGrabInteractable"))
                sb.AppendLine("  " + t.name);
        return sb.ToString();
    }
}
