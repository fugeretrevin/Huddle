using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Documents;
using MuseDesk.Networking;
using MuseDesk.Workspace;
using MuseDesk.Phase2;
using MuseDesk.Phase2.Board;

/// <summary>
/// Final refinement pass, Receipt part (docs/Design/REFINEMENT_BRIEF_FOR_AGENTS.md).
///   1. Artifact viewer: WorkspaceRoot/ArtifactPanel becomes a 28 x 22 cm paper sheet in front of
///      the user that opens only on request. Its old Root (inactive, so its keys never registered)
///      is left switched off; the new content lives under ArtifactPanel/ViewerSheetRoot, which
///      stays active and is hidden by scale. Keys: ViewerPrevKey, ViewerNextKey, ViewerCloseKey.
///   2. Completion receipt: OfficeV2/CompletionReceipt becomes a 14 x 10.5 cm card standing in a
///      small base block on the desk, with a View key (opens the viewer) and Run again.
///   3. Archive: the floating dark ArchiveReadout is switched off; OfficeV2/ArchiveCard is a
///      14 x 10 cm paper card that rises from the out-tray stack (same DecisionArchive).
/// Runs after BuildOfficeV2 / BuildPaperV2 / IntegrateV2. Reuses the objects they made, creates
/// what is missing, deactivates what it replaces, never destroys. Idempotent; saves the scene once.
///   unity command script run --file AgentScripts/BuildReceiptV3.cs --entry BuildReceiptV3.Apply
/// </summary>
public static class BuildReceiptV3
{
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";
    const float Hidden = 0.001f;                                       // ScaleEase.HiddenFactor
    static readonly Vector3 Eye = new Vector3(0f, 0.30f, 0.62f);       // seated eye, root-local
    static readonly Vector3 EyeForward = new Vector3(0f, 0f, -1f);     // level gaze toward the office
    const float ConeHalfDeg = 30f;                                     // "inside a 60 degree cone"
    const float Reach = 0.55f;

    // Layout (root-local metres). Change here, re-run.
    static readonly Vector3 ViewerPos = new Vector3(0f, 0.32f, 0.26f);
    const float ViewerW = 0.28f, ViewerH = 0.22f;
    const float ViewerKeyScale = 0.030f, ViewerKeyRatio = 1.5f;

    const float ReceiptX = -0.15f, ReceiptZ = 0.37f;
    const float ReceiptW = 0.14f, ReceiptH = 0.105f;
    const float ReceiptBaseH = 0.012f, ReceiptBottomY = 0.010f;        // the card sits 2 mm into its base
    const float ReceiptKeyScale = 0.028f, ReceiptKeyRatio = 1.6f;

    const float ArchiveW = 0.14f, ArchiveH = 0.10f;
    const float FiledKeyScale = 0.040f;

    // Design tokens (brief, section 4).
    static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    static readonly Color Paper = Hex("#F7F2E3");
    static readonly Color PaperUnder = Hex("#DDD5BF");                  // BuildPaperV2's under-sheet tone
    static readonly Color Ink = Hex("#1F2530");
    static readonly Color InkSoft = Hex("#4B5563");
    static readonly Color Housing = Hex("#2A2F3A");
    static readonly Color Teal = Hex("#2FA39A");
    static readonly Color Lavender = Hex("#8C7BD6");
    static readonly Color Amber = Hex("#E0A030");

