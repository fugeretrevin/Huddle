using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Phase2.Board;
using MuseDesk.Workspace;

/// <summary>
/// Text-fit pass for the owner's headset feedback: "task card text is slightly big and spills onto the
/// edges", "the agent tags overflow down off the card and are not centred".
///
/// Everything here is geometry-driven rather than constant-driven: the card face, the role strip, the
/// nameplate face and the gaze chip plate are measured from the meshes in the open scene, then every
/// label gets a rect inside the face with at least MarginMm on every side, word wrap or one line as
/// the label needs, Ellipsis on overflow and auto-size between a sane min and max. Labels keep the
/// 0.01 scale convention (font units = mm), so TMP auto-size does not collapse.
///
/// Covered: TaskCardView labels (title, role chip, priority, state, source), AgentNameplate (desk
/// nameplate: role + activity), AgentStatusChip (the sign above a character: role, words, mode) and
/// TaskDock labels (clamped inside the dock plate). Cards are instantiated at run time from the scene
/// CardTemplate (or from a prefab asset if BoardPresenter.cardPrefab is one: that is edited through
/// PrefabUtility here), so fixing the template fixes every pooled card.
///
/// Idempotent. Original label settings are saved once in Library/TextFitV1_backup.json; Revert()
/// restores them. Scene is saved. The report lists, for each label, the longest realistic string,
/// the available rect, the text size and the margin, before and after.
/// </summary>
public static class BuildTextFitV1
{
    public const float MarginMm = 2.5f;           // brief: at least ~2 mm clear on every side
    const string BackupFile = "Library/TextFitV1_backup.json";

    // Longest realistic strings (TaskCardView truncates titles at 48 characters, with three dots).
    public const string LongTitle = "Migrate the billing service to the new payment...";
    public const string LongRole = "Researcher";
    public const string LongPriority = "Urgent";
    public const string LongState = "Planned for Researcher";
    public const string SourceWords = "SCRIPTED DEMO";
    public const string LongProgress = "Reading the requirements doc";   // AgentNameplate.MaxProgressChars = 28
    public const string ChipRichSample = "<b>Researcher</b>\n<size=87%>" + LongProgress + "</size>\n<size=73%>" + SourceWords + "</size>";

    // ───────────────────────────── backup ─────────────────────────────
    [Serializable] class Entry
    {
        public string path;
        public Vector3 pos, scale;
        public Vector2 size, pivot;
        public bool auto;
        public float fs, fmin, fmax;
        public int align, wrap, overflow;
        public Vector4 margin;
    }
    [Serializable] class Backup { public List<Entry> items = new List<Entry>(); }

    static Backup _bk;
    static string _prefix = "";

    static Backup LoadBackup()
    {
        try
        {
            if (File.Exists(BackupFile)) return JsonUtility.FromJson<Backup>(File.ReadAllText(BackupFile)) ?? new Backup();
        }
        catch { }
        return new Backup();
    }

    static void SaveBackup()
    {
        try { File.WriteAllText(BackupFile, JsonUtility.ToJson(_bk)); } catch { }
    }

