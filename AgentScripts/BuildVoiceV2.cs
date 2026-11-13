using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Networking;
using MuseDesk.Voice;
using MuseDesk.Workspace;
using Object = UnityEngine.Object;

/// <summary>
/// Voice V2: the microphone key, its halo, the readout plate with Confirm / Cancel keys, and the
/// components that run voice (VoiceCommandCenter, VoiceHud, WitVoiceSource, VoiceGestureActivator).
///
/// Where things go:
///   AppServices/VoiceRig        the recogniser and the command centre (created if missing; an
///                               existing AppVoiceExperience on it is kept and used).
///   ModeConsole/MicSlot/VoiceHud  when the mode console exists (24 x 24 mm slot), else
///   TaskDock/VoiceHud           beside the task dock (it then travels with the dock in every mode).
/// Existing objects are only read, except: one key is cloned (FocusToggle or another existing key)
/// and the decision paper's Btn_Voice, if present, is pointed at the same microphone toggle.
///
/// The Wit configuration asset is referenced, never read, printed or copied. If there is no
/// speech service in the scene and no configuration asset, the UI is still built and voice
/// reports "no speech service" when asked; VoiceDrive still drives every command.
///
/// Idempotent (safe to run twice); saves the scene; returns a report.
///   unity command script run --file AgentScripts/BuildVoiceV2.cs --entry BuildVoiceV2.Apply
/// </summary>
public static class BuildVoiceV2
{
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";
    const string WitConfigPath = "Assets/MuseDesk/Voice/WitConfig_MuseDesk.asset";
    const float Hidden = 0.001f;                        // ScaleEase.HiddenFactor
    const float TextZ = -0.0005f;                       // 0.5 mm proud of the plate

    // Layout, metres, in the VoiceHud holder's space (+X = the user's right, -Z = toward the user).
    static readonly Vector3 DockHolderPos = new Vector3(-0.114f, -0.018f, 0f);   // left of the dock plate (dock-local)
    static readonly Vector3 DockReadoutPos = new Vector3(0.114f, 0.123f, 0f);    // holder-local: centred above the dock
    static readonly Vector3 SlotReadoutPos = new Vector3(0f, 0.080f, 0f);        // holder-local: above the console slot
    const float DockMicKeyScale = 0.026f;               // face about 25 mm
    const float SlotMicKeyScale = 0.023f;               // face about 22.5 mm (slot is 24 x 24 mm)
    const float AnswerKeyScale = 0.032f;                // face about 31 mm ("Confirm" at 5.5 mm bold)
    const float ReadoutW = 0.168f, ReadoutH = 0.104f;

    static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    static readonly Color Ink = Hex("#1F2530");
    static readonly Color Plate = Hex("#1B1F27");
    static readonly Color OnPlate = Hex("#F3F4F6");
    static readonly Color Housing = Hex("#2A2F3A");
    static readonly Color Lavender = Hex("#8C7BD6");
    static readonly Color Success = Hex("#2E9B5F");
    static readonly Color Coral = Hex("#D9534A");

