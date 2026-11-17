using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using MuseDesk.Agents;
using MuseDesk.Documents;
using MuseDesk.Networking;
using MuseDesk.Spatial;
using MuseDesk.Voice;
using MuseDesk.Workspace;

/// <summary>
/// Spatial workspace, reach side (docs/Engineering/SPATIAL_WORKSPACE_INTEGRATION.md): the
/// attention cue for a far-away office, the courier that brings the decision paper to the user,
/// the near-user summon keys and the summary perch.
///
/// Everything it creates lives under the scene object "Spatial" (created next to WorkspaceRoot if
/// missing), in a holder "SpatialReach". NOTHING is created under WorkspaceRoot: the office moves
/// away in Floor and Ceiling modes, and these objects are positioned in world space at run time.
/// Existing objects are only read (to wire references by name) and one key is cloned.
///
/// Everything is saved hidden by scale and stays hidden in Desk mode: with
/// SpatialWorkspaceState.IsRemote false (the default) the scene behaves exactly as before.
/// Idempotent; tolerates missing objects; saves the scene once.
///   unity command script run --file AgentScripts/BuildSpatialReachV1.cs --entry BuildSpatialReachV1.Apply
/// </summary>
public static class BuildSpatialReachV1
{
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";
    const float Hidden = 0.001f;                       // ScaleEase.HiddenFactor
    const float KeyScale = 0.045f;
    const float TextZ = -0.0005f;                      // 0.5 mm proud of the plate

    static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    static readonly Color Ink = Hex("#1F2530");
    static readonly Color Plate = Hex("#1B1F27");
    static readonly Color OnPlate = Hex("#F3F4F6");
    static readonly Color Housing = Hex("#2A2F3A");
    static readonly Color Teal = Hex("#2FA39A");
    static readonly Color Lavender = Hex("#8C7BD6");
    static readonly Color InfoBlue = Hex("#4C85DB");
    static readonly Color Success = Hex("#2E9B5F");
    static readonly Color Coral = Hex("#D9534A");
    static readonly Color Amber = Hex("#E0A030");

    static readonly (string station, string id)[] Stations =
    {
        ("ResearchStation", "researcher"),
        ("DevStation", "developer"),
        ("QAStation", "tester"),
    };

