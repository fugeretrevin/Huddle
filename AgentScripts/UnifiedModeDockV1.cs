using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using Oculus.Interaction;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Ui;
using MuseDesk.Workspace;

/// <summary>
/// rc8 feedback: ONE place selector. The mode console (Spatial/NearUserRig/Panel_Mode/ModeConsole) becomes the Spaces
/// dock: Desk | Floor | Ceiling | Wall in one row, and the Wall's context keys in the same context row and visual language
/// (Next spot / Place / Cancel are shared by every preview; Full or Compact for the wall layout; Move, Select, Manager while
/// the office is on the wall). The wall host then builds no floating dock of its own (the director tells it at Start).
///
/// What it does, idempotently:
///   - widens the console by one key column (new meshes UnifiedDock_* for the body, inlay, edge line, the two wells and the
///     status lip; the ModeConsole_* meshes on disk are untouched, so Revert puts them back): the left group (Focus) moves
///     half a column left, the mic slot half a column right, the rows stay centred;
///   - clones Console_Ceiling into Console_Wall (with a wall icon) and Indicator_Ceiling into Indicator_Wall; clones context
///     keys into Console_Layout, Console_Move, Console_Select, Console_Manager (saved hidden by scale), each wired to its
///     ModeConsole method;
///   - one short word per key ("Place", "Next spot", "Bring", ...), every dock label auto-sized and never ellipsised; the
///     status line reads "Spaces" at the desk;
///   - wires ModeConsole.wallHostBehaviour, NearUserKit start-up reach (desk placement, director) and the panel footprint.
/// Feature flags are untouched. Saves the scene once.
///
///   unity command script run --file AgentScripts/UnifiedModeDockV1.cs --entry UnifiedModeDockV1.Apply
///   (UnifiedModeDockV1.Revert puts the three-place console back and removes the keys it created.)
/// Play Mode: AgentScripts/UnifiedDockDrive.cs.
/// </summary>
public static class UnifiedModeDockV1
{
    const string GenDir = "Assets/MuseDesk/Art/Generated";
    const float Hidden = 0.001f;
    const float CornerRadius = 0.009f, Bevel = 0.0015f, NearThickness = 0.008f, TiltDeg = 12f;
    const float LipHeight = 0.013f, LipDepth = 0.004f, WellRadius = 0.004f, Inset = 0.0025f;
    const int CornerSegs = 6, BevelSegs = 4;
    const float LabelMinMm = 5.5f;

    static readonly string[] Regenerated = { "Body", "Inlay", "EdgeLine", "StatusLip", "SegmentWell", "ContextWell" };
    static readonly string[] Created = { "Console_Wall", "Indicator_Wall", "Console_Layout", "Console_Move", "Console_Select", "Console_Manager" };

