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
/// Builds the physical SELF-STAMP approval: two grabbable desk stamps (green APPROVE / red REJECT) the
/// user picks up and presses onto the decision paper to drive the REAL approval path (StampTool ->
/// SessionController.Approve/RejectPending). Adds a StampZone child on the DecisionDocument the ink head
/// presses onto. Grab rig is copied from the proven "[BuildingBlock] Grabbable Item" cube exactly like
/// AddGrabbablePaper (trigger collider, repoint _rigidbody + _pointableElement, ReRegisterInteractable).
/// Idempotent: removes prior stamps + zone, then rebuilds. Poke buttons on the paper are left intact as a
/// fallback.
/// </summary>
public static class BuildSelfStamp
{
    static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(n)).FirstOrDefault(t => t != null);
    static Component FindComp(GameObject go, string typeName)
    {
        var t = T(typeName);
        return t != null ? go.GetComponent(t) : null;
    }

    public static string Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();
        try
        {
            var roots = scene.GetRootGameObjects();
            var all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToArray();

            var ws = all.FirstOrDefault(g => g.name == "WorkspaceRoot");
            if (ws == null) return "ERROR: WorkspaceRoot not found.";
            var doc = roots.FirstOrDefault(r => r.name == "DecisionDocument");
            if (doc == null) return "ERROR: DecisionDocument not found (run AddGrabbablePaper/BuildDecisionDoc first).";
            var controller = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            if (controller == null) return "ERROR: SessionController not found.";

            var cube = all.FirstOrDefault(g => g.name.Contains("Grabbable Item"));
            if (cube == null) return "ERROR: '[BuildingBlock] Grabbable Item' grab source not found.";
            var srcGrab = FindComp(cube, "Oculus.Interaction.Grabbable");
            var srcHand = FindComp(cube, "Oculus.Interaction.HandGrab.HandGrabInteractable");
            if (srcGrab == null || srcHand == null) return "ERROR: cube missing Grabbable/HandGrabInteractable.";

            // ── idempotency ──
            foreach (var n in new[] { "StampApprove", "StampReject" })
            {
                var old = ws.transform.Find(n);
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            var oldZone = doc.transform.Find("StampZone");
            if (oldZone != null) UnityEngine.Object.DestroyImmediate(oldZone.gameObject);

            // ── StampZone on the paper (front face, lower-centre; card face is local -Z) ──
            var zone = new GameObject("StampZone");
            zone.transform.SetParent(doc.transform, false);
            zone.transform.localPosition = new Vector3(0f, -0.06f, -0.015f);
            zone.transform.localRotation = Quaternion.identity;
            // Subtle target marker (thin disc). Small + faint to avoid adding clutter.
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "ZoneMarker";
            disc.transform.SetParent(zone.transform, false);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // flat against the paper
            disc.transform.localScale = new Vector3(0.08f, 0.002f, 0.08f);
            StripCollider(disc);
            disc.GetComponent<Renderer>().sharedMaterial = LitMat(new Color(0.85f, 0.80f, 0.55f), 0.12f);
            sb.AppendLine("StampZone added on DecisionDocument (local -0.06y / -0.015z).");

            // ── two stamps ──
            BuildStamp(ws.transform, "StampApprove", StampTool.StampAction.Approve,
                       new Color(0.20f, 0.72f, 0.40f), new Vector3(-0.22f, 0.02f, -0.32f),
                       controller, zone.transform, srcGrab, srcHand, sb);
            BuildStamp(ws.transform, "StampReject", StampTool.StampAction.Reject,
                       new Color(0.86f, 0.25f, 0.20f), new Vector3(0.22f, 0.02f, -0.32f),
                       controller, zone.transform, srcGrab, srcHand, sb);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            sb.AppendLine("SAVED scene + assets.");
            return sb.ToString();
        }
        catch (Exception e)
        {
            return sb.ToString() + $"\nERROR: {e.GetType().Name}: {e.Message}\n{e.StackTrace}";
        }
    }

    static void BuildStamp(Transform parent, string name, StampTool.StampAction action, Color headColor,
        Vector3 localPos, SessionController controller, Transform zone,
        Component srcGrab, Component srcHand, StringBuilder sb)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPos;
        root.transform.localRotation = Quaternion.identity;

        // Handle (dark) above, colored head below, ink tip at the bottom.
        var handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        handle.name = "Handle";
        handle.transform.SetParent(root.transform, false);
        handle.transform.localPosition = new Vector3(0f, 0.035f, 0f);
        handle.transform.localScale = new Vector3(0.018f, 0.022f, 0.018f);
        StripCollider(handle);
        handle.GetComponent<Renderer>().sharedMaterial = LitMat(new Color(0.24f, 0.21f, 0.19f));

        var head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        head.name = "Head";
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0f, 0.008f, 0f);
        head.transform.localScale = new Vector3(0.03f, 0.012f, 0.03f);
        StripCollider(head);
        head.GetComponent<Renderer>().sharedMaterial = LitMat(headColor, 0.18f);

        var ink = new GameObject("InkHead");
        ink.transform.SetParent(root.transform, false);
        ink.transform.localPosition = new Vector3(0f, -0.006f, 0f);   // the inking face

        // Kinematic body (never falls) + trigger grab volume (ISDK grab needs isTrigger = true).
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;
        var bc = root.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.center = new Vector3(0f, 0.02f, 0f);
        bc.size = new Vector3(0.05f, 0.075f, 0.05f);

        var audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false; audio.spatialBlend = 1f;

        // Copy the proven grab rig + repoint to this stamp.
        ComponentUtility.CopyComponent(srcGrab); ComponentUtility.PasteComponentAsNew(root);
        ComponentUtility.CopyComponent(srcHand); ComponentUtility.PasteComponentAsNew(root);
        var grab = FindComp(root, "Oculus.Interaction.Grabbable");
        var hand = FindComp(root, "Oculus.Interaction.HandGrab.HandGrabInteractable");
        Repoint(grab, "_rigidbody", rb, sb);
        Repoint(hand, "_rigidbody", rb, sb);
        Repoint(hand, "_pointableElement", grab, sb);
        Repoint(hand, "<PointableElement>k__BackingField", null, sb);

        var rereg = root.AddComponent<ReRegisterInteractable>();
        rereg.interactables = new Behaviour[] { grab as Behaviour, hand as Behaviour };

        var tool = root.AddComponent<StampTool>();
        tool.grabbable = grab;
        tool.inkHead = ink.transform;
        tool.stampZone = zone;
        tool.controller = controller;
        tool.action = action;
        tool.headRenderer = head.GetComponent<Renderer>();
        tool.returnHome = true;

        EditorUtility.SetDirty(root);
        sb.AppendLine($"{name} built ({action}); grab={grab != null} hand={hand != null} at local {localPos}.");
    }

    // ── helpers ──
    static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c != null) UnityEngine.Object.DestroyImmediate(c);
    }

    static Material LitMat(Color c, float emission = 0f)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader);
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (emission > 0f && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", c * emission);
        }
        return m;
    }

    static void Repoint(Component c, string field, object value, StringBuilder sb)
    {
        if (c == null) { sb.AppendLine($"  (null component; skipped {field})"); return; }
        for (var t = c.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(field, BindingFlags.Instance | BindingFlags.Public |
                                      BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (f != null) { f.SetValue(c, value); return; }
        }
        sb.AppendLine($"  WARN: {c.GetType().Name}.{field} not found");
    }
}
