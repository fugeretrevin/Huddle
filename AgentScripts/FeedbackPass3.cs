using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Documents;

/// <summary>
/// Fixes from the on-head pass of 2026-11-12 (night). Idempotent; saves the scene.
///   1. The paper the Researcher carries was not visible: it was parented to a rig bone (inside a
///      100x-scaled armature, low on the chest, 6 cm across). It now rides on a hold point on the
///      agent's root, held out beside the chest at about 8 cm.
///   2. Task-card text floated about a centimetre in front of the card (so it looked "off" the card
///      in stereo). It now sits on the card's printed surface.
///   3. The task board overlapped the Tester's station. Moved further to the user's left and
///      toward the user.
///   4. Distance grab is only on the core items: task cards, the decision paper and the two stamps.
///      It was also on thirteen decor props.
/// (Keys not pressing was a script bug, fixed in PokeVisualRestGuard.cs.)
///   unity command script run --file AgentScripts/FeedbackPass3.cs --entry FeedbackPass3.Apply
/// </summary>
public static class FeedbackPass3
{
    static readonly Vector3 Eye = new Vector3(0f, 0.30f, 0.62f);

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

    public static string Apply()
    {
        var all = All();
        var sb = new StringBuilder();
        var ws = all.First(t => t.name == "WorkspaceRoot");

        // ── 1. carried paper ──
        var doc = all.FirstOrDefault(t => t.name == "DecisionDocument");
        var researcher = all.FirstOrDefault(t => t.name == "Agent_Researcher");
        var held = doc != null ? doc.GetComponent<HeldPaperController>() : null;
        if (held != null && researcher != null)
        {
            var hold = researcher.Find("PaperHoldPoint");
            if (hold == null)
            {
                var go = new GameObject("PaperHoldPoint");
                Undo.RegisterCreatedObjectUndo(go, "paper hold point");
                go.transform.SetParent(researcher, false);
                hold = go.transform;
            }
            // Agent front is local +Z. Held out in front of the chest, clear of the body and arms.
            hold.localPosition = new Vector3(0.055f, 0.125f, 0.13f);   // beside the chest: never in front of the face
            hold.localRotation = Quaternion.identity;
            hold.localScale = Vector3.one;
            Undo.RecordObject(held, "paper hold");
            held.agentHoldPoint = hold;
            held.heldScale = 0.24f;                                  // 34 cm sheet -> about 8 cm in the hand
            held.heldLocalEuler = new Vector3(-12f, 180f, 0f);       // printed side to the user, leaning back a little
            EditorUtility.SetDirty(held);
            sb.AppendLine($"paper: carried on {researcher.name}/PaperHoldPoint at {hold.localPosition}, world size about {0.34f * held.heldScale * 100f:F0} cm (hold point lossy scale {hold.lossyScale.x:F2})");
        }
        else sb.AppendLine("SKIP paper: DecisionDocument / Agent_Researcher / HeldPaperController not found");

        // ── 2. card text on the card surface ──
        var card = all.FirstOrDefault(t => t.name == "CardTemplate");
        if (card != null)
        {
            // Card-local metres. The paper body's front face is at z = +1.0 mm and the role header
            // strip's at +0.3 mm (measured from the meshes); the reader is on -Z.
            var layout = new (string name, Vector3 pos)[]
            {
                ("SH_Role",     new Vector3(-0.0090f,  0.0212f, -0.0003f)),   // on the header strip
                ("SH_Priority", new Vector3( 0.0180f,  0.0212f, -0.0003f)),
                ("Title",       new Vector3( 0.0000f,  0.0040f,  0.0004f)),   // on the paper body
                ("SH_State",    new Vector3( 0.0000f, -0.0095f,  0.0004f)),
                ("SH_Source",   new Vector3( 0.0000f, -0.0172f,  0.0004f)),
                ("SH_IdRev",    new Vector3( 0.0120f, -0.0225f,  0.0004f)),
            };
            foreach (var (name, pos) in layout)
            {
                var t = card.Find(name);
                if (t == null) { sb.AppendLine("  card label not found: " + name); continue; }
                Undo.RecordObject(t, "card text depth");
                t.localPosition = pos;
                EditorUtility.SetDirty(t);
            }
            // Header labels must stay inside the 7 mm strip (y 18..25 mm).
            foreach (var n in new[] { "SH_Role", "SH_Priority" })
            {
                var tmp = card.Find(n) != null ? card.Find(n).GetComponent<TMP_Text>() : null;
                if (tmp == null) continue;
                Undo.RecordObject(tmp.rectTransform, "header box");
                var s = tmp.rectTransform.sizeDelta; tmp.rectTransform.sizeDelta = new Vector2(s.x, 0.62f);
                EditorUtility.SetDirty(tmp);
            }
            sb.AppendLine("card text: moved from 6.5 mm in front of the card onto its surface (0.6 mm proud)");
        }

        // ── 3. task board ──
        var board = all.FirstOrDefault(t => t.name == "TaskBoard");
        if (board != null)
        {
            var pos = new Vector3(0.58f, 0.36f, 0.48f);
            Vector3 away = pos - Eye; away.y = 0f;
            Undo.RecordObject(board, "board position");
            board.localPosition = pos;
            board.localRotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            EditorUtility.SetDirty(board);
            // Clearance from the Tester's station (x up to 0.40, z up to 0.13).
            Vector3 plane = Vector3.Cross(Vector3.up, away.normalized);
            Vector3 e1 = pos + plane * 0.33f, e2 = pos - plane * 0.33f;
            sb.AppendLine($"board: {pos}, {Vector3.Distance(pos, Eye):F2} m from the eye; frame edges at x={e1.x:F2},z={e1.z:F2} and x={e2.x:F2},z={e2.z:F2} (Tester's desk ends at x=0.40, z=0.13)");
        }

        // ── 4. distance grab only on core items ──
        var core = new HashSet<string> { "CardTemplate", "DecisionDocument", "StampApprove", "StampReject" };
        System.Type dgType = null;
        int removed = 0;
        foreach (var t in all)
        {
            if (t == null) continue;
            foreach (var mb in t.GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "DistanceHandGrabInteractable") continue;
                dgType = mb.GetType();
                if (core.Contains(t.name)) continue;
                Undo.DestroyObjectImmediate(mb);
                removed++;
                sb.AppendLine("  distance grab removed from " + t.name);
            }
        }
        // Clean any now-empty slots in re-register lists.
        foreach (var t in all)
        {
            if (t == null) continue;
            foreach (var mb in t.GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "ReRegisterInteractable") continue;
                var so = new SerializedObject(mb);
                var arr = so.FindProperty("interactables");
                if (arr == null || !arr.isArray) continue;
                for (int i = arr.arraySize - 1; i >= 0; i--)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == null) arr.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedProperties();
            }
        }
        int added = 0;
        if (dgType != null)
            foreach (var name in new[] { "StampApprove", "StampReject" })
            {
                var s = all.FirstOrDefault(x => x != null && x.name == name);
                if (s == null || s.GetComponent(dgType) != null) continue;
                var comp = Undo.AddComponent(s.gameObject, dgType) as MonoBehaviour;     // Reset() copies the sibling HandGrabInteractable's setup
                added++;
                // Re-register it with the others (interactables inactive at init do not self-register).
                var rr = s.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "ReRegisterInteractable");
                if (rr != null && comp != null)
                {
                    var so = new SerializedObject(rr);
                    var arr = so.FindProperty("interactables");
                    if (arr != null && arr.isArray)
                    {
                        arr.InsertArrayElementAtIndex(arr.arraySize);
                        arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = comp;
                        so.ApplyModifiedProperties();
                    }
                }
            }
        var left = All().Where(t => t != null && t.GetComponents<MonoBehaviour>().Any(m => m != null && m.GetType().Name == "DistanceHandGrabInteractable")).Select(t => t.name).ToList();
        sb.AppendLine($"distance grab: removed from {removed} objects, added to {added}; now on: {string.Join(", ", left)}");

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }

    /// <summary>Editor-only preview (never saved): put the paper where the Researcher carries it,
    /// with the Researcher standing at the hand-over spot, and frame it from the seated eye.</summary>
    public static string PreviewCarriedPaper()
    {
        var all = All();
        var ws = all.First(t => t.name == "WorkspaceRoot");
        var doc = all.First(t => t.name == "DecisionDocument");
        var researcher = all.First(t => t.name == "Agent_Researcher");
        var held = doc.GetComponent<HeldPaperController>();
        var spot = all.FirstOrDefault(t => t.name == "DeliveryZone");
        if (spot != null) researcher.position = spot.position;
        Vector3 eyeW = ws.TransformPoint(Eye);
        Vector3 to = eyeW - researcher.position; to.y = 0f;
        researcher.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);

        doc.gameObject.SetActive(true);
        foreach (var g in held.hideWhileHeld) if (g != null) g.SetActive(false);
        var details = doc.Find("Details"); if (details != null) details.gameObject.SetActive(false);
        doc.SetParent(held.agentHoldPoint, false);
        doc.localPosition = Vector3.zero;
        doc.localRotation = Quaternion.Euler(held.heldLocalEuler);
        Vector3 p = held.agentHoldPoint.lossyScale;
        doc.localScale = new Vector3(held.heldScale / p.x, held.heldScale / p.y, held.heldScale / p.z);

        var sv = SceneView.lastActiveSceneView;
        sv.orthographic = false; sv.cameraSettings.fieldOfView = 60f;
        Vector3 look = researcher.position + Vector3.up * 0.16f;
        float dist = (look - eyeW).magnitude;
        sv.LookAtDirect(look, Quaternion.LookRotation(look - eyeW, Vector3.up), dist * Mathf.Sin(30f * Mathf.Deg2Rad));
        sv.Repaint();
        var card = doc.Find("Card").GetComponent<Renderer>();
        return $"paper world size {card.bounds.size.x * 100:F1} x {card.bounds.size.y * 100:F1} cm at root-local {ws.InverseTransformPoint(doc.position)}; reload the scene afterwards";
    }
}
