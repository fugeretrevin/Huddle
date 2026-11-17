using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Documents;

/// <summary>
/// Desk decision paper: only two keys are left, "More" (the details sheet, DocumentDeliveryController.OnToggleDetails)
/// and "Revise" (request changes, OnRequestChanges). The physical stamps stay the approve / reject path.
///
///   Approve, Reject, Why (Btn_Ask_why) and Voice (Btn_Voice) are HIDDEN BY SCALE (never SetActive: they are
///   Interaction SDK pokes); their original scale / position is saved so Revert() can bring them back.
///   The two kept keys are made 1.5x bigger, centred side by side on one row, relabelled "More" / "Revise",
///   and their labels are sized from the measured cap face so the words fit with slack and never print an
///   ellipsis (overflow mode Overflow, no auto-size, the box is the cap face minus 10 percent).
///   The pending state's inert Approve / Reject plates are deactivated (plain quads, not interactables).
///   DecisionPanel is re-pointed: chipKeys = [More, Revise], no voice chip.
///
/// Idempotent. State for Revert() is written once to Library/PaperKeysTrimV1.json (never overwritten by a re-run).
///   unity command script run --file AgentScripts/PaperKeysTrimV1.cs --entry PaperKeysTrimV1.Apply
///   unity command script run --file AgentScripts/PaperKeysTrimV1.cs --entry PaperKeysTrimV1.Revert
/// </summary>
public static class PaperKeysTrimV1
{
    const string StateFile = "Library/PaperKeysTrimV1.json";
    const float Grow = 1.5f;
    const float HiddenScale = 0.0001f;
    // Key centres on the paper (doc-local metres): one row under the status line, above the bottom margin.
    static readonly Vector2 MoreAt = new Vector2(-0.052f, -0.108f), ReviseAt = new Vector2(0.052f, -0.108f);
    const float MinWorldMm = 5.5f, WantWorldMm = 8f;
    static readonly string[] HideKeys = { "Btn_Approve", "Btn_Reject", "Btn_Ask_why", "Btn_Voice" };
    static readonly string[] GhostNames = { "GhostApprovePlate", "GhostRejectPlate", "GhostApproveLabel", "GhostRejectLabel", "GhostApprove", "GhostReject" };

