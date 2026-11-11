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
using MuseDesk.Voice;

/// <summary>
/// Wires the Wit voice rig into the scene for a LIVE-mode test build. NOT for the offline judge
/// APK: Wit-based voice needs the client token in the build, so run this only for a dev/live build
/// and do not commit the resulting scene. Idempotent. Adds: a VoiceRig with AppVoiceExperience
/// (WitConfiguration assigned) + WitVoiceSource; a VoiceDecisionController on AppServices routed to
/// SessionController (voice -> same approve/reject/ask, backend still authorizes); and a poke
/// "Voice" button on the decision paper that starts listening.
/// </summary>
public static class WireVoice
{
    const string ConfigPath = "Assets/MuseDesk/Voice/WitConfig_MuseDesk.asset";

    static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
        .FirstOrDefault(t => t.Name == n);

    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var sb = new StringBuilder();

        var services = roots.FirstOrDefault(r => r.name == "AppServices");
        if (services == null) return "ERROR: no AppServices";
        var controller = services.GetComponent<SessionController>();

        var aveType = T("AppVoiceExperience");
        var wrcType = T("WitRuntimeConfiguration");
        var cfgType = T("WitConfiguration");
        if (aveType == null || wrcType == null || cfgType == null) return "ERROR: Voice SDK types missing";

        var cfg = AssetDatabase.LoadAssetAtPath(ConfigPath, cfgType);
        if (cfg == null) return $"ERROR: WitConfiguration not found at {ConfigPath} (run SetupWit first)";

        // ── VoiceRig GameObject under AppServices ──
        var rig = services.transform.Find("VoiceRig")?.gameObject;
        if (rig == null)
        {
            rig = new GameObject("VoiceRig");
            rig.transform.SetParent(services.transform, false);
        }

        var ave = rig.GetComponent(aveType) ?? rig.AddComponent(aveType);

        // Assign the WitConfiguration via RuntimeConfiguration { witConfiguration = cfg }
        var wrc = Activator.CreateInstance(wrcType);
        wrcType.GetField("witConfiguration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(wrc, cfg);
        var runtimeProp = aveType.GetProperty("RuntimeConfiguration", BindingFlags.Instance | BindingFlags.Public);
        runtimeProp?.SetValue(ave, wrc);
        sb.AppendLine($"AppVoiceExperience config assigned ({runtimeProp != null})");

        var source = rig.GetComponent<WitVoiceSource>() ?? rig.AddComponent<WitVoiceSource>();
        source.appVoice = (Oculus.Voice.AppVoiceExperience)ave;

        // ── VoiceDecisionController on AppServices ──
        var vdc = services.GetComponent<VoiceDecisionController>() ?? services.AddComponent<VoiceDecisionController>();
        vdc.controller = controller;
        vdc.sourceBehaviour = source;
        vdc.voiceEnabled = true;
        // Test build: let voice drive the (labeled) demo decision over wifi, so it's testable with
        // no PC/backend. Voice still only acts when a decision is actually pending, and the offline
        // judge APK is built voice-free regardless.
        vdc.requireLiveMode = false;
        sb.AppendLine($"VoiceDecisionController wired (controller={controller != null})");

        // ── poke "Voice" button on the decision paper ──
        var doc = roots.FirstOrDefault(r => r.name == "DecisionDocument");
        var actions = doc != null ? doc.transform.Find("Actions") : null;
        var pokeSrc = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name.StartsWith("Btn_"))?.gameObject;
        if (actions != null && pokeSrc != null && actions.Find("Btn_Voice") == null)
            MakeButton(pokeSrc, actions, "Voice", new Vector3(0f, -0.2f, 0f), source, "StartListening", sb);
        else
            sb.AppendLine($"voice button: actions={actions != null} pokeSrc={pokeSrc != null} (skipped if present)");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("VOICE WIRED (live-test only — do NOT commit this scene for the offline judge build)");
        return sb.ToString();
    }

    static void MakeButton(GameObject src, Transform parent, string label, Vector3 localPos,
                           object target, string method, StringBuilder sb)
    {
        var btn = UnityEngine.Object.Instantiate(src, parent);
        btn.name = $"Btn_{label}";
        btn.SetActive(true);
        btn.transform.localPosition = localPos;
        btn.transform.localRotation = Quaternion.identity;
        btn.transform.localScale = Vector3.one * 0.5f;
        foreach (var p in btn.GetComponentsInChildren<MuseDesk.Workspace.PlaceInFrontOnStart>(true))
            UnityEngine.Object.DestroyImmediate(p);

        var lblGo = new GameObject("Label");
        lblGo.transform.SetParent(btn.transform, false);
        lblGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        lblGo.transform.localScale = Vector3.one * 0.01f;
        var lt = lblGo.AddComponent<TextMeshPro>();
        lt.text = "Voice"; lt.fontSize = 3f; lt.alignment = TextAlignmentOptions.Center;
        lt.color = Color.white; lt.fontStyle = FontStyles.Bold;
        lt.rectTransform.sizeDelta = new Vector2(28, 8);

        var wrapperType = T("InteractableUnityEventWrapper");
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
                sb.AppendLine($"voice button -> {method} wired");
            }
        }
    }
}