    // ───────────────────────────── helpers (idioms from BuildSpatialReachV1) ─────────────────────────────

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Find(List<Transform> all, string name) => all.FirstOrDefault(t => t != null && t.name == name);
    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t.name == name);

    static Material Mat(string existingName, string ownName, Color c, bool unlit = true)
    {
        if (!string.IsNullOrEmpty(existingName))
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{existingName}.mat");
            if (existing != null) return existing;
        }
        if (!AssetDatabase.IsValidFolder("Assets/MuseDesk/Art/Materials")) AssetDatabase.CreateFolder("Assets/MuseDesk/Art", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/MuseDesk/Art/Materials", "UI");
        string path = $"{MatDir}/{ownName}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Simple Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
        if (!unlit && m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.3f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(go);
        return mesh;
    }

    static Transform Holder(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            t = go.transform;
        }
        t.gameObject.layer = parent.gameObject.layer;
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        return t;
    }

    static Transform Shape(Transform parent, string name, Mesh mesh, Material mat, Vector3 lp, Quaternion lr, Vector3 ls)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            t = go.transform;
        }
        t.gameObject.layer = parent.gameObject.layer;
        t.gameObject.SetActive(true);
        t.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = t.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        t.localPosition = lp;
        t.localRotation = lr;
        t.localScale = ls;
        EditorUtility.SetDirty(t.gameObject);
        return t;
    }

    /// <summary>Find or create a TextMeshPro label at scale 0.01 (font size N = about N mm; the box is in cm).</summary>
    static TMP_Text Label(Transform parent, string name, Vector3 lp, string text, Vector2 boxCm, float minMm, float maxMm,
                          Color color, FontStyles style, TextAlignmentOptions align, bool wrap)
    {
        var t = parent.Find(name);
        TextMeshPro tmp;
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            tmp = go.AddComponent<TextMeshPro>();
            go.transform.SetParent(parent, false);
            t = go.transform;
        }
        else
        {
            tmp = t.GetComponent<TextMeshPro>();
            if (tmp == null) tmp = t.gameObject.AddComponent<TextMeshPro>();
            t = tmp.transform;
        }
        t.gameObject.layer = parent.gameObject.layer;
        t.gameObject.SetActive(true);
        t.localPosition = lp;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one * 0.01f;
        var mr = t.GetComponent<MeshRenderer>();
        if (mr != null) { mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; }

        tmp.text = text ?? "";
        tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        tmp.rectTransform.sizeDelta = boxCm;
        tmp.enableAutoSizing = minMm < maxMm;
        tmp.fontSizeMin = minMm; tmp.fontSizeMax = maxMm; tmp.fontSize = maxMm;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.margin = Vector4.zero;
        tmp.richText = false;                            // a transcript can never carry markup
        EditorUtility.SetDirty(tmp);
        return tmp;
    }

    static bool OwnMeshBounds(Transform part, Transform space, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        var f = part.GetComponent<MeshFilter>();
        if (f == null || f.sharedMesh == null) return false;
        var b = f.sharedMesh.bounds;
        for (int i = 0; i < 8; i++)
        {
            var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var l = space.InverseTransformPoint(part.TransformPoint(c));
            mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
        }
        return true;
    }

    static int Rewire(Transform key, Object target, string method)
    {
        int n = 0;
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "InteractableUnityEventWrapper") continue;
            var so = new SerializedObject(mb);
            var calls = so.FindProperty("_whenSelect.m_PersistentCalls.m_Calls");
            if (calls == null) continue;
            if (calls.arraySize == 0) calls.InsertArrayElementAtIndex(0);
            while (calls.arraySize > 1) calls.DeleteArrayElementAtIndex(calls.arraySize - 1);
            var c = calls.GetArrayElementAtIndex(0);
            c.FindPropertyRelative("m_Target").objectReferenceValue = target;
            c.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = target.GetType().AssemblyQualifiedName;
            c.FindPropertyRelative("m_MethodName").stringValue = method;
            c.FindPropertyRelative("m_Mode").intValue = 1;
            c.FindPropertyRelative("m_CallState").intValue = 2;
            so.ApplyModifiedProperties();
            n++;
        }
        return n;
    }

    /// <summary>Find or clone a poke key from a template key (keeps the Housing3D / ButtonVisual / Cap3D structure).</summary>
    static Transform CloneKey(Transform parent, Transform template, string name, out bool created)
    {
        created = false;
        var k = parent.Find(name);
        if (k == null)
        {
            var go = Object.Instantiate(template.gameObject, parent);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.name = name;
            k = go.transform;
            created = true;
            // Things the template key carries for its own job and this key must not inherit.
            foreach (var mb in go.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n == "FaceUser" || n == "ScaleReveal" || n == "MovablePanel" || n == "KitKeyRecord") Object.DestroyImmediate(mb);
            }
        }
        k.gameObject.layer = parent.gameObject.layer;
        k.gameObject.SetActive(true);
        return k;
    }

    static void CapColor(Transform key, string matName, Color c)
    {
        var capT = Under(key, "Cap3D");
        var mr = capT != null ? capT.GetComponent<MeshRenderer>() : null;
        if (mr != null)
        {
            mr.sharedMaterial = Mat(null, matName, c, unlit: false);
            EditorUtility.SetDirty(mr);
        }
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "InteractableColorVisual") continue;
            var so = new SerializedObject(mb);
            SetStateColor(so, "_normalColorState", c);
            SetStateColor(so, "_hoverColorState", Color.Lerp(c, Color.white, 0.25f));
            SetStateColor(so, "_selectColorState", Color.Lerp(c, Color.black, 0.15f));
            so.ApplyModifiedProperties();
        }
    }

    static void SetStateColor(SerializedObject so, string state, Color c)
    {
        var st = so.FindProperty(state);
        var col = st != null ? st.FindPropertyRelative("Color") : null;
        if (col != null) col.colorValue = c;
    }

    /// <summary>Key label sized from its world scale (glyphs <paramref name="mm"/> mm tall, 5.5 mm floor). Call at full key scale.</summary>
    static TMP_Text KeyLabel(Transform key, string text, float mm, Color color)
    {
        var tmp = key.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null) return null;
        float s = Mathf.Abs(tmp.transform.lossyScale.x);
        if (s < 1e-7f) return tmp;
        float capWidth = Mathf.Abs(key.lossyScale.x) * 0.98f;
        var cap = Under(key, "Cap3D");
        if (cap != null && OwnMeshBounds(cap, key, out var cmn, out var cmx))
            capWidth = (cmx.x - cmn.x) * Mathf.Abs(key.lossyScale.x);
        tmp.text = text;
        tmp.rectTransform.sizeDelta = new Vector2(0.90f * capWidth / s, 0.62f * capWidth / s);
        float max = mm * 0.01f / s;
        float min = Mathf.Min(max, 5.5f * 0.01f / s);
        tmp.enableAutoSizing = min < max;
        tmp.fontSizeMax = max; tmp.fontSizeMin = min; tmp.fontSize = max;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.richText = false;
        EditorUtility.SetDirty(tmp);
        return tmp;
    }

    static float HousingDepth(Transform key, float keyScale)
    {
        var housing = Under(key, "Housing3D");
        if (housing != null && OwnMeshBounds(housing, key, out _, out var hmx) && hmx.z > 0f)
            return Mathf.Clamp(hmx.z * keyScale, 0.002f, 0.02f);
        return 0.008f;
    }

    static float CapWidth(Transform key, float keyScale)
    {
        var cap = Under(key, "Cap3D");
        if (cap != null && OwnMeshBounds(cap, key, out var mn, out var mx)) return (mx.x - mn.x) * keyScale;
        return keyScale * 0.98f;
    }

    static T Ensure<T>(GameObject go, out bool added) where T : Component
    {
        var c = go.GetComponent<T>();
        added = c == null;
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    static Type TypeNamed(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
        .FirstOrDefault(t => t.Name == name);

    // ───────────────────────────── entry ─────────────────────────────

    public static string Apply()
    {
        var sb = new StringBuilder("BuildVoiceV2\n");
        var scene = EditorSceneManager.GetActiveScene();
        var all = All();

        var services = Find(all, "AppServices");
        if (services == null) return "ERROR: no AppServices in the open scene (open Assets/MuseDesk/Scenes/MainMRScene.unity).";
        var session = services.GetComponent<SessionController>();
        if (session == null) session = Object.FindAnyObjectByType<SessionController>(FindObjectsInactive.Include);
        sb.AppendLine($"session controller: {(session != null ? "found" : "MISSING (voice will look for one at run time)")}");

        // ── 1. recogniser and command centre ──
        sb.AppendLine("-- 1. voice rig --");
        var rig = Holder(services, "VoiceRig");
        Component appVoice = rig.GetComponent("AppVoiceExperience");
        if (appVoice == null) appVoice = TryAddSpeechService(rig.gameObject, sb);
        else sb.AppendLine("  speech service (AppVoiceExperience) already on AppServices/VoiceRig: kept");

        var source = Ensure<WitVoiceSource>(rig.gameObject, out bool addedSource);
        source.appVoice = appVoice as Oculus.Voice.AppVoiceExperience;
        source.maxRecordingSeconds = 6f;
        source.silenceSeconds = 1.4f;
        EditorUtility.SetDirty(source);
        sb.AppendLine($"  {(addedSource ? "added" : "updated")} WitVoiceSource (speech service wired: {source.appVoice != null}; 6 s max, 1.4 s silence)");

        var center = Ensure<VoiceCommandCenter>(rig.gameObject, out bool addedCenter);
        center.session = session;
        center.sourceBehaviour = source;
        sb.AppendLine($"  {(addedCenter ? "added" : "updated")} VoiceCommandCenter");

        var policy = Object.FindAnyObjectByType<VoiceDecisionController>(FindObjectsInactive.Include);
        if (policy != null)
        {
            policy.controller = session;
            policy.sourceBehaviour = source;
            policy.voiceEnabled = true;
            policy.requireLiveMode = false;       // voice needs internet, not the desktop
            EditorUtility.SetDirty(policy);
            center.policy = policy;
            sb.AppendLine("  VoiceDecisionController (policy only since V2): voiceEnabled on, requireLiveMode off, source set");
        }
        else sb.AppendLine("  no VoiceDecisionController in the scene: the paper's Voice chip stays hidden (the microphone key is the way in)");

        var gesture = Ensure<VoiceGestureActivator>(rig.gameObject, out bool addedGesture);
        gesture.center = center;
        if (addedGesture) gesture.gestureEnabled = false;
        EditorUtility.SetDirty(gesture);
        sb.AppendLine($"  {(addedGesture ? "added" : "kept")} VoiceGestureActivator (pinch in front of the mouth, 0.5 s): enabled={gesture.gestureEnabled}");

        // ── 2. the visible part ──
        sb.AppendLine("-- 2. microphone key and readout --");
        var slot = Find(all, "MicSlot");
        if (slot != null && (slot.parent == null || slot.parent.name != "ModeConsole")) slot = null;
        var dock = Find(all, "TaskDock");
        Transform parent = slot != null ? slot : dock;
        VoiceHud hud = null;
        if (parent == null) sb.AppendLine("  SKIP: neither ModeConsole/MicSlot nor TaskDock exists. Voice runs without a key (driver only).");
        else hud = BuildHud(all, parent, slot != null, center, sb);
        center.hud = hud;
        EditorUtility.SetDirty(center);

        // ── 3. the paper's own Voice chip, if the scene has one, is the same toggle ──
        sb.AppendLine("-- 3. decision paper --");
        var chip = Find(all, "Btn_Voice");
        if (chip != null)
        {
            int n = Rewire(chip, center, nameof(VoiceCommandCenter.PressMic));
            sb.AppendLine(n > 0 ? "  Btn_Voice -> VoiceCommandCenter.PressMic" : "  Btn_Voice has no InteractableUnityEventWrapper: left as it is");
        }
        else sb.AppendLine("  no Btn_Voice on the paper: nothing to rewire");

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine($"scene saved: {saved}");
        sb.AppendLine("Driver: AgentScripts/VoiceDrive.cs (Say, Partial, State, Mic, Confirm, Cancel).");
        return sb.ToString();
    }

    /// <summary>Add the Voice SDK service when the local Wit configuration asset exists. The asset is
    /// only referenced; nothing in it is read or reported.</summary>
    static Component TryAddSpeechService(GameObject rig, StringBuilder sb)
    {
        var aveType = TypeNamed("AppVoiceExperience");
        var wrcType = TypeNamed("WitRuntimeConfiguration");
        var cfgType = TypeNamed("WitConfiguration");
        if (aveType == null || wrcType == null || cfgType == null)
        {
            sb.AppendLine("  Voice SDK types not found: no speech service added (voice reports 'no speech service')");
            return null;
        }
        var cfg = AssetDatabase.LoadAssetAtPath(WitConfigPath, cfgType);
        if (cfg == null)
        {
            sb.AppendLine("  no Wit configuration asset on this machine: no speech service added (voice reports 'no speech service'; see docs/VOICE_SETUP.md)");
            return null;
        }
        var ave = rig.AddComponent(aveType);
        var wrc = Activator.CreateInstance(wrcType);
        wrcType.GetField("witConfiguration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(wrc, cfg);
        aveType.GetProperty("RuntimeConfiguration", BindingFlags.Instance | BindingFlags.Public)?.SetValue(ave, wrc);
        EditorUtility.SetDirty(ave);
        sb.AppendLine("  added the speech service (AppVoiceExperience) with the local Wit configuration asset");
        return ave;
    }

    static VoiceHud BuildHud(List<Transform> all, Transform parent, bool inSlot, VoiceCommandCenter center, StringBuilder sb)
    {
        var quad = PrimitiveMesh(PrimitiveType.Quad);
        var cylinder = PrimitiveMesh(PrimitiveType.Cylinder);

        // A holder left under the other parent by an earlier run (the console appeared later) is moved, not duplicated.
        Transform holder = parent.Find("VoiceHud");
        if (holder == null)
        {
            var stray = all.FirstOrDefault(t => t != null && t.name == "VoiceHud" && t.GetComponent<VoiceHud>() != null);
            if (stray != null) { stray.SetParent(parent, false); holder = stray; sb.AppendLine("  moved the existing VoiceHud to " + parent.name); }
        }
        if (holder == null) holder = Holder(parent, "VoiceHud");
        holder.gameObject.layer = parent.gameObject.layer;
        holder.localRotation = Quaternion.identity;
        holder.localPosition = inSlot ? Vector3.zero : DockHolderPos;
        // The holder is in metres whatever the parent's scale is.
        Vector3 ps = parent.lossyScale;
        holder.localScale = new Vector3(1f / Mathf.Max(1e-5f, Mathf.Abs(ps.x)), 1f / Mathf.Max(1e-5f, Mathf.Abs(ps.y)), 1f / Mathf.Max(1e-5f, Mathf.Abs(ps.z)));
        if (!inSlot) holder.localPosition = Vector3.Scale(DockHolderPos, holder.localScale);
        sb.AppendLine($"  VoiceHud under {(inSlot ? "ModeConsole/MicSlot" : "TaskDock (left of the dock plate)")}");

        Transform template = null;
        foreach (string n in new[] { "FocusToggle", "DockBoardKey", "BringKey", "RunAgainKey" })
        {
            template = Find(all, n);
            if (template != null && !template.IsChildOf(holder)) break;
            template = null;
        }

        var plateMat = Mat("OfficeV2_Plate", "Voice_Plate", Plate);
        var housingMat = Mat(null, "Voice_Frame", Housing);
        var haloMat = Mat(null, "Voice_Halo", Lavender);
        var stripMat = Mat(null, "Voice_ToneStrip", Lavender);

        bool addedHud;
        var hud = Ensure<VoiceHud>(holder.gameObject, out addedHud);

        // ── microphone key ──
        float micScale = inSlot ? SlotMicKeyScale : DockMicKeyScale;
        float face = micScale;
        if (template != null)
        {
            var mic = CloneKey(holder, template, "MicKey", out bool created);
            mic.localRotation = Quaternion.identity;
            mic.localScale = Vector3.one * micScale;
            float depth = HousingDepth(mic, micScale);
            mic.localPosition = new Vector3(0f, 0f, -(depth + 0.001f));
            face = CapWidth(mic, micScale);
            hud.micLabel = KeyLabel(mic, "Mic", 6.5f, Ink);
            CapColor(mic, "Key_VoiceMic", Lavender);
            int wired = Rewire(mic, center, nameof(VoiceCommandCenter.PressMic));
            EditorUtility.SetDirty(mic.gameObject);
            sb.AppendLine($"  {(created ? "created" : "updated")} MicKey (clone of {template.name}, scale {micScale}, cap {face * 1000f:F0} mm, travel {0.22f * micScale * 1000f:F1} mm) -> VoiceCommandCenter.PressMic ({wired} event wrapper)");
            if (face < 0.022f) sb.AppendLine("  WARNING: the microphone cap is under 22 mm; raise the key scale constant");
        }
        else sb.AppendLine("  SKIP MicKey: no key to clone (FocusToggle / DockBoardKey / BringKey / RunAgainKey). Voice still runs from the driver and the paper's chip.");

        // Backing plate (not in the console slot: the console is the surface there) and the halo.
        if (!inSlot)
        {
            Shape(holder, "MicFrame", quad, housingMat, new Vector3(0f, 0f, 0.0008f), Quaternion.identity, new Vector3(0.056f, 0.056f, 1f));
            Shape(holder, "MicPlate", quad, plateMat, Vector3.zero, Quaternion.identity, new Vector3(0.050f, 0.050f, 1f));
        }
        else
        {
            foreach (string n in new[] { "MicFrame", "MicPlate" })
            {
                var old = holder.Find(n);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            }
        }
        // A thin disc lying on the plate behind the key; its rim shows as a ring and swells with the level.
        float haloD = Mathf.Max(face * 1.42f, micScale * 1.4f);
        var halo = Shape(holder, "MicHalo", cylinder, haloMat, new Vector3(0f, 0f, -0.0005f), Quaternion.Euler(90f, 0f, 0f),
                         new Vector3(haloD, 0.0002f, haloD));
        hud.halo = halo;
        hud.haloRenderer = halo.GetComponent<Renderer>();
        hud.haloRestScale = halo.localScale;
        hud.haloSwell = inSlot ? 0.18f : 0.30f;
        sb.AppendLine($"  MicHalo: disc {haloD * 1000f:F0} mm behind the key, swelling to {haloD * (1f + hud.haloSwell) * 1000f:F0} mm with the microphone level");

        // ── readout ──
        Vector3 readoutPos = inSlot ? SlotReadoutPos : DockReadoutPos;
        var readout = Holder(holder, "VoiceReadout");
        readout.localPosition = readoutPos;
        readout.localRotation = Quaternion.identity;
        readout.localScale = Vector3.one;                 // laid out at full size; hidden again below
        float w = ReadoutW, h = ReadoutH;
        Shape(readout, "Frame", quad, housingMat, new Vector3(0f, 0f, 0.0008f), Quaternion.identity, new Vector3(w + 0.008f, h + 0.008f, 1f));
        Shape(readout, "Plate", quad, plateMat, Vector3.zero, Quaternion.identity, new Vector3(w, h, 1f));
        var strip = Shape(readout, "ToneStrip", quad, stripMat, new Vector3(0f, h * 0.5f - 0.003f, -0.0002f), Quaternion.identity, new Vector3(w, 0.006f, 1f));

        hud.titleText = Label(readout, "Title", new Vector3(-0.026f, 0.0370f, TextZ), "", new Vector2(10.4f, 1.2f), 5.5f, 7.5f, OnPlate,
                              FontStyles.Bold, TextAlignmentOptions.Left, false);
        hud.modeText = Label(readout, "Mode", new Vector3(0.053f, 0.0370f, TextZ), "", new Vector2(5.0f, 0.9f), 5.5f, 5.5f, OnPlate,
                             FontStyles.Bold, TextAlignmentOptions.Right, false);
        hud.bodyText = Label(readout, "Body", new Vector3(0f, -0.0010f, TextZ), "", new Vector2(15.6f, 6.0f), 5.5f, 7.0f, OnPlate,
                             FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
        hud.footText = Label(readout, "Foot", new Vector3(0f, -0.0440f, TextZ), "", new Vector2(15.6f, 0.9f), 5.5f, 5.5f, OnPlate,
                             FontStyles.Normal, TextAlignmentOptions.Left, false);
        hud.readout = readout;
        hud.readoutShownScale = Vector3.one;
        hud.toneStrip = strip.GetComponent<Renderer>();

        // ── Confirm / Cancel: siblings of the readout (so a hidden key is never scaled twice), over its lower edge ──
        if (template != null)
        {
            hud.confirmKey = AnswerKey(holder, template, "VoiceConfirmKey", readoutPos + new Vector3(0.045f, -0.032f, 0f), "Confirm", "Key_VoiceConfirm", Success,
                                       center, nameof(VoiceCommandCenter.PressConfirm), out TMP_Text confirmLabel, sb);
            hud.cancelKey = AnswerKey(holder, template, "VoiceCancelKey", readoutPos + new Vector3(-0.045f, -0.032f, 0f), "Cancel", "Key_VoiceCancel", Coral,
                                      center, nameof(VoiceCommandCenter.PressCancel), out TMP_Text cancelLabel, sb);
            hud.confirmLabel = confirmLabel;
            hud.cancelLabel = cancelLabel;
            hud.answerKeyScale = Vector3.one * AnswerKeyScale;
        }

        readout.localScale = Vector3.one * Hidden;
        EditorUtility.SetDirty(hud);
        sb.AppendLine($"  VoiceReadout: {w * 1000f:F0} x {h * 1000f:F0} mm plate at holder-local {readoutPos.ToString("F3")} (title, LIVE / SCRIPTED DEMO, body up to 8 lines, foot line), saved hidden by scale");
        sb.AppendLine("  CHECK with XrVisualQa and on the head: the readout must not cross the mode console, the stamps or the receipt. The offsets are the constants at the top of this file.");
        return hud;
    }

    static Transform AnswerKey(Transform holder, Transform template, string name, Vector3 xy, string word, string matName, Color color,
                               VoiceCommandCenter center, string method, out TMP_Text label, StringBuilder sb)
    {
        var key = CloneKey(holder, template, name, out bool created);
        key.localRotation = Quaternion.identity;
        key.localScale = Vector3.one * AnswerKeyScale;     // full size while the label is fitted
        float depth = HousingDepth(key, AnswerKeyScale);
        key.localPosition = new Vector3(xy.x, xy.y, xy.z - (depth + 0.0005f));
        label = KeyLabel(key, word, 6.5f, Ink);
        CapColor(key, matName, color);
        int wired = Rewire(key, center, method);
        key.localScale = Vector3.one * AnswerKeyScale * Hidden;   // VoiceHud shows it only while a question is open
        EditorUtility.SetDirty(key.gameObject);
        sb.AppendLine($"  {(created ? "created" : "updated")} {name} (scale {AnswerKeyScale}) -> VoiceCommandCenter.{method} ({wired} event wrapper), saved hidden by scale");
        return key;
    }
}
