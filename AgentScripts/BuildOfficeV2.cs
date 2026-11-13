using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Agents;
using MuseDesk.DebugTools;
using MuseDesk.Documents;
using MuseDesk.Feedback;
using MuseDesk.Networking;
using MuseDesk.Workspace;

/// <summary>
/// UX redesign, Office part (docs/Design/REDESIGN_BRIEF_FOR_AGENTS.md). Makes the three agents and
/// their desks the focus of the scene:
///   1. floating status panels (Monitor) and name tags (NameTag) are switched off; each desk gets a
///      small nameplate on its front edge (AgentNameplate);
///   2. the large floating mode sign is switched off; a small desk plaque says SCRIPTED DEMO / LIVE
///      (ModePlaque), with the one-line first-use guide directly above it (FirstUseGuide);
///   3. the loose keys (Start, Replay, Tips, Filed, Explain) and the old guide sign are switched
///      off; FocusToggle stays as the one persistent key, moved to the user's front right;
///   4. a completion receipt with a "Run again" key (CompletionReceipt);
///   5. a poke target on the out-tray stack for the decision archive (ArchiveStackPoke);
///   6. decor is curated down to a few items away from the interaction band;
///   7. feedback audio and the touch-summary card are re-wired / re-styled.
/// Everything new lives under WorkspaceRoot/OfficeV2. Existing objects are deactivated, never
/// destroyed. Idempotent; tolerates missing objects; saves the scene once. TaskBoard and
/// DecisionDocument are not modified (the paper's HeldPaperController is only referenced).
///   unity command script run --file AgentScripts/BuildOfficeV2.cs --entry BuildOfficeV2.Apply
/// </summary>
public static class BuildOfficeV2
{
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";
    const float Hidden = 0.001f;                                   // ScaleEase.HiddenFactor
    static readonly Vector3 Eye = new Vector3(0f, 0.30f, 0.62f);   // seated eye, root-local

    // Layout (root-local metres). Change here, re-run.
    static readonly Vector3 PlaquePos = new Vector3(0f, 0.016f, 0.34f);
    static readonly Vector3 ReceiptPos = new Vector3(-0.15f, 0.055f, 0.37f);
    static readonly Vector3 FocusPos = new Vector3(-0.26f, 0.03f, 0.46f);
    const float NameplateTiltDeg = 30f;
    const float RunAgainKeyScale = 0.040f;
    const float FiledKeyScale = 0.040f;

    // Design tokens (brief, section 4).
    static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    static readonly Color Paper = Hex("#F7F2E3");
    static readonly Color Ink = Hex("#1F2530");
    static readonly Color InkSoft = Hex("#4B5563");
    static readonly Color Plate = Hex("#1B1F27");
    static readonly Color OnPlate = Hex("#F3F4F6");
    static readonly Color Teal = Hex("#2FA39A");
    static readonly Color InfoBlue = Hex("#4C85DB");
    static readonly Color Success = Hex("#2E9B5F");
    static readonly Color Amber = Hex("#E0A030");