    // ───────────────────────────── helpers (idioms from BuildOfficeV2 / FinalPass) ─────────────────────────────

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Find(List<Transform> all, string name) => all.FirstOrDefault(t => t != null && t.name == name);
    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t.name == name);

    static string P(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
    static Vector3 RootLocal(Transform ws, Transform t) => ws.InverseTransformPoint(t.position);

    /// <summary>Load a UI material, or create it. An existing material keeps its colour unless
    /// <paramref name="recolour"/> is set (materials owned by other builders are not repainted).</summary>
    static Material Mat(string name, Color c, bool unlit = true, bool recolour = false)
    {
        if (!AssetDatabase.IsValidFolder("Assets/MuseDesk/Art/Materials")) AssetDatabase.CreateFolder("Assets/MuseDesk/Art", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/MuseDesk/Art/Materials", "UI");
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool created = false;
        if (m == null)
        {
            m = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Simple Lit"));
            AssetDatabase.CreateAsset(m, path);
            created = true;
        }
        if (created || recolour)
        {
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }
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

    /// <summary>Find or create a child shape (mesh + material, no collider). A quad is visible from its -Z side.</summary>
    static Transform Shape(Transform parent, string name, Mesh mesh, Material mat, Vector3 lp, Vector3 ls)
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
        var mf = t.GetComponent<MeshFilter>();
        if (mf == null) mf = t.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var r = t.GetComponent<MeshRenderer>();
        if (r == null) r = t.gameObject.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        t.localPosition = lp;
        t.localRotation = Quaternion.identity;
        t.localScale = ls;
        EditorUtility.SetDirty(t.gameObject);
        return t;
    }

    /// <summary>Find or create a TextMeshPro label at scale 0.01 (font size N = about N mm, box in cm).</summary>
    static TMP_Text Label(Transform parent, string name, Vector3 lp)
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
        return tmp;
    }

    static void Style(TMP_Text t, string text, Vector2 boxCm, float minMm, float maxMm, Color color, FontStyles style,
                      TextAlignmentOptions align, bool wrap)
    {
        if (t == null) return;
        Undo.RecordObject(t, "style");
        if (text != null) t.text = text;
        t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        t.rectTransform.sizeDelta = boxCm;
        t.enableAutoSizing = minMm < maxMm;
        t.fontSizeMin = minMm; t.fontSizeMax = maxMm; t.fontSize = maxMm;
        t.color = color;
        t.fontStyle = style;
        t.alignment = align;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.margin = Vector4.zero;
        t.richText = false;                                  // every text here is data or a fixed label
        EditorUtility.SetDirty(t);
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

    static bool Drawn(MeshFilter f)
    {
        if (f == null || f.sharedMesh == null || !f.gameObject.activeInHierarchy) return false;
        var r = f.GetComponent<MeshRenderer>();
        return r != null && r.enabled && f.GetComponent<TMP_Text>() == null;
    }

    /// <summary>Bounds of everything drawn under a root, in another transform's space.</summary>
    static bool DrawnBounds(Transform root, Transform space, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        bool any = false;
        if (root == null) return false;
        foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!Drawn(f) || !OwnMeshBounds(f.transform, space, out var a, out var b)) continue;
            mn = Vector3.Min(mn, a); mx = Vector3.Max(mx, b); any = true;
        }
        return any;
    }

    static bool BoxesOverlap(Vector3 amn, Vector3 amx, Vector3 bmn, Vector3 bmx) =>
        amn.x < bmx.x && amx.x > bmn.x && amn.y < bmx.y && amx.y > bmn.y && amn.z < bmx.z && amx.z > bmn.z;

    static void Deactivate(Transform t, StringBuilder sb, Transform ws, string why)
    {
        if (t == null) return;
        bool was = t.gameObject.activeSelf;
        if (was)
        {
            Undo.RecordObject(t.gameObject, "deactivate");
            t.gameObject.SetActive(false);
            EditorUtility.SetDirty(t.gameObject);
        }
        sb.AppendLine($"  deactivated {t.name} at {P(RootLocal(ws, t))}{(was ? "" : " (already inactive)")}{(string.IsNullOrEmpty(why) ? "" : ": " + why)}");
    }

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

    /// <summary>Find or clone a poke key from a template key (keeps the Housing3D / Cap3D structure).</summary>
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
            // The template may billboard itself; a key that is part of a sheet must not.
            var face = go.GetComponent<FaceUser>();
            if (face != null) Object.DestroyImmediate(face);
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
            var m = Mat(matName, c, unlit: false, recolour: true);
            m.SetFloat("_Smoothness", 0.35f);
            mr.sharedMaterial = m;
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

    /// <summary>
    /// Key labels sit under the key's own scale, so the size is worked out from the label's world
    /// scale: glyphs <paramref name="mm"/> millimetres tall, never below 5.5 mm. The box is sized
    /// for the cap the key will have once the Kit builder has applied its WideKey ratio.
    /// Call while the key and its parents are at full scale.
    /// </summary>
    static TMP_Text KeyLabel(Transform key, string text, float mm, Color color, float wideRatio)
    {
        var tmp = key.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null) return null;
        float s = Mathf.Abs(tmp.transform.lossyScale.x);
        if (s < 1e-7f) return tmp;
        float capHeight = Mathf.Abs(key.lossyScale.x) * 0.98f;
        var cap = Under(key, "Cap3D");
        if (cap != null && OwnMeshBounds(cap, key, out var cmn, out var cmx))
            capHeight = (cmx.y - cmn.y) * Mathf.Abs(key.lossyScale.x);
        Undo.RecordObject(tmp, "key label");
        tmp.text = text;
        tmp.rectTransform.sizeDelta = new Vector2(0.90f * capHeight * Mathf.Max(1f, wideRatio) / s, 0.50f * capHeight / s);
        float max = mm * 0.01f / s;                     // font size 10 = one unit of em height
        float min = Mathf.Min(max, 5.5f * 0.01f / s);
        tmp.enableAutoSizing = min < max;
        tmp.fontSizeMax = max; tmp.fontSizeMin = min; tmp.fontSize = max;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        EditorUtility.SetDirty(tmp);
        return tmp;
    }

    /// <summary>How far behind the poke surface the key's housing reaches, in metres at this key scale.</summary>
    static float HousingDepth(Transform key, float keyScale)
    {
        var housing = Under(key, "Housing3D");
        if (housing != null && OwnMeshBounds(housing, key, out _, out var hmx) && hmx.z > 0f)
            return Mathf.Clamp(hmx.z * keyScale, 0.002f, 0.02f);
        return 0.008f;
    }

    /// <summary>The component-free marker the Kit builder reads to make a key wide (brief: WideKey_ratio).</summary>
    static void WideMarker(Transform key, float ratio)
    {
        string want = "WideKey_" + ratio.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
        Transform marker = null;
        foreach (Transform c in key)
            if (c.name.StartsWith("WideKey_")) { marker = c; break; }
        if (marker == null)
        {
            var go = new GameObject(want);
            Undo.RegisterCreatedObjectUndo(go, want);
            go.transform.SetParent(key, false);
            marker = go.transform;
        }
        marker.name = want;
        marker.localPosition = Vector3.zero;
        marker.localRotation = Quaternion.identity;
        marker.localScale = Vector3.one;
        marker.gameObject.layer = key.gameObject.layer;
        EditorUtility.SetDirty(marker.gameObject);
    }

    /// <summary>A key on a sheet: cloned (or reused), uniform scale, proud of the sheet by its housing, labelled, wired.</summary>
    static Transform SheetKey(Transform sheet, Transform template, string name, string label, float scale, float ratio,
                              Vector2 at, string capMat, Color capColor, Object target, string method,
                              Transform ws, StringBuilder sb)
    {
        var key = CloneKey(sheet, template, name, out bool created);
        key.localScale = Vector3.one * scale;
        key.localRotation = Quaternion.identity;            // the sheet already faces the eye
        key.localPosition = new Vector3(at.x, at.y, -(HousingDepth(key, scale) + 0.0005f));
        KeyLabel(key, label, label.Length > 6 ? 5.5f : 6.5f, Ink, ratio);
        CapColor(key, capMat, capColor);
        if (ratio > 1.01f) WideMarker(key, ratio);
        Rewire(key, target, method);
        EditorUtility.SetDirty(key.gameObject);
        Vector3 rl = RootLocal(ws, key);
        float d = Vector3.Distance(rl, Eye);
        string verb = created ? "created" : "updated";
        string face = ratio > 1.01f
            ? ", wide marker x" + ratio.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) +
              " (face " + (scale * ratio * 1000f).ToString("F0") + " x " + (scale * 1000f).ToString("F0") + " mm after Kit)"
            : " (face " + (scale * 1000f).ToString("F0") + " mm)";
        sb.AppendLine($"  {verb} {sheet.parent.name}/{sheet.name}/{name} \"{label}\" at {P(rl)}, uniform scale {scale:F3}{face}, {d:F3} m from the eye -> {target.GetType().Name}.{method}");
        if (d > Reach) sb.AppendLine($"  WARNING: {name} is {d:F3} m from the eye, beyond the {Reach:F2} m reach");
        return key;
    }

    static Quaternion FaceEye(Vector3 rootPos) => Quaternion.LookRotation((rootPos - Eye).normalized, Vector3.up);

    /// <summary>
    /// Print pose, size, distance and angular offset of a flat sheet (its transform at full scale)
    /// and return its padded root-local box. Warns when a corner leaves the viewing cone.
    /// </summary>
    static void Measure(StringBuilder sb, Transform ws, string label, Transform t, float w, float h, string coneNote,
                        out Vector3 mn, out Vector3 mx)
    {
        Vector3 c = RootLocal(ws, t);
        Quaternion rot = Quaternion.Inverse(ws.rotation) * t.rotation;
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        float maxAng = 0f;
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = c + rot * new Vector3((i & 1) == 0 ? -w * 0.5f : w * 0.5f, (i & 2) == 0 ? -h * 0.5f : h * 0.5f, 0f);
            mn = Vector3.Min(mn, corner); mx = Vector3.Max(mx, corner);
            maxAng = Mathf.Max(maxAng, Vector3.Angle(EyeForward, corner - Eye));
        }
        Vector3 toC = c - Eye;
        float centreAng = Vector3.Angle(EyeForward, toC);
        float elevation = Mathf.Atan2(toC.y, new Vector2(toC.x, toC.z).magnitude) * Mathf.Rad2Deg;
        float facing = Vector3.Angle(rot * Vector3.forward, toC);
        Vector3 e = rot.eulerAngles;
        sb.AppendLine($"  POSE {label}: root-local {P(c)}, euler ({e.x:F0}, {e.y:F0}, {e.z:F0}), {w * 100f:F1} x {h * 100f:F1} cm, " +
                      $"{toC.magnitude:F3} m from the eye, centre {centreAng:F1} deg off the eye's forward (elevation {elevation:F1} deg), " +
                      $"furthest corner {maxAng:F1} deg, face {facing:F1} deg off the line to the eye");
        if (maxAng > ConeHalfDeg)
            sb.AppendLine($"  WARNING: {label} reaches {maxAng:F1} deg off the eye's forward, outside the 60 degree cone (30 deg half-angle){(string.IsNullOrEmpty(coneNote) ? "" : ": " + coneNote)}");
        Vector3 pad = Vector3.one * 0.004f;
        mn -= pad; mx += pad;
    }

    static void Neighbours(StringBuilder sb, Transform ws, List<Transform> all, string label, Vector3 mn, Vector3 mx, string[] names)
    {
        foreach (var n in names)
        {
            var o = Find(all, n);
            if (o == null || !o.gameObject.activeInHierarchy) continue;
            if (!DrawnBounds(o, ws, out var a, out var b)) continue;
            if ((b - a).magnitude < 0.005f) continue;        // hidden by scale
            if (BoxesOverlap(mn, mx, a, b))
                sb.AppendLine($"  WARNING: {label} (box {P(mn)} .. {P(mx)}) overlaps {n} (box {P(a)} .. {P(b)}); boxes are axis-aligned, so check by eye");
        }
    }

    // ───────────────────────────── entry ─────────────────────────────

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildReceiptV3: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildReceiptV3: no open scene.\n";

        var sb = new StringBuilder();
        var all = All();
        var ws = Find(all, "WorkspaceRoot");
        if (ws == null) return "BuildReceiptV3: WorkspaceRoot not found; nothing changed.\n";
        var session = Object.FindAnyObjectByType<SessionController>(FindObjectsInactive.Include);
        if (session == null) sb.AppendLine("WARNING: no SessionController in the scene; views are built but not bound.");
        var focus = Find(all, "FocusToggle");
        if (focus == null) sb.AppendLine("WARNING: FocusToggle not found: no key can be cloned, so new keys are skipped.");

        var quad = PrimitiveMesh(PrimitiveType.Quad);
        var cube = PrimitiveMesh(PrimitiveType.Cube);
        var office = Holder(ws, "OfficeV2");

        sb.AppendLine("== BuildReceiptV3 ==");
        sb.AppendLine($"root-local axes: +Z to the user, user's right is -X. Eye {P(Eye)}; angles are measured from a level gaze {P(EyeForward)}; cone half-angle {ConeHalfDeg:F0} deg; reach {Reach:F2} m.");

        var viewer = Viewer(all, ws, session, focus, quad, sb);
        Receipt(all, ws, office, session, viewer, focus, quad, cube, sb);
        Archive(all, ws, office, quad, sb);

        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // ───────────────────────────── 1. artifact viewer ─────────────────────────────

    static ArtifactViewer Viewer(List<Transform> all, Transform ws, SessionController session, Transform focus, Mesh quad, StringBuilder sb)
    {
        sb.AppendLine("-- 1. artifact viewer --");
        var panel = ws.Find("ArtifactPanel");
        if (panel == null) panel = Find(all, "ArtifactPanel");
        if (panel == null)
        {
            var go = new GameObject("ArtifactPanel");
            Undo.RegisterCreatedObjectUndo(go, "ArtifactPanel");
            go.transform.SetParent(ws, false);
            panel = go.transform;
            sb.AppendLine("  note: ArtifactPanel was not in the scene; created it under WorkspaceRoot");
        }
        if (!panel.gameObject.activeSelf) { panel.gameObject.SetActive(true); sb.AppendLine("  note: ArtifactPanel was inactive; switched on (its sheet hides by scale)"); }
        Vector3 before = RootLocal(ws, panel);
        Undo.RecordObject(panel, "viewer pose");
        panel.position = ws.TransformPoint(ViewerPos);
        panel.rotation = ws.rotation * FaceEye(ViewerPos);
        panel.localScale = Vector3.one;
        EditorUtility.SetDirty(panel);
        sb.AppendLine($"  moved ArtifactPanel from {P(before)} to {P(ViewerPos)}, facing the eye");

        // The panel no longer billboards: it has one authored reading pose (a yaw-only billboard would undo the tilt).
        var bill = panel.GetComponent<FaceUser>();
        if (bill != null && bill.enabled)
        {
            Undo.RecordObject(bill, "viewer billboard off");
            bill.enabled = false;
            EditorUtility.SetDirty(bill);
            sb.AppendLine("  disabled FaceUser on ArtifactPanel (component kept)");
        }
        // A saved layout must not put the panel back where it was.
        var store = ws.GetComponent<WorkspaceLayoutStore>();
        if (store != null && store.tracked != null && store.tracked.Contains(panel))
        {
            Undo.RecordObject(store, "layout store");
            store.tracked.Remove(panel);
            EditorUtility.SetDirty(store);
            sb.AppendLine("  removed ArtifactPanel from WorkspaceLayoutStore.tracked (a saved layout no longer moves it)");
        }

        // The old content: a Root that starts inactive (so its three keys never registered) and loose keys.
        var oldRoot = panel.Find("Root");
        if (oldRoot != null) Deactivate(oldRoot, sb, ws, "old 50 x 60 cm panel content with PrevPoke / NextPoke / ClosePoke");
        foreach (var n in new[] { "PrevPoke", "NextPoke", "ClosePoke", "Backing", "Title", "Body", "Page" })
        {
            var loose = panel.Find(n);
            if (loose != null) Deactivate(loose, sb, ws, "old panel part");
        }

        var root = Holder(panel, "ViewerSheetRoot");
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;                      // full size while it is laid out; hidden again below

        const float w = ViewerW, h = ViewerH, bandH = 0.016f, z = -0.0005f, textW = 25.6f;
        var paper = Mat("Paper_Sheet", Paper);
        var bandMat = Mat("Paper_ModeBand", Amber);
        Shape(root, "ViewerUnderSheet", quad, Mat("PaperV2_SheetUnder", PaperUnder), new Vector3(0.004f, -0.004f, 0.003f), new Vector3(w, h, 1f));
        Shape(root, "ViewerSheet", quad, paper, Vector3.zero, new Vector3(w, h, 1f));
        var band = Shape(root, "ViewerModeBand", quad, bandMat, new Vector3(0f, h * 0.5f - bandH * 0.5f, -0.0002f), new Vector3(w, bandH, 1f));

        var mode = Label(root, "Mode", new Vector3(0f, h * 0.5f - bandH * 0.5f, -0.0007f));
        Style(mode, "", new Vector2(textW, 1.2f), 5.5f, 6.5f, Ink, FontStyles.Bold, TextAlignmentOptions.Left, false);
        var title = Label(root, "Title", new Vector3(0f, 0.081f, z));
        Style(title, "", new Vector2(textW, 1.4f), 7.2f, 10f, Ink, FontStyles.Bold, TextAlignmentOptions.Left, false);
        var prov = Label(root, "Provenance", new Vector3(0f, 0.0635f, z));
        Style(prov, "", new Vector2(textW, 1.4f), 5.5f, 5.5f, InkSoft, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
        // Body: 7.5 mm, fixed size, split into parts that fit (TextMeshPro page overflow); plain text.
        var body = Label(root, "Body", new Vector3(0f, -0.0055f, z));
        Style(body, "", new Vector2(textW, 11.7f), 7.5f, 7.5f, Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
        body.overflowMode = TextOverflowModes.Page;
        body.pageToDisplay = 1;
        EditorUtility.SetDirty(body);
        const float keyY = -0.088f;
        var page = Label(root, "Page", new Vector3(0.030f, keyY, z));
        Style(page, "", new Vector2(9.4f, 2.6f), 5.5f, 6.5f, InkSoft, FontStyles.Normal, TextAlignmentOptions.Center, true);

        var viewer = panel.GetComponent<ArtifactViewer>();
        if (viewer == null) viewer = Undo.AddComponent<ArtifactViewer>(panel.gameObject);
        Undo.RecordObject(viewer, "viewer wiring");
        var boot = Object.FindAnyObjectByType<Phase2Bootstrap>(FindObjectsInactive.Include);
        if (boot != null) viewer.phase2 = boot;
        else sb.AppendLine("  WARNING: no Phase2Bootstrap in the scene; the viewer will look for one at run time");
        viewer.session = session;
        viewer.root = root.gameObject;
        viewer.shownScale = Vector3.one;
        viewer.easeSeconds = 0.25f;
        viewer.titleLabel = title;
        viewer.bodyText = body;
        viewer.pageLabel = page;
        viewer.modeLabel = mode;
        viewer.provenanceLabel = prov;
        viewer.modeBand = band.GetComponent<Renderer>();
        viewer.autoOpenOnCreate = false;                    // ignored by the code now; cleared so the scene says what happens

        if (focus != null)
        {
            var prev = SheetKey(root, focus, "ViewerPrevKey", "Previous", ViewerKeyScale, ViewerKeyRatio, new Vector2(-0.110f, keyY),
                                "Key_ViewerNav", Paper, viewer, nameof(ArtifactViewer.PrevPage), ws, sb);
            var next = SheetKey(root, focus, "ViewerNextKey", "Next", ViewerKeyScale, ViewerKeyRatio, new Vector2(-0.050f, keyY),
                                "Key_ViewerNav", Paper, viewer, nameof(ArtifactViewer.NextPage), ws, sb);
            SheetKey(root, focus, "ViewerCloseKey", "Close", ViewerKeyScale, ViewerKeyRatio, new Vector2(0.110f, keyY),
                     "Key_ViewerClose", Teal, viewer, nameof(ArtifactViewer.Close), ws, sb);
            viewer.prevKey = prev;
            viewer.nextKey = next;
            viewer.keyScale = Vector3.one * ViewerKeyScale;
        }
        else sb.AppendLine("  SKIP viewer keys: no FocusToggle to clone (the sheet can then only be closed by a reset or a new run)");
        EditorUtility.SetDirty(viewer);

        Measure(sb, ws, "ArtifactPanel/ViewerSheetRoot (artifact viewer)", root, w, h, null, out var mn, out var mx);
        Neighbours(sb, ws, all, "the artifact viewer", mn, mx, new[] { "SoftwareFactory", "TaskBoard", "TaskDock", "ModePlaque", "StampApprove", "StampReject",
                                                                        "ResearchStation", "DevStation", "QAStation", "Nameplate_Researcher", "Nameplate_Developer", "Nameplate_Tester" });
        // Which agents' faces the open sheet covers, seen from the eye.
        Quaternion rot = Quaternion.Inverse(ws.rotation) * root.rotation;
        Vector3 centre = RootLocal(ws, root);
        Vector3 normal = rot * Vector3.forward;
        foreach (var a in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" })
        {
            var agent = Find(all, a);
            if (agent == null) continue;
            Vector3 head = RootLocal(ws, agent) + new Vector3(0f, 0.26f, 0f);
            float denom = Vector3.Dot(head - Eye, normal);
            if (Mathf.Abs(denom) < 1e-6f) continue;
            float k = Vector3.Dot(centre - Eye, normal) / denom;
            Vector3 hit = Quaternion.Inverse(rot) * (Eye + (head - Eye) * k - centre);
            bool covered = k > 0f && k < 1f && Mathf.Abs(hit.x) < w * 0.5f && Mathf.Abs(hit.y) < h * 0.5f;
            sb.AppendLine($"  {a}: head at about {P(head)} is {(covered ? "COVERED by the open sheet (only while the user has it open)" : "clear of the open sheet")}");
        }

        root.localScale = Vector3.one * Hidden;             // active (so the keys register) but not seen
        EditorUtility.SetDirty(root.gameObject);
        sb.AppendLine("  ArtifactPanel/ViewerSheetRoot saved hidden by scale; ArtifactViewer never opens by itself (Open / OpenDelivered only)");
        sb.AppendLine("  text: title 10 mm bold, provenance 5.5 mm, body 7.5 mm fixed (parts that fit; nothing is cut off), page line 5.5 to 6.5 mm, mode words 6.5 mm bold on the band");
        return viewer;
    }

    // ───────────────────────────── 2. completion receipt ─────────────────────────────

    static void Receipt(List<Transform> all, Transform ws, Transform office, SessionController session, ArtifactViewer viewer,
                        Transform focus, Mesh quad, Mesh cube, StringBuilder sb)
    {
        sb.AppendLine("-- 2. completion receipt --");
        const float w = ReceiptW, h = ReceiptH, bandH = 0.012f, z = -0.0005f, textW = 12.8f;
        var card = Holder(office, "CompletionReceipt");

        // Faces the eye; its bottom edge rests in the base block on the desk surface.
        Vector3 pos = new Vector3(ReceiptX, 0.05f, ReceiptZ);
        Quaternion rot = FaceEye(pos);
        for (int i = 0; i < 4; i++)
        {
            rot = FaceEye(pos);
            pos.y = ReceiptBottomY + (rot * Vector3.up).y * h * 0.5f;
        }
        Undo.RecordObject(card, "receipt pose");
        card.localPosition = office.InverseTransformPoint(ws.TransformPoint(pos));
        card.rotation = ws.rotation * rot;
        card.localScale = Vector3.one;                      // full size while it is laid out; hidden again below

        Shape(card, "Sheet", quad, Mat("Paper_Sheet", Paper), Vector3.zero, new Vector3(w, h, 1f));
        var strip = Shape(card, "ModeStrip", quad, Mat("Paper_ModeBand", Amber), new Vector3(0f, h * 0.5f - bandH * 0.5f, -0.0002f), new Vector3(w, bandH, 1f));
        // Thickness: a 3 mm card body behind the printed face (inset, so it stays behind the face once
        // the Kit builder has rounded the face's corners).
        Shape(card, "ReceiptCardBody", cube, Mat("PaperV2_SheetUnder", PaperUnder), new Vector3(0f, 0f, 0.0018f), new Vector3(w - 0.010f, h - 0.010f, 0.003f));
        // Stand: a low base block on the desk surface, level, turned with the card.
        Vector3 up = rot * Vector3.up;
        Vector3 bottom = pos - up * (h * 0.5f);
        Vector3 away = pos - Eye; away.y = 0f; away.Normalize();
        var stand = Shape(card, "ReceiptStandBase", cube, Mat("ReceiptV3_Stand", Housing, unlit: false), Vector3.zero, new Vector3(0.100f, ReceiptBaseH, 0.026f));
        Vector3 standPos = new Vector3(bottom.x, ReceiptBaseH * 0.5f, bottom.z) + away * 0.005f;
        stand.position = ws.TransformPoint(standPos);
        stand.rotation = ws.rotation * Quaternion.LookRotation(away, Vector3.up);
        EditorUtility.SetDirty(stand.gameObject);

        // Hierarchy: mode words on the band, heading large, the event's summary, tests and files, then the keys.
        var mode = Label(card, "Mode", new Vector3(0f, h * 0.5f - bandH * 0.5f, -0.0007f));
        Style(mode, "", new Vector2(textW, 0.9f), 5.5f, 5.5f, Ink, FontStyles.Bold, TextAlignmentOptions.Left, false);
        var heading = Label(card, "Heading", new Vector3(0f, 0.0300f, z));
        Style(heading, "", new Vector2(textW, 1.5f), 10f, 13f, Ink, FontStyles.Bold, TextAlignmentOptions.Left, false);
        var body = Label(card, "Body", new Vector3(0f, 0.0085f, z));
        Style(body, "", new Vector2(textW, 2.4f), 5.5f, 7.5f, Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
        var detail = Label(card, "Detail", new Vector3(0f, -0.0090f, z));
        Style(detail, "", new Vector2(textW, 0.9f), 5.5f, 5.5f, InkSoft, FontStyles.Normal, TextAlignmentOptions.Left, false);
        const float keyY = -0.0315f, keyX = 0.035f;
        var hint = Label(card, "Hint", new Vector3(keyX, keyY, z));
        Style(hint, "", new Vector2(6.2f, 2.6f), 5.5f, 6.5f, InkSoft, FontStyles.Normal, TextAlignmentOptions.Center, true);

        var receipt = card.GetComponent<CompletionReceipt>();
        if (receipt == null) receipt = Undo.AddComponent<CompletionReceipt>(card.gameObject);
        Undo.RecordObject(receipt, "receipt wiring");
        if (receipt.controller == null) receipt.controller = session;
        if (receipt.guide == null) receipt.guide = Object.FindAnyObjectByType<FirstUseGuide>(FindObjectsInactive.Include);
        receipt.shownScale = Vector3.one;
        receipt.heading = heading;
        receipt.modeText = mode;
        receipt.body = body;
        receipt.detail = detail;
        receipt.hint = hint;
        receipt.modeStrip = strip.GetComponent<Renderer>();
        receipt.viewer = viewer;
        // startAfterReset is left as the coordinator set it (IntegrateV2).

        if (focus != null)
        {
            var view = SheetKey(card, focus, "ViewKey", "View", ReceiptKeyScale, ReceiptKeyRatio, new Vector2(-keyX, keyY),
                                "Key_ReceiptView", Lavender, receipt, nameof(CompletionReceipt.View), ws, sb);
            var again = SheetKey(card, focus, "RunAgainKey", "Run again", ReceiptKeyScale, ReceiptKeyRatio, new Vector2(keyX, keyY),
                                 "Key_RunAgain", Teal, receipt, nameof(CompletionReceipt.RunAgain), ws, sb);
            receipt.viewKey = view;
            receipt.viewKeyScale = Vector3.one * ReceiptKeyScale;
            receipt.runAgainKey = again;
            receipt.runAgainKeyScale = Vector3.one * ReceiptKeyScale;
        }
        else sb.AppendLine("  SKIP receipt keys: no FocusToggle to clone (an existing RunAgainKey is left as it is)");
        EditorUtility.SetDirty(receipt);

        Measure(sb, ws, "OfficeV2/CompletionReceipt (receipt card)", card, w, h, null, out var mn, out var mx);
        mn.y = 0f;                                          // the base block reaches the surface
        sb.AppendLine($"  receipt: bottom edge at y={ReceiptBottomY:F3} in ReceiptStandBase {P(standPos)} (10.0 x 1.2 x 2.6 cm, level), card body 3 mm thick; leans back {Vector3.Angle(up, Vector3.up):F0} deg");
        Neighbours(sb, ws, all, "the receipt", mn, mx, new[] { "ModePlaque", "StampApprove", "StampReject", "Doc_StampRestPad", "TaskDock", "FocusToggle",
                                                                "SoftwareFactory", "InboxTray", "DeliveryZone" });
        // The neighbours the brief names, by their nominal centres (in case one was not found above).
        foreach (var (name, at, half) in new[] {
                     ("the mode plaque", new Vector3(0f, 0.016f, 0.34f), new Vector3(0.07f, 0.03f, 0.02f)),
                     ("the stamps", new Vector3(0f, 0.05f, 0.415f), new Vector3(0.075f, 0.06f, 0.04f)),
                     ("the task dock", new Vector3(0.24f, 0.04f, 0.44f), new Vector3(0.09f, 0.04f, 0.04f)),
                     ("the Focus key", new Vector3(-0.26f, 0.03f, 0.46f), new Vector3(0.03f, 0.03f, 0.03f)) })
            if (BoxesOverlap(mn, mx, at - half, at + half))
                sb.AppendLine($"  WARNING: the receipt's box {P(mn)} .. {P(mx)} overlaps the nominal box of {name} at {P(at)}");

        card.localScale = Vector3.one * Hidden;             // active (so the keys register) but not seen
        EditorUtility.SetDirty(card.gameObject);
        sb.AppendLine("  CompletionReceipt saved hidden by scale; text: heading 10 to 13 mm bold, summary up to 7.5 mm, tests and files 5.5 mm, mode words 5.5 mm bold on the band");
        sb.AppendLine($"  View -> CompletionReceipt.View -> ArtifactViewer.OpenDelivered ({(viewer != null ? "wired" : "NOT WIRED: no viewer, the key stays hidden")}); poke-only (no grab)");
    }

    // ───────────────────────────── 3. archive card ─────────────────────────────

    static void Archive(List<Transform> all, Transform ws, Transform office, Mesh quad, StringBuilder sb)
    {
        sb.AppendLine("-- 3. archive card --");
        var archive = Object.FindAnyObjectByType<DecisionArchive>(FindObjectsInactive.Include);
        if (archive == null) { sb.AppendLine("  SKIP: no DecisionArchive in the scene"); return; }

        // The floating dark panel goes out of view (kept in the scene).
        var old = ws.Find("ArchiveReadout");
        if (old == null) old = Find(all, "ArchiveReadout");
        if (old != null) Deactivate(old, sb, ws, "floating dark readout, replaced by OfficeV2/ArchiveCard");

        var anchor = archive.stackAnchor != null ? archive.stackAnchor : Find(all, "Outbox_Stack_Anchor");
        if (anchor == null) anchor = Find(all, "OutboxTray");
        if (anchor == null) { sb.AppendLine("  SKIP: no out-tray stack anchor found; the archive keeps its old wiring"); return; }
        Vector3 anchorPos = RootLocal(ws, anchor);

        // The Filed plate: keep it where BuildOfficeV2 put it unless it is out of reach.
        var filed = office.Find("FiledPoke");
        Vector3 keyPos = anchorPos + new Vector3(0f, 0.040f, 0.012f);
        float keyScale = FiledKeyScale;
        if (filed != null)
        {
            keyPos = RootLocal(ws, filed);
            var poke = filed.GetComponent<ArchiveStackPoke>();
            if (poke != null && poke.shownScale.x > 1e-4f) keyScale = poke.shownScale.x;
            float d = Vector3.Distance(keyPos, Eye);
            if (d > Reach)
            {
                Vector3 toEye = Eye - keyPos; toEye.y = 0f; toEye.Normalize();
                Vector3 moved = keyPos;
                for (int i = 0; i < 8 && Vector3.Distance(moved, Eye) > Reach - 0.005f; i++) moved += toEye * 0.005f;
                Undo.RecordObject(filed, "filed plate reach");
                filed.position = ws.TransformPoint(moved);
                filed.rotation = ws.rotation * FaceEye(moved);
                EditorUtility.SetDirty(filed);
                sb.AppendLine($"  FiledPoke was {d:F3} m from the eye: moved from {P(keyPos)} to {P(moved)} ({Vector3.Distance(moved, Eye):F3} m), still over the out-tray stack");
                keyPos = moved;
            }
            float dNow = Vector3.Distance(keyPos, Eye);
            sb.AppendLine($"  FiledPoke at {P(keyPos)}, {keyScale * 1000f:F0} mm paper cap reading \"Filed N\" at 6.5 mm, {dNow:F3} m from the eye, {Vector3.Angle(EyeForward, keyPos - Eye):F1} deg off the eye's forward");
            if (dNow > Reach) sb.AppendLine($"  WARNING: FiledPoke is {dNow:F3} m from the eye, beyond the {Reach:F2} m reach");
        }
        else sb.AppendLine("  WARNING: OfficeV2/FiledPoke not found (run BuildOfficeV2): the archive card has no plate to open it");

        // The card: just above and behind the plate, facing the eye.
        const float w = ArchiveW, h = ArchiveH, bandH = 0.011f, z = -0.0005f, textW = 12.8f;
        float bottomY = keyPos.y + keyScale * 0.6f + 0.010f;
        Vector3 pos = new Vector3(keyPos.x, bottomY + h * 0.5f, keyPos.z - 0.030f);
        Quaternion rot = FaceEye(pos);
        for (int i = 0; i < 4; i++)
        {
            rot = FaceEye(pos);
            pos.y = bottomY + (rot * Vector3.up).y * h * 0.5f;
        }
        var card = Holder(office, "ArchiveCard");
        Undo.RecordObject(card, "archive card pose");
        card.localPosition = office.InverseTransformPoint(ws.TransformPoint(pos));
        card.rotation = ws.rotation * rot;
        card.localScale = Vector3.one;                      // full size while it is laid out; hidden again below

        Shape(card, "ArchiveCardUnderSheet", quad, Mat("PaperV2_SheetUnder", PaperUnder), new Vector3(0.003f, -0.003f, 0.002f), new Vector3(w, h, 1f));
        Shape(card, "ArchiveCardSheet", quad, Mat("Paper_Sheet", Paper), Vector3.zero, new Vector3(w, h, 1f));
        var band = Shape(card, "ArchiveCardBand", quad, Mat("Paper_ModeBand", Amber), new Vector3(0f, h * 0.5f - bandH * 0.5f, -0.0002f), new Vector3(w, bandH, 1f));

        float bandY = h * 0.5f - bandH * 0.5f;
        var mode = Label(card, "Mode", new Vector3(-0.024f, bandY, -0.0007f));
        Style(mode, "", new Vector2(8.0f, 0.9f), 5.5f, 5.5f, Ink, FontStyles.Bold, TextAlignmentOptions.Left, false);
        var count = Label(card, "Count", new Vector3(0.041f, bandY, -0.0007f));
        Style(count, "", new Vector2(4.6f, 0.9f), 5.5f, 5.5f, Ink, FontStyles.Bold, TextAlignmentOptions.Right, false);
        var title = Label(card, "Title", new Vector3(0f, 0.0285f, z));
        Style(title, "", new Vector2(textW, 1.9f), 6.5f, 7.5f, Ink, FontStyles.Bold, TextAlignmentOptions.TopLeft, true);
        var outcome = Label(card, "Outcome", new Vector3(0f, 0.0120f, z));
        Style(outcome, "", new Vector2(textW, 0.9f), 5.5f, 6.5f, Ink, FontStyles.Normal, TextAlignmentOptions.Left, false);
        var result = Label(card, "Result", new Vector3(0f, -0.0125f, z));
        Style(result, "", new Vector2(textW, 3.5f), 5.5f, 6.5f, Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
        var prov = Label(card, "Provenance", new Vector3(0f, -0.0390f, z));
        Style(prov, "", new Vector2(textW, 1.5f), 5.5f, 5.5f, InkSoft, FontStyles.Normal, TextAlignmentOptions.BottomLeft, true);

        Undo.RecordObject(archive, "archive card wiring");
        archive.readoutRoot = card.gameObject;
        archive.readout = null;                             // the legacy single text block is no longer used
        archive.readoutShownScale = Vector3.one;
        archive.readoutSeconds = 8f;
        archive.cardTitle = title;
        archive.cardOutcome = outcome;
        archive.cardResult = result;
        archive.cardProvenance = prov;
        archive.cardCount = count;
        archive.cardMode = mode;
        archive.cardBand = band.GetComponent<Renderer>();
        archive.riseFromLocal = office.InverseTransformPoint(ws.TransformPoint(anchorPos + new Vector3(0f, 0.020f, 0f)));
        archive.riseFromStack = true;
        archive.riseSeconds = 0.3f;
        EditorUtility.SetDirty(archive);

        Measure(sb, ws, "OfficeV2/ArchiveCard (archive card)", card, w, h,
                "it belongs to the out-tray, which is itself that far to the user's left; a head turn is expected (zone C)", out var mn, out var mx);
        Neighbours(sb, ws, all, "the archive card", mn, mx, new[] { "TaskBoard", "TaskDock", "QAStation", "Nameplate_Tester", "SoftwareFactory", "OutboxTray" });
        float gap = (pos.y - (rot * Vector3.up).y * h * 0.5f) - (keyPos.y + keyScale * 0.6f);
        sb.AppendLine($"  archive card: bottom edge {gap * 1000f:F0} mm above the Filed plate's housing, 3 cm behind it; rises from the stack at {P(anchorPos + new Vector3(0f, 0.020f, 0f))} in 0.3 s; closes after 8 s or on the poke after the oldest record");
        sb.AppendLine("  text: decision title 6.5 to 7.5 mm bold, outcome 5.5 to 6.5 mm, result 5.5 to 6.5 mm, provenance and band words 5.5 mm; no rich-text size tags");

        card.localScale = Vector3.one * Hidden;             // it holds no keys; hidden by scale like the rest
        EditorUtility.SetDirty(card.gameObject);
        sb.AppendLine("  ArchiveCard saved hidden by scale -> DecisionArchive.readoutRoot (same records, same ShowNext)");
    }
}
