using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using TMPro;
using MuseDesk.Agents;
using MuseDesk.Documents;
using MuseDesk.Workspace;

/// <summary>
/// On-device polish pass: readable text (white + dark outline, faces the user), dark panel
/// behind the decision document, grab/expand detail grouping + a Details button, exclamation
/// alert over the researcher, cuter characters (eyes), and an obvious grabbable cube.
/// </summary>
public static class PolishV2
{
    static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(n)).FirstOrDefault(t => t != null);

    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var sb = new StringBuilder();

        PolishDecisionDoc(roots, sb);
        PolishAgents(roots, sb);
        PolishGrabCube(roots, sb);
        PolishDiagnostics(roots, sb);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved");
        return sb.ToString();
    }

    // ───────────────────── decision document ─────────────────────
    static void PolishDecisionDoc(GameObject[] roots, StringBuilder sb)
    {
        var doc = roots.FirstOrDefault(r => r.name == "DecisionDocument");
        if (doc == null) { sb.AppendLine("no DecisionDocument"); return; }
        var delivery = roots.SelectMany(r => r.GetComponentsInChildren<DocumentDeliveryController>(true)).FirstOrDefault();
        var panel = doc.GetComponentInChildren<DecisionPanel>(true);

        // dark card
        var card = doc.transform.Find("Card");
        if (card != null)
        {
            card.localScale = new Vector3(0.34f, 0.26f, 1f);
            Tint(card.GetComponent<Renderer>(), new Color(0.10f, 0.11f, 0.14f));
        }
        // white + outlined text for everything on the doc
        foreach (var tmp in doc.GetComponentsInChildren<TMP_Text>(true)) Readable(tmp);

        // face the user (text reads from -Z, so point -Z at the user)
        AddFace(doc, false);

        // group detail (body + answer) so it only shows when expanded
        if (panel != null && doc.transform.Find("Details") == null)
        {
            var details = new GameObject("Details");
            details.transform.SetParent(doc.transform, false);
            foreach (var n in new[] { "Body", "Answer" })
            {
                var t = doc.transform.Find(n);
                if (t != null) t.SetParent(details.transform, true);
            }
            panel.detailsRoot = details;
            sb.AppendLine("detailsRoot grouped");
        }

        // Details button that expands (reliable poke)
        var actions = doc.transform.Find("Actions");
        var pokeSrc = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name.Contains("Poke Interaction"))?.gameObject
            ?? FindAnyPokeClone(roots);
        if (actions != null && delivery != null && pokeSrc != null && actions.Find("Btn_Details") == null)
            MakeButton(pokeSrc, actions, "Details", new Vector3(0f, -0.14f, 0f), delivery, "OnExpand", sb);

        sb.AppendLine("decision doc polished");
    }

    static GameObject FindAnyPokeClone(GameObject[] roots) =>
        roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name.StartsWith("Btn_"))?.gameObject;

    // ───────────────────── agents ─────────────────────
    static void PolishAgents(GameObject[] roots, StringBuilder sb)
    {
        foreach (var v in roots.SelectMany(r => r.GetComponentsInChildren<AgentView>(true)))
        {
            var bob = v.bob != null ? v.bob : v.transform;
            var head = bob.Find("Head");

            // cute eyes
            if (head != null && head.Find("Eye_L") == null)
            {
                Eye(head, "Eye_L", new Vector3(-0.3f, 0.15f, 0.82f));
                Eye(head, "Eye_R", new Vector3(0.3f, 0.15f, 0.82f));
            }

            // readable label faces the user (-Z toward user)
            if (v.label != null) { Readable(v.label); AddFace(v.label.gameObject, false); }

            // agent faces the user with its +Z (where the eyes are)
            AddFace(v.gameObject, true);

            // exclamation alert
            if (v.alert == null)
            {
                var alert = new GameObject("Alert");
                alert.transform.SetParent(v.transform, false);
                alert.transform.localPosition = new Vector3(0f, 0.22f, 0f);
                // bright backing + "!"
                var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
                back.name = "Back"; back.transform.SetParent(alert.transform, false);
                back.transform.localScale = new Vector3(0.03f, 0.045f, 1f);
                StripCollider(back); Tint(back.GetComponent<Renderer>(), new Color(0.95f, 0.78f, 0.1f));
                var tgo = new GameObject("Bang"); tgo.transform.SetParent(alert.transform, false);
                tgo.transform.localPosition = new Vector3(0f, 0f, -0.001f);
                tgo.transform.localScale = Vector3.one * 0.012f;
                var tmp = tgo.AddComponent<TextMeshPro>();
                tmp.text = "!"; tmp.fontSize = 10f; tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = new Color(0.6f, 0.05f, 0.05f);
                tmp.rectTransform.sizeDelta = new Vector2(4f, 5f);
                AddFace(alert, false);
                v.alert = alert;
                alert.SetActive(false);
            }
            sb.AppendLine($"{v.agentId}: eyes + label + alert");
        }
    }

    static void Eye(Transform head, string name, Vector3 lp)
    {
        var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        e.name = name; e.transform.SetParent(head, false);
        e.transform.localPosition = lp; e.transform.localScale = Vector3.one * 0.34f;
        StripCollider(e); Tint(e.GetComponent<Renderer>(), Color.white);
        var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        p.name = "Pupil"; p.transform.SetParent(e.transform, false);
        p.transform.localPosition = new Vector3(0f, 0f, 0.55f); p.transform.localScale = Vector3.one * 0.5f;
        StripCollider(p); Tint(p.GetComponent<Renderer>(), new Color(0.06f, 0.06f, 0.08f));
    }

    // ───────────────────── grabbable cube ─────────────────────
    static void PolishGrabCube(GameObject[] roots, StringBuilder sb)
    {
        var grab = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name.Contains("Grabbable Item"))?.gameObject;
        if (grab == null) { sb.AppendLine("no Grabbable Item"); return; }
        grab.SetActive(true);
        foreach (var r in grab.GetComponentsInChildren<Renderer>(true))
            Tint(r, new Color(0.95f, 0.55f, 0.15f));
        if (grab.GetComponentInChildren<PlaceInFrontOnStart>(true) == null)
        {
            var pf = grab.AddComponent<PlaceInFrontOnStart>();
            pf.sideOffset = 0.14f; pf.distance = 0.4f; pf.dropBelowEye = 0.18f;
        }
        if (grab.transform.Find("GrabLabel") == null)
        {
            var go = new GameObject("GrabLabel");
            go.transform.SetParent(grab.transform, false);
            go.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            go.transform.localScale = Vector3.one * 0.12f;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = "grab me"; tmp.fontSize = 5f; tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(12f, 4f);
            Readable(tmp);
            AddFace(go, false);
        }
        sb.AppendLine("grabbable cube: bright + labeled + reachable");
    }

    // ───────────────────── diagnostics ─────────────────────
    static void PolishDiagnostics(GameObject[] roots, StringBuilder sb)
    {
        var diag = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name == "DiagnosticsText")?.gameObject;
        if (diag == null) return;
        foreach (var tmp in diag.GetComponentsInChildren<TMP_Text>(true)) Readable(tmp);
        AddFace(diag, false);
        sb.AppendLine("diagnostics readable");
    }

    // ───────────────────── helpers ─────────────────────
    static void Readable(TMP_Text t)
    {
        if (t == null) return;
        // White + bold reads far better than black on passthrough; a dark backdrop quad
        // behind floating text gives full contrast. (outlineWidth NREs on a fresh TMP.)
        t.color = Color.white;
        t.fontStyle |= FontStyles.Bold;
        Backdrop(t);
    }

    // A dark, double-sided backdrop a hair behind the glyphs so white text is readable on
    // any real-world background. Quad is parented to the text and offset on both faces.
    static void Backdrop(TMP_Text t)
    {
        if (t == null || t.transform.Find("Backdrop") != null) return;
        var w = Mathf.Max(4f, t.rectTransform.sizeDelta.x) * 1.02f;
        var h = Mathf.Max(3f, t.rectTransform.sizeDelta.y) * 1.02f;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Backdrop";
        go.transform.SetParent(t.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, 0.2f);   // a hair behind the glyph plane
        go.transform.localScale = new Vector3(w, h, 0.02f);
        StripCollider(go);
        Tint(go.GetComponent<Renderer>(), new Color(0.08f, 0.09f, 0.12f));
    }

    static void MakeButton(GameObject src, Transform parent, string label, Vector3 localPos,
                           object target, string method, StringBuilder sb)
    {
        var btn = UnityEngine.Object.Instantiate(src, parent);
        btn.name = $"Btn_{label.Replace(' ', '_')}";
        btn.SetActive(true);
        btn.transform.localPosition = localPos;
        btn.transform.localRotation = Quaternion.identity;
        btn.transform.localScale = Vector3.one * 0.5f;
        foreach (var p in btn.GetComponentsInChildren<PlaceInFrontOnStart>(true))
            UnityEngine.Object.DestroyImmediate(p);

        var lblGo = new GameObject("Label");
        lblGo.transform.SetParent(btn.transform, false);
        lblGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        lblGo.transform.localScale = Vector3.one * 0.01f;
        var lt = lblGo.AddComponent<TextMeshPro>();
        lt.text = label; lt.fontSize = 3f; lt.alignment = TextAlignmentOptions.Center;
        lt.rectTransform.sizeDelta = new Vector2(28, 8);
        Readable(lt);

        var wrapperType = T("Oculus.Interaction.InteractableUnityEventWrapper");
        var wrapper = wrapperType != null ? btn.GetComponentInChildren(wrapperType, true) : null;
        if (wrapper != null)
        {
            var field = wrapperType.GetField("_whenSelect", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(wrapper) is UnityEvent evt)
            {
                for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
                    UnityEventTools.RemovePersistentListener(evt, i);
                var call = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), target, method);
                UnityEventTools.AddPersistentListener(evt, call);
                EditorUtility.SetDirty(wrapper);
                sb.AppendLine($"button {label} -> {method}");
            }
        }
    }

    static void AddFace(GameObject go, bool forward)
    {
        var f = go.GetComponent<FaceUser>() ?? go.AddComponent<FaceUser>();
        f.forward = forward;
    }

    static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>(); if (c) UnityEngine.Object.DestroyImmediate(c);
    }

    static void Tint(Renderer r, Color c)
    {
        if (r == null) return;
        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetColor(Shader.PropertyToID("_BaseColor"), c);
        r.SetPropertyBlock(mpb);
    }
}
