using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using MuseDesk.Agents;
using MuseDesk.Workspace;

/// <summary>
/// On-device feedback pass #1 (readability + facing + rig):
///  - Decision paper: readable (FaceUser.forward=false, since TMP's readable face is local -Z),
///    placer stops fighting rotation, bigger legible fonts + bigger card, grab collider solid
///    (match the proven cube).
///  - Agents: drop the persistent body billboard (they only turn while walking up); keep name
///    tags / alert facing the user but un-mirrored; tuck the arms so hands hang naturally.
/// Idempotent-ish: safe to re-run.
/// </summary>
public static class PolishV3
{
    // ── tunables (edit + re-run; verify via scene_view capture) ──
    const float CardW = 0.34f, CardH = 0.27f;

    public static string Go()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var sb = new StringBuilder();

        // ════ DECISION PAPER ════
        var doc = roots.FirstOrDefault(r => r.name == "DecisionDocument");
        if (doc == null) sb.AppendLine("WARN: no DecisionDocument");
        else
        {
            var face = doc.GetComponent<FaceUser>();
            if (face) { face.forward = false; face.enabled = true; sb.AppendLine("paper FaceUser.forward=false"); }
            var pf = doc.GetComponent<PlaceInFrontOnStart>();
            if (pf) { pf.faceUser = false; sb.AppendLine("paper placer faceUser=false (FaceUser owns rotation)"); }
            var bc = doc.GetComponent<BoxCollider>();
            if (bc) { bc.isTrigger = false; sb.AppendLine("paper grab collider solid (match proven cube)"); }

            // bigger card
            var card = doc.transform.Find("Card");
            if (card) card.localScale = new Vector3(CardW, CardH, 1f);

            // legible fonts — role by child name (card is 0.34 x 0.27; fill it)
            SetFont(doc, "Status", 3.0f, new Vector3(0f, 0.118f, 0f), 32, 3);
            SetFont(doc, "Badge",  3.0f, new Vector3(0f, 0.092f, 0f), 32, 3);
            SetFont(doc, "Title",  6.0f, new Vector3(0f, 0.055f, 0f), 31, 5);
            SetFont(doc, "Summary",4.3f, new Vector3(0f, 0.006f, 0f), 31, 6);
            SetFont(doc, "Reco",   4.0f, new Vector3(0f,-0.036f, 0f), 31, 4);
            SetFont(doc, "Body",   3.3f, new Vector3(-0.145f,-0.060f, 0f), 29, 14, TextAlignmentOptions.TopLeft);
            SetFont(doc, "Answer", 3.3f, new Vector3(0f,-0.108f, 0f), 31, 5);

            // action buttons: scale up + reposition onto the card, bigger labels.
            LayoutButton(doc, "Btn_Approve",  new Vector3(-0.108f, -0.108f, -0.004f));
            LayoutButton(doc, "Btn_Reject",   new Vector3( 0.108f, -0.108f, -0.004f));
            LayoutButton(doc, "Btn_Ask_why",  new Vector3( 0.000f, -0.108f, -0.004f));
            EditorUtility.SetDirty(doc);
        }

        // ════ AGENTS ════
        var sleeve = roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
            .FirstOrDefault(x => x.sharedMaterial && x.sharedMaterial.name == "AgentArm")?.sharedMaterial;
        foreach (var v in roots.SelectMany(r => r.GetComponentsInChildren<AgentView>(true)))
        {
            // Drop the persistent body billboard; movement handles facing on walk-up.
            var bodyFace = v.GetComponent<FaceUser>();
            if (bodyFace) { UnityEngine.Object.DestroyImmediate(bodyFace); sb.AppendLine($"{v.agentId}: body FaceUser removed"); }

            // Keep label/alert facing the user, but un-mirror the name tag.
            foreach (var f in v.GetComponentsInChildren<FaceUser>(true))
                if (f.GetComponent<TMP_Text>() != null) { f.forward = false; EditorUtility.SetDirty(f); }

            // Tuck arms: hang down close to the body (were jutting straight out).
            TuckArm(v.armL, -0.03f, sb, v.agentId + ".L");
            TuckArm(v.armR,  0.03f, sb, v.agentId + ".R");
            EditorUtility.SetDirty(v);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved");
        return sb.ToString();
    }

    static void LayoutButton(GameObject doc, string name, Vector3 lp)
    {
        var btn = doc.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        if (btn == null) return;
        btn.localScale = Vector3.one * 0.62f;
        btn.localPosition = lp;
        var lbl = btn.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Label");
        if (lbl != null)
        {
            lbl.fontSize = 8.0f;
            lbl.rectTransform.sizeDelta = new Vector2(44, 12);
            lbl.transform.localPosition = new Vector3(0f, 0f, -0.012f);
            EditorUtility.SetDirty(lbl);
        }
    }

    static void TuckArm(Transform pivot, float x, StringBuilder sb, string who)
    {
        if (pivot == null) { sb.AppendLine($"{who}: no arm"); return; }
        pivot.localPosition = new Vector3(x, 0.055f, 0.005f);
        pivot.localRotation = Quaternion.Euler(6f, 0f, 0f);   // tiny forward hang, not straight out
        var mesh = pivot.Find("ArmMesh");
        if (mesh) { mesh.localPosition = new Vector3(0f, -0.030f, 0f); mesh.localScale = new Vector3(0.010f, 0.030f, 0.010f); }
        var hand = pivot.Find("Hand");
        if (hand) { hand.localPosition = new Vector3(0f, -0.060f, 0f); hand.localScale = Vector3.one * 0.013f; }
    }

    static void SetFont(GameObject doc, string name, float size, Vector3 lp, float w, float h,
                        TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var t = doc.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(x => x.name == name);
        if (t == null) return;
        t.fontSize = size;
        t.transform.localPosition = lp;
        t.alignment = align;
        t.rectTransform.sizeDelta = new Vector2(w, h);
        EditorUtility.SetDirty(t);
    }
}
