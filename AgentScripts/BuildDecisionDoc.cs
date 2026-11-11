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
using MuseDesk.Documents;
using MuseDesk.Networking;
using MuseDesk.Workspace;

/// <summary>
/// Builds the decision-document UI: a readable panel bound from decision events plus
/// hand-pokeable Approve / Reject / Ask buttons (cloned from the known-working poke button
/// and rewired). Wires a DocumentDeliveryController on AppServices.
/// </summary>
public static class BuildDecisionDoc
{
    static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(n)).FirstOrDefault(t => t != null);

    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var services = roots.FirstOrDefault(r => r.name == "AppServices");
        var controller = services ? services.GetComponent<SessionController>() : null;
        var sb = new StringBuilder();

        if (roots.Any(r => r.name == "DecisionDocument"))
            return "DecisionDocument already exists; skipping";

        // Source poke button to clone (the standalone foundation one).
        var pokeSrc = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                           .FirstOrDefault(t => t.name.Contains("Poke Interaction"))?.gameObject;
        if (pokeSrc == null) return "ERROR: source poke button not found";

        // ── document root (hidden until a decision arrives; parks in front on enable) ──
        var doc = new GameObject("DecisionDocument");
        doc.transform.position = new Vector3(0f, 1.1f, 0.5f);
        var pf = doc.AddComponent<PlaceInFrontOnStart>();
        pf.distance = 0.5f; pf.dropBelowEye = 0.05f; pf.faceUser = true;

        // backing card
        var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
        card.name = "Card";
        card.transform.SetParent(doc.transform, false);
        card.transform.localScale = new Vector3(0.30f, 0.22f, 1f);
        card.transform.localPosition = new Vector3(0f, 0f, 0.002f);
        var col = card.GetComponent<Collider>(); if (col) UnityEngine.Object.DestroyImmediate(col);
        TintWhite(card.GetComponent<Renderer>());

        var panelGo = new GameObject("DecisionPanel");
        panelGo.transform.SetParent(doc.transform, false);
        var panel = panelGo.AddComponent<DecisionPanel>();

        panel.badge = Text(doc.transform, "Badge", new Vector3(0f, 0.085f, 0), 3.2f, TextAlignmentOptions.Center, 28, 3);
        panel.title = Text(doc.transform, "Title", new Vector3(0f, 0.055f, 0), 4.2f, TextAlignmentOptions.Center, 28, 5);
        panel.summary = Text(doc.transform, "Summary", new Vector3(0f, 0.022f, 0), 3.4f, TextAlignmentOptions.Center, 28, 6);
        panel.recommendation = Text(doc.transform, "Reco", new Vector3(0f, -0.004f, 0), 3.0f, TextAlignmentOptions.Center, 28, 4);
        panel.body = Text(doc.transform, "Body", new Vector3(0f, -0.045f, 0), 2.4f, TextAlignmentOptions.TopLeft, 28, 14);
        panel.answer = Text(doc.transform, "Answer", new Vector3(0f, -0.085f, 0), 2.4f, TextAlignmentOptions.Center, 28, 5);
        panel.status = Text(doc.transform, "Status", new Vector3(0f, 0.105f, 0), 3.2f, TextAlignmentOptions.Center, 28, 3);

        var actions = new GameObject("Actions");
        actions.transform.SetParent(doc.transform, false);
        panel.actionsRoot = actions;

        // ── delivery controller ──
        var delivery = services.GetComponent<DocumentDeliveryController>()
                       ?? services.AddComponent<DocumentDeliveryController>();
        delivery.controller = controller;
        delivery.paperRoot = doc;
        delivery.panel = panel;

        // ── action buttons: clone + rewire ──
        MakeButton(pokeSrc, actions.transform, "Approve", new Vector3(-0.09f, -0.12f, 0f), delivery, "OnApprove", sb);
        MakeButton(pokeSrc, actions.transform, "Reject", new Vector3(0.09f, -0.12f, 0f), delivery, "OnReject", sb);
        MakeButton(pokeSrc, actions.transform, "Ask why", new Vector3(0f, -0.12f, 0f), delivery, "OnAskWhy", sb);

        // Retire the standalone foundation poke button (panel now owns approvals).
        pokeSrc.SetActive(false);
        sb.AppendLine("standalone foundation poke button disabled");

        doc.SetActive(false);   // shown by DocumentDeliveryController when a decision arrives

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("DecisionDocument built + saved");
        return sb.ToString();
    }

    static void MakeButton(GameObject src, Transform parent, string label, Vector3 localPos,
                           DocumentDeliveryController delivery, string method, StringBuilder sb)
    {
        var btn = UnityEngine.Object.Instantiate(src, parent);
        btn.name = $"Btn_{label.Replace(' ', '_')}";
        btn.SetActive(true);
        btn.transform.localPosition = localPos;
        btn.transform.localRotation = Quaternion.identity;
        btn.transform.localScale = Vector3.one * 0.5f;

        // clones inherit PlaceInFrontOnStart from the source — remove it (panel positions them)
        foreach (var p in btn.GetComponentsInChildren<PlaceInFrontOnStart>(true))
            UnityEngine.Object.DestroyImmediate(p);

        // label
        var lbl = Text(btn.transform, "Label", new Vector3(0f, 0f, -0.01f), 3.0f, TextAlignmentOptions.Center, 28, 3);
        lbl.text = label;

        // rewire the wrapper's WhenSelect -> delivery.<method>
        var wrapperType = T("Oculus.Interaction.InteractableUnityEventWrapper");
        var wrapper = wrapperType != null ? btn.GetComponentInChildren(wrapperType, true) : null;
        if (wrapper != null)
        {
            var field = wrapperType.GetField("_whenSelect", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(wrapper) is UnityEvent evt)
            {
                for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
                    UnityEventTools.RemovePersistentListener(evt, i);
                UnityAction call = (UnityAction)Delegate.CreateDelegate(
                    typeof(UnityAction), delivery, method);
                UnityEventTools.AddPersistentListener(evt, call);
                EditorUtility.SetDirty(wrapper);
                sb.AppendLine($"button {label} -> {method} wired");
            }
            else sb.AppendLine($"button {label}: _whenSelect missing");
        }
        else sb.AppendLine($"button {label}: wrapper missing");
    }

    static TMP_Text Text(Transform parent, string name, Vector3 lp, float fontSize,
                         TextAlignmentOptions align, float width, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp;
        go.transform.localScale = Vector3.one * 0.01f;
        var t = go.AddComponent<TextMeshPro>();
        t.fontSize = fontSize; t.alignment = align; t.textWrappingMode = TextWrappingModes.Normal;
        t.rectTransform.sizeDelta = new Vector2(width, height);
        t.color = Color.black;
        return t;
    }

    static void TintWhite(Renderer r)
    {
        if (r == null) return;
        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetColor(Shader.PropertyToID("_BaseColor"), new Color(0.96f, 0.96f, 0.92f));
        r.SetPropertyBlock(mpb);
    }
}