    [Serializable] class KeyState { public string name; public Vector3 scale; public Vector3 pos; public string text; public float fontSize; public Vector2 box; public int overflow; public bool autosize; public int style; }
    [Serializable] class GhostState { public string name; public bool active; }
    [Serializable] class State { public List<KeyState> keys = new List<KeyState>(); public List<GhostState> ghosts = new List<GhostState>(); public string voiceChip; public bool hasVoiceChip; }

    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t.name == name);

    static State Load()
    {
        try { return File.Exists(StateFile) ? JsonUtility.FromJson<State>(File.ReadAllText(StateFile)) : null; }
        catch { return null; }
    }

    static bool Bounds(Transform part, Transform space, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        if (part == null) return false;
        bool any = false;
        foreach (var f in part.GetComponentsInChildren<MeshFilter>(true))
        {
            if (f.sharedMesh == null) continue;
            var b = f.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var l = space.InverseTransformPoint(f.transform.TransformPoint(c));
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l); any = true;
            }
        }
        return any;
    }

    static string WireOf(Transform key, out UnityEngine.Object target)
    {
        target = null;
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "InteractableUnityEventWrapper") continue;
            var so = new SerializedObject(mb);
            var calls = so.FindProperty("_whenSelect.m_PersistentCalls.m_Calls");
            if (calls == null || calls.arraySize == 0) return null;
            var c = calls.GetArrayElementAtIndex(0);
            target = c.FindPropertyRelative("m_Target").objectReferenceValue;
            return c.FindPropertyRelative("m_MethodName").stringValue;
        }
        return null;
    }

    // Wires WhenSelect -> delivery.method when the key has no persistent call at all. Returns true if it added one.
    static bool WireIfMissing(Transform key, DocumentDeliveryController delivery, string method, StringBuilder sb)
    {
        string current = WireOf(key, out var target);
        if (current == method && target == delivery) return false;
        if (current != null) { sb.AppendLine($"  WARNING {key.name} is wired to {(target != null ? target.GetType().Name : "?")}.{current}, expected {method}; left as it is"); return false; }
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "InteractableUnityEventWrapper") continue;
            var prop = mb.GetType().GetProperty("WhenSelect", BindingFlags.Public | BindingFlags.Instance);
            var ev = prop != null ? prop.GetValue(mb) as UnityEngine.Events.UnityEvent : null;
            if (ev == null || delivery == null) break;
            var mi = typeof(DocumentDeliveryController).GetMethod(method, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (mi == null) break;
            var act = (UnityEngine.Events.UnityAction)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction), delivery, mi);
            UnityEventTools.AddVoidPersistentListener(ev, act);
            EditorUtility.SetDirty(mb);
            sb.AppendLine($"  {key.name}: WhenSelect was empty, wired to DocumentDeliveryController.{method}");
            return true;
        }
        sb.AppendLine($"  WARNING {key.name}: no select wiring found and none could be added");
        return false;
    }

    public static string Apply()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        var doc = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t != null && t.name == "DecisionDocument");
        if (doc == null) return "PaperKeysTrimV1: DecisionDocument not found\n";
        var actions = doc.Find("Actions");
        var panel = doc.GetComponentInChildren<DecisionPanel>(true);
        if (actions == null || panel == null) return "PaperKeysTrimV1: Actions / DecisionPanel not found on DecisionDocument\n";
        var delivery = UnityEngine.Object.FindAnyObjectByType<DocumentDeliveryController>(FindObjectsInactive.Include);
        var more = actions.Find("Btn_Details");
        var revise = actions.Find("Btn_Changes");
        if (more == null || revise == null) return $"PaperKeysTrimV1: Btn_Details ({(more != null ? "ok" : "missing")}) / Btn_Changes ({(revise != null ? "ok" : "missing")}) not found under Actions\n";

        var state = Load();
        bool fresh = state == null;
        if (fresh) state = new State();
        var pending = doc.Find("Pending");
        Transform voice = actions.Find("Btn_Voice");

        // ── save what Revert needs (first run only) ──
        if (fresh)
        {
            foreach (var n in new[] { "Btn_Details", "Btn_Changes" }.Concat(HideKeys))
            {
                var k = actions.Find(n);
                if (k == null) continue;
                var ks = new KeyState { name = n, scale = k.localScale, pos = k.localPosition };
                var lab = k.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
                if (lab != null)
                {
                    ks.text = lab.text; ks.fontSize = lab.fontSize; ks.box = lab.rectTransform.sizeDelta;
                    ks.overflow = (int)lab.overflowMode; ks.autosize = lab.enableAutoSizing; ks.style = (int)lab.fontStyle;
                }
                state.keys.Add(ks);
            }
            if (pending != null)
                foreach (var g in GhostNames)
                {
                    var t = Under(pending, g);
                    if (t != null) state.ghosts.Add(new GhostState { name = g, active = t.gameObject.activeSelf });
                }
            state.hasVoiceChip = panel.voiceChip != null;
            state.voiceChip = panel.voiceChip != null ? panel.voiceChip.name : null;
            File.WriteAllText(StateFile, JsonUtility.ToJson(state, true));
            sb.AppendLine("saved revert state: " + StateFile);
        }

        // ── hide the four keys by scale ──
        sb.AppendLine("hidden by scale (never SetActive):");
        foreach (var n in HideKeys)
        {
            var k = actions.Find(n);
            if (k == null) { sb.AppendLine($"  {n}: not in the scene, nothing to hide"); continue; }
            k.localScale = Vector3.one * HiddenScale;
            EditorUtility.SetDirty(k);
            sb.AppendLine($"  {n} -> scale {HiddenScale}");
        }

        // ── the two kept keys: bigger, side by side, labels that fit ──
        var warn = new List<string>();
        var keep = new[] { (key: more, at: MoreAt, word: "More"), (key: revise, at: ReviseAt, word: "Revise") };
        foreach (var e in keep)
        {
            var saved = state.keys.FirstOrDefault(s => s.name == e.key.name);
            Vector3 baseScale = saved != null ? saved.scale : e.key.localScale;     // always from the ORIGINAL scale: re-runs do not compound
            e.key.localScale = baseScale * Grow;

            var housing = Under(e.key, "Housing3D");
            Vector3 mn, mx;
            bool ok = Bounds(housing != null ? housing : e.key, doc, out mn, out mx);
            if (ok)
            {
                var delta = new Vector3(e.at.x - (mn.x + mx.x) * 0.5f, e.at.y - (mn.y + mx.y) * 0.5f, 0f);
                e.key.position += doc.TransformVector(delta);
            }
            else warn.Add($"{e.key.name}: no housing mesh to centre on");
            EditorUtility.SetDirty(e.key);

            // Label: measured from the cap face.
            var visual = Under(e.key, "ButtonVisual");
            var cap = visual != null ? Under(visual, "Cap3D") : null;
            var label = e.key.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
            if (label == null || cap == null || !Bounds(cap, doc, out var cmn, out var cmx)) { warn.Add($"{e.key.name}: label or cap face not found, label not resized"); continue; }
            float capWWorld = (cmx.x - cmn.x) * doc.lossyScale.x, capHWorld = (cmx.y - cmn.y) * doc.lossyScale.y;
            var ls = label.transform.lossyScale;
            label.text = e.word;
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = false;
            label.overflowMode = TextOverflowModes.Overflow;           // never an ellipsis
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.alignment = TextAlignmentOptions.Center;
            label.margin = Vector4.zero;
            float boxW = capWWorld * 0.90f / Mathf.Max(1e-6f, ls.x), boxH = capHWorld * 0.70f / Mathf.Max(1e-6f, ls.y);
            label.rectTransform.sizeDelta = new Vector2(boxW, boxH);
            float k100 = ls.y * 100f;                                  // font size N at label scale 0.01 is N mm
            float wantMm = Mathf.Clamp(capHWorld * 1000f * 0.36f, MinWorldMm, WantWorldMm);
            label.fontSize = wantMm / Mathf.Max(1e-6f, k100);
            float need = label.GetPreferredValues(e.word).x;
            if (need > boxW * 0.92f && need > 0f) label.fontSize *= boxW * 0.92f / need;      // shrink to fit: never print "..."
            float shownMm = label.fontSize * k100;
            if (shownMm < MinWorldMm - 0.01f) warn.Add($"{e.key.name}: '{e.word}' needs {shownMm:F1} mm text to fit (under {MinWorldMm} mm)");
            float need2 = label.GetPreferredValues(e.word).x;
            sb.AppendLine($"  {e.key.name} -> '{e.word}': cap {capWWorld * 1000f:F0} x {capHWorld * 1000f:F0} mm, text {shownMm:F1} mm, needs {need2:F2} of {boxW:F2} (box {need2 / Mathf.Max(0.01f, boxW) * 100f:F0} percent), overflow mode Overflow");
            EditorUtility.SetDirty(label);
            if (e.key == more) panel.detailsChipLabel = label;
        }

        // ── wiring: only More and Revise, and only the right way round ──
        sb.AppendLine("poke wiring (InteractableUnityEventWrapper._whenSelect):");
        bool wiredNow = WireIfMissing(more, delivery, nameof(DocumentDeliveryController.OnToggleDetails), sb);
        wiredNow |= WireIfMissing(revise, delivery, nameof(DocumentDeliveryController.OnRequestChanges), sb);
        sb.AppendLine($"  More -> {WireOf(more, out _) ?? "(none)"}; Revise -> {WireOf(revise, out _) ?? "(none)"}{(wiredNow ? " (added)" : "")}");

        // ── panel: two keys, no voice chip, they sit where they were put ──
        panel.chipKeys = new[] { more, revise };
        panel.chipPositionsWithVoice = new[] { more.localPosition, revise.localPosition };
        panel.chipPositionsWithoutVoice = new[] { more.localPosition, revise.localPosition };
        panel.voiceChip = null;
        EditorUtility.SetDirty(panel);
        sb.AppendLine("DecisionPanel.chipKeys = [More, Revise]; voiceChip cleared (the panel no longer shows or positions a Voice key)");

        // ── the pending plates that mirrored the Approve / Reject keys ──
        if (pending != null)
            foreach (var g in GhostNames)
            {
                var t = Under(pending, g);
                if (t != null && t.gameObject.activeSelf) { t.gameObject.SetActive(false); EditorUtility.SetDirty(t.gameObject); sb.AppendLine($"  deactivated pending plate {g}"); }
            }

        foreach (var w in warn) sb.AppendLine("  WARNING: " + w);
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }

    public static string Revert()
    {
        var state = Load();
        if (state == null) return "PaperKeysTrimV1.Revert: no saved state (Library/PaperKeysTrimV1.json); nothing to do\n";
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        var doc = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t != null && t.name == "DecisionDocument");
        if (doc == null) return "PaperKeysTrimV1.Revert: DecisionDocument not found\n";
        var actions = doc.Find("Actions");
        var panel = doc.GetComponentInChildren<DecisionPanel>(true);
        if (actions == null || panel == null) return "PaperKeysTrimV1.Revert: Actions / DecisionPanel not found\n";
        var pending = doc.Find("Pending");

        foreach (var ks in state.keys)
        {
            var k = actions.Find(ks.name);
            if (k == null) continue;
            k.localScale = ks.scale; k.localPosition = ks.pos;
            var lab = k.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
            if (lab != null && ks.text != null)
            {
                lab.text = ks.text; lab.fontSize = ks.fontSize; lab.rectTransform.sizeDelta = ks.box;
                lab.overflowMode = (TextOverflowModes)ks.overflow; lab.enableAutoSizing = ks.autosize; lab.fontStyle = (FontStyles)ks.style;
                EditorUtility.SetDirty(lab);
            }
            EditorUtility.SetDirty(k);
            sb.AppendLine($"restored {ks.name}");
        }
        if (pending != null)
            foreach (var gs in state.ghosts)
            {
                var t = Under(pending, gs.name);
                if (t != null) { t.gameObject.SetActive(gs.active); EditorUtility.SetDirty(t.gameObject); }
            }

        // Panel: the original four-chip row (Why, Details, Changes, Voice) and its voice chip.
        var why = actions.Find("Btn_Ask_why"); var details = actions.Find("Btn_Details"); var changes = actions.Find("Btn_Changes"); var voice = actions.Find("Btn_Voice");
        panel.chipKeys = new[] { why, details, changes, voice };
        Vector3 P(Transform t) => t != null ? t.localPosition : Vector3.zero;
        var four = new[] { P(why), P(details), P(changes), P(voice) };
        panel.chipPositionsWithVoice = four;
        panel.chipPositionsWithoutVoice = four;   // the original three-chip spacing is BuildPaperV3's; re-run it for that
        if (state.hasVoiceChip && voice != null) panel.voiceChip = voice.gameObject;
        if (details != null) panel.detailsChipLabel = details.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
        EditorUtility.SetDirty(panel);

        try { File.Delete(StateFile); } catch { }
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        sb.AppendLine("note: the stored three-chip spacing is BuildPaperV3's; run it again if the Voice key stays hidden");
        return sb.ToString();
    }
}