    // ───────────────────────────── helpers (idioms from BuildOfficeV2 / FinalPass) ─────────────────────────────

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Find(List<Transform> all, string name) => all.FirstOrDefault(t => t != null && t.name == name);
    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t.name == name);

    /// <summary>An existing UI material if the earlier builders made it, else one of this builder's own.</summary>
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

    static void Identity(Transform t)
    {
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
    }

    /// <summary>Find or create a child mesh (no collider). A quad is visible from its -Z side.</summary>
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

    /// <summary>Find or create a TextMeshPro label at scale 0.01 x <paramref name="k"/> (font size N = about N x k mm).</summary>
    static TMP_Text Label(Transform parent, string name, Vector3 lp, float k, string text, Vector2 boxCm, float minMm, float maxMm,
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
        t.localScale = Vector3.one * 0.01f * k;
        var mr = t.GetComponent<MeshRenderer>();
        if (mr != null) { mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; }

        if (text != null) tmp.text = text;
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
        tmp.richText = false;
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

    /// <summary>Key label sized from its world scale (glyphs <paramref name="mm"/> mm tall, 5.5 mm floor).</summary>
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

    static T Ensure<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    // ───────────────────────────── entry ─────────────────────────────

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildSpatialReachV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildSpatialReachV1: no open scene.\n";

        var sb = new StringBuilder();
        var all = All();
        var ws = Find(all, "WorkspaceRoot");
        if (ws == null) return "BuildSpatialReachV1: WorkspaceRoot not found; nothing changed.\n";

        sb.AppendLine("== BuildSpatialReachV1 ==");

        // The Spatial object: next to WorkspaceRoot (same parent), never under it.
        Transform spatial = all.FirstOrDefault(t => t != null && t.name == "Spatial" && !t.IsChildOf(ws));
        if (spatial == null)
        {
            var go = new GameObject("Spatial");
            Undo.RegisterCreatedObjectUndo(go, "Spatial");
            if (ws.parent != null) go.transform.SetParent(ws.parent, false);
            go.layer = ws.gameObject.layer;
            spatial = go.transform;
            Identity(spatial);
            sb.AppendLine("  created scene object Spatial " + (ws.parent != null ? "under " + ws.parent.name : "at the scene root"));
        }
        else sb.AppendLine("  found scene object Spatial (left as it is)");
        if (!spatial.gameObject.activeSelf)
            sb.AppendLine("  WARNING: Spatial is inactive; the reach components will not run until it is activated.");
        Vector3 ls = spatial.lossyScale;
        if (Mathf.Abs(ls.x - 1f) > 1e-3f || Mathf.Abs(ls.y - 1f) > 1e-3f || Mathf.Abs(ls.z - 1f) > 1e-3f)
            sb.AppendLine($"  WARNING: Spatial has world scale ({ls.x:F3}, {ls.y:F3}, {ls.z:F3}); the reach objects are authored for scale 1.");

        var reach = Holder(spatial, "SpatialReach");
        Identity(reach);

        var session = Object.FindAnyObjectByType<SessionController>(FindObjectsInactive.Include);
        if (session == null) sb.AppendLine("  WARNING: no SessionController in the scene; components are built but not bound.");

        var quad = PrimitiveMesh(PrimitiveType.Quad);
        var cube = PrimitiveMesh(PrimitiveType.Cube);
        var cylinder = PrimitiveMesh(PrimitiveType.Cylinder);

        // Does the rig have Interaction SDK ray interactors? (No second framework is ever added.)
        int rayInteractors = Object.FindObjectsByType<RayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        sb.AppendLine(rayInteractors > 0
            ? $"  rig has {rayInteractors} RayInteractor(s): ray targets are added to the beacon and the agents"
            : "  rig has NO RayInteractor: hand-ray summon is not built; the near-user keys are the summon path");

        // Stations (read only).
        var stationAnchors = new Transform[Stations.Length];
        var stationIds = new string[Stations.Length];
        for (int i = 0; i < Stations.Length; i++)
        {
            stationAnchors[i] = Find(all, Stations[i].station);
            stationIds[i] = Stations[i].id;
            if (stationAnchors[i] == null) sb.AppendLine($"  WARNING: station {Stations[i].station} not found; the beacon falls back to the office centre for {Stations[i].id}");
        }

        // 1. attention
        var attention = Ensure<AttentionController>(reach.gameObject);
        attention.controller = session;
        EditorUtility.SetDirty(attention);
        sb.AppendLine("-- 1. AttentionController on Spatial/SpatialReach --");

        var beacon = Beacon(reach, attention, stationAnchors, stationIds, quad, rayInteractors > 0, sb, out RayInteractable beaconRay);

        // 2. courier + remote delivery
        var remote = Courier(reach, ws, session, cube, cylinder, sb);

        // 3. perch
        var perch = Perch(reach, session, attention, quad, cube, sb);

        // 4. summon keys, ray targets, voice
        Summon(all, reach, stationAnchors, stationIds, beaconRay, rayInteractors > 0, session, sb);

        EditorUtility.SetDirty(beacon);
        EditorUtility.SetDirty(remote);
        EditorUtility.SetDirty(perch);

        sb.AppendLine("everything is saved hidden by scale; with SpatialWorkspaceState.IsRemote false (Desk) nothing shows or moves.");
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // ───────────────────────────── 1. attention beacon ─────────────────────────────

    /// <summary>The three faces of a cue. <paramref name="k"/> scales the whole layout (1 = office cue,
    /// plate 5 cm tall; the mirror cue uses a smaller k).</summary>
    static void Faces(Transform parent, Mesh quad, float k, out GameObject blocking, out GameObject completed, out GameObject error)
    {
        float h = 0.05f * k;
        var amber = Mat("OfficeV2_Strip_Amber", "SpatialReach_Amber", Amber);
        var green = Mat("OfficeV2_Strip_Green", "SpatialReach_Green", Success);
        var coral = Mat(null, "SpatialReach_Coral", Coral);

        // Blocking: amber plate with words, and a diamond "!" marker above it.
        var fb = Holder(parent, "Face_Blocking"); Identity(fb);
        float wb = 0.16f * k;
        Shape(fb, "BlockingPlate", quad, amber, Vector3.zero, new Vector3(wb, h, 1f));
        Label(fb, "Words", new Vector3(0f, 0f, TextZ), k, "Needs you", new Vector2(14.6f, 4.0f), 16f, 22f, Ink,
              FontStyles.Bold, TextAlignmentOptions.Center, false);
        float d = 0.036f * k;
        var mark = Shape(fb, "BlockingMarkChip", quad, amber, new Vector3(0f, h * 0.5f + d * 0.80f, 0f), new Vector3(d, d, 1f));
        mark.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Label(fb, "Mark", new Vector3(0f, h * 0.5f + d * 0.80f, TextZ), k, "!", new Vector2(3.0f, 3.2f), 22f, 22f, Ink,
              FontStyles.Bold, TextAlignmentOptions.Center, false);
        blocking = fb.gameObject;

        // Completed: a plain, narrower green plate. No marker, no symbol.
        var fc = Holder(parent, "Face_Completed"); Identity(fc);
        Shape(fc, "CompletedPlate", quad, green, Vector3.zero, new Vector3(0.10f * k, h, 1f));
        Label(fc, "Words", new Vector3(0f, 0f, TextZ), k, "Done", new Vector2(8.8f, 4.0f), 16f, 22f, OnPlate,
              FontStyles.Bold, TextAlignmentOptions.Center, false);
        completed = fc.gameObject;

        // Error: coral plate with a bar across its top.
        var fe = Holder(parent, "Face_Error"); Identity(fe);
        float we = 0.14f * k;
        Shape(fe, "ErrorPlate", quad, coral, Vector3.zero, new Vector3(we, h, 1f));
        Shape(fe, "ErrorTopBand", quad, coral, new Vector3(0f, h * 0.5f + 0.011f * k, 0f), new Vector3(we, 0.010f * k, 1f));
        Label(fe, "Words", new Vector3(0f, 0f, TextZ), k, "Problem", new Vector2(12.6f, 4.0f), 16f, 22f, OnPlate,
              FontStyles.Bold, TextAlignmentOptions.Center, false);
        error = fe.gameObject;

        // Saved with no face showing; AttentionBeacon switches the right one on.
        blocking.SetActive(false); completed.SetActive(false); error.SetActive(false);
    }

    static AttentionBeacon Beacon(Transform reach, AttentionController attention, Transform[] stationAnchors, string[] stationIds,
                                  Mesh quad, bool rigHasRay, StringBuilder sb, out RayInteractable beaconRay)
    {
        sb.AppendLine("-- 2. attention beacon --");
        beaconRay = null;
        var holder = Holder(reach, "AttentionBeacon"); Identity(holder);

        var office = Holder(holder, "OfficeCue");
        office.localPosition = Vector3.zero; office.localRotation = Quaternion.identity; office.localScale = Vector3.one;
        var sizer = Holder(office, "Sizer"); Identity(sizer);
        Faces(sizer, quad, 1f, out var ob, out var oc, out var oe);

        var mirror = Holder(holder, "MirrorCue");
        mirror.localPosition = Vector3.zero; mirror.localRotation = Quaternion.identity; mirror.localScale = Vector3.one;
        // 2 cm plate, 8.8 mm words: a glance cue at arm's length (5.5 mm floor respected).
        Faces(mirror, quad, 0.4f, out var mb, out var mc, out var me);

        if (rigHasRay)
        {
            // Ray + pinch on the cue asks for the office. It scales with the cue (and is unusably
            // small while the cue is hidden, which is always the case in Desk mode).
            beaconRay = RayTarget(sizer, "SummonRayTarget", new Vector3(0f, 0.012f, 0f), new Vector3(0.19f, 0.10f, 0.02f));
            sb.AppendLine("  added OfficeCue/Sizer/SummonRayTarget (BoxCollider trigger + ColliderSurface + RayInteractable)");
        }

        var beacon = Ensure<AttentionBeacon>(holder.gameObject);
        beacon.attention = attention;
        beacon.officeCue = office;
        beacon.officeCueShownScale = Vector3.one;
        beacon.officeSizer = sizer;
        beacon.officeBlocking = ob; beacon.officeCompleted = oc; beacon.officeError = oe;
        beacon.stationAnchors = stationAnchors;
        beacon.stationAgentIds = stationIds;
        beacon.plateHeight = 0.05f;
        beacon.mirrorCue = mirror;
        beacon.mirrorCueShownScale = Vector3.one;
        beacon.mirrorBlocking = mb; beacon.mirrorCompleted = mc; beacon.mirrorError = me;
        // showInDeskMode is left as it is (default false) so a coordinator's choice survives a re-run.

        office.localScale = Vector3.one * Hidden;
        mirror.localScale = Vector3.one * Hidden;
        sb.AppendLine("  created/updated Spatial/SpatialReach/AttentionBeacon: OfficeCue (plate 5 cm tall at sizer scale 1, sized at run time to about 2 degrees) and MirrorCue (2 cm), both saved hidden");
        sb.AppendLine($"  showInDeskMode={beacon.showInDeskMode} showMirror={beacon.showMirror}");
        return beacon;
    }

    static RayInteractable RayTarget(Transform parent, string name, Vector3 centre, Vector3 size)
    {
        var t = Holder(parent, name);
        t.localPosition = centre; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
        var col = Ensure<BoxCollider>(t.gameObject);
        col.isTrigger = true;                 // never pushes hands or props
        col.center = Vector3.zero;
        col.size = size;
        var surface = Ensure<ColliderSurface>(t.gameObject);
        surface.InjectCollider(col);
        var ray = Ensure<RayInteractable>(t.gameObject);
        ray.InjectSurface(surface);
        EditorUtility.SetDirty(col);
        EditorUtility.SetDirty(surface);
        EditorUtility.SetDirty(ray);
        return ray;
    }

    // ───────────────────────────── 2. courier and remote delivery ─────────────────────────────

    static RemoteDocumentDeliveryController Courier(Transform reach, Transform ws, SessionController session,
                                                    Mesh cube, Mesh cylinder, StringBuilder sb)
    {
        sb.AppendLine("-- 3. courier and remote delivery --");
        var anchor = Holder(reach, "CourierAnchor"); Identity(anchor);

        var courier = Holder(reach, "Courier");
        courier.localPosition = Vector3.zero; courier.localRotation = Quaternion.identity; courier.localScale = Vector3.one;
        var housing = Mat(null, "SpatialReach_Housing", Housing, unlit: false);
        var teal = Mat(null, "SpatialReach_Teal", Teal, unlit: false);
        // A small tray: a flat bed, a low rail on the far side, two round pods at the ends. Opaque.
        Shape(courier, "TrayBed", cube, housing, new Vector3(0f, -0.0025f, 0f), new Vector3(0.078f, 0.005f, 0.050f));
        Shape(courier, "TrayRail", cube, housing, new Vector3(0f, 0.003f, 0.023f), new Vector3(0.078f, 0.011f, 0.004f));
        Shape(courier, "PodLeft", cylinder, teal, new Vector3(-0.046f, -0.001f, 0f), new Vector3(0.018f, 0.004f, 0.018f));
        Shape(courier, "PodRight", cylinder, teal, new Vector3(0.046f, -0.001f, 0f), new Vector3(0.018f, 0.004f, 0.018f));
        var visual = Ensure<CourierVisual>(courier.gameObject);
        visual.shownScale = Vector3.one;
        EditorUtility.SetDirty(visual);
        courier.localScale = Vector3.one * Hidden;

        var remote = Ensure<RemoteDocumentDeliveryController>(reach.gameObject);
        remote.controller = session;
        remote.courierAnchor = anchor;
        remote.courier = visual;
        remote.workspaceRootFallback = ws;

        var delivery = Object.FindAnyObjectByType<DocumentDeliveryController>(FindObjectsInactive.Include);
        remote.delivery = delivery;
        HeldPaperController held = null;
        if (delivery != null && delivery.paperRoot != null) held = delivery.paperRoot.GetComponent<HeldPaperController>();
        if (held == null) held = Object.FindAnyObjectByType<HeldPaperController>(FindObjectsInactive.Include);
        remote.paper = held;
        if (delivery == null) sb.AppendLine("  WARNING: no DocumentDeliveryController; the filing glide is not lengthened while remote");
        if (held == null) sb.AppendLine("  WARNING: no HeldPaperController; remote delivery is inert");
        else
        {
            sb.AppendLine($"  paper: {held.name} agentHoldPoint={(held.agentHoldPoint != null ? held.agentHoldPoint.name : "NULL")} followHand={(held.followHand != null ? held.followHand.name : "NULL")} heldScale={held.heldScale} sheetHeight={held.sheetHeight}");
            if (held.agentHoldPoint == null)
                sb.AppendLine("  NOTE: the paper has no agentHoldPoint, so it already appears in front of the user; the courier never runs");
        }

        var registry = Object.FindAnyObjectByType<AgentRegistry>(FindObjectsInactive.Include);
        MuseDesk.Agents.AgentView researcher = null;
        if (registry != null && registry.agents != null)
            researcher = registry.agents.FirstOrDefault(a => a != null && a.agentId == "researcher");
        remote.researcherMovement = researcher != null ? researcher.GetComponent<AgentMovement>() : null;
        remote.researcherBridge = researcher != null ? researcher.GetComponent<MuseDesk.Agents.AgentAnimatorBridge>() : null;
        sb.AppendLine($"  researcher: movement={(remote.researcherMovement != null)} animatorBridge={(remote.researcherBridge != null)} (both optional)");
        sb.AppendLine($"  platform at office root-local ({remote.platformLocal.x:F2}, {remote.platformLocal.y:F2}, {remote.platformLocal.z:F2}); courier and anchor are under Spatial/SpatialReach, saved hidden");
        return remote;
    }

    // ───────────────────────────── 3. summary perch ─────────────────────────────

    static PerchController Perch(Transform reach, SessionController session, AttentionController attention, Mesh quad, Mesh cube, StringBuilder sb)
    {
        sb.AppendLine("-- 4. summary perch --");
        const float w = 0.13f, h = 0.075f;
        var stand = Holder(reach, "PerchStand");
        stand.localPosition = Vector3.zero; stand.localRotation = Quaternion.identity; stand.localScale = Vector3.one;

        Shape(stand, "PerchPlate", quad, Mat("OfficeV2_Plate", "SpatialReach_Plate", Plate), Vector3.zero, new Vector3(w, h, 1f));
        var strip = Shape(stand, "PerchRoleStrip", quad, Mat(null, "SpatialReach_RoleStrip", InfoBlue),
                          new Vector3(0f, h * 0.5f - 0.004f, -0.0002f), new Vector3(w, 0.008f, 1f));
        Shape(stand, "PerchFoot", cube, Mat(null, "SpatialReach_Housing", Housing, unlit: false),
              new Vector3(0f, -h * 0.5f - 0.003f, 0.012f), new Vector3(0.05f, 0.006f, 0.03f));

        // Name: x -6.1 .. +0.7 cm. Mode: x +1.0 .. +6.2 cm ("SCRIPTED DEMO" at 5.5 mm bold needs about 4.8 cm).
        var name = Label(stand, "Name", new Vector3(-0.027f, 0.0190f, TextZ), 1f, "", new Vector2(6.8f, 1.3f), 7.2f, 10f, OnPlate,
                         FontStyles.Bold, TextAlignmentOptions.Left, false);
        var mode = Label(stand, "Mode", new Vector3(0.036f, 0.0190f, TextZ), 1f, "", new Vector2(5.2f, 0.9f), 5.5f, 5.5f, OnPlate,
                         FontStyles.Bold, TextAlignmentOptions.Right, false);
        var state = Label(stand, "State", new Vector3(0f, 0.0040f, TextZ), 1f, "", new Vector2(12.0f, 1.1f), 5.5f, 7.5f, OnPlate,
                          FontStyles.Bold, TextAlignmentOptions.Left, false);
        var progress = Label(stand, "Progress", new Vector3(0f, -0.0170f, TextZ), 1f, "", new Vector2(12.0f, 2.4f), 5.5f, 6.5f, OnPlate,
                             FontStyles.Normal, TextAlignmentOptions.TopLeft, true);

        var perch = Ensure<PerchController>(reach.gameObject);
        perch.controller = session;
        perch.attention = attention;
        perch.stand = stand;
        perch.standShownScale = Vector3.one;
        perch.nameText = name;
        perch.modeText = mode;
        perch.stateText = state;
        perch.progressText = progress;
        perch.roleStrip = strip.GetComponent<Renderer>();

        stand.localScale = Vector3.one * Hidden;
        sb.AppendLine("  created/updated Spatial/SpatialReach/PerchStand (13 x 7.5 cm plate: name, LIVE / SCRIPTED DEMO, activity words, progress line), saved hidden");
        return perch;
    }

    // ───────────────────────────── 4. summon keys, ray targets, voice ─────────────────────────────

    static void Summon(List<Transform> all, Transform reach, Transform[] stationAnchors, string[] stationIds,
                       RayInteractable beaconRay, bool rigHasRay, SessionController session, StringBuilder sb)
    {
        sb.AppendLine("-- 5. summon --");
        var summon = Ensure<SummonController>(reach.gameObject);

        var keys = Holder(reach, "SummonKeys");
        keys.localPosition = Vector3.zero; keys.localRotation = Quaternion.identity;
        keys.localScale = Vector3.one;                       // full size while laid out; hidden again below

        Transform template = Find(all, "FocusToggle");
        if (template == null) template = Find(all, "RunAgainKey");
        if (template != null && !template.IsChildOf(reach))
        {
            // Holder +X is the user's right (the holder faces the head at run time).
            var bring = CloneKey(keys, template, "BringKey", out bool c1);
            bring.localScale = Vector3.one * KeyScale;
            bring.localRotation = Quaternion.identity;
            bring.localPosition = new Vector3(0.028f, 0f, -(HousingDepth(bring, KeyScale) + 0.0005f));
            var label = KeyLabel(bring, "Bring it\nhere", 5.5f, Ink);
            CapColor(bring, "Key_SpatialBring", Teal);
            Rewire(bring, summon, nameof(SummonController.PressBring));
            EditorUtility.SetDirty(bring.gameObject);
            summon.bringLabel = label;

            var status = CloneKey(keys, template, "StatusKey", out bool c2);
            status.localScale = Vector3.one * KeyScale;
            status.localRotation = Quaternion.identity;
            status.localPosition = new Vector3(-0.028f, 0f, -(HousingDepth(status, KeyScale) + 0.0005f));
            KeyLabel(status, "Status", 5.5f, Ink);
            CapColor(status, "Key_SpatialStatus", Lavender);
            Rewire(status, summon, nameof(SummonController.PressStatus));
            EditorUtility.SetDirty(status.gameObject);

            sb.AppendLine($"  {(c1 ? "created" : "updated")} SummonKeys/BringKey -> SummonController.PressBring; {(c2 ? "created" : "updated")} SummonKeys/StatusKey -> SummonController.PressStatus (clones of {template.name}, uniform scale {KeyScale})");
        }
        else sb.AppendLine("  SKIP summon keys: no FocusToggle / RunAgainKey to clone. Requests can still be raised by a driver (SummonRequests.Raise...).");

        summon.keysRoot = keys;
        summon.keysShownScale = Vector3.one;
        summon.agentIds = stationIds;
        summon.stationAnchors = stationAnchors;
        summon.beaconRayTarget = beaconRay;

        if (rigHasRay)
        {
            var targets = new RayInteractable[stationIds.Length];
            for (int i = 0; i < stationIds.Length; i++)
            {
                targets[i] = RayTarget(reach, "RayTarget_" + stationIds[i], Vector3.zero, new Vector3(0.20f, 0.36f, 0.20f));
                targets[i].transform.localScale = Vector3.one * Hidden;      // SummonController shows them only while remote
            }
            summon.agentRayTargets = targets;
            sb.AppendLine($"  created/updated {targets.Length} agent ray targets under Spatial/SpatialReach (hidden by scale outside remote modes)");
        }
        else summon.agentRayTargets = new RayInteractable[0];
        EditorUtility.SetDirty(summon);

        keys.localScale = Vector3.one * Hidden;

        // Voice (optional, additive): the router only listens to the existing source's status line.
        var router = Ensure<SummonVoiceRouter>(reach.gameObject);
        router.controller = session;
        var policy = Object.FindAnyObjectByType<VoiceDecisionController>(FindObjectsInactive.Include);
        router.policy = policy;
        MonoBehaviour source = policy != null ? policy.sourceBehaviour : null;
        if (!(source is IVoiceCommandSource))
            source = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                           .FirstOrDefault(m => m is IVoiceCommandSource);
        router.sourceBehaviour = source;
        EditorUtility.SetDirty(router);
        sb.AppendLine($"  voice router: policy={(policy != null)} source={(source != null ? source.GetType().Name : "none")} (inert unless voice is enabled, available and, by the existing policy, live)");
    }
}