    static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (var x = t; x != null; x = x.parent) parts.Add(x.name + "[" + x.GetSiblingIndex() + "]");
        parts.Reverse();
        return _prefix + string.Join("/", parts);
    }

    static void Record(TMP_Text t)
    {
        string p = PathOf(t.transform);
        foreach (var e in _bk.items) if (e.path == p) return;          // keep the very first (original) state
        var rt = t.rectTransform;
        _bk.items.Add(new Entry
        {
            path = p, pos = t.transform.localPosition, scale = t.transform.localScale,
            size = rt.sizeDelta, pivot = rt.pivot, auto = t.enableAutoSizing,
            fs = t.fontSize, fmin = t.fontSizeMin, fmax = t.fontSizeMax,
            align = (int)t.alignment, wrap = (int)t.textWrappingMode, overflow = (int)t.overflowMode, margin = t.margin,
        });
    }

    static Transform Resolve(IList<Transform> roots, string path)
    {
        var parts = path.Split('/');
        Transform cur = null;
        for (int i = 0; i < parts.Length; i++)
        {
            int br = parts[i].LastIndexOf('[');
            if (br < 0) return null;
            string name = parts[i].Substring(0, br);
            int idx;
            if (!int.TryParse(parts[i].Substring(br + 1).TrimEnd(']'), out idx)) return null;
            if (i == 0)
            {
                cur = null;
                foreach (var r in roots) if (r != null && r.name == name && r.GetSiblingIndex() == idx) { cur = r; break; }
            }
            else cur = idx < cur.childCount && cur.GetChild(idx).name == name ? cur.GetChild(idx) : null;
            if (cur == null) return null;
        }
        return cur;
    }

    // ───────────────────────────── geometry helpers ─────────────────────────────
    /// <summary>Local-to-owner matrix built from local TRS only (independent of the owner's own scale,
    /// which may be zero while the object is hidden by scale).</summary>
    static Matrix4x4 Chain(Transform from, Transform owner)
    {
        var m = Matrix4x4.identity;
        for (var x = from; x != null && x != owner; x = x.parent)
            m = Matrix4x4.TRS(x.localPosition, x.localRotation, x.localScale) * m;
        return m;
    }

    public struct Face { public bool ok; public Vector2 mn, mx; public string how; }

    static bool LocalRect(Renderer r, Transform owner, out Vector2 mn, out Vector2 mx)
    {
        mn = mx = Vector2.zero;
        var mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return false;
        var b = mf.sharedMesh.bounds;
        var m = Chain(r.transform, owner);
        mn = new Vector2(float.MaxValue, float.MaxValue); mx = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var p = m.MultiplyPoint3x4(c);
            mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p);
        }
        return true;
    }

    static readonly Regex Banned = new Regex("rim|edge|housing|strip|selection|tint|shadow|pocket|marker|chip|brass|ink|glow|halo|key|pad|stamp|outline|border|cardplate", RegexOptions.IgnoreCase);

    static bool IsText(Renderer r) => r.GetComponent<TMP_Text>() != null || r.GetComponent<TMP_SubMesh>() != null;

    static Face FindFace(Transform owner, string preferName, HashSet<Renderer> exclude)
    {
        var f = new Face();
        float best = -1f;
        foreach (var r in owner.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!r.enabled || IsText(r) || (exclude != null && exclude.Contains(r))) continue;
            if (!LocalRect(r, owner, out var mn, out var mx)) continue;
            bool pref = preferName != null && r.name.IndexOf(preferName, StringComparison.OrdinalIgnoreCase) >= 0;
            if (!pref && Banned.IsMatch(r.name)) continue;
            float area = (mx.x - mn.x) * (mx.y - mn.y) * (pref ? 1000f : 1f);
            if (area > best) { best = area; f = new Face { ok = true, mn = mn, mx = mx, how = r.name + (pref ? " (named)" : " (largest)") }; }
        }
        return f;
    }

    static bool FindStrip(Transform owner, Renderer hint, out Vector2 mn, out Vector2 mx, out string name)
    {
        mn = mx = Vector2.zero; name = null;
        if (hint != null && hint.enabled && LocalRect(hint, owner, out mn, out mx)) { name = hint.name; return true; }
        foreach (var r in owner.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!r.enabled || IsText(r)) continue;
            if (r.name.IndexOf("strip", StringComparison.OrdinalIgnoreCase) < 0 && r.name.IndexOf("OwnerChip", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (LocalRect(r, owner, out mn, out mx)) { name = r.name; return true; }
        }
        return false;
    }

    /// <summary>World millimetres per owner-local unit. If the owner is hidden by scale, use the
    /// scale it will have when shown (assumed 1 relative to its parent).</summary>
    static float MmPerUnit(Transform owner, float shownScale, out bool assumed)
    {
        float s = Mathf.Abs(owner.lossyScale.y);
        assumed = false;
        if (s > 1e-4f) return s * 1000f;
        assumed = true;
        float ps = owner.parent != null ? Mathf.Abs(owner.parent.lossyScale.y) : 1f;
        return Mathf.Max(1e-4f, ps * shownScale) * 1000f;
    }

    // ───────────────────────────── label fitting ─────────────────────────────
    /// <summary>Put the label's rect at <paramref name="r"/> (owner-local units), pivot 0.5, and set
    /// the text settings. minMm / maxMm are world millimetres of font size (equal = fixed size).</summary>
    static void Fit(TMP_Text t, Transform owner, float U, Rect r, float minMm, float maxMm, bool wrap,
                    HorizontalAlignmentOptions? h, VerticalAlignmentOptions? v)
    {
        Record(t);
        var tr = t.transform;
        if (Mathf.Abs(tr.localScale.y - 0.01f) > 0.0005f || Mathf.Abs(tr.localScale.x - 0.01f) > 0.0005f)
            tr.localScale = new Vector3(0.01f, 0.01f, 0.01f);               // font units = mm: TMP auto-size stays stable

        var m = Chain(tr, owner);
        var sc = m.lossyScale;
        float sx = Mathf.Max(1e-6f, Mathf.Abs(sc.x)), sy = Mathf.Max(1e-6f, Mathf.Abs(sc.y));

        var rt = t.rectTransform;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(r.width / sx, r.height / sy);

        float curZ = m.MultiplyPoint3x4(Vector3.zero).z;
        var pm = Chain(tr.parent, owner);
        tr.localPosition = pm.inverse.MultiplyPoint3x4(new Vector3(r.center.x, r.center.y, curZ));

        float perMm = FontUnitsPerMm(sy, U);
        t.enableAutoSizing = minMm < maxMm - 0.01f;
        t.fontSizeMin = minMm * perMm;
        t.fontSizeMax = maxMm * perMm;
        t.fontSize = maxMm * perMm;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.margin = Vector4.zero;
        if (h.HasValue) t.horizontalAlignment = h.Value;
        if (v.HasValue) t.verticalAlignment = v.Value;
        EditorUtility.SetDirty(t);
    }

    /// <summary>TMP world text: a rect unit is sy*U millimetres, but a font unit is only a tenth of that
    /// (TMP scales perspective text by 0.1). At label scale 0.01 under unscaled parents, 1 font unit =
    /// 1 mm and 1 rect unit = 1 cm.</summary>
    static float FontUnitsPerMm(float labelScaleY, float mmPerOwnerUnit) => 10f / (labelScaleY * mmPerOwnerUnit);

    /// <summary>Width in mm that <paramref name="text"/> needs on one line at fontMm.</summary>
    static float PrefWidthMm(TMP_Text t, Transform owner, float U, string text, float fontMm)
    {
        var m = Chain(t.transform, owner);
        float sx = Mathf.Abs(m.lossyScale.x), sy = Mathf.Abs(m.lossyScale.y);
        bool a = t.enableAutoSizing; float fs = t.fontSize; var wrap = t.textWrappingMode;
        t.enableAutoSizing = false; t.fontSize = fontMm * FontUnitsPerMm(sy, U); t.textWrappingMode = TextWrappingModes.NoWrap;
        float w = 0f;
        try { w = t.GetPreferredValues(text).x * sx * U; } catch { }
        t.enableAutoSizing = a; t.fontSize = fs; t.textWrappingMode = wrap;
        return w;
    }

    static float FontMm(TMP_Text t, Transform owner, float U, bool max)
    {
        var m = Chain(t.transform, owner);
        float sy = Mathf.Abs(m.lossyScale.y);
        float v = t.enableAutoSizing ? (max ? t.fontSizeMax : t.fontSizeMin) : t.fontSize;
        return v / FontUnitsPerMm(sy, U);
    }

    // ───────────────────────────── targets ─────────────────────────────
    public class Label { public TMP_Text t; public string sample; public string name; }
    public class Target
    {
        public string kind, name;
        public Transform owner;
        public float U;                 // mm per owner unit
        public bool assumedScale;
        public Face face;
        public List<Label> labels = new List<Label>();
        public string note;
        internal Component comp;
    }

    static Label L(TMP_Text t, string name, string sample) => new Label { t = t, name = name, sample = sample };

    static bool IsSceneObject(Component c) => c != null && c.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(c);

    public static List<Target> CollectTargets(Transform searchRoot = null)
    {
        var list = new List<Target>();
        foreach (var v in UnityEngine.Object.FindObjectsByType<TaskCardView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (IsSceneObject(v)) list.Add(CardTarget(v));
        foreach (var n in UnityEngine.Object.FindObjectsByType<AgentNameplate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (IsSceneObject(n)) list.Add(NameplateTarget(n));
        foreach (var c in UnityEngine.Object.FindObjectsByType<AgentStatusChip>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (IsSceneObject(c)) list.Add(ChipTarget(c));
        foreach (var d in UnityEngine.Object.FindObjectsByType<TaskDock>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (IsSceneObject(d)) list.Add(DockTarget(d));
        return list;
    }

    public static Target CardTarget(TaskCardView v)
    {
        var tg = new Target { kind = "Card", name = v.name, owner = v.transform, comp = v };
        tg.U = MmPerUnit(v.transform, 1f, out tg.assumedScale);
        var ex = new HashSet<Renderer>();
        if (v.edgePlate != null) ex.Add(v.edgePlate);
        if (v.statusMarker != null) ex.Add(v.statusMarker);
        tg.face = FindFace(v.transform, "Card_Body", ex);
        tg.labels.Add(L(v.roleLabel, "role", LongRole));
        tg.labels.Add(L(v.priorityLabel, "priority", LongPriority));
        tg.labels.Add(L(v.titleLabel, "title", LongTitle));
        tg.labels.Add(L(v.stateLabel, "state", LongState));
        tg.labels.Add(L(v.sourceLabel, "source", SourceWords));
        return tg;
    }

    public static Target NameplateTarget(AgentNameplate n)
    {
        var tg = new Target { kind = "Nameplate", name = n.name, owner = n.transform, comp = n };
        tg.U = MmPerUnit(n.transform, Mathf.Abs(n.shownScale.y), out tg.assumedScale);
        tg.face = FindFace(n.transform, "Nameplate_Plate", null);
        tg.labels.Add(L(n.roleText, "role", LongRole));
        tg.labels.Add(L(n.activityText, "activity", LongProgress));
        return tg;
    }

    public static Target ChipTarget(AgentStatusChip c)
    {
        // The chip is scaled at run time for viewing distance; its rect and text scale together, so the
        // margin is measured at scale 1 (plate-local millimetres). Origin: bottom centre.
        var tg = new Target { kind = "GazeChip", name = c.name, owner = c.transform, comp = c, U = 1000f };
        tg.face = new Face { ok = true, mn = new Vector2(-c.width * 0.5f, 0f), mx = new Vector2(c.width * 0.5f, c.height), how = "AgentStatusChip width x height" };
        tg.labels.Add(L(c.label, "text", ChipRichSample));
        return tg;
    }

    public static Target DockTarget(TaskDock d)
    {
        var tg = new Target { kind = "Dock", name = d.name, owner = d.transform, comp = d };
        tg.U = MmPerUnit(d.transform, 1f, out tg.assumedScale);
        tg.face = FindFace(d.transform, "TaskDock_Plate", null);
        tg.labels.Add(L(d.summaryLabel, "summary", "Task board - 12 ready"));
        tg.labels.Add(L(d.titleLabel, "title", LongTitle));
        tg.labels.Add(L(d.stateLabel, "state", "Ready to start on Researcher"));
        tg.labels.Add(L(d.modeLabel, "mode", SourceWords));
        return tg;
    }

    // ───────────────────────────── layouts ─────────────────────────────
    static void LayoutCard(Target tg, StringBuilder log)
    {
        var v = (TaskCardView)tg.comp;
        var o = tg.owner; float U = tg.U, mm = 1f / U, M = MarginMm * mm;
        var f = tg.face;
        Vector2 imn = f.mn + new Vector2(M, M), imx = f.mx - new Vector2(M, M);

        // Role strip (or a synthetic 7 mm band at the top of the face when the art has none).
        var tint = v.GetComponent<CardRoleTint>();
        float sy0, sy1; string stripName;
        if (FindStrip(o, tint != null ? tint.headerStrip : null, out var smn, out var smx, out stripName) && smx.y - smn.y > 4f * mm)
        { sy0 = Mathf.Max(smn.y, f.mn.y); sy1 = Mathf.Min(smx.y, f.mx.y); }
        else { sy1 = f.mx.y; sy0 = sy1 - 7f * mm; stripName = "(synthetic top band)"; }
        // The strip runs to the card's top edge: keep the chip text the same margin clear of that edge
        // (headset feedback: chip text sat 1.2 to 1.8 mm from the edge and read as spilling over it).
        sy1 = Mathf.Min(sy1, f.mx.y - M);
        float chipH = sy1 - sy0, chipCy = (sy0 + sy1) * 0.5f;
        float chipMax = Mathf.Clamp(chipH * U / 1.35f, 4f, 5.5f);
        float chipMin = Mathf.Min(chipMax, 4f);

        var cr = new Rect(imn.x, chipCy - chipH * 0.5f, imx.x - imn.x, chipH);
        if (v.priorityLabel != null)
            Fit(v.priorityLabel, o, U, cr, chipMin, chipMax, false, HorizontalAlignmentOptions.Right, VerticalAlignmentOptions.Geometry);
        float wPri = v.priorityLabel != null ? PrefWidthMm(v.priorityLabel, o, U, LongPriority, chipMin) : 0f;
        float wRole = 0f; bool centred = true;
        if (v.roleLabel != null)
        {
            Fit(v.roleLabel, o, U, cr, chipMin, chipMax, false, HorizontalAlignmentOptions.Center, VerticalAlignmentOptions.Geometry);
            wRole = PrefWidthMm(v.roleLabel, o, U, LongRole, chipMin);
            float innerW = (imx.x - imn.x) * U;
            // Centred role needs room for the priority word on the right (role half + gap + priority).
            centred = wRole * 0.5f + 2f + wPri <= innerW * 0.5f;
            if (!centred)
            {
                var left = new Rect(imn.x, cr.y, (imx.x - imn.x) - (wPri + 2f) * mm, chipH);
                Fit(v.roleLabel, o, U, left, chipMin, chipMax, false, HorizontalAlignmentOptions.Left, VerticalAlignmentOptions.Geometry);
            }
        }

        // Body: source line at the bottom, then the state line, the title takes the rest under the strip.
        float gap = 0.8f * mm, srcH = 5.8f * mm, stateH = 7.4f * mm;
        float y = imn.y;
        float titleTop = sy0 - 1.0f * mm;
        float titleH = titleTop - (y + srcH + gap + stateH + gap);
        float stateMax = 6.2f, srcMax = 5.0f;
        if (titleH < 9f * mm)
        {   // very short card: tighten the lower lines rather than crush the title
            srcH = 5.0f * mm; stateH = 6.2f * mm; stateMax = 5.2f; srcMax = 4.4f;
            titleH = titleTop - (y + srcH + gap + stateH + gap);
        }
        var hSrc = v.sourceLabel != null ? v.sourceLabel.horizontalAlignment : HorizontalAlignmentOptions.Left;
        var hState = v.stateLabel != null ? v.stateLabel.horizontalAlignment : HorizontalAlignmentOptions.Left;
        var hTitle = v.titleLabel != null ? v.titleLabel.horizontalAlignment : HorizontalAlignmentOptions.Left;
        float w = imx.x - imn.x;
        if (v.sourceLabel != null)
            Fit(v.sourceLabel, o, U, new Rect(imn.x, y, w, srcH), Mathf.Min(srcMax, 4.4f), srcMax, false, hSrc, VerticalAlignmentOptions.Geometry);
        y += srcH + gap;
        if (v.stateLabel != null)
            Fit(v.stateLabel, o, U, new Rect(imn.x, y, w, stateH), 4.6f, stateMax, false, hState, VerticalAlignmentOptions.Geometry);
        y += stateH + gap;
        float titleHmm = titleH * U;
        float titleMax = Mathf.Clamp(titleHmm / 2.5f, 5f, 9.5f);
        float titleMin = Mathf.Min(titleMax, Mathf.Max(5f, titleMax * 0.7f));
        if (v.titleLabel != null)
            Fit(v.titleLabel, o, U, new Rect(imn.x, y, w, Mathf.Max(titleH, 4f * mm)), titleMin, titleMax, true, hTitle, VerticalAlignmentOptions.Top);

        log.AppendLine("  " + o.name + ": face " + f.how + " " + ((f.mx.x - f.mn.x) * U).ToString("F1") + " x " + ((f.mx.y - f.mn.y) * U).ToString("F1") +
                       " mm; strip " + stripName + " " + (chipH * U).ToString("F1") + " mm tall; role " + (centred ? "centred" : "left (priority word needs the room)") +
                       "; fonts mm: chips " + chipMin.ToString("F1") + "-" + chipMax.ToString("F1") + ", title " + titleMin.ToString("F1") + "-" + titleMax.ToString("F1") +
                       " in " + (w * U).ToString("F1") + " x " + titleHmm.ToString("F1") + ", state 4.6-" + stateMax.ToString("F1") + ", source " + srcMax.ToString("F1") +
                       (titleHmm < 9f ? "  WARNING: title area only " + titleHmm.ToString("F1") + " mm tall" : ""));
    }

    static void LayoutNameplate(Target tg, StringBuilder log)
    {
        var n = (AgentNameplate)tg.comp;
        var o = tg.owner; float U = tg.U, mm = 1f / U, M = MarginMm * mm;
        var f = tg.face;
        Vector2 imn = f.mn + new Vector2(M, M), imx = f.mx - new Vector2(M, M);
        string stripNote = "no strip";
        if (FindStrip(o, null, out var smn, out var smx, out var sname) && smx.x < (f.mn.x + f.mx.x) * 0.5f)
        {
            imn.x = Mathf.Max(imn.x, smx.x + M);                // the colour strip on the left edge is not text space
            stripNote = "text starts " + MarginMm.ToString("F1") + " mm right of " + sname;
        }
        float avail = imx.y - imn.y, gap = 0.8f * mm;
        float roleH = (avail - gap) * 0.55f, actH = (avail - gap) * 0.45f;
        float roleMax = Mathf.Min(6.5f, roleH * U / 1.25f), actMax = Mathf.Min(5.5f, actH * U / 1.25f);
        float roleMin = Mathf.Min(roleMax, 5.0f), actMin = Mathf.Min(actMax, 4.4f);
        float cx = (imn.x + imx.x) * 0.5f;
        float w = imx.x - imn.x;
        // Block (role over activity) centred vertically on the plate face.
        float top = (imn.y + imx.y) * 0.5f + (roleH + gap + actH) * 0.5f;
        if (n.roleText != null)
            Fit(n.roleText, o, U, new Rect(imn.x, top - roleH, w, roleH), roleMin, roleMax, false, HorizontalAlignmentOptions.Center, VerticalAlignmentOptions.Geometry);
        if (n.activityText != null)
            Fit(n.activityText, o, U, new Rect(imn.x, top - roleH - gap - actH, w, actH), actMin, actMax, false, HorizontalAlignmentOptions.Center, VerticalAlignmentOptions.Geometry);
        log.AppendLine("  " + o.name + ": face " + f.how + " " + ((f.mx.x - f.mn.x) * U).ToString("F1") + " x " + ((f.mx.y - f.mn.y) * U).ToString("F1") + " mm; " + stripNote +
                       "; text rect " + (w * U).ToString("F1") + " mm wide; role " + roleMin.ToString("F1") + "-" + roleMax.ToString("F1") + " mm, activity " +
                       actMin.ToString("F1") + "-" + actMax.ToString("F1") + " mm; both centred, one line, Ellipsis");
    }

    static void LayoutChip(Target tg, StringBuilder log)
    {
        var c = (AgentStatusChip)tg.comp;
        if (c.label == null) return;
        float U = tg.U, mm = 1f / U, M = MarginMm * mm;
        float stripRight = -c.width * 0.5f + 0.0058f + 0.0016f;               // AgentTalkRig.EnsureChip: strip centre and half width
        float left = stripRight + M;
        float right = c.width * 0.5f - 0.0035f;                              // the plate corners are rounded (4.2 mm): a little extra
        float bottom = M, top = c.height - M;
        // Three lines (role 100%, words 87%, mode 73%) auto-size together between 4.0 and 7.0 mm for the role line.
        Fit(c.label, tg.owner, U, new Rect(left, bottom, right - left, top - bottom), 4.0f, 7.0f, false, HorizontalAlignmentOptions.Center, VerticalAlignmentOptions.Geometry);
        log.AppendLine("  " + tg.owner.name + ": plate " + (c.width * 1000f).ToString("F1") + " x " + (c.height * 1000f).ToString("F1") + " mm; text rect " +
                       ((right - left) * U).ToString("F1") + " x " + ((top - bottom) * U).ToString("F1") + " mm clear of the strip and edges by >= " + MarginMm.ToString("F1") +
                       " mm; centred both ways; 4.0-7.0 mm (was Left aligned, min 6.3)");
    }

    static void LayoutDock(Target tg, StringBuilder log)
    {
        var d = (TaskDock)tg.comp;
        if (!tg.face.ok) { log.AppendLine("  " + tg.owner.name + ": plate face not found, left alone"); return; }
        float U = tg.U, mm = 1f / U, M = MarginMm * mm;
        Vector2 imn = tg.face.mn + new Vector2(M, M), imx = tg.face.mx - new Vector2(M, M);
        int clamped = 0;
        foreach (var lb in tg.labels)
        {
            var t = lb.t; if (t == null) continue;
            var chain = Chain(t.transform, tg.owner);
            var rc = t.rectTransform.rect;
            var a = chain.MultiplyPoint3x4(new Vector3(rc.xMin, rc.yMin, 0f));
            var b = chain.MultiplyPoint3x4(new Vector3(rc.xMax, rc.yMax, 0f));
            Vector2 mn = Vector2.Min(a, b), mx = Vector2.Max(a, b);
            Vector2 nmn = Vector2.Max(mn, imn), nmx = Vector2.Min(mx, imx);
            if (nmx.x <= nmn.x || nmx.y <= nmn.y) continue;
            float maxMm = Mathf.Max(4f, FontMm(t, tg.owner, U, true));
            float minMm = Mathf.Min(maxMm, Mathf.Max(4f, t.enableAutoSizing ? FontMm(t, tg.owner, U, false) : maxMm * 0.75f));
            bool wrap = t.textWrappingMode != TextWrappingModes.NoWrap;
            if (nmn != mn || nmx != mx) clamped++;
            Fit(t, tg.owner, U, Rect.MinMaxRect(nmn.x, nmn.y, nmx.x, nmx.y), minMm, maxMm, wrap, null, null);
        }
        log.AppendLine("  " + tg.owner.name + ": plate " + tg.face.how + " " + ((tg.face.mx.x - tg.face.mn.x) * U).ToString("F1") + " x " + ((tg.face.mx.y - tg.face.mn.y) * U).ToString("F1") +
                       " mm; " + clamped + " label rect(s) pulled inside the " + MarginMm.ToString("F1") + " mm margin; auto-size now shrinks to 75% before it ellipsises");
    }

    static void Layout(Target tg, StringBuilder log)
    {
        if (!tg.face.ok) { log.AppendLine("  " + tg.kind + " " + tg.owner.name + ": face not found, left alone"); return; }
        switch (tg.kind)
        {
            case "Card": LayoutCard(tg, log); break;
            case "Nameplate": LayoutNameplate(tg, log); break;
            case "GazeChip": LayoutChip(tg, log); break;
            case "Dock": LayoutDock(tg, log); break;
        }
    }

    // ───────────────────────────── measurement ─────────────────────────────
    /// <summary>One table: for each label, the string, its rect, the text at the smallest auto-size,
    /// the auto-sized result and the clear margin to the face, in world mm.</summary>
    public static string MeasureTargets(List<Target> targets, bool samples)
    {
        var sb = new StringBuilder();
        sb.AppendLine("label | text | rect mm (w x h) | text at min size (w x h) | auto-sized font mm | rendered (w x h) mm | margin to face mm | verdict");
        foreach (var tg in targets)
        {
            sb.AppendLine("[" + tg.kind + " " + PathName(tg.owner) + "]" + (tg.assumedScale ? " (owner hidden by scale: shown scale assumed)" : "") +
                          (tg.face.ok ? "" : "  FACE NOT FOUND: margins unavailable"));
            foreach (var lb in tg.labels)
            {
                if (lb.t == null) { sb.AppendLine("  " + lb.name + " | (no label)"); continue; }
                sb.AppendLine("  " + MeasureLabel(tg, lb, samples));
            }
        }
        return sb.ToString();
    }

    static string PathName(Transform t)
    {
        var n = t.name;
        if (t.parent != null) n = t.parent.name + "/" + n;
        return n;
    }

    static string MeasureLabel(Target tg, Label lb, bool samples)
    {
        var t = lb.t;
        string saved = t.text;
        string text = samples && lb.sample != null ? lb.sample : saved;
        bool wasEnabled = t.enabled;
        try
        {
            var m = Chain(t.transform, tg.owner);
            float sx = Mathf.Abs(m.lossyScale.x), sy = Mathf.Abs(m.lossyScale.y);
            float U = tg.U;
            var rt = t.rectTransform;
            float rectW = rt.rect.width * sx * U, rectH = rt.rect.height * sy * U;

            if (samples && lb.sample != null && saved != lb.sample) t.text = lb.sample;
            if (!t.enabled) t.enabled = true;

            // Size at the smallest auto-size (the worst case the label can still shrink to).
            bool a = t.enableAutoSizing; float fs = t.fontSize;
            float minFont = a ? t.fontSizeMin : t.fontSize;
            t.enableAutoSizing = false; t.fontSize = minFont;
            Vector2 pref = Vector2.zero;
            try { pref = t.GetPreferredValues(text, t.textWrappingMode == TextWrappingModes.NoWrap ? float.PositiveInfinity : rt.rect.width, float.PositiveInfinity); } catch { }
            t.enableAutoSizing = a; t.fontSize = fs;
            float pW = pref.x * sx * U, pH = pref.y * sy * U;

            // Rendered result with the real auto-size.
            string rendered = "n/a"; string margin = "n/a"; string verdict = "";
            float fontMm = 0f; bool truncated = false;
            try
            {
                t.ForceMeshUpdate(true, true);
                fontMm = t.fontSize / FontUnitsPerMm(sy, U);
                truncated = t.isTextTruncated;
                var bnd = t.textBounds;
                float rw = bnd.size.x * sx * U, rh = bnd.size.y * sy * U;
                rendered = rw.ToString("F1") + " x " + rh.ToString("F1");
                if (tg.face.ok && text.Length > 0)
                {
                    var p0 = m.MultiplyPoint3x4(new Vector3(bnd.min.x, bnd.min.y, 0f));
                    var p1 = m.MultiplyPoint3x4(new Vector3(bnd.max.x, bnd.max.y, 0f));
                    Vector2 mn = Vector2.Min(p0, p1), mx = Vector2.Max(p0, p1);
                    float mg = Mathf.Min(Mathf.Min(mn.x - tg.face.mn.x, tg.face.mx.x - mx.x), Mathf.Min(mn.y - tg.face.mn.y, tg.face.mx.y - mx.y)) * U;
                    margin = mg.ToString("F1");
                    verdict = mg >= 2f ? "OK" : (mg >= 0f ? "TIGHT (<2 mm)" : "OVERFLOWS THE FACE");
                }
            }
            catch (Exception) { }
            if (truncated) verdict += (verdict.Length > 0 ? ", " : "") + "ellipsised";
            bool fitsMin = pW <= rectW + 0.3f && (t.textWrappingMode == TextWrappingModes.NoWrap ? pH <= rectH + 0.3f : pH <= rectH + 0.3f);
            if (!fitsMin) verdict += (verdict.Length > 0 ? ", " : "") + "longest string exceeds its rect even at min size (ellipsis)";

            string shown = text.Replace("\n", " / ");
            if (shown.Length > 44) shown = shown.Substring(0, 44) + "..";
            return lb.name + " | '" + shown + "' | " + rectW.ToString("F1") + " x " + rectH.ToString("F1") + " | " + pW.ToString("F1") + " x " + pH.ToString("F1") +
                   " | " + fontMm.ToString("F1") + " | " + rendered + " | " + margin + " | " + verdict;
        }
        finally
        {
            if (samples && t.text != saved) t.text = saved;
            if (t.enabled != wasEnabled) t.enabled = wasEnabled;
        }
    }

    // ───────────────────────────── entry points ─────────────────────────────
    public static string Apply()
    {
        var sb = new StringBuilder("=== BuildTextFitV1.Apply ===\n");
        _bk = LoadBackup(); _prefix = "";
        var targets = CollectTargets();
        sb.AppendLine("found " + targets.Count + " text owners in the open scene (cards, nameplates, gaze chips, docks)");

        sb.AppendLine();
        sb.AppendLine("BEFORE (longest realistic strings, world mm; chips measured at plate scale 1):");
        sb.Append(MeasureTargets(targets, true));

        sb.AppendLine();
        sb.AppendLine("layout:");
        foreach (var tg in targets) { try { Layout(tg, sb); } catch (Exception e) { sb.AppendLine("  " + tg.owner.name + ": EXCEPTION " + e.GetType().Name + ": " + e.Message); } }

        // Card prefab asset, if BoardPresenter instantiates one rather than the scene template.
        foreach (var bp in UnityEngine.Object.FindObjectsByType<BoardPresenter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (bp.cardPrefab == null || !EditorUtility.IsPersistent(bp.cardPrefab)) continue;
            string ap = AssetDatabase.GetAssetPath(bp.cardPrefab);
            var contents = PrefabUtility.LoadPrefabContents(ap);
            try
            {
                _prefix = "asset:" + ap + "|";
                var v = contents.GetComponent<TaskCardView>();
                if (v != null)
                {
                    var tg = CardTarget(v);
                    sb.AppendLine("card prefab asset " + ap + ":");
                    sb.AppendLine(MeasureTargets(new List<Target> { tg }, true));
                    Layout(tg, sb);
                    PrefabUtility.SaveAsPrefabAsset(contents, ap);
                }
            }
            finally { _prefix = ""; PrefabUtility.UnloadPrefabContents(contents); }
        }
        SaveBackup();

        sb.AppendLine();
        sb.AppendLine("AFTER:");
        sb.Append(MeasureTargets(CollectTargets(), true));

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        sb.AppendLine("scene saved. Original label settings are in " + BackupFile + ". Revert: BuildTextFitV1.Revert");
        return sb.ToString();
    }

    public static string Revert()
    {
        var sb = new StringBuilder("=== BuildTextFitV1.Revert ===\n");
        _bk = LoadBackup();
        if (_bk.items.Count == 0) return sb.Append("no backup file; nothing to revert\n").ToString();
        var scene = EditorSceneManager.GetActiveScene();
        var roots = new List<Transform>();
        foreach (var go in scene.GetRootGameObjects()) roots.Add(go.transform);
        int done = 0, missed = 0;
        var byAsset = new Dictionary<string, List<Entry>>();
        foreach (var e in _bk.items)
        {
            if (e.path.StartsWith("asset:"))
            {
                string ap = e.path.Substring(6, e.path.IndexOf('|') - 6);
                if (!byAsset.TryGetValue(ap, out var l)) byAsset[ap] = l = new List<Entry>();
                l.Add(e);
                continue;
            }
            if (Restore(Resolve(roots, e.path), e)) done++; else missed++;
        }
        foreach (var kv in byAsset)
        {
            var contents = PrefabUtility.LoadPrefabContents(kv.Key);
            try
            {
                var r = new List<Transform> { contents.transform };
                foreach (var e in kv.Value)
                {
                    string rel = e.path.Substring(e.path.IndexOf('|') + 1);
                    if (Restore(Resolve(r, rel), e)) done++; else missed++;
                }
                PrefabUtility.SaveAsPrefabAsset(contents, kv.Key);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        try { File.Delete(BackupFile); } catch { }
        sb.AppendLine("restored " + done + " labels, " + missed + " not found (renamed or removed since). Scene saved.");
        return sb.ToString();
    }

    static bool Restore(Transform tr, Entry e)
    {
        var t = tr != null ? tr.GetComponent<TMP_Text>() : null;
        if (t == null) return false;
        tr.localPosition = e.pos; tr.localScale = e.scale;
        var rt = t.rectTransform; rt.pivot = e.pivot; rt.sizeDelta = e.size;
        t.enableAutoSizing = e.auto; t.fontSize = e.fs; t.fontSizeMin = e.fmin; t.fontSizeMax = e.fmax;
        t.alignment = (TextAlignmentOptions)e.align;
        t.textWrappingMode = (TextWrappingModes)e.wrap; t.overflowMode = (TextOverflowModes)e.overflow; t.margin = e.margin;
        EditorUtility.SetDirty(t);
        return true;
    }

    /// <summary>Measurement only (no changes), edit mode: same table as Apply's BEFORE section.</summary>
    public static string Measure()
    {
        return "=== BuildTextFitV1.Measure ===\n" + MeasureTargets(CollectTargets(), true);
    }
}
