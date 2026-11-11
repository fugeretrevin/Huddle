using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using MuseDesk.Documents;
using MuseDesk.Networking;
using MuseDesk.Workspace;

/// <summary>
/// Makes the delivered decision paper physically grabbable by replicating the scene's proven
/// grab rig (BoxCollider + kinematic Rigidbody + Oculus.Interaction.Grabbable +
/// HandGrabInteractable, copied from the working "Grabbable Item") onto the DecisionDocument
/// root, then adds HeldPaperController so a grab expands the paper and stops FaceUser/placer
/// from fighting the hand. Idempotent: re-running is a no-op once the rig is present.
/// Approvals are unaffected — they still go only through the poke buttons -> backend.
/// </summary>
public static class AddGrabbablePaper
{
    static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(n)).FirstOrDefault(t => t != null);

    static Component Find(GameObject go, string typeName)
    {
        var t = T(typeName);
        return t != null ? go.GetComponent(t) : null;
    }

    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var sb = new StringBuilder();

        var doc = roots.FirstOrDefault(r => r.name == "DecisionDocument");
        if (doc == null) return "ERROR: no DecisionDocument (run BuildDecisionDoc first)";

        if (Find(doc, "Oculus.Interaction.HandGrab.HandGrabInteractable") != null
            || doc.GetComponent<HeldPaperController>() != null)
            return "grabbable paper already present; skipping";

        var src = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                       .FirstOrDefault(t => t.name.Contains("Grabbable Item"))?.gameObject;
        if (src == null) return "ERROR: source 'Grabbable Item' grab rig not found";

        var services = roots.FirstOrDefault(r => r.name == "AppServices");
        var delivery = services ? services.GetComponent<DocumentDeliveryController>() : null;
        var face = doc.GetComponent<FaceUser>();
        var placer = doc.GetComponent<PlaceInFrontOnStart>();

        // ── kinematic body so the sheet never falls (explicit null check: '??' bypasses
        //    Unity's overloaded == and would treat an absent component as non-null) ──
        var rb = doc.GetComponent<Rigidbody>();
        if (rb == null) rb = doc.AddComponent<Rigidbody>();
        rb.useGravity = false; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None;

        // ── trigger collider sized to the card (card quad is ~0.34 x 0.26 in doc-local metres) ──
        var bc = doc.GetComponent<BoxCollider>();
        if (bc == null) bc = doc.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.center = new Vector3(0f, 0f, 0.01f);
        bc.size = new Vector3(0.36f, 0.28f, 0.06f);

        // ── copy the two SDK components from the proven rig, then re-point their rigidbody ──
        var srcGrab = Find(src, "Oculus.Interaction.Grabbable");
        var srcHand = Find(src, "Oculus.Interaction.HandGrab.HandGrabInteractable");
        if (srcGrab == null || srcHand == null)
            return "ERROR: source rig missing Grabbable/HandGrabInteractable";

        if (!ComponentUtility.CopyComponent(srcGrab) || !ComponentUtility.PasteComponentAsNew(doc))
            return "ERROR: failed to copy Grabbable";
        if (!ComponentUtility.CopyComponent(srcHand) || !ComponentUtility.PasteComponentAsNew(doc))
            return "ERROR: failed to copy HandGrabInteractable";

        var grab = Find(doc, "Oculus.Interaction.Grabbable");
        var hand = Find(doc, "Oculus.Interaction.HandGrab.HandGrabInteractable");
        RepointRigidbody(grab, rb, sb);
        RepointRigidbody(hand, rb, sb);

        // ── controller: grab -> expand + stop fighting the hand ──
        var held = doc.AddComponent<HeldPaperController>();
        held.grabbable = grab;
        held.face = face;
        held.placer = placer;
        held.delivery = delivery;
        sb.AppendLine($"HeldPaperController wired (grab={grab != null} face={face != null} " +
                      $"placer={placer != null} delivery={delivery != null})");

        EditorUtility.SetDirty(doc);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("grabbable paper built + saved");
        return sb.ToString();
    }

    static void RepointRigidbody(Component c, Rigidbody rb, StringBuilder sb)
    {
        if (c == null) { sb.AppendLine("  (null component, skipped repoint)"); return; }
        var f = c.GetType().GetField("_rigidbody",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (f != null) { f.SetValue(c, rb); sb.AppendLine($"  {c.GetType().Name}._rigidbody -> self"); }
        else sb.AppendLine($"  {c.GetType().Name}: _rigidbody field not found");
    }
}