    static string P(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
    static float Safe(float v) => Mathf.Abs(v) < 1e-9f ? 1e-9f : v;

    // ───────────────────────────── mesh building (BuildModeConsoleV1's recipe) ─────────────────────────────

    class MB
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> tris = new List<int>();
        public int Add(Vector3 p, Vector3 normal, Vector2 t) { v.Add(p); n.Add(normal); uv.Add(t); return v.Count - 1; }
        public void Tri(int a, int b, int c)
        {
            Vector3 g = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (g.sqrMagnitude < 1e-20f) return;
            if (Vector3.Dot(g, n[a] + n[b] + n[c]) < 0f) { int t = b; b = c; c = t; }
            tris.Add(a); tris.Add(b); tris.Add(c);
        }
        public void Band(int[] a, int[] b)
        {
            int count = a.Length;
            for (int j = 0; j < count; j++) { int k = (j + 1) % count; Tri(a[j], a[k], b[k]); Tri(a[j], b[k], b[j]); }
        }
        public void Fan(int centre, int[] ring) { for (int j = 0; j < ring.Length; j++) Tri(centre, ring[j], ring[(j + 1) % ring.Length]); }
        public void Fill(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv);
            mesh.subMeshCount = 1; mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
        }
    }

    static int[] Ring(MB mb, float cx, float cy, float hw, float hh, float r, float z, float nxy, float nz, float uvW, float uvH)
    {
        var idx = new int[4 * (CornerSegs + 1)];
        int k = 0;
        hw = Mathf.Max(hw, 0f); hh = Mathf.Max(hh, 0f);
        float cr = Mathf.Clamp(r, 0f, Mathf.Min(hw, hh));
        for (int c = 0; c < 4; c++)
        {
            float sx = (c == 0 || c == 3) ? 1f : -1f;
            float sy = c < 2 ? 1f : -1f;
            for (int s = 0; s <= CornerSegs; s++)
            {
                float a = (c * 90f + 90f * s / CornerSegs) * Mathf.Deg2Rad;
                float dx = Mathf.Cos(a), dy = Mathf.Sin(a);
                if (s == 0 || s == CornerSegs) { dx = Mathf.Round(dx); dy = Mathf.Round(dy); }
                var p = new Vector3(cx + sx * (hw - cr) + cr * dx, cy + sy * (hh - cr) + cr * dy, z);
                var normal = new Vector3(dx * nxy, dy * nxy, nz).normalized;
                var t = new Vector2((p.x - cx) / Safe(uvW) + 0.5f, (p.y - cy) / Safe(uvH) + 0.5f);
                idx[k++] = mb.Add(p, normal, t);
            }
        }
        return idx;
    }

    static Mesh Store(string name, MB mb)
    {
        string path = GenDir + "/" + name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool isNew = mesh == null;
        if (isNew) mesh = new Mesh { name = name };
        mb.Fill(mesh);
        if (isNew) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    static void FlatPlate(MB mb, float cx, float cy, float hw, float hh, float r, float z)
    {
        int centre = mb.Add(new Vector3(cx, cy, z), Vector3.back, new Vector2(0.5f, 0.5f));
        mb.Fan(centre, Ring(mb, cx, cy, hw, hh, r, z, 0f, -1f, hw * 2f, hh * 2f));
    }

    static void FlatOutline(MB mb, float hw, float hh, float r, float t, float z)
    {
        mb.Band(Ring(mb, 0f, 0f, hw, hh, r, z, 0f, -1f, hw * 2f, hh * 2f),
                Ring(mb, 0f, 0f, hw - t, hh - t, Mathf.Max(0f, r - t), z, 0f, -1f, hw * 2f, hh * 2f));
    }

    static void Slab(MB mb, float cx, float cy, float hw, float hh, float r, float b, float zFront, float zBack0, float slope)
    {
        float uw = hw * 2f, uh = hh * 2f;
        int centre = mb.Add(new Vector3(cx, cy, zFront), Vector3.back, new Vector2(0.5f, 0.5f));
        mb.Fan(centre, Ring(mb, cx, cy, hw - b, hh - b, r - b, zFront, 0f, -1f, uw, uh));
        int[] prev = null;
        for (int i = 0; i <= BevelSegs; i++)
        {
            float phi = i * Mathf.PI * 0.5f / BevelSegs;
            float d = b * (1f - Mathf.Sin(phi));
            float nxy = Mathf.Sin(phi), nz = -Mathf.Cos(phi);
            if (i == BevelSegs) { d = 0f; nxy = 1f; nz = 0f; }
            int[] ring = Ring(mb, cx, cy, hw - d, hh - d, r - d, zFront + b * (1f - Mathf.Cos(phi)), nxy, nz, uw, uh);
            if (prev != null) mb.Band(prev, ring);
            prev = ring;
        }
        int[] wall = Ring(mb, cx, cy, hw, hh, r, 0f, 1f, 0f, uw, uh);
        int[] back = Ring(mb, cx, cy, hw, hh, r, 0f, 0f, 1f, uw, uh);
        foreach (int i in wall) { Vector3 p = mb.v[i]; p.z = zBack0 + (p.y - (cy - hh)) * slope; mb.v[i] = p; }
        foreach (int i in back) { Vector3 p = mb.v[i]; p.z = zBack0 + (p.y - (cy - hh)) * slope; mb.v[i] = p; }
        mb.Band(prev, wall);
        int bc = mb.Add(new Vector3(cx, cy, zBack0 + hh * slope), Vector3.forward, new Vector2(0.5f, 0.5f));
        mb.Fan(bc, back);
    }

    static Mesh Icon(string name, params float[] rects)
    {
        var mb = new MB();
        for (int i = 0; i + 3 < rects.Length; i += 4)
        {
            float cx = rects[i], cy = rects[i + 1], hw = rects[i + 2] * 0.5f, hh = rects[i + 3] * 0.5f;
            int a = mb.Add(new Vector3(cx - hw, cy - hh, 0f), Vector3.back, new Vector2(0f, 0f));
            int b = mb.Add(new Vector3(cx + hw, cy - hh, 0f), Vector3.back, new Vector2(1f, 0f));
            int c = mb.Add(new Vector3(cx + hw, cy + hh, 0f), Vector3.back, new Vector2(1f, 1f));
            int d = mb.Add(new Vector3(cx - hw, cy + hh, 0f), Vector3.back, new Vector2(0f, 1f));
            mb.Tri(a, b, c); mb.Tri(a, c, d);
        }
        return Store(name, mb);
    }

    // ───────────────────────────── scene helpers ─────────────────────────────

    static Vector3 MeshSize(Transform t)
    {
        var f = t != null ? t.GetComponent<MeshFilter>() : null;
        return f != null && f.sharedMesh != null ? f.sharedMesh.bounds.size : Vector3.zero;
    }

    static void SetMesh(Transform t, Mesh m)
    {
        var f = t != null ? t.GetComponent<MeshFilter>() : null;
        if (f == null || m == null || f.sharedMesh == m) return;
        Undo.RecordObject(f, "mesh");
        f.sharedMesh = m;
        EditorUtility.SetDirty(f);
    }

    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t != root && t.name == name);

    static void Rewire(Transform key, Object target, string method)
    {
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "InteractableUnityEventWrapper") continue;
            var so = new SerializedObject(mb);
            var calls = so.FindProperty("_whenSelect.m_PersistentCalls.m_Calls");
            if (calls == null) continue;
            if (calls.arraySize == 0) calls.InsertArrayElementAtIndex(0);
            for (int i = 0; i < calls.arraySize; i++)
            {
                var c = calls.GetArrayElementAtIndex(i);
                c.FindPropertyRelative("m_Target").objectReferenceValue = target;
                c.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = target.GetType().AssemblyQualifiedName;
                c.FindPropertyRelative("m_MethodName").stringValue = method;
                c.FindPropertyRelative("m_Mode").intValue = 1;
                c.FindPropertyRelative("m_CallState").intValue = 2;
            }
            so.ApplyModifiedProperties();
        }
    }

    /// <summary>One line, auto-sized down to 5.5 mm, never an ellipsis (an over-long word may only spill onto the cap rim).</summary>
    static void Words(TMP_Text label, string words)
    {
        if (label == null) return;
        Undo.RecordObject(label, "label");
        label.text = words;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        float s = Mathf.Abs(label.transform.lossyScale.x);
        if (s > 1e-7f)
        {
            float min = LabelMinMm * 0.01f / s;
            if (label.fontSizeMax < min) label.fontSizeMax = min;
            label.fontSizeMin = Mathf.Min(min, label.fontSizeMax);
            label.enableAutoSizing = label.fontSizeMin < label.fontSizeMax;
        }
        EditorUtility.SetDirty(label);
    }

    static ModeConsole.ConsoleKey CloneKey(Transform console, ModeConsole.ConsoleKey template, string name, string words, Vector3 localPos,
                                           Material cap, ModeConsole target, string method, bool savedHidden, StringBuilder sb)
    {
        Transform k = console.Find(name);
        bool created = k == null;
        if (created)
        {
            var go = Object.Instantiate(template.root.gameObject, console);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.name = name;
            k = go.transform;
        }
        k.gameObject.SetActive(true);
        k.localPosition = localPos;
        k.localRotation = template.root.localRotation;
        k.localScale = template.shownScale * (savedHidden ? Hidden : 1f);
        Transform capT = Under(k, "Cap3D");
        var capR = capT != null ? capT.GetComponent<MeshRenderer>() : null;
        if (capR != null && cap != null) { capR.sharedMaterial = cap; EditorUtility.SetDirty(capR); }
        var label = k.GetComponentInChildren<TMP_Text>(true);
        Words(label, words);
        var ksv = k.GetComponent<KeyStateVisual>();
        if (ksv != null) { ksv.capRenderer = capR; ksv.label = label; EditorUtility.SetDirty(ksv); }
        Rewire(k, target, method);
        EditorUtility.SetDirty(k.gameObject);
        var ck = new ModeConsole.ConsoleKey
        {
            root = k, shownScale = template.shownScale, label = label, visual = ksv,
            poke = k.GetComponentInChildren<PokeInteractable>(true), cap = capR,
        };
        sb.AppendLine($"  {(created ? "created" : "updated")} {name} \"{words}\" at {P(localPos)} -> ModeConsole.{method}{(savedHidden ? " (saved hidden)" : "")}{(ck.poke == null ? "  WARNING: no PokeInteractable" : "")}");
        return ck;
    }

    static bool IsCentred(string n) => Regenerated.Contains(n) || n == "Console_Stop";

    // ───────────────────────────── entry ─────────────────────────────

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "UnifiedModeDockV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "UnifiedModeDockV1: no open scene.\n";
        var sb = new StringBuilder("== UnifiedModeDockV1.Apply ==\n");

        var console = Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);
        if (console == null) return "UnifiedModeDockV1: no ModeConsole (run BuildModeConsoleV1 first); nothing changed.\n";
        var director = Object.FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
        var wall = Object.FindAnyObjectByType<MuseDesk.Wall.WallHabitatController>(FindObjectsInactive.Include);
        var kit = Object.FindAnyObjectByType<NearUserKit>(FindObjectsInactive.Include);
        var desk = Object.FindAnyObjectByType<WorkspacePlacement>(FindObjectsInactive.Include);
        var summon = Object.FindAnyObjectByType<SummonController>(FindObjectsInactive.Include);
        Transform ct = console.transform;
        if (console.deskKey.root == null || console.floorKey.root == null || console.ceilingKey.root == null
            || console.nextKey.root == null || console.cancelKey.root == null || console.statusKey.root == null)
            return "UnifiedModeDockV1: the console is missing a key (Desk/Floor/Ceiling/Next/Cancel/Status); nothing changed.\n";

        Undo.RecordObject(console, "UnifiedModeDockV1");
        float pitch = console.floorKey.root.localPosition.x - console.deskKey.root.localPosition.x;
        if (console.unifiedApplied) pitch = console.unifiedShift * 2f;
        if (Mathf.Abs(pitch) < 0.01f || Mathf.Abs(pitch) > 0.1f) return $"UnifiedModeDockV1: key pitch {pitch:F4} m is implausible; nothing changed.\n";
        sb.AppendLine($"  place key pitch {pitch * 1000f:F1} mm (console-local x)");

        // ── 1. widen by one column (once) ──
        if (!console.unifiedApplied)
        {
            float ceilX = console.ceilingKey.root.localPosition.x;
            float half = pitch * 0.5f;
            var shifted = new List<Transform>(); var deltas = new List<float>();
            foreach (Transform child in ct.Cast<Transform>().ToList())
            {
                if (IsCentred(child.name)) continue;
                // Beyond the last place column (the mic end) moves out by half a column; everything else (Focus end, rows) the other way.
                bool beyondRow = Mathf.Sign(pitch) * (child.localPosition.x - ceilX) > Mathf.Abs(pitch) * 0.5f;
                float d = beyondRow || child.name == "MicSlot" ? half : -half;
                Undo.RecordObject(child, "shift");
                child.localPosition += new Vector3(d, 0f, 0f);
                EditorUtility.SetDirty(child);
                shifted.Add(child); deltas.Add(d);
            }
            console.unifiedShifted = shifted.ToArray();
            console.unifiedDeltas = deltas.ToArray();
            sb.AppendLine($"  shifted {shifted.Count} parts: left group and rows by {-half * 1000f:F1} mm, right group (mic) by {half * 1000f:F1} mm");

            float w = Mathf.Abs(pitch);
            Transform body = ct.Find("Body"), inlay = ct.Find("Inlay"), edge = ct.Find("EdgeLine"), lip = ct.Find("StatusLip");
            Transform segWell = ct.Find("SegmentWell"), ctxWell = ct.Find("ContextWell");
            Vector3 bs = MeshSize(body);
            if (body == null || inlay == null || bs.x < 0.05f) return sb.Append("UnifiedModeDockV1: console Body / Inlay mesh not found; partial shift recorded, run Revert.\n").ToString();
            float W = bs.x + w, H = bs.y;
            float zTop = inlay.localPosition.z + 0.0004f;
            float tan = Mathf.Tan(TiltDeg * Mathf.Deg2Rad);
            var mb = new MB(); Slab(mb, 0f, 0f, W * 0.5f, H * 0.5f, CornerRadius, Bevel, zTop, zTop + NearThickness, tan);
            SetMesh(body, Store("UnifiedDock_Body", mb));
            mb = new MB(); FlatPlate(mb, 0f, 0f, W * 0.5f - Inset, H * 0.5f - Inset, CornerRadius - Inset, 0f);
            SetMesh(inlay, Store("UnifiedDock_Inlay", mb));
            if (edge != null) { mb = new MB(); FlatOutline(mb, W * 0.5f - Inset, H * 0.5f - Inset, CornerRadius - Inset, 0.0009f, 0f); SetMesh(edge, Store("UnifiedDock_EdgeLine", mb)); }
            foreach (var well in new[] { segWell, ctxWell })
            {
                if (well == null) continue;
                Vector3 s = MeshSize(well);
                mb = new MB(); FlatPlate(mb, 0f, 0f, (s.x + w) * 0.5f, s.y * 0.5f, WellRadius, 0f);
                SetMesh(well, Store("UnifiedDock_" + well.name, mb));
            }
            if (lip != null)
            {
                float lipW = MeshSize(lip).x + w;
                mb = new MB(); Slab(mb, 0f, LipHeight * 0.5f, lipW * 0.5f, LipHeight * 0.5f, 0.003f, 0.0008f, 0f, LipDepth, 0f);
                SetMesh(lip, Store("UnifiedDock_Lip", mb));
                var status = lip.GetComponentInChildren<TMP_Text>(true);
                if (status != null) { Undo.RecordObject(status.rectTransform, "status"); status.rectTransform.sizeDelta += new Vector2(w * 100f, 0f); EditorUtility.SetDirty(status); }
            }
            sb.AppendLine($"  console face {bs.x * 1000f:F0} -> {W * 1000f:F0} mm wide (meshes UnifiedDock_*; ModeConsole_* left on disk for Revert)");

            var panel = console.GetComponentInParent<MovablePanel>(true);
            if (panel != null) { Undo.RecordObject(panel, "footprint"); panel.footprint.x += w; EditorUtility.SetDirty(panel); sb.AppendLine($"  {panel.panelId} panel footprint now {panel.footprint.x * 1000f:F0} mm wide"); }
            console.unifiedShift = half;
            console.unifiedApplied = true;
        }

        // ── 2. the Wall place key, its indicator and the Wall context keys ──
        Material idle = console.segmentIdle;
        Vector3 Right(Transform t, float n) => t.localPosition + new Vector3(pitch * n, 0f, 0f);
        console.wallKey = CloneKey(ct, console.ceilingKey, "Console_Wall", SpacesDockLogic.Wall, Right(console.ceilingKey.root, 1f), idle,
                                   console, nameof(ModeConsole.PressWall), false, sb);
        Mesh wallIcon = Icon("UnifiedDock_Icon_Wall",
            -0.24f, 0.30f, 0.44f, 0.22f, 0.24f, 0.30f, 0.44f, 0.22f,
            -0.38f, 0f, 0.22f, 0.22f, 0f, 0f, 0.46f, 0.22f, 0.38f, 0f, 0.22f, 0.22f,
            -0.24f, -0.30f, 0.44f, 0.22f, 0.24f, -0.30f, 0.44f, 0.22f);
        Transform icon = Under(console.wallKey.root, "Icon");
        if (icon != null) SetMesh(icon, wallIcon); else sb.AppendLine("  NOTE: the Ceiling key has no Icon child: the Wall key is word-only");

        var bars = console.indicators != null ? console.indicators.ToList() : new List<Transform>();
        while (bars.Count > 3) bars.RemoveAt(bars.Count - 1);
        if (bars.Count == 3 && bars[2] != null)
        {
            Transform bar = ct.Find("Indicator_Wall");
            if (bar == null)
            {
                var go = Object.Instantiate(bars[2].gameObject, ct);
                Undo.RegisterCreatedObjectUndo(go, "Indicator_Wall");
                go.name = "Indicator_Wall";
                bar = go.transform;
            }
            bar.localPosition = Right(bars[2], 1f);
            bar.localRotation = bars[2].localRotation;
            bar.localScale = Vector3.one * Hidden;
            bars.Add(bar);
            console.indicators = bars.ToArray();
        }
        else sb.AppendLine("  WARNING: the console has no three indicator bars: no bar under Wall");

        Vector3 col0 = console.nextKey.root.localPosition, col1 = console.statusKey.root.localPosition, col2 = console.cancelKey.root.localPosition;
        console.layoutKey = CloneKey(ct, console.cancelKey, "Console_Layout", SpacesDockLogic.Compact, col2 + new Vector3(pitch, 0f, 0f), idle, console, nameof(ModeConsole.PressLayout), true, sb);
        console.moveKey = CloneKey(ct, console.nextKey, "Console_Move", SpacesDockLogic.Move, col0, idle, console, nameof(ModeConsole.PressMove), true, sb);
        console.selectKey = CloneKey(ct, console.statusKey, "Console_Select", SpacesDockLogic.Select, col1, idle, console, nameof(ModeConsole.PressSelect), true, sb);
        console.managerKey = CloneKey(ct, console.cancelKey, "Console_Manager", SpacesDockLogic.Manager, col2, idle, console, nameof(ModeConsole.PressManager), true, sb);

        // ── 3. short words on the existing keys ──
        console.confirmWords = SpacesDockLogic.Place; console.placeHereWords = SpacesDockLogic.Place;
        console.nextWords = SpacesDockLogic.NextSpot; console.otherSpotWords = SpacesDockLogic.NextSpot;
        console.sendBackWords = SpacesDockLogic.SendBack;
        console.deskWords = "Spaces";
        Words(console.confirmKey.label, SpacesDockLogic.Place);
        Words(console.nextKey.label, SpacesDockLogic.NextSpot);
        Words(console.cancelKey.label, SpacesDockLogic.Cancel);
        Words(console.stopKey.label, SpacesDockLogic.Stop);
        Words(console.podKey.label, ModeConsoleLogic.Lower);
        Words(console.bringKey.label, SpacesDockLogic.Bring);
        Words(console.statusKey.label, SpacesDockLogic.Status);
        Words(console.deskKey.label, SpacesDockLogic.Desk);
        Words(console.floorKey.label, SpacesDockLogic.Floor);
        Words(console.ceilingKey.label, SpacesDockLogic.Ceiling);
        if (console.statusLabel != null) { Undo.RecordObject(console.statusLabel, "status"); console.statusLabel.text = "Spaces"; EditorUtility.SetDirty(console.statusLabel); }
        if (summon != null) { Undo.RecordObject(summon, "summon words"); summon.bringWords = SpacesDockLogic.Bring; summon.sendWords = SpacesDockLogic.SendBack; EditorUtility.SetDirty(summon); }

        var tooLong = SpacesDockLogic.AllWords.Where(x => !SpacesDockLogic.FitsOnKey(x)).ToList();
        sb.AppendLine(tooLong.Count == 0
            ? $"  every dock word fits one line of a {SpacesDockLogic.KeyCapWidthMm:F0} mm key at {LabelMinMm} mm (TextFit estimate): " + string.Join(", ", SpacesDockLogic.AllWords)
            : "  WARNING: these words may not fit a key at 5.5 mm: " + string.Join(", ", tooLong));

        // ── 4. wiring ──
        if (wall != null) { console.wallHostBehaviour = wall; sb.AppendLine("  ModeConsole.wallHostBehaviour = " + wall.name + " (the director sets ExternalDock at Start: no WallDock is built)"); }
        else sb.AppendLine("  NOTE: no WallHabitatController: the Wall key stays disabled");
        if (director != null && console.director == null) console.director = director;
        EditorUtility.SetDirty(console);
        if (kit != null)
        {
            Undo.RecordObject(kit, "kit");
            kit.startupReach = true;
            if (desk != null) kit.deskPlacement = desk;
            if (director != null) kit.director = director;
            EditorUtility.SetDirty(kit);
            sb.AppendLine($"  NearUserKit start-up reach on (max {StartupReachRule.MaxReach} m, window {StartupReachRule.WindowSeconds} s); desk placement {(desk != null ? "wired" : "MISSING")}");
        }
        else sb.AppendLine("  WARNING: no NearUserKit: start-up reach is not active");

        // ── 5. report: reach from the seated eye (rig space), and overlap with other panels ──
        Transform rig = kit != null && kit.rig != null ? kit.rig : ct.parent != null ? ct.parent.parent : null;
        // WorkspacePlacement puts the root at head + right*side + fwd*depth - up*drop with +Z at the user: the eye is root-local (side, drop, depth).
        Vector3 eye = desk != null ? new Vector3(desk.offDeskSide, desk.offDeskDrop, desk.offDeskDepth) : new Vector3(0f, 0.39f, 0.62f);
        if (rig != null)
        {
            float worst = 0f; string worstName = "";
            foreach (var k in new[] { console.deskKey, console.floorKey, console.ceilingKey, console.wallKey, console.layoutKey })
            {
                if (k == null || k.root == null) continue;
                float d = (rig.InverseTransformPoint(k.root.position) - eye).magnitude;
                if (d > worst) { worst = d; worstName = k.root.name; }
            }
            sb.AppendLine($"  farthest dock key from the seated eye {P(eye)} (rig-local): {worstName} at {worst:F3} m{(worst > 0.55f ? "  NOTE: beyond 0.55 m; the start-up reach keeps it under 0.8 m" : "")}");
            Renderer bodyR = ct.Find("Body") != null ? ct.Find("Body").GetComponent<Renderer>() : null;
            if (bodyR != null)
                foreach (var p in rig.GetComponentsInChildren<MovablePanel>(true))
                {
                    if (p.transform == ct.parent) continue;
                    foreach (var r in p.GetComponentsInChildren<Renderer>(false))
                        if (r.enabled && r.bounds.size.sqrMagnitude > 1e-6f && r.bounds.Intersects(bodyR.bounds) && r.transform.lossyScale.x > 0.0005f)
                        { sb.AppendLine($"  NOTE: the widened console's box meets {p.panelId}/{r.name} (world AABB; check in the Scene view)"); break; }
                }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    /// <summary>Back to the three-place console: created keys removed, parts shifted back, ModeConsole_* meshes and the old words restored.</summary>
    public static string Revert()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "UnifiedModeDockV1.Revert: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder("== UnifiedModeDockV1.Revert ==\n");
        var console = Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);
        if (console == null) return "UnifiedModeDockV1.Revert: no ModeConsole.\n";
        Transform ct = console.transform;
        Undo.RecordObject(console, "revert");

        foreach (string n in Created)
        {
            Transform t = ct.Find(n);
            if (t != null) { Undo.DestroyObjectImmediate(t.gameObject); sb.AppendLine("  removed " + n); }
        }
        console.wallKey = new ModeConsole.ConsoleKey(); console.layoutKey = new ModeConsole.ConsoleKey();
        console.moveKey = new ModeConsole.ConsoleKey(); console.selectKey = new ModeConsole.ConsoleKey(); console.managerKey = new ModeConsole.ConsoleKey();
        console.wallHostBehaviour = null;
        if (console.indicators != null && console.indicators.Length > 3) console.indicators = console.indicators.Take(3).ToArray();

        if (console.unifiedApplied)
        {
            float w = Mathf.Abs(console.unifiedShift * 2f);
            if (console.unifiedShifted != null)
                for (int i = 0; i < console.unifiedShifted.Length; i++)
                {
                    Transform t = console.unifiedShifted[i];
                    if (t == null || console.unifiedDeltas == null || i >= console.unifiedDeltas.Length) continue;
                    Undo.RecordObject(t, "unshift");
                    t.localPosition -= new Vector3(console.unifiedDeltas[i], 0f, 0f);
                    EditorUtility.SetDirty(t);
                }
            string[,] meshes = { { "Body", "ModeConsole_Body" }, { "Inlay", "ModeConsole_Inlay" }, { "EdgeLine", "ModeConsole_EdgeLine" },
                                 { "SegmentWell", "ModeConsole_SegmentWell" }, { "ContextWell", "ModeConsole_ContextWell" }, { "StatusLip", "ModeConsole_Lip" } };
            for (int i = 0; i < meshes.GetLength(0); i++)
            {
                Transform t = ct.Find(meshes[i, 0]);
                var m = AssetDatabase.LoadAssetAtPath<Mesh>(GenDir + "/" + meshes[i, 1] + ".asset");
                if (t != null && m != null) SetMesh(t, m); else sb.AppendLine("  WARNING: could not restore " + meshes[i, 0]);
            }
            Transform lip = ct.Find("StatusLip");
            var status = lip != null ? lip.GetComponentInChildren<TMP_Text>(true) : null;
            if (status != null) { Undo.RecordObject(status.rectTransform, "status"); status.rectTransform.sizeDelta -= new Vector2(w * 100f, 0f); }
            var panel = console.GetComponentInParent<MovablePanel>(true);
            if (panel != null) { Undo.RecordObject(panel, "footprint"); panel.footprint.x -= w; EditorUtility.SetDirty(panel); }
            console.unifiedApplied = false; console.unifiedShift = 0f; console.unifiedShifted = null; console.unifiedDeltas = null;
            sb.AppendLine("  console back to three columns");
        }

        console.confirmWords = "Confirm"; console.placeHereWords = "Place here";
        console.nextWords = "Next"; console.otherSpotWords = "Other spot";
        console.deskWords = "Office at your desk";
        if (console.confirmKey.label != null) console.confirmKey.label.text = "Confirm";
        if (console.nextKey.label != null) console.nextKey.label.text = "Next";
        if (console.bringKey.label != null) console.bringKey.label.text = "Bring here";
        if (console.statusLabel != null) console.statusLabel.text = console.deskWords;
        var summon = Object.FindAnyObjectByType<SummonController>(FindObjectsInactive.Include);
        if (summon != null) { Undo.RecordObject(summon, "summon"); summon.bringWords = "Bring here"; EditorUtility.SetDirty(summon); }
        var kit = Object.FindAnyObjectByType<NearUserKit>(FindObjectsInactive.Include);
        if (kit != null) { Undo.RecordObject(kit, "kit"); kit.startupReach = false; EditorUtility.SetDirty(kit); sb.AppendLine("  NearUserKit start-up reach off"); }
        EditorUtility.SetDirty(console);
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }
}