    // ───────────────────────────── helpers (idioms from FinalPass / FeedbackPass4) ─────────────────────────────

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Find(List<Transform> all, string name) => all.FirstOrDefault(t => t != null && t.name == name);
    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t.name == name);

    static string P(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
    static string RootPos(Transform ws, Transform t) => P(ws.InverseTransformPoint(t.position));

    static Material Mat(string name, Color c, bool unlit = true)
    {
        if (!AssetDatabase.IsValidFolder("Assets/MuseDesk/Art/Materials")) AssetDatabase.CreateFolder("Assets/MuseDesk/Art", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/MuseDesk/Art/Materials", "UI");
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Simple Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
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

    /// <summary>Find or create a child quad (mesh + material, no collider). Visible from its -Z side.</summary>
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
        t.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = t.GetComponent<MeshRenderer>();
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
        t.richText = true;
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
        sb.AppendLine($"  deactivated {t.name} at {RootPos(ws, t)}{(was ? "" : " (already inactive)")}{(string.IsNullOrEmpty(why) ? "" : ": " + why)}");
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
            // The template may billboard itself; a key that is part of a sign must not.
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
            var m = Mat(matName, c, unlit: false);
            m.SetFloat("_Smoothness", 0.35f);
            mr.sharedMaterial = m;
            EditorUtility.SetDirty(mr);
        }
        // The key's colour visual tints the cap per state; keep its states on the cap colour.
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
    /// Key labels are not authored at scale 0.01 (they sit under the key's own scale), so the size is
    /// worked out from the label's world scale: glyphs <paramref name="mm"/> millimetres tall, never
    /// below 5.5 mm, in a box that covers most of the cap.
    /// </summary>
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
        Undo.RecordObject(tmp, "key label");
        tmp.text = text;
        tmp.rectTransform.sizeDelta = new Vector2(0.90f * capWidth / s, 0.50f * capWidth / s);
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

    static Quaternion FaceEye(Vector3 rootPos) => Quaternion.LookRotation((rootPos - Eye).normalized, Vector3.up);

    // ───────────────────────────── entry ─────────────────────────────

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildOfficeV2: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildOfficeV2: no open scene.\n";

        var sb = new StringBuilder();
        var all = All();
        var ws = Find(all, "WorkspaceRoot");
        if (ws == null) return "BuildOfficeV2: WorkspaceRoot not found; nothing changed.\n";
        var session = Object.FindAnyObjectByType<SessionController>(FindObjectsInactive.Include);
        if (session == null) sb.AppendLine("WARNING: no SessionController in the scene; views are built but not bound.");

        var quad = PrimitiveMesh(PrimitiveType.Quad);
        var office = Holder(ws, "OfficeV2");
        office.localPosition = Vector3.zero;
        office.localRotation = Quaternion.identity;
        office.localScale = Vector3.one;

        sb.AppendLine("== BuildOfficeV2 ==");
        sb.AppendLine("root-local axes: +Z to the user, user's right is -X. New objects are under WorkspaceRoot/OfficeV2.");

        Nameplates(all, ws, office, session, quad, sb);
        var guide = ModePlaqueAndGuide(all, ws, office, session, quad, sb);
        var focus = LooseKeys(all, ws, sb);
        Receipt(ws, office, session, guide, focus, quad, sb);
        ArchivePoke(all, ws, office, focus, sb);
        Decor(all, ws, sb);
        Feedback(all, ws, session, sb);
        TouchCards(all, sb);

        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // ───────────────────────────── 1. nameplates ─────────────────────────────

    static readonly (string station, string agent, string id, string role, string hex, string mat)[] Stations =
    {
        ("ResearchStation", "Agent_Researcher", "researcher", "Researcher", "#4C85DB", "OfficeV2_Strip_Blue"),
        ("DevStation",      "Agent_Developer",  "developer",  "Developer",  "#2E9B5F", "OfficeV2_Strip_Green"),
        ("QAStation",       "Agent_Tester",     "tester",     "Tester",     "#E0A030", "OfficeV2_Strip_Amber"),
    };

    /// <summary>
    /// Where the desk top meets its front edge, measured from the desk meshes (they are not centred
    /// on their pivots). Looks only at geometry near desk-top height, so a wide plinth or a tall
    /// monitor does not decide the answer.
    /// </summary>
    static bool MeasureDeskFront(Transform desk, Transform ws, out float frontZ, out float topY, out float centreX, out string method)
    {
        const float bandLo = 0.09f, bandHi = 0.21f, edge = 0.015f;
        frontZ = float.NegativeInfinity; topY = 0.15f; centreX = 0f; method = "none";
        float xmin = float.PositiveInfinity, xmax = float.NegativeInfinity;
        var readable = new List<(Matrix4x4 m, Vector3[] v)>();
        var coarse = new List<(Vector3 a, Vector3 b)>();

        foreach (var f in desk.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!Drawn(f)) continue;
            if (f.sharedMesh.isReadable)
                readable.Add((ws.worldToLocalMatrix * f.transform.localToWorldMatrix, f.sharedMesh.vertices));
            else if (OwnMeshBounds(f.transform, ws, out var a, out var b))
                coarse.Add((a, b));
        }

        foreach (var (m, verts) in readable)
            for (int i = 0; i < verts.Length; i++)
            {
                var p = m.MultiplyPoint3x4(verts[i]);
                if (p.y < bandLo || p.y > bandHi) continue;
                if (p.z > frontZ) frontZ = p.z;
                if (p.x < xmin) xmin = p.x;
                if (p.x > xmax) xmax = p.x;
            }
        // Parts whose vertices cannot be read: use the part's box if the part is desk-height furniture.
        foreach (var (a, b) in coarse)
        {
            if (b.y < bandLo || a.y > bandHi || b.y > 0.24f) continue;
            if (b.z > frontZ) frontZ = b.z;
            if (a.x < xmin) xmin = a.x;
            if (b.x > xmax) xmax = b.x;
        }
        if (float.IsNegativeInfinity(frontZ)) return false;

        float best = float.NegativeInfinity;
        foreach (var (m, verts) in readable)
            for (int i = 0; i < verts.Length; i++)
            {
                var p = m.MultiplyPoint3x4(verts[i]);
                if (p.z >= frontZ - edge && p.y >= bandLo && p.y <= bandHi && p.y > best) best = p.y;
            }
        foreach (var (a, b) in coarse)
            if (b.z >= frontZ - edge && b.y >= bandLo && b.y <= bandHi && b.y > best) best = b.y;

        if (!float.IsNegativeInfinity(best)) topY = best;
        centreX = (xmin + xmax) * 0.5f;
        method = readable.Count > 0 ? (coarse.Count > 0 ? "vertices + part boxes" : "vertices") : "part boxes (meshes not readable)";
        return true;
    }

    static void Nameplates(List<Transform> all, Transform ws, Transform office, SessionController session, Mesh quad, StringBuilder sb)
    {
        sb.AppendLine("-- 1. status panels, name tags, desk nameplates --");
        var plateMat = Mat("OfficeV2_Plate", Plate);
        const float w = 0.09f, h = 0.026f;

        // Obstacles the plates must stay clear of.
        var obstacles = new List<(string name, Vector3 mn, Vector3 mx)>();
        foreach (var n in new[] { "SoftwareFactory", "InboxTray", "OutboxTray" })
        {
            var o = Find(all, n);
            if (o != null && DrawnBounds(o, ws, out var a, out var b)) obstacles.Add((n, a, b));
        }

        foreach (var s in Stations)
        {
            var station = ws.Find(s.station);
            if (station == null) station = Find(all, s.station);
            if (station == null) { sb.AppendLine($"  SKIP {s.station}: not found"); continue; }

            // Old floating panel and name tag: out of view, kept in the scene.
            var monitor = station.Find("Monitor");
            if (monitor != null) Deactivate(monitor, sb, ws, "floating status panel");
            var agent = Under(station, s.agent);
            var tag = agent != null ? agent.Find("NameTag") : null;
            if (tag != null) Deactivate(tag, sb, ws, "floating name tag");

            // Desk front edge.
            var desk = station.Find("Workstation");
            float frontZ = 0.19f, topY = 0.15f, cx = ws.InverseTransformPoint(station.position).x;
            string method = "FALLBACK (no desk found): station x, y 0.15, z 0.19";
            if (desk != null)
            {
                if (MeasureDeskFront(desk, ws, out float fz, out float ty, out float mx, out string how))
                {
                    frontZ = fz; topY = ty; cx = mx; method = how;
                }
                else if (DrawnBounds(desk, ws, out var dmn, out var dmx))
                {
                    frontZ = dmx.z; cx = (dmn.x + dmx.x) * 0.5f;
                    method = "FALLBACK (no desk-height geometry found): overall desk box front, y 0.15";
                }
            }

            // A 9 x 2.6 cm plate standing on the desk top at its front edge, leaning back 30 degrees.
            float tilt = NameplateTiltDeg * Mathf.Deg2Rad;
            var normal = new Vector3(0f, Mathf.Sin(tilt), Mathf.Cos(tilt));      // faces the user and up
            var up = new Vector3(0f, Mathf.Cos(tilt), -Mathf.Sin(tilt));
            var bottom = new Vector3(cx, topY + 0.001f, frontZ - 0.004f);
            var pos = bottom + up * (h * 0.5f);

            var plate = Holder(office, "Nameplate_" + s.role);
            plate.localPosition = pos;
            plate.localRotation = Quaternion.LookRotation(-normal, Vector3.up);
            plate.localScale = Vector3.one;

            Shape(plate, "Plate", quad, plateMat, Vector3.zero, new Vector3(w, h, 1f));
            Shape(plate, "Strip", quad, Mat(s.mat, Hex(s.hex)), new Vector3(-w * 0.5f + 0.0025f, 0f, -0.0002f), new Vector3(0.005f, h, 1f));
            var role = Label(plate, "Role", new Vector3(0.003f, 0.0058f, -0.0005f));
            Style(role, s.role, new Vector2(7.8f, 1.0f), 5.5f, 6.5f, OnPlate, FontStyles.Bold, TextAlignmentOptions.Left, false);
            var activity = Label(plate, "Activity", new Vector3(0.003f, -0.0062f, -0.0005f));
            Style(activity, "Idle", new Vector2(7.8f, 0.9f), 5.5f, 5.5f, OnPlate, FontStyles.Normal, TextAlignmentOptions.Left, false);

            var np = plate.GetComponent<AgentNameplate>();
            if (np == null) np = Undo.AddComponent<AgentNameplate>(plate.gameObject);
            np.controller = session;
            np.agentId = s.id;
            np.roleName = s.role;
            np.roleText = role;
            np.activityText = activity;
            np.shownScale = Vector3.one;
            EditorUtility.SetDirty(np);

            sb.AppendLine($"  created/updated Nameplate_{s.role} at {P(pos)}, leaning back {NameplateTiltDeg:F0} deg; desk front z={frontZ:F3}, top y={topY:F3}, centre x={cx:F3} [{method}]");

            // Clearance report (plate box, slightly padded).
            var half = new Vector3(w * 0.5f, h * 0.5f, 0.009f);
            Vector3 pmn = pos - half, pmx = pos + half;
            if (agent != null)
            {
                var ap = ws.InverseTransformPoint(agent.position);
                if (BoxesOverlap(pmn, pmx, ap + new Vector3(-0.07f, 0f, -0.07f), ap + new Vector3(0.07f, 0.31f, 0.07f)))
                    sb.AppendLine($"  WARNING: Nameplate_{s.role} overlaps the agent's standing volume at {P(ap)}");
            }
            foreach (var (name, a, b) in obstacles)
                if (BoxesOverlap(pmn, pmx, a, b))
                    sb.AppendLine($"  WARNING: Nameplate_{s.role} overlaps {name} (box {P(a)} .. {P(b)})");
        }
    }

    // ───────────────────────────── 2 + 3. mode plaque, one-line guide ─────────────────────────────

    static FirstUseGuide ModePlaqueAndGuide(List<Transform> all, Transform ws, Transform office, SessionController session, Mesh quad, StringBuilder sb)
    {
        sb.AppendLine("-- 2. mode plaque, 3. one-line guide --");

        // The large floating sign: no longer drawn. DiagnosticsOverlay keeps receiving the mode label
        // (the plaque listens to it) and can be shown again for debugging (showInWorld).
        var overlay = Object.FindAnyObjectByType<DiagnosticsOverlay>(FindObjectsInactive.Include);
        if (overlay != null)
        {
            Undo.RecordObject(overlay, "hide diagnostics sign");
            overlay.showInWorld = false;
            EditorUtility.SetDirty(overlay);
        }
        var signT = Find(all, "DiagnosticsText");
        var signText = overlay != null && overlay.text != null ? overlay.text : (signT != null ? signT.GetComponent<TMP_Text>() : null);
        if (signText != null)
        {
            Undo.RecordObject(signText, "hide diagnostics sign");
            signText.enabled = false;
            EditorUtility.SetDirty(signText);
            sb.AppendLine($"  hidden: {signText.name} (text renderer disabled, DiagnosticsOverlay.showInWorld=false; object stays active so diagnostics keep working)");
        }
        else sb.AppendLine("  SKIP: DiagnosticsText not found");

        // Keep the plaque and the guide line clear of the factory behind them and the stamp pad in front.
        Vector3 pos = PlaquePos;
        const float w = 0.12f, h = 0.024f, gw = 0.14f, gh = 0.017f, gap = 0.003f;
        float reachBack = (h * 0.5f + gap + gh) * 0.72f + 0.004f;        // how far the guide line's top edge leans back
        var factory = Find(all, "SoftwareFactory");
        if (factory != null && DrawnBounds(factory, ws, out _, out var fmx) && fmx.z > pos.z - reachBack)
        {
            float z = Mathf.Min(0.37f, fmx.z + reachBack);
            sb.AppendLine($"  note: plaque moved from z={pos.z:F3} to z={z:F3} to clear the factory front (z={fmx.z:F3})");
            pos.z = z;
        }
        var pad = Find(all, "Doc_StampRestPad");
        if (pad != null && DrawnBounds(pad, ws, out var pmn, out _) && pmn.z < pos.z + 0.012f)
            sb.AppendLine($"  WARNING: the stamp rest pad (back edge z={pmn.z:F3}) is within 1.2 cm of the plaque (z={pos.z:F3})");

        var plaque = Holder(office, "ModePlaque");
        plaque.localPosition = pos;
        plaque.localRotation = FaceEye(pos);
        plaque.localScale = Vector3.one;

        var plateMat = Mat("OfficeV2_Plate", Plate);
        Shape(plaque, "Plate", quad, plateMat, Vector3.zero, new Vector3(w, h, 1f));
        var strip = Shape(plaque, "Strip", quad, Mat("OfficeV2_Strip_Mode", Amber), new Vector3(-w * 0.5f + 0.0025f, 0f, -0.0002f), new Vector3(0.005f, h, 1f));
        var mode = Label(plaque, "Mode", new Vector3(0.003f, 0.0050f, -0.0005f));
        // Saved as STARTING: the true words are written at run time from the session, never baked in.
        Style(mode, "STARTING", new Vector2(10.8f, 1.0f), 5.5f, 6.5f, OnPlate, FontStyles.Bold, TextAlignmentOptions.Left, false);
        var status = Label(plaque, "Status", new Vector3(0.003f, -0.0062f, -0.0005f));
        Style(status, "", new Vector2(10.8f, 0.9f), 5.5f, 5.5f, OnPlate, FontStyles.Normal, TextAlignmentOptions.Left, false);

        var mp = plaque.GetComponent<ModePlaque>();
        if (mp == null) mp = Undo.AddComponent<ModePlaque>(plaque.gameObject);
        mp.controller = session;
        mp.diagnostics = overlay;
        mp.modeText = mode;
        mp.statusText = status;
        mp.strip = strip.GetComponent<Renderer>();
        EditorUtility.SetDirty(mp);
        sb.AppendLine($"  created/updated ModePlaque at {P(pos)} facing the eye, 12 x 2.4 cm (words: SCRIPTED DEMO / LIVE + connection word)");

        // The guide line: its own strip directly above the plaque, hidden (by scale) until a step shows.
        var line = Holder(plaque, "GuideLine");
        line.localPosition = new Vector3(0f, h * 0.5f + gap + gh * 0.5f, 0f);
        line.localRotation = Quaternion.identity;
        line.localScale = Vector3.one;
        Shape(line, "Plate", quad, plateMat, Vector3.zero, new Vector3(gw, gh, 1f));
        Shape(line, "Strip", quad, Mat("OfficeV2_Strip_Teal", Teal), new Vector3(-gw * 0.5f + 0.0025f, 0f, -0.0002f), new Vector3(0.005f, gh, 1f));
        var text = Label(line, "Text", new Vector3(0.003f, 0f, -0.0005f));
        Style(text, "Press Start on the task dock.", new Vector2(12.8f, 1.2f), 5.5f, 6.5f, OnPlate, FontStyles.Normal, TextAlignmentOptions.Left, false);

        var guide = line.GetComponent<FirstUseGuide>();
        if (guide == null) guide = Undo.AddComponent<FirstUseGuide>(line.gameObject);
        guide.controller = session;
        var doc = Find(all, "DecisionDocument");
        guide.paper = doc != null ? doc.GetComponent<HeldPaperController>() : null;   // referenced, not modified
        guide.label = text;
        guide.root = line.gameObject;
        guide.startHint = null;
        guide.shownScale = Vector3.one;
        EditorUtility.SetDirty(guide);
        line.localScale = Vector3.one * Hidden;
        sb.AppendLine($"  created/updated ModePlaque/GuideLine at {RootPos(ws, line)}, 14 x 1.7 cm, saved hidden (FirstUseGuide shows one line per step; paper {(guide.paper != null ? "wired" : "NOT FOUND: the Decide step will not show")})");

        // The old guide sign (and its component) goes out of view.
        var oldSign = ws.Find("GuideSign");
        if (oldSign == null) oldSign = Find(all, "GuideSign");
        if (oldSign != null) Deactivate(oldSign, sb, ws, "old guide sign, replaced by ModePlaque/GuideLine");
        return guide;
    }

    // ───────────────────────────── 4. loose keys ─────────────────────────────

    static Transform LooseKeys(List<Transform> all, Transform ws, StringBuilder sb)
    {
        sb.AppendLine("-- 4. loose keys --");
        foreach (var n in new[] { "StartKey", "ReplayKey", "TipsKey", "FiledKey", "ExplainToggle" })
        {
            var k = Find(all, n);
            if (k != null) Deactivate(k, sb, ws, "loose key");
            else sb.AppendLine($"  SKIP {n}: not found");
        }

        var focus = Find(all, "FocusToggle");
        if (focus == null) { sb.AppendLine("  SKIP FocusToggle: not found (Run again and Filed keys cannot be cloned)"); return null; }
        Undo.RecordObject(focus, "focus key");
        if (!focus.gameObject.activeSelf) focus.gameObject.SetActive(true);
        focus.position = ws.TransformPoint(FocusPos);
        Vector3 away = FocusPos - Eye; away.y = 0f;                     // yaw only: the key stands upright
        focus.rotation = ws.rotation * Quaternion.LookRotation(away.normalized, Vector3.up);
        EditorUtility.SetDirty(focus);
        var tmp = focus.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null && tmp.text != "Focus")
        {
            Undo.RecordObject(tmp, "focus label");
            tmp.text = "Focus";
            EditorUtility.SetDirty(tmp);
        }
        sb.AppendLine($"  moved FocusToggle to {RootPos(ws, focus)} (user's front right), label \"Focus\"; ButtonVisual / Cap3D / Housing3D untouched");
        return focus;
    }

    // ───────────────────────────── 5. completion receipt ─────────────────────────────

    static void Receipt(Transform ws, Transform office, SessionController session, FirstUseGuide guide, Transform focus, Mesh quad, StringBuilder sb)
    {
        sb.AppendLine("-- 5. completion receipt --");
        const float w = 0.13f, h = 0.08f;
        var card = Holder(office, "CompletionReceipt");
        card.localPosition = ReceiptPos;
        card.localRotation = FaceEye(ReceiptPos);
        card.localScale = Vector3.one;                      // full size while it is laid out; hidden again below

        Shape(card, "Sheet", quad, Mat("OfficeV2_Paper", Paper), Vector3.zero, new Vector3(w, h, 1f));
        var strip = Shape(card, "ModeStrip", quad, Mat("OfficeV2_Strip_Mode", Amber), new Vector3(0f, h * 0.5f - 0.0025f, -0.0002f), new Vector3(w, 0.005f, 1f));

        const float z = -0.0005f;
        var mode = Label(card, "Mode", new Vector3(0f, 0.0300f, z));
        Style(mode, "", new Vector2(12.0f, 0.9f), 5.5f, 6.5f, InkSoft, FontStyles.Bold, TextAlignmentOptions.Left, false);
        // Left column, 7.4 cm wide (x -6.1 .. +1.3 cm); the key has the right-hand column.
        var heading = Label(card, "Heading", new Vector3(-0.024f, 0.0185f, z));
        Style(heading, "", new Vector2(7.4f, 1.3f), 7.2f, 10f, Ink, FontStyles.Bold, TextAlignmentOptions.Left, false);
        var body = Label(card, "Body", new Vector3(-0.024f, -0.0025f, z));
        Style(body, "", new Vector2(7.4f, 2.7f), 5.5f, 7.5f, Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
        var detail = Label(card, "Detail", new Vector3(-0.024f, -0.0245f, z));
        Style(detail, "", new Vector2(7.4f, 1.4f), 5.5f, 5.5f, InkSoft, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
        var hint = Label(card, "Hint", new Vector3(0.040f, -0.011f, z));
        Style(hint, "", new Vector2(4.0f, 3.2f), 5.5f, 7.5f, InkSoft, FontStyles.Normal, TextAlignmentOptions.Center, true);

        var receipt = card.GetComponent<CompletionReceipt>();
        if (receipt == null) receipt = Undo.AddComponent<CompletionReceipt>(card.gameObject);
        receipt.controller = session;
        receipt.guide = guide;
        receipt.shownScale = Vector3.one;
        receipt.heading = heading;
        receipt.modeText = mode;
        receipt.body = body;
        receipt.detail = detail;
        receipt.hint = hint;
        receipt.modeStrip = strip.GetComponent<Renderer>();

        if (focus != null)
        {
            var key = CloneKey(card, focus, "RunAgainKey", out bool created);
            key.localScale = Vector3.one * RunAgainKeyScale;
            key.localRotation = Quaternion.identity;        // the receipt already faces the eye
            // Right-hand column of the receipt; the housing (1.2 x the key) ends at the sheet's edge.
            key.localPosition = new Vector3(0.040f, -0.011f, -(HousingDepth(key, RunAgainKeyScale) + 0.0005f));
            KeyLabel(key, "Run again", 5.5f, Ink);
            CapColor(key, "Key_RunAgain", Teal);
            Rewire(key, receipt, nameof(CompletionReceipt.RunAgain));
            EditorUtility.SetDirty(key.gameObject);
            receipt.runAgainKey = key;
            receipt.runAgainKeyScale = Vector3.one * RunAgainKeyScale;
            sb.AppendLine($"  {(created ? "created" : "updated")} CompletionReceipt/RunAgainKey (clone of FocusToggle, scale {RunAgainKeyScale}) at {RootPos(ws, key)} -> CompletionReceipt.RunAgain (= SessionController.ResetDemo, what ReplayKey called)");
        }
        else sb.AppendLine("  SKIP RunAgainKey: no FocusToggle to clone; the receipt shows without a key");
        EditorUtility.SetDirty(receipt);

        card.localScale = Vector3.one * Hidden;             // active (so the key registers) but not seen
        sb.AppendLine($"  created/updated CompletionReceipt at {P(ReceiptPos)} facing the eye, 13 x 8 cm, saved hidden by scale");
    }

    // ───────────────────────────── 6. archive via the out-tray ─────────────────────────────

    static void ArchivePoke(List<Transform> all, Transform ws, Transform office, Transform focus, StringBuilder sb)
    {
        sb.AppendLine("-- 6. archive via the out-tray --");
        var archive = Object.FindAnyObjectByType<DecisionArchive>(FindObjectsInactive.Include);
        if (archive == null) { sb.AppendLine("  SKIP: no DecisionArchive in the scene"); return; }

        Undo.RecordObject(archive, "archive readout");
        archive.readoutSeconds = 8f;
        if (archive.readoutRoot != null)
        {
            Vector3 s = archive.readoutRoot.transform.localScale;
            archive.readoutShownScale = s.sqrMagnitude < 1e-4f ? Vector3.one : s;
        }
        EditorUtility.SetDirty(archive);
        if (archive.readout != null)
            Style(archive.readout, null, archive.readout.rectTransform.sizeDelta, 7f, 9.5f, OnPlate, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
        sb.AppendLine($"  ArchiveReadout kept{(archive.readoutRoot != null ? " at " + RootPos(ws, archive.readoutRoot.transform) : " (NOT WIRED)")}: opens for 8 s after a poke, then closes by scale");

        if (focus == null) { sb.AppendLine("  SKIP FiledPoke: no FocusToggle to clone"); return; }
        var anchor = archive.stackAnchor != null ? archive.stackAnchor : Find(all, "Outbox_Stack_Anchor");
        if (anchor == null) anchor = Find(all, "OutboxTray");
        if (anchor == null) { sb.AppendLine("  SKIP FiledPoke: no out-tray stack anchor found"); return; }

        // Above the tallest possible stack (8 sheets), a little toward the user, facing the eye.
        Vector3 pos = ws.InverseTransformPoint(anchor.position) + new Vector3(0f, 0.040f, 0.012f);
        var key = CloneKey(office, focus, "FiledPoke", out bool created);
        key.localScale = Vector3.one * FiledKeyScale;       // full size while the label is measured
        key.localPosition = pos;
        key.localRotation = FaceEye(pos);
        var label = KeyLabel(key, "Filed", 6.5f, Ink);
        CapColor(key, "Key_FiledPoke", Paper);

        var poke = key.GetComponent<ArchiveStackPoke>();
        if (poke == null) poke = Undo.AddComponent<ArchiveStackPoke>(key.gameObject);
        poke.archive = archive;
        poke.countLabel = label;
        poke.shownScale = Vector3.one * FiledKeyScale;
        EditorUtility.SetDirty(poke);
        Rewire(key, poke, nameof(ArchiveStackPoke.Poke));
        key.localScale = Vector3.one * FiledKeyScale * Hidden;   // nothing to poke until something is filed
        EditorUtility.SetDirty(key.gameObject);
        sb.AppendLine($"  {(created ? "created" : "updated")} FiledPoke (clone of FocusToggle, scale {FiledKeyScale}) at {P(pos)} over the out-tray stack -> ArchiveStackPoke.Poke -> DecisionArchive.ShowNext; saved hidden, appears with the first filed paper");
    }

    // ───────────────────────────── 7. decor ─────────────────────────────

    static void Decor(List<Transform> all, Transform ws, StringBuilder sb)
    {
        sb.AppendLine("-- 7. decor --");
        // In order of preference; the first four that sit clear of the hands and the hand-over are kept.
        var preferred = new[] { "Decor_MiniPlant", "Decor_DeskLamp", "Decor_BookStack", "Decor_MiniClock", "Decor_CoffeeMug", "Decor_FilingCabinet" };
        var protect = new HashSet<string> { "Doc_StampRestPad", "Collab_ArtifactToken" };
        const int maxKept = 4;

        // Anything the project shelf shows or awards is functional, not decor.
        var functional = new HashSet<Transform>();
        foreach (var shelf in Object.FindObjectsByType<ProjectShelfController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            functional.Add(shelf.transform);
            if (shelf.awards != null) foreach (var a in shelf.awards) if (a != null) functional.Add(a.transform);
            if (shelf.slots != null) foreach (var sl in shelf.slots) if (sl != null) functional.Add(sl);
        }

        var containers = new List<Transform> { ws };
        var p6 = ws.Find("Phase6"); if (p6 == null) p6 = Find(all, "Phase6");
        if (p6 != null)
        {
            containers.Add(p6);
            var props = p6.Find("OfficeProps_Collection");
            if (props != null) containers.Add(props);
        }

        var candidates = new List<Transform>();
        foreach (var c in containers)
            foreach (Transform child in c)
            {
                string n = child.name;
                bool decor = n.StartsWith("Decor_") || n.StartsWith("Doc_") || n.StartsWith("Collab_") || n.StartsWith("Prop_")
                             || n == "PhysicsTray" || n == "SpatialDiagram_DeskLayout" || child.GetComponent<PushableProp>() != null;
                if (!decor || protect.Contains(n) || functional.Contains(child) || n == "OfficeV2") continue;
                candidates.Add(child);
            }

        Vector3 delivery = new Vector3(-0.20f, 0f, 0.17f);
        var dz = Find(all, "DeliveryZone");
        if (dz != null) delivery = ws.InverseTransformPoint(dz.position);

        bool Clear(Vector3 p)
        {
            if (p.z > 0.33f && p.z < 0.53f) return false;                                   // interaction band (with margin)
            if (new Vector2(p.x - delivery.x, p.z - delivery.z).magnitude < 0.14f) return false;   // paper hand-over
            if (new Vector2(Mathf.Abs(p.x) - 0.32f, p.z - 0.25f).magnitude < 0.10f) return false;  // the two trays
            return Mathf.Abs(p.x) <= 0.56f;
        }

        var kept = new List<Transform>();
        foreach (var name in preferred)
        {
            if (kept.Count >= maxKept) break;
            var t = candidates.FirstOrDefault(x => x.name == name);
            if (t == null) continue;
            if (!Clear(ws.InverseTransformPoint(t.position))) continue;
            kept.Add(t);
        }

        var removed = new List<Transform>();
        foreach (var t in candidates)
        {
            if (kept.Contains(t))
            {
                if (!t.gameObject.activeSelf) { Undo.RecordObject(t.gameObject, "keep decor"); t.gameObject.SetActive(true); EditorUtility.SetDirty(t.gameObject); }
                sb.AppendLine($"  kept {t.name} at {RootPos(ws, t)}");
                continue;
            }
            string why = t.name == "PhysicsTray" ? "toy-prop tray"
                       : t.GetComponent<PushableProp>() != null ? "pushable prop" : "decor";
            Deactivate(t, sb, ws, why);
            removed.Add(t);
        }
        sb.AppendLine($"  decor kept: {kept.Count} ({string.Join(", ", kept.Select(k => k.name))}); deactivated: {removed.Count}");

        // Pushable props outside the known containers are reported, not touched.
        foreach (var pp in ws.GetComponentsInChildren<PushableProp>(true))
            if (!candidates.Contains(pp.transform) && !candidates.Any(c => pp.transform.IsChildOf(c)))
                sb.AppendLine($"  left alone (outside the decor containers): {pp.name} at {RootPos(ws, pp.transform)}, active={pp.gameObject.activeInHierarchy}");

        // A saved layout must not switch removed decor back on.
        var store = ws.GetComponent<WorkspaceLayoutStore>();
        if (store != null && store.tracked != null)
        {
            Undo.RecordObject(store, "layout store");
            int before = store.tracked.Count;
            store.tracked.RemoveAll(x => x == null || removed.Contains(x));
            EditorUtility.SetDirty(store);
            sb.AppendLine($"  WorkspaceLayoutStore.tracked: {before} -> {store.tracked.Count} (removed decor is no longer restored from a saved layout)");
        }
    }

    // ───────────────────────────── 8. feedback audio ─────────────────────────────

    static void Feedback(List<Transform> all, Transform ws, SessionController session, StringBuilder sb)
    {
        sb.AppendLine("-- 8. feedback audio --");
        var fb = Object.FindAnyObjectByType<FeedbackController>(FindObjectsInactive.Include);
        if (fb == null)
        {
            var host = session != null ? session.gameObject : null;
            if (host == null) { sb.AppendLine("  SKIP: no FeedbackController and no SessionController object to add one to"); return; }
            fb = Undo.AddComponent<FeedbackController>(host);
            sb.AppendLine($"  added FeedbackController to {host.name}");
        }
        Undo.RecordObject(fb, "feedback wiring");
        fb.controller = session;
        fb.volume = 0.25f;
        fb.soundAnchor = ws;
        var doc = Find(all, "DecisionDocument");
        fb.paper = doc != null ? doc.GetComponent<HeldPaperController>() : null;   // referenced, not modified
        fb.keyPressSound = true;
        EditorUtility.SetDirty(fb);
        sb.AppendLine($"  FeedbackController on {fb.name}: volume 0.25, sounds come from WorkspaceRoot, paper-taken cue {(fb.paper != null ? "wired" : "NOT wired (no HeldPaperController found)")}, key-press click on every poke key");
    }

    // ───────────────────────────── touch summary cards ─────────────────────────────

    static void TouchCards(List<Transform> all, StringBuilder sb)
    {
        int n = 0;
        foreach (var summary in Object.FindObjectsByType<AgentTouchSummary>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (summary.cardText == null) continue;
            // The text was 1.4 mm tall and 4 mm in front of its plate; make it legible and flush.
            var t = summary.cardText.transform;
            Undo.RecordObject(t, "touch card text");
            t.localScale = Vector3.one * 0.01f;
            var plate = summary.cardRoot != null ? summary.cardRoot.transform.Find("Plate") : null;
            float plateZ = plate != null ? plate.localPosition.z : 0.002f;
            t.localPosition = new Vector3(0f, 0f, plateZ - 0.0005f);
            Style(summary.cardText, null, new Vector2(16.0f, 5.8f), 7f, 8f, OnPlate, FontStyles.Normal, TextAlignmentOptions.Center, true);
            n++;
        }
        sb.AppendLine($"-- touch summary cards: {n} restyled (7 to 8 mm text, 0.5 mm in front of the plate) --");
    }
}
