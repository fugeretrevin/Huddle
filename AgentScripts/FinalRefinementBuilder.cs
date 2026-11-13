using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Art.FinalRefinement;

/// <summary>
/// Builds URP materials, verifies FBX bounds, generates reusable Final Refinement prefabs under
/// Assets/MuseDesk/Art/Prefabs/FinalRefinement/, creates isolated FinalRefinementPreviewScene.unity,
/// and captures Before-vs-After comparison Game-view PNGs. Does NOT modify MainMRScene.unity or runtime code.
///
/// Invoke via:
///   unity command script run --file AgentScripts/FinalRefinementBuilder.cs --entry FinalRefinementBuilder.RunAll
/// </summary>
public static class FinalRefinementBuilder
{
    const string MatDir       = "Assets/MuseDesk/Art/Materials";
    const string PrefabDir    = "Assets/MuseDesk/Art/Prefabs/FinalRefinement";
    const string ModelsRoot   = "Assets/MuseDesk/Art/Models";
    const string PreviewScene = "Assets/MuseDesk/Scenes/FinalRefinementPreviewScene.unity";
    const string ShotDir      = "Screenshots/FinalRefinement";
    const string FontPath     = "Assets/MuseDesk/Art/Fonts/Inter SDF.asset";
    const string FontBoldPath = "Assets/MuseDesk/Art/Fonts/Inter-Bold SDF.asset";

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out var c);
        return c;
    }

    // Exact Design System Tokens
    static readonly Color Paper        = Hex("#F7F2E3");
    static readonly Color PaperEdge    = Hex("#DDD5BF");
    static readonly Color PaperTint    = Hex("#ECE4CF");
    static readonly Color Ink          = Hex("#1F2530");
    static readonly Color InkSoft      = Hex("#4B5563");
    static readonly Color Plate        = Hex("#1B1F27");
    static readonly Color PlateRim     = Hex("#252A34");
    static readonly Color OnPlate      = Hex("#F3F4F6");
    static readonly Color Housing      = Hex("#2A2F3A");
    static readonly Color HousingInner = Hex("#14171E");
    static readonly Color KeyCream     = Hex("#F4EFE2");
    static readonly Color Teal         = Hex("#2FA39A");
    static readonly Color Lavender     = Hex("#8C7BD6");
    static readonly Color InfoBlue     = Hex("#4C85DB");
    static readonly Color Success      = Hex("#2E9B5F");
    static readonly Color Coral        = Hex("#D9534A");
    static readonly Color Amber        = Hex("#E0A030");
    static readonly Color Brass        = Hex("#D1A84C");
    static readonly Color TrayWood     = Hex("#4A372B");
    static readonly Color WarmWood     = Hex("#7C5840");
    static readonly Color FeltSlate    = Hex("#272D37");

    public static string RunAll()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== FinalRefinementBuilder.RunAll ===");

        EnsureFolder(MatDir);
        EnsureFolder("Assets/MuseDesk/Art/Prefabs");
        EnsureFolder(PrefabDir);
        Directory.CreateDirectory(ShotDir);

        // 1. Create / update Design-System URP Materials FIRST so FBX reimport binds them automatically
        var mats = EnsureDesignSystemMaterials();
        sb.AppendLine($"Materials created/updated: {mats.Count}");

        // 2. Refresh & reimport our new FBXs so ModelBoundsFixer binds materials + recalculates bounds
        string[] fbxPaths = GatherNewFbxPaths();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (var p in fbxPaths)
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.SaveAssets();

        // 3. Verify non-zero bounds & factory clip count
        VerifyImportedModels(fbxPaths, sb);

        // 4. Build all reusable Prefabs under Assets/MuseDesk/Art/Prefabs/FinalRefinement/
        var font     = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        var fontBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBoldPath) ?? font;
        var prefabs  = BuildAllPrefabs(mats, font, fontBold, sb);

        InspectPrefabChildren(prefabs["Doc_PaperV2_Expanded_Polished"], sb);
        InspectPrefabChildren(prefabs["Doc_ReceiptCard_Polished"], sb);

        // 5. Build isolated FinalRefinementPreviewScene & render 6 side-by-side comparison views
        BuildPreviewSceneAndCapture(prefabs, mats, font, fontBold, sb);

        AssetDatabase.SaveAssets();
        sb.AppendLine("=== FinalRefinementBuilder DONE ===");
        return sb.ToString();
    }

    static void EnsureFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder)) return;
        string parent = Path.GetDirectoryName(assetFolder).Replace('\\', '/');
        string name = Path.GetFileName(assetFolder);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    static Material UpsertMat(string name, string shaderName, Color col, float smoothness = 0.32f, float metallic = 0f, Color? emission = null)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find(shaderName) ?? Shader.Find("Universal Render Pipeline/Simple Lit");
        if (m == null)
        {
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader != sh && sh != null)
        {
            m.shader = sh;
        }
        m.SetColor("_BaseColor", col);
        m.SetColor("_Color", col);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_EmissionColor"))
        {
            m.SetColor("_EmissionColor", emission ?? Color.black);
            if (emission.HasValue && emission.Value != Color.black)
                m.EnableKeyword("_EMISSION");
        }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Dictionary<string, Material> EnsureDesignSystemMaterials()
    {
        const string simpleLit = "Universal Render Pipeline/Simple Lit";
        const string keyShader = "MuseDesk/TactileKeyCap";

        var dict = new Dictionary<string, Material>
        {
            ["Mat_DS_Paper"]          = UpsertMat("Mat_DS_Paper",          simpleLit, Paper,        0.14f),
            ["Mat_DS_PaperEdge"]      = UpsertMat("Mat_DS_PaperEdge",      simpleLit, PaperEdge,    0.12f),
            ["Mat_DS_PaperTint"]      = UpsertMat("Mat_DS_PaperTint",      simpleLit, PaperTint,    0.14f),
            ["Mat_DS_Plate"]          = UpsertMat("Mat_DS_Plate",          simpleLit, Plate,        0.26f),
            ["Mat_DS_PlateRim"]       = UpsertMat("Mat_DS_PlateRim",       simpleLit, PlateRim,     0.32f),
            ["Mat_DS_Housing"]        = UpsertMat("Mat_DS_Housing",        simpleLit, Housing,      0.34f),
            ["Mat_DS_HousingInner"]   = UpsertMat("Mat_DS_HousingInner",   simpleLit, HousingInner, 0.16f),
            ["Mat_DS_AccentAmber"]    = UpsertMat("Mat_DS_AccentAmber",    simpleLit, Amber,        0.28f),
            ["Mat_DS_AccentBlue"]     = UpsertMat("Mat_DS_AccentBlue",     simpleLit, InfoBlue,     0.28f),
            ["Mat_DS_AccentSuccess"]  = UpsertMat("Mat_DS_AccentSuccess",  simpleLit, Success,      0.28f),
            ["Mat_DS_AccentTeal"]     = UpsertMat("Mat_DS_AccentTeal",     simpleLit, Teal,         0.28f),
            ["Mat_DS_AccentLavender"] = UpsertMat("Mat_DS_AccentLavender", simpleLit, Lavender,     0.28f),
            ["Mat_DS_InkSoft"]        = UpsertMat("Mat_DS_InkSoft",        simpleLit, InkSoft,      0.18f),
            ["Mat_DS_Brass"]          = UpsertMat("Mat_DS_Brass",          simpleLit, Brass,        0.58f),
            ["Mat_DS_TrayWood"]       = UpsertMat("Mat_DS_TrayWood",       simpleLit, TrayWood,     0.24f),
            ["Mat_DS_WarmWood"]       = UpsertMat("Mat_DS_WarmWood",       simpleLit, WarmWood,     0.26f),
            ["Mat_DS_FeltSlate"]      = UpsertMat("Mat_DS_FeltSlate",      simpleLit, FeltSlate,    0.15f),

            // Tactile keycap materials using MuseDesk/TactileKeyCap (supports both _Color & _BaseColor MPB states)
            ["Mat_DS_KeyCapCream"]    = UpsertMat("Mat_DS_KeyCapCream",    keyShader, KeyCream,     0.42f),
            ["Mat_DS_KeyCapTeal"]     = UpsertMat("Mat_DS_KeyCapTeal",     keyShader, Teal,         0.42f),
            ["Mat_DS_KeyCapLavender"] = UpsertMat("Mat_DS_KeyCapLavender", keyShader, Lavender,     0.42f),
            ["Mat_DS_KeyCapSuccess"]  = UpsertMat("Mat_DS_KeyCapSuccess",  keyShader, Success,      0.42f),
            ["Mat_DS_KeyCapCoral"]    = UpsertMat("Mat_DS_KeyCapCoral",    keyShader, Coral,        0.42f),
            ["Mat_DS_KeyStateRim"]    = UpsertMat("Mat_DS_KeyStateRim",    keyShader, Color.white,  0.48f, 0f, Color.white * 0.10f),
        };
        return dict;
    }

    static string[] GatherNewFbxPaths()
    {
        return new[]
        {
            $"{ModelsRoot}/Documents/Doc_PaperV2_Expanded.fbx",
            $"{ModelsRoot}/Documents/Doc_PaperV2_DetailsSheet.fbx",
            $"{ModelsRoot}/Documents/Doc_PaperV2_Small.fbx",
            $"{ModelsRoot}/Documents/Doc_ReceiptCard.fbx",
            $"{ModelsRoot}/Documents/Doc_ArchiveTray_Polished.fbx",
            $"{ModelsRoot}/UI/UI_PokeKey_Square_Housing.fbx",
            $"{ModelsRoot}/UI/UI_PokeKey_Square_Cap.fbx",
            $"{ModelsRoot}/UI/UI_PokeKey_Wide_Housing.fbx",
            $"{ModelsRoot}/UI/UI_PokeKey_Wide_Cap.fbx",
            $"{ModelsRoot}/UI/UI_PokeKey_HeroWide_Housing.fbx",
            $"{ModelsRoot}/UI/UI_PokeKey_HeroWide_Cap.fbx",
            $"{ModelsRoot}/UI/UI_PokeKey_Chip_Housing.fbx",
            $"{ModelsRoot}/UI/UI_PokeKey_Chip_Cap.fbx",
            $"{ModelsRoot}/TaskBoard/TaskCard_V2_Polished.fbx",
            $"{ModelsRoot}/TaskBoard/TaskDock_Plate.fbx",
            $"{ModelsRoot}/TaskBoard/TaskBoard_KanbanFrame_V2.fbx",
            $"{ModelsRoot}/Plates/Plate_AgentNameplate.fbx",
            $"{ModelsRoot}/Plates/Plate_ModePlaque.fbx",
            $"{ModelsRoot}/Plates/Plate_UtilitySign.fbx",
            $"{ModelsRoot}/Optimized/Doc_InboxTray_Optimized.fbx",
            $"{ModelsRoot}/Factory/SoftwareFactory_Complete_Polished.fbx",
        };
    }

    static void VerifyImportedModels(string[] fbxPaths, StringBuilder sb)
    {
        sb.AppendLine("-- FBX Import & Bounds Verification --");
        foreach (var path in fbxPaths)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) { sb.AppendLine($"  MISSING: {path}"); continue; }
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            int meshCount = 0;
            int nonReadable = 0;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                meshCount++;
                if (!mf.sharedMesh.isReadable) nonReadable++;
                var b = mf.sharedMesh.bounds;
                if (!hasBounds) { bounds = b; hasBounds = true; } else bounds.Encapsulate(b);
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                meshCount++;
                if (!smr.sharedMesh.isReadable) nonReadable++;
                var b = smr.sharedMesh.bounds;
                if (!hasBounds) { bounds = b; hasBounds = true; } else bounds.Encapsulate(b);
            }
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            string clipInfo = clips.Length > 0 ? $", clips={clips.Length}" : "";
            sb.AppendLine($"  OK {Path.GetFileName(path),-40} meshes={meshCount} nonRead={nonReadable} size=({bounds.size.x:F3},{bounds.size.y:F3},{bounds.size.z:F3})m{clipInfo}");
        }
    }

    // --------------------------------------------------------------------------
    // Helpers for Prefab Authoring
    // --------------------------------------------------------------------------

    static void InspectPrefabChildren(GameObject root, StringBuilder sb)
    {
        if (root == null) return;
        sb.AppendLine($"-- Inspect {root.name} --");
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>();
            var tmp = r.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                sb.AppendLine($"  TMP {r.name,-16} pos={r.transform.localPosition:F4} rot={r.transform.localEulerAngles:F1} mat={r.sharedMaterial?.name} txt='{tmp.text}' fs={tmp.fontSize:F1}");
            }
            else if (mf != null && mf.sharedMesh != null)
            {
                var b = mf.sharedMesh.bounds;
                Vector3 wMin = r.transform.localPosition + b.min;
                Vector3 wMax = r.transform.localPosition + b.max;
                sb.AppendLine($"  MSH {r.name,-16} pos={r.transform.localPosition:F4} z=[{wMin.z:F4}..{wMax.z:F4}] mat={r.sharedMaterial?.name}");
            }
        }
    }

    static GameObject LoadModel(string relPath) => AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsRoot}/{relPath}");

    static GameObject SavePrefab(string name, GameObject tempRoot)
    {
        string path = $"{PrefabDir}/{name}.prefab";
        var pf = PrefabUtility.SaveAsPrefabAsset(tempRoot, path);
        UnityEngine.Object.DestroyImmediate(tempRoot);
        return pf;
    }

    static TMP_Text AddTmp(Transform parent, string name, Vector3 localPos, Vector2 sizeCm,
                           float fontSizeMm, FontStyles style, Color color, TextAlignmentOptions align,
                           bool wrap, string text, TMP_FontAsset font, TMP_FontAsset boldFont)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * 0.01f; // Design system: size N mm -> fontSize N at 0.01 scale

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = (style & FontStyles.Bold) != 0 && boldFont != null ? boldFont : font;
        tmp.text = text;
        tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        tmp.rectTransform.sizeDelta = sizeCm;
        tmp.fontSize = fontSizeMm;
        tmp.fontSizeMin = Mathf.Min(fontSizeMm, 5.5f);
        tmp.fontSizeMax = fontSizeMm;
        tmp.enableAutoSizing = fontSizeMm > 5.5f;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.margin = Vector4.zero;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
        return tmp;
    }

    static Bounds MeasureLocalBounds(Transform target, Transform space)
    {
        bool any = false;
        var res = new Bounds();
        foreach (var mf in target.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var lp = space.InverseTransformPoint(mf.transform.TransformPoint(c));
                if (!any) { res = new Bounds(lp, Vector3.zero); any = true; } else res.Encapsulate(lp);
            }
        }
        return res;
    }

    static GameObject BuildPokeKeyPrefab(string prefabName, string housingModel, string capModel,
                                         Material capMat, Color capColor, Color activeRimColor,
                                         string defaultLabel, Color labelColor,
                                         TMP_FontAsset font, TMP_FontAsset boldFont)
    {
        var root = new GameObject(prefabName);

        var hSrc = LoadModel(housingModel);
        var hInst = (GameObject)PrefabUtility.InstantiatePrefab(hSrc, root.transform);
        hInst.name = "Housing3D";
        hInst.transform.localPosition = Vector3.zero;
        hInst.transform.localRotation = Quaternion.identity;
        hInst.transform.localScale = Vector3.one;

        var visual = new GameObject("ButtonVisual");
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = new Vector3(0f, 0f, -0.22f); // V2 standard poke rest travel

        var cSrc = LoadModel(capModel);
        var cInst = (GameObject)PrefabUtility.InstantiatePrefab(cSrc, visual.transform);
        cInst.name = "Cap3D";
        cInst.transform.localPosition = Vector3.zero;
        cInst.transform.localRotation = Quaternion.identity;
        cInst.transform.localScale = Vector3.one;

        // Assign keycap material & measure cap front Z
        Renderer capBodyR = null, rimR = null;
        foreach (var r in cInst.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (r.name.Contains("Cap_Body"))
            {
                r.sharedMaterial = capMat;
                capBodyR = r;
            }
            else if (r.name.Contains("Cap_StateRim"))
            {
                rimR = r;
            }
        }
        foreach (var r in hInst.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = ShadowCastingMode.Off;

        var st = root.AddComponent<PokeKeyStateVisual>();
        st.capRenderer = capBodyR;
        st.stateRimRenderer = rimR;
        st.ConfigurePalette(capColor, activeRimColor);

        // Place TMP label centered inside visual, proud of cap front face
        var capBounds = MeasureLocalBounds(cInst.transform, visual.transform);
        float labelZ = capBounds.min.z - 0.014f;
        var lblGo = new GameObject("Label");
        lblGo.transform.SetParent(visual.transform, false);
        lblGo.transform.localPosition = new Vector3(0f, 0f, labelZ);
        lblGo.transform.localScale = Vector3.one * 0.42f;
        var tmp = lblGo.AddComponent<TextMeshPro>();
        tmp.font = boldFont ?? font;
        tmp.text = defaultLabel;
        tmp.rectTransform.sizeDelta = new Vector2(capBounds.size.x * 2.1f, capBounds.size.y * 1.7f);
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 2.8f;
        tmp.fontSizeMax = 6.2f;
        tmp.fontSize = 5.4f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = labelColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        var lmr = lblGo.GetComponent<MeshRenderer>();
        if (lmr != null) { lmr.shadowCastingMode = ShadowCastingMode.Off; lmr.receiveShadows = false; }

        return SavePrefab(prefabName, root);
    }

    static void ConfigureKeyInstance(GameObject keyGo, string text, Color capColor, Color rimAccent,
                                     Color labelColor, float uniformHeightMeters,
                                     PokeKeyVisualState state = PokeKeyVisualState.Normal)
    {
        // Scale uniformly by desired height (Housing outer height = ~1.18 in key units for 0.96+0.18+0.05)
        var housing = keyGo.transform.Find("Housing3D");
        float unitH = housing != null ? MeasureLocalBounds(housing, keyGo.transform).size.y : 1.18f;
        float s = uniformHeightMeters / Mathf.Max(0.01f, unitH);
        keyGo.transform.localScale = new Vector3(s, s, s * 0.72f);

        var st = keyGo.GetComponent<PokeKeyStateVisual>();
        if (st != null)
        {
            st.ConfigurePalette(capColor, rimAccent);
            st.SetState(state);
        }
        var visual = keyGo.transform.Find("ButtonVisual");
        if (visual != null && state == PokeKeyVisualState.Pressed)
            visual.localPosition = Vector3.zero; // physically depressed onto housing plane!

        var tmp = keyGo.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
        {
            tmp.text = text;
            tmp.color = state == PokeKeyVisualState.Disabled ? new Color32(0xC2, 0xC8, 0xD2, 255) : labelColor;
        }
    }

    static Dictionary<string, GameObject> BuildAllPrefabs(Dictionary<string, Material> mats,
                                                          TMP_FontAsset font, TMP_FontAsset boldFont,
                                                          StringBuilder sb)
    {
        var pf = new Dictionary<string, GameObject>();

        // 1. Proportioned Poke Keys
        pf["UI_PokeKey_Square"] = BuildPokeKeyPrefab(
            "UI_PokeKey_Square", "UI/UI_PokeKey_Square_Housing.fbx", "UI/UI_PokeKey_Square_Cap.fbx",
            mats["Mat_DS_KeyCapCream"], KeyCream, Teal, "Focus", Ink, font, boldFont);
        pf["UI_PokeKey_Wide"] = BuildPokeKeyPrefab(
            "UI_PokeKey_Wide", "UI/UI_PokeKey_Wide_Housing.fbx", "UI/UI_PokeKey_Wide_Cap.fbx",
            mats["Mat_DS_KeyCapTeal"], Teal, Teal, "Start", OnPlate, font, boldFont);
        pf["UI_PokeKey_HeroWide"] = BuildPokeKeyPrefab(
            "UI_PokeKey_HeroWide", "UI/UI_PokeKey_HeroWide_Housing.fbx", "UI/UI_PokeKey_HeroWide_Cap.fbx",
            mats["Mat_DS_KeyCapSuccess"], Success, Success, "Approve", OnPlate, font, boldFont);
        pf["UI_PokeKey_Chip"] = BuildPokeKeyPrefab(
            "UI_PokeKey_Chip", "UI/UI_PokeKey_Chip_Housing.fbx", "UI/UI_PokeKey_Chip_Cap.fbx",
            mats["Mat_DS_KeyCapLavender"], Lavender, Lavender, "Details", Ink, font, boldFont);

        // 2. Expanded Decision Paper + Companion Details Sheet
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel("Documents/Doc_PaperV2_Expanded.fbx"));
            root.name = "Doc_PaperV2_Expanded_Polished";
            var t = root.transform;
            float zBand = -0.0017f, zSheet = -0.0005f;
            AddTmp(t, "Badge", new Vector3(-0.070f, 0.137f, zBand), new Vector2(13.0f, 1.8f), 6.5f, FontStyles.Bold, Ink, TextAlignmentOptions.Left, false, "SCRIPTED DEMO", font, boldFont);
            AddTmp(t, "NeedsLabel", new Vector3(0.0675f, 0.137f, zBand), new Vector2(13.5f, 1.8f), 5.5f, FontStyles.Normal, Ink, TextAlignmentOptions.Right, false, "Needs your decision", font, boldFont);
            AddTmp(t, "Title", new Vector3(0f, 0.094f, zSheet), new Vector2(26.8f, 3.6f), 13.0f, FontStyles.Bold, Ink, TextAlignmentOptions.TopLeft, true, "Build the login flow", font, boldFont);
            AddTmp(t, "Summary", new Vector3(-0.049f, 0.052f, zSheet), new Vector2(17.0f, 3.0f), 7.5f, FontStyles.Normal, Ink, TextAlignmentOptions.TopLeft, true, "Use OAuth 2.1 + PKCE with short-lived HTTP-only session cookies.", font, boldFont);
            AddTmp(t, "Reco", new Vector3(-0.049f, 0.025f, zSheet), new Vector2(17.0f, 1.4f), 7.5f, FontStyles.Bold, new Color(0.10f, 0.42f, 0.24f), TextAlignmentOptions.Left, false, "Recommendation: Approve plan", font, boldFont);
            AddTmp(t, "Provenance", new Vector3(-0.049f, 0.007f, zSheet), new Vector2(17.0f, 1.8f), 5.5f, FontStyles.Normal, InkSoft, TextAlignmentOptions.TopLeft, true, "Template recommendation. Not live AI research.", font, boldFont);
            AddTmp(t, "StampHere", new Vector3(0.091f, 0.043f, -0.0011f), new Vector2(7.6f, 1.5f), 6.5f, FontStyles.Bold, Ink, TextAlignmentOptions.Center, false, "Stamp here", font, boldFont);
            AddTmp(t, "StampHint", new Vector3(0.091f, 0.021f, -0.0011f), new Vector2(7.6f, 2.2f), 5.5f, FontStyles.Normal, InkSoft, TextAlignmentOptions.Center, true, "Use a stamp instead of a key", font, boldFont);

            // Mount 4 proportioned Chip keys & 2 HeroWide primary keys
            float chipPitch = (0.268f - 0.060f) / 3f;
            string[] chipLabels = { "Why", "Details", "Changes", "Voice" };
            for (int i = 0; i < 4; i++)
            {
                var k = (GameObject)PrefabUtility.InstantiatePrefab(pf["UI_PokeKey_Chip"], t);
                k.name = $"Btn_{chipLabels[i]}";
                k.transform.localPosition = new Vector3(-0.134f + 0.030f + chipPitch * i, -0.042f, -0.005f);
                ConfigureKeyInstance(k, chipLabels[i], Lavender, Teal, Ink, 0.028f);
            }
            var btnA = (GameObject)PrefabUtility.InstantiatePrefab(pf["UI_PokeKey_HeroWide"], t);
            btnA.name = "Btn_Approve";
            btnA.transform.localPosition = new Vector3(-0.070f, -0.109f, -0.006f);
            ConfigureKeyInstance(btnA, "Approve", Success, Success, OnPlate, 0.050f);

            var btnR = (GameObject)PrefabUtility.InstantiatePrefab(pf["UI_PokeKey_HeroWide"], t);
            btnR.name = "Btn_Reject";
            btnR.transform.localPosition = new Vector3(0.070f, -0.109f, -0.006f);
            ConfigureKeyInstance(btnR, "Reject", Coral, Coral, OnPlate, 0.050f);

            pf["Doc_PaperV2_Expanded_Polished"] = SavePrefab("Doc_PaperV2_Expanded_Polished", root);
        }

        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel("Documents/Doc_PaperV2_DetailsSheet.fbx"));
            root.name = "Doc_PaperV2_DetailsSheet_Polished";
            AddTmp(root.transform, "Body", new Vector3(0f, 0.015f, -0.0005f), new Vector2(26.8f, 7.2f), 7.5f, FontStyles.Normal, Ink, TextAlignmentOptions.TopLeft, true, "Researcher recommends OAuth 2.1 + PKCE so tokens stay out of browser storage.", font, boldFont);
            pf["Doc_PaperV2_DetailsSheet_Polished"] = SavePrefab("Doc_PaperV2_DetailsSheet_Polished", root);
        }

        // 3. Carried Small Paper
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel("Documents/Doc_PaperV2_Small.fbx"));
            root.name = "Doc_PaperV2_Small_Polished";
            AddTmp(root.transform, "CoverMode", new Vector3(0f, 0.1075f, -0.0038f), new Vector2(28.0f, 6.0f), 26.0f, FontStyles.Bold, Ink, TextAlignmentOptions.Center, false, "SCRIPTED DEMO", font, boldFont);
            AddTmp(root.transform, "CoverTitle", new Vector3(0f, -0.020f, -0.0012f), new Vector2(26.8f, 9.0f), 46.0f, FontStyles.Bold, Ink, TextAlignmentOptions.Center, false, "DECISION", font, boldFont);
            AddTmp(root.transform, "GrabTabLabel", new Vector3(0f, 0.174f, -0.0022f), new Vector2(12.0f, 3.6f), 26.0f, FontStyles.Bold, OnPlate, TextAlignmentOptions.Center, false, "TAKE", font, boldFont);
            pf["Doc_PaperV2_Small_Polished"] = SavePrefab("Doc_PaperV2_Small_Polished", root);
        }

        // 4. Completion Receipt Card
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel("Documents/Doc_ReceiptCard.fbx"));
            root.name = "Doc_ReceiptCard_Polished";
            float z = -0.0008f;
            AddTmp(root.transform, "Mode",    new Vector3(-0.005f, 0.028f, z), new Vector2(11.6f, 1.1f), 6.0f, FontStyles.Bold, InkSoft, TextAlignmentOptions.Left, false, "SCRIPTED DEMO", font, boldFont);
            AddTmp(root.transform, "Heading", new Vector3(-0.025f, 0.015f, z), new Vector2(7.6f, 1.4f), 9.5f, FontStyles.Bold, Ink, TextAlignmentOptions.Left, false, "Delivered", font, boldFont);
            AddTmp(root.transform, "Body",    new Vector3(-0.025f,-0.006f, z), new Vector2(7.6f, 2.8f), 7.0f, FontStyles.Normal, Ink, TextAlignmentOptions.TopLeft, true, "Build the login flow\n4 / 4 tests passed", font, boldFont);
            AddTmp(root.transform, "Detail",  new Vector3(-0.025f,-0.030f, z), new Vector2(7.6f, 1.4f), 5.5f, FontStyles.Normal, InkSoft, TextAlignmentOptions.Left, true, "Artifact saved to out-tray", font, boldFont);

            var runKey = (GameObject)PrefabUtility.InstantiatePrefab(pf["UI_PokeKey_Wide"], root.transform);
            runKey.name = "RunAgainKey";
            runKey.transform.localPosition = new Vector3(0.039f, -0.014f, -0.0052f);
            ConfigureKeyInstance(runKey, "Run again", Teal, Teal, OnPlate, 0.030f);

            pf["Doc_ReceiptCard_Polished"] = SavePrefab("Doc_ReceiptCard_Polished", root);
        }

        // 5. Physical Archive Tray
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel("Documents/Doc_ArchiveTray_Polished.fbx"));
            root.name = "Doc_ArchiveTray_Polished";
            var anchor = root.transform.Find("Anchor_FiledPlate");
            var fKey = (GameObject)PrefabUtility.InstantiatePrefab(pf["UI_PokeKey_Wide"], root.transform);
            fKey.name = "FiledPoke";
            if (anchor != null)
            {
                fKey.transform.localPosition = anchor.localPosition + anchor.localRotation * new Vector3(0f, 0f, -0.003f);
                fKey.transform.localRotation = anchor.localRotation;
            }
            else
            {
                fKey.transform.localPosition = new Vector3(0f, 0.018f, 0.062f);
            }
            ConfigureKeyInstance(fKey, "Filed (3)", KeyCream, Teal, Ink, 0.024f);
            pf["Doc_ArchiveTray_Polished"] = SavePrefab("Doc_ArchiveTray_Polished", root);
        }

        // 6. TaskCard_V2_Polished
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel("TaskBoard/TaskCard_V2_Polished.fbx"));
            root.name = "TaskCard_V2_Polished";
            AddTmp(root.transform, "SH_Role",     new Vector3(-0.0120f, 0.0185f, -0.0024f), new Vector2(3.6f, 0.7f), 5.8f, FontStyles.Bold, OnPlate, TextAlignmentOptions.Left, false, "Researcher", font, boldFont);
            AddTmp(root.transform, "SH_Priority", new Vector3( 0.0165f, 0.0185f, -0.0024f), new Vector2(1.7f, 0.7f), 5.8f, FontStyles.Bold, OnPlate, TextAlignmentOptions.Right, false, "High", font, boldFont);
            AddTmp(root.transform, "Title",       new Vector3( 0.0000f, 0.0055f, -0.0020f), new Vector2(6.6f, 1.9f), 8.2f, FontStyles.Bold, Ink, TextAlignmentOptions.Center, true, "Build the login flow", font, boldFont);
            AddTmp(root.transform, "SH_State",    new Vector3( 0.0000f,-0.0095f, -0.0020f), new Vector2(6.6f, 0.9f), 6.2f, FontStyles.Normal, Ink, TextAlignmentOptions.Center, false, "Needs your approval", font, boldFont);
            AddTmp(root.transform, "SH_Source",   new Vector3( 0.0000f,-0.0185f, -0.0020f), new Vector2(4.8f, 0.8f), 5.5f, FontStyles.Bold, InkSoft, TextAlignmentOptions.Center, false, "SCRIPTED DEMO", font, boldFont);
            pf["TaskCard_V2_Polished"] = SavePrefab("TaskCard_V2_Polished", root);
        }

        // 7. TaskDock_Polished
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel("TaskBoard/TaskDock_Plate.fbx"));
            root.name = "TaskDock_Polished";
            float tz = -0.0020f;
            AddTmp(root.transform, "Summary", new Vector3(0f, 0.036f, tz), new Vector2(14.6f, 1.2f), 6.5f, FontStyles.Bold, OnPlate, TextAlignmentOptions.Left, false, "Task board · 3 ready", font, boldFont);
            AddTmp(root.transform, "Title",   new Vector3(0f, 0.023f, tz), new Vector2(14.6f, 1.6f), 9.5f, FontStyles.Bold, OnPlate, TextAlignmentOptions.Left, false, "Build the login flow", font, boldFont);
            AddTmp(root.transform, "State",   new Vector3(0f, 0.010f, tz), new Vector2(14.6f, 1.2f), 7.0f, FontStyles.Normal, OnPlate, TextAlignmentOptions.Left, false, "Ready to start on Researcher", font, boldFont);
            AddTmp(root.transform, "Mode",    new Vector3(-0.052f, -0.024f, -0.0021f), new Vector2(4.6f, 1.5f), 4.6f, FontStyles.Bold, Ink, TextAlignmentOptions.Center, false, "SCRIPTED DEMO", font, boldFont);

            var sk = (GameObject)PrefabUtility.InstantiatePrefab(pf["UI_PokeKey_Wide"], root.transform);
            sk.name = "DockStartKey";
            sk.transform.localPosition = new Vector3(0.004f, -0.024f, -0.0048f);
            ConfigureKeyInstance(sk, "Start", Teal, Teal, OnPlate, 0.028f);

            var bk = (GameObject)PrefabUtility.InstantiatePrefab(pf["UI_PokeKey_Wide"], root.transform);
            bk.name = "DockBoardKey";
            bk.transform.localPosition = new Vector3(0.052f, -0.024f, -0.0048f);
            ConfigureKeyInstance(bk, "Board", Lavender, Lavender, Ink, 0.028f);

            pf["TaskDock_Polished"] = SavePrefab("TaskDock_Polished", root);
        }

        // 8. TaskBoard_KanbanFrame_V2
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel("TaskBoard/TaskBoard_KanbanFrame_V2.fbx"));
            root.name = "TaskBoard_KanbanFrame_V2";
            AddTmp(root.transform, "BoardTitle", new Vector3(-0.020f, 0.298f, -0.012f), new Vector2(18.0f, 2.0f), 9.0f, FontStyles.Bold, OnPlate, TextAlignmentOptions.Center, false, "TASK BOARD", font, boldFont);
            var nk = (GameObject)PrefabUtility.InstantiatePrefab(pf["UI_PokeKey_Square"], root.transform);
            nk.name = "NewTaskButton";
            nk.transform.localPosition = new Vector3(0.176f, 0.298f, -0.012f);
            ConfigureKeyInstance(nk, "+ New", KeyCream, Teal, Ink, 0.032f);

            string[] hNames = { "Queued", "InProgress", "Blocked", "Completed" };
            string[] hLabels = { "Planned", "Working", "Needs me", "Done" };
            float[] hx = { -0.147f, -0.049f, 0.049f, 0.147f };
            for (int i = 0; i < 4; i++)
            {
                AddTmp(root.transform, $"Label_{hNames[i]}", new Vector3(hx[i], 0.248f, -0.011f),
                       new Vector2(8.0f, 2.0f), 8.0f, FontStyles.Bold, i == 2 ? Ink : OnPlate,
                       TextAlignmentOptions.Center, false, hLabels[i], font, boldFont);
            }
            pf["TaskBoard_KanbanFrame_V2"] = SavePrefab("TaskBoard_KanbanFrame_V2", root);
        }

        // 9. Rounded Plates
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel("Plates/Plate_AgentNameplate.fbx"));
            root.name = "Plate_AgentNameplate_Polished";
            AddTmp(root.transform, "Role",     new Vector3(0.004f,  0.006f, -0.0018f), new Vector2(9.2f, 1.2f), 6.5f, FontStyles.Bold, OnPlate, TextAlignmentOptions.Left, false, "Researcher", font, boldFont);
            AddTmp(root.transform, "Activity", new Vector3(0.004f, -0.006f, -0.0018f), new Vector2(9.2f, 1.1f), 5.5f, FontStyles.Normal, OnPlate, TextAlignmentOptions.Left, false, "Analyzing requirements", font, boldFont);
            pf["Plate_AgentNameplate_Polished"] = SavePrefab("Plate_AgentNameplate_Polished", root);
        }
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel("Plates/Plate_ModePlaque.fbx"));
            root.name = "Plate_ModePlaque_Polished";
            AddTmp(root.transform, "Mode",   new Vector3(0.003f,  0.005f, -0.0018f), new Vector2(10.6f, 1.1f), 6.5f, FontStyles.Bold, OnPlate, TextAlignmentOptions.Left, false, "SCRIPTED DEMO", font, boldFont);
            AddTmp(root.transform, "Status", new Vector3(0.003f, -0.006f, -0.0018f), new Vector2(10.6f, 1.0f), 5.5f, FontStyles.Normal, OnPlate, TextAlignmentOptions.Left, false, "Offline demo · Ready", font, boldFont);
            pf["Plate_ModePlaque_Polished"] = SavePrefab("Plate_ModePlaque_Polished", root);
        }
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel("Plates/Plate_UtilitySign.fbx"));
            root.name = "Plate_UtilitySign_Polished";
            AddTmp(root.transform, "Text", new Vector3(0.003f, 0f, -0.0016f), new Vector2(18.6f, 1.3f), 6.5f, FontStyles.Normal, OnPlate, TextAlignmentOptions.Center, false, "Drop card on a teammate pad to assign", font, boldFont);
            pf["Plate_UtilitySign_Polished"] = SavePrefab("Plate_UtilitySign_Polished", root);
        }

        // 10. Factory & Optimized Tray
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel("Factory/SoftwareFactory_Complete_Polished.fbx"));
            root.name = "SoftwareFactory_Complete_Polished";
            pf["SoftwareFactory_Complete_Polished"] = SavePrefab("SoftwareFactory_Complete_Polished", root);
        }
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel("Optimized/Doc_InboxTray_Optimized.fbx"));
            root.name = "Doc_InboxTray_Optimized";
            pf["Doc_InboxTray_Optimized"] = SavePrefab("Doc_InboxTray_Optimized", root);
        }

        sb.AppendLine($"Prefabs saved in {PrefabDir}: {pf.Count}");
        return pf;
    }

    // --------------------------------------------------------------------------
    // Before vs After Preview Scene + Offscreen Render Captures
    // --------------------------------------------------------------------------

    static GameObject MakeQuad(Transform parent, string name, Vector3 localPos, Vector2 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    static GameObject MakeLegacyStretchedButton(Transform parent, string name, Vector3 pos, Vector2 size, Color col,
                                                string label, Color textColor, Dictionary<string, Material> mats,
                                                TMP_FontAsset font, TMP_FontAsset boldFont)
    {
        // Recreates the exact V2 stretched 1:1 key issue from UI_PokeKey_Housing / Cap
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = pos;
        root.transform.localScale = new Vector3(size.x / 1.20f, size.y / 1.20f, 0.04f);

        var hSrc = LoadModel("UI/UI_PokeKey_Housing.fbx");
        var cSrc = LoadModel("UI/UI_PokeKey_Cap.fbx");
        if (hSrc != null) PrefabUtility.InstantiatePrefab(hSrc, root.transform);
        if (cSrc != null)
        {
            var c = (GameObject)PrefabUtility.InstantiatePrefab(cSrc, root.transform);
            c.transform.localPosition = new Vector3(0f, 0f, -0.22f);
            var mr = c.GetComponentInChildren<MeshRenderer>();
            if (mr != null)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", col);
                mpb.SetColor("_Color", col);
                mr.SetPropertyBlock(mpb);
            }
        }
        var lbl = AddTmp(root.transform, "Label", new Vector3(0f, 0f, -0.37f), new Vector2(3.5f, 1.5f), 2.2f,
                         FontStyles.Bold, textColor, TextAlignmentOptions.Center, false, label, font, boldFont);
        lbl.transform.localScale = new Vector3(0.25f * (size.y / size.x), 0.25f, 0.25f);
        return root;
    }

    static void CaptureShot(Camera cam, string fileName, Vector3 camPos, Vector3 lookAt, int w = 1600, int h = 1000)
    {
        cam.transform.position = camPos;
        cam.transform.LookAt(lookAt, Vector3.up);
        cam.stereoTargetEye = StereoTargetEyeMask.None;

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        RenderTexture.active = rt;
        cam.Render();

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(ShotDir, fileName), tex.EncodeToPNG());

        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(tex);
    }

    static void BuildPreviewSceneAndCapture(Dictionary<string, GameObject> pf, Dictionary<string, Material> mats,
                                            TMP_FontAsset font, TMP_FontAsset boldFont, StringBuilder sb)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientIntensity = 1.05f;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.65f, 0.71f);
        RenderSettings.ambientEquatorColor = new Color(0.46f, 0.48f, 0.52f);
        RenderSettings.ambientGroundColor = new Color(0.26f, 0.28f, 0.31f);

        // Lighting (pointing from camera side -Z toward +Z and slightly down so -Z front faces are natural & crisp)
        var keyLightGo = new GameObject("KeyLight");
        var keyLight = keyLightGo.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.55f;
        keyLight.shadows = LightShadows.None;
        keyLight.color = new Color(1.0f, 0.98f, 0.94f);
        keyLightGo.transform.rotation = Quaternion.Euler(28f, -18f, 0f);

        var fillLightGo = new GameObject("FillLight");
        var fillLight = fillLightGo.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.intensity = 0.65f;
        fillLight.shadows = LightShadows.None;
        fillLight.color = new Color(0.86f, 0.92f, 1.0f);
        fillLightGo.transform.rotation = Quaternion.Euler(18f, 38f, 0f);

        var camGo = new GameObject("Cam_Bay1");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.11f, 0.13f, 0.16f, 1f);
        cam.fieldOfView = 38f;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 20f;
        cam.depth = 0f;
        cam.stereoTargetEye = StereoTargetEyeMask.None;

        Camera MakeShotCam(string name, Vector3 pos, Vector3 lookAt)
        {
            var g = new GameObject(name);
            var c = g.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.11f, 0.13f, 0.16f, 1f);
            c.fieldOfView = 38f;
            c.nearClipPlane = 0.02f;
            c.farClipPlane = 20f;
            c.depth = -5f;
            c.stereoTargetEye = StereoTargetEyeMask.None;
            c.enabled = true;
            g.transform.position = pos;
            g.transform.LookAt(lookAt, Vector3.up);
            return c;
        }

        void AddBayHeader(Transform bay, string title, Vector3 offset)
        {
            AddTmp(bay, "BayTitle", offset, new Vector2(40f, 4f), 14f, FontStyles.Bold, OnPlate,
                   TextAlignmentOptions.Center, false, title, font, boldFont);
        }

        // Desk backdrop plinth helper
        void AddPlinth(Transform bay, Vector3 pos, Vector3 size)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "DeskPlinth";
            cube.transform.SetParent(bay, false);
            cube.transform.localPosition = pos;
            cube.transform.localScale = size;
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.GetComponent<MeshRenderer>().sharedMaterial = mats["Mat_DS_FeltSlate"];
        }

        // ======================================================================
        // BAY 1: Documents & Receipt (X = 0)
        // ======================================================================
        var bay1 = new GameObject("Bay1_Documents_And_Receipt").transform;
        bay1.position = new Vector3(0f, 0f, 0f);
        AddPlinth(bay1, new Vector3(0f, -0.02f, 0.05f), new Vector3(1.10f, 0.02f, 0.45f));
        AddBayHeader(bay1, "BEFORE (Flat Quads)          vs          AFTER (Refined Rounded Assets)", new Vector3(0f, 0.40f, 0f));

        // BEFORE side (left x = -0.26)
        var bPaper = new GameObject("Before_PaperV2").transform;
        bPaper.SetParent(bay1, false);
        bPaper.localPosition = new Vector3(-0.27f, 0.17f, 0f);
        MakeQuad(bPaper, "Sheet", Vector3.zero, new Vector2(0.30f, 0.30f), mats["Mat_DS_Paper"]);
        MakeQuad(bPaper, "Band", new Vector3(0f, 0.137f, -0.0005f), new Vector2(0.30f, 0.026f), mats["Mat_DS_AccentAmber"]);
        MakeQuad(bPaper, "StampBox", new Vector3(0.091f, 0.033f, -0.0005f), new Vector2(0.086f, 0.067f), mats["Mat_DS_PaperTint"]);
        MakeLegacyStretchedButton(bPaper, "StretchedApprove", new Vector3(-0.07f, -0.109f, -0.004f), new Vector2(0.128f, 0.050f), Success, "Approve", OnPlate, mats, font, boldFont);
        MakeLegacyStretchedButton(bPaper, "StretchedReject",  new Vector3( 0.07f, -0.109f, -0.004f), new Vector2(0.128f, 0.050f), Coral,   "Reject",  OnPlate, mats, font, boldFont);
        AddTmp(bPaper, "Title", new Vector3(0f, 0.094f, -0.001f), new Vector2(26f, 3f), 13f, FontStyles.Bold, Ink, TextAlignmentOptions.TopLeft, false, "Before: Flat Quad Sheet", font, boldFont);

        // Before small receipt
        var bRec = new GameObject("Before_Receipt").transform;
        bRec.SetParent(bay1, false);
        bRec.localPosition = new Vector3(-0.47f, 0.055f, -0.05f);
        MakeQuad(bRec, "Sheet", Vector3.zero, new Vector2(0.13f, 0.08f), mats["Mat_DS_Paper"]);
        MakeQuad(bRec, "Strip", new Vector3(0f, 0.0375f, -0.0004f), new Vector2(0.13f, 0.005f), mats["Mat_DS_AccentSuccess"]);
        AddTmp(bRec, "Heading", new Vector3(-0.02f, 0.015f, -0.001f), new Vector2(8f, 2f), 8.5f, FontStyles.Bold, Ink, TextAlignmentOptions.Left, false, "Before Receipt", font, boldFont);

        // AFTER side (right x = +0.20)
        var aPaper = (GameObject)PrefabUtility.InstantiatePrefab(pf["Doc_PaperV2_Expanded_Polished"], bay1);
        aPaper.transform.localPosition = new Vector3(0.18f, 0.17f, 0f);
        var aSmall = (GameObject)PrefabUtility.InstantiatePrefab(pf["Doc_PaperV2_Small_Polished"], bay1);
        aSmall.transform.localPosition = new Vector3(-0.04f, 0.05f, -0.06f);
        aSmall.transform.localScale = Vector3.one * 0.25f;
        var aRec = (GameObject)PrefabUtility.InstantiatePrefab(pf["Doc_ReceiptCard_Polished"], bay1);
        aRec.transform.localPosition = new Vector3(0.43f, 0.06f, -0.05f);

        // ======================================================================
        // BAY 2: Proportioned Buttons & Interactive States (X = 2.0)
        // ======================================================================
        var bay2 = new GameObject("Bay2_Buttons_And_States").transform;
        bay2.position = new Vector3(2.0f, 0f, 0f);
        AddPlinth(bay2, new Vector3(0f, -0.02f, 0.05f), new Vector3(0.95f, 0.02f, 0.40f));
        AddBayHeader(bay2, "TOP: Old Stretched vs New Proportioned   |   BOTTOM: Normal / Hover / Pressed / Active / Disabled", new Vector3(0f, 0.26f, 0f));

        MakeLegacyStretchedButton(bay2, "Before_Stretched", new Vector3(-0.22f, 0.13f, 0f), new Vector2(0.128f, 0.050f), Success, "Before (Stretched)", OnPlate, mats, font, boldFont);
        var pHero = (GameObject)PrefabUtility.InstantiatePrefab(pf["UI_PokeKey_HeroWide"], bay2);
        pHero.transform.localPosition = new Vector3(0.02f, 0.13f, 0f);
        ConfigureKeyInstance(pHero, "HeroWide", Success, Success, OnPlate, 0.050f);

        var pChip = (GameObject)PrefabUtility.InstantiatePrefab(pf["UI_PokeKey_Chip"], bay2);
        pChip.transform.localPosition = new Vector3(0.18f, 0.13f, 0f);
        ConfigureKeyInstance(pChip, "Chip", Lavender, Lavender, Ink, 0.028f);

        var pSq = (GameObject)PrefabUtility.InstantiatePrefab(pf["UI_PokeKey_Square"], bay2);
        pSq.transform.localPosition = new Vector3(0.29f, 0.13f, 0f);
        ConfigureKeyInstance(pSq, "Square", KeyCream, Teal, Ink, 0.044f);

        // Row of 5 key states on Wide key
        var states = new[] {
            (PokeKeyVisualState.Normal,   "Normal"),
            (PokeKeyVisualState.Hover,    "Hover"),
            (PokeKeyVisualState.Pressed,  "Pressed"),
            (PokeKeyVisualState.Active,   "Active"),
            (PokeKeyVisualState.Disabled, "Disabled"),
        };
        for (int i = 0; i < states.Length; i++)
        {
            float kx = -0.24f + i * 0.12f;
            var kState = (GameObject)PrefabUtility.InstantiatePrefab(pf["UI_PokeKey_Wide"], bay2);
            kState.name = $"State_{states[i].Item2}";
            kState.transform.localPosition = new Vector3(kx, 0.045f, 0f);
            ConfigureKeyInstance(kState, states[i].Item2, Teal, Amber, OnPlate, 0.036f, states[i].Item1);
        }

        // ======================================================================
        // BAY 3: TaskDock, Cards & Selected Rim (X = 4.0)
        // ======================================================================
        var bay3 = new GameObject("Bay3_TaskDock_And_Cards").transform;
        bay3.position = new Vector3(4.0f, 0f, 0f);
        AddPlinth(bay3, new Vector3(0f, -0.02f, 0.05f), new Vector3(0.95f, 0.02f, 0.40f));
        AddBayHeader(bay3, "BEFORE (Sharp TaskDock Quad)      vs      AFTER (Rounded Dock + Cards + Selection Rim)", new Vector3(0f, 0.25f, 0f));

        // Before dock
        var bDock = new GameObject("Before_TaskDock").transform;
        bDock.SetParent(bay3, false);
        bDock.localPosition = new Vector3(-0.22f, 0.07f, 0f);
        MakeQuad(bDock, "Frame", new Vector3(0f, 0f, 0.001f), new Vector2(0.168f, 0.098f), mats["Mat_DS_Housing"]);
        MakeQuad(bDock, "Plate", Vector3.zero, new Vector2(0.160f, 0.090f), mats["Mat_DS_Plate"]);
        MakeQuad(bDock, "Tag", new Vector3(-0.050f, -0.022f, -0.001f), new Vector2(0.048f, 0.016f), mats["Mat_DS_AccentAmber"]);
        MakeLegacyStretchedButton(bDock, "OldStart", new Vector3(0.000f, -0.022f, -0.003f), new Vector2(0.046f, 0.036f), Teal, "Start", OnPlate, mats, font, boldFont);
        AddTmp(bDock, "Title", new Vector3(0f, 0.025f, -0.001f), new Vector2(14.6f, 1.5f), 9f, FontStyles.Bold, OnPlate, TextAlignmentOptions.Left, false, "Before: Sharp Quad", font, boldFont);

        // After dock + Unselected card + Selected card
        var aDock = (GameObject)PrefabUtility.InstantiatePrefab(pf["TaskDock_Polished"], bay3);
        aDock.transform.localPosition = new Vector3(0.02f, 0.07f, 0f);

        var cardUn = (GameObject)PrefabUtility.InstantiatePrefab(pf["TaskCard_V2_Polished"], bay3);
        cardUn.name = "Card_Unselected";
        cardUn.transform.localPosition = new Vector3(0.20f, 0.07f, 0f);
        var selRimOff = cardUn.transform.Find("Card_SelectionRim");
        if (selRimOff != null) selRimOff.gameObject.SetActive(false);

        var cardSel = (GameObject)PrefabUtility.InstantiatePrefab(pf["TaskCard_V2_Polished"], bay3);
        cardSel.name = "Card_Selected";
        cardSel.transform.localPosition = new Vector3(0.31f, 0.07f, 0f);

        // ======================================================================
        // BAY 4: Rounded Plates & Physical Archive Tray (X = 6.0)
        // ======================================================================
        var bay4 = new GameObject("Bay4_Plates_And_ArchiveTray").transform;
        bay4.position = new Vector3(6.0f, 0f, 0f);
        AddPlinth(bay4, new Vector3(0f, -0.02f, 0.05f), new Vector3(0.95f, 0.02f, 0.40f));
        AddBayHeader(bay4, "BEFORE (Flat Nameplate/Plaque Quads)     vs     AFTER (Rounded Plates + File Tray)", new Vector3(0f, 0.24f, 0f));

        // Before Nameplate & ModePlaque
        var bNp = new GameObject("Before_Nameplate").transform;
        bNp.SetParent(bay4, false);
        bNp.localPosition = new Vector3(-0.22f, 0.09f, 0f);
        MakeQuad(bNp, "Plate", Vector3.zero, new Vector2(0.09f, 0.026f), mats["Mat_DS_Plate"]);
        MakeQuad(bNp, "Strip", new Vector3(-0.0425f, 0f, -0.0005f), new Vector2(0.005f, 0.026f), mats["Mat_DS_AccentBlue"]);
        AddTmp(bNp, "Role", new Vector3(0.004f, 0.004f, -0.001f), new Vector2(8f, 1.2f), 6.5f, FontStyles.Bold, OnPlate, TextAlignmentOptions.Left, false, "Before Flat", font, boldFont);

        // After Nameplates (Research Blue, Dev Green, QA Amber), ModePlaque, UtilitySign, Archive Tray
        var roles = new[] { ("Researcher", "Mat_DS_AccentBlue", 0.00f), ("Developer", "Mat_DS_AccentSuccess", 0.05f), ("Tester", "Mat_DS_AccentAmber", 0.10f) };
        foreach (var (rName, rMat, ry) in roles)
        {
            var npInst = (GameObject)PrefabUtility.InstantiatePrefab(pf["Plate_AgentNameplate_Polished"], bay4);
            npInst.transform.localPosition = new Vector3(-0.02f, 0.03f + ry, 0f);
            var strip = npInst.transform.Find("RoleStrip");
            if (strip != null) strip.GetComponent<Renderer>().sharedMaterial = mats[rMat];
            var rTmp = npInst.transform.Find("Role")?.GetComponent<TMP_Text>();
            if (rTmp != null) rTmp.text = rName;
        }
        var mpInst = (GameObject)PrefabUtility.InstantiatePrefab(pf["Plate_ModePlaque_Polished"], bay4);
        mpInst.transform.localPosition = new Vector3(0.14f, 0.10f, 0f);
        var utInst = (GameObject)PrefabUtility.InstantiatePrefab(pf["Plate_UtilitySign_Polished"], bay4);
        utInst.transform.localPosition = new Vector3(0.14f, 0.05f, 0f);
        var trayInst = (GameObject)PrefabUtility.InstantiatePrefab(pf["Doc_ArchiveTray_Polished"], bay4);
        trayInst.transform.localPosition = new Vector3(0.32f, 0.01f, 0.04f);
        trayInst.transform.localRotation = Quaternion.Euler(0f, 18f, 0f);

        // ======================================================================
        // BAY 5: Software Factory Before vs After (X = 8.0)
        // ======================================================================
        var bay5 = new GameObject("Bay5_Factory_Before_After").transform;
        bay5.position = new Vector3(8.0f, 0f, 0f);
        AddPlinth(bay5, new Vector3(0f, -0.02f, 0f), new Vector3(1.10f, 0.02f, 0.45f));
        AddBayHeader(bay5, "TOP: Original SoftwareFactory     |     BOTTOM: Polished Factory (Rails, Collars, Gussets)", new Vector3(0f, 0.46f, 0f));

        var facOldSrc = LoadModel("Factory/SoftwareFactory_Complete.fbx");
        if (facOldSrc != null)
        {
            var fOld = (GameObject)PrefabUtility.InstantiatePrefab(facOldSrc, bay5);
            fOld.transform.localPosition = new Vector3(0f, 0.21f, 0f);
        }
        var fNew = (GameObject)PrefabUtility.InstantiatePrefab(pf["SoftwareFactory_Complete_Polished"], bay5);
        fNew.transform.localPosition = new Vector3(0f, 0.00f, 0f);

        // ======================================================================
        // BAY 6: Hero Desk Ensemble + Kanban Board (X = 10.0)
        // ======================================================================
        var bay6 = new GameObject("Bay6_Hero_Ensemble").transform;
        bay6.position = new Vector3(10.0f, 0f, 0f);
        AddPlinth(bay6, new Vector3(0f, -0.015f, 0.08f), new Vector3(1.10f, 0.02f, 0.50f));
        var bFrame = (GameObject)PrefabUtility.InstantiatePrefab(pf["TaskBoard_KanbanFrame_V2"], bay6);
        bFrame.transform.localPosition = new Vector3(-0.18f, 0.00f, 0.16f);
        var hPaper = (GameObject)PrefabUtility.InstantiatePrefab(pf["Doc_PaperV2_Expanded_Polished"], bay6);
        hPaper.transform.localPosition = new Vector3(0.24f, 0.16f, 0.08f);
        var hDock = (GameObject)PrefabUtility.InstantiatePrefab(pf["TaskDock_Polished"], bay6);
        hDock.transform.localPosition = new Vector3(-0.22f, 0.055f, -0.05f);
        hDock.transform.localRotation = Quaternion.Euler(26f, 0f, 0f);
        var hRec = (GameObject)PrefabUtility.InstantiatePrefab(pf["Doc_ReceiptCard_Polished"], bay6);
        hRec.transform.localPosition = new Vector3(0.02f, 0.06f, -0.04f);
        hRec.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
        var hTray = (GameObject)PrefabUtility.InstantiatePrefab(pf["Doc_ArchiveTray_Polished"], bay6);
        hTray.transform.localPosition = new Vector3(0.40f, 0.00f, -0.04f);
        hTray.transform.localRotation = Quaternion.Euler(0f, 22f, 0f);

        // Save isolated preview scene with 6 dedicated inspection cameras (NEVER touches MainMRScene.unity)
        camGo.transform.position = new Vector3(-0.01f, 0.20f, -0.96f);
        camGo.transform.LookAt(new Vector3(-0.01f, 0.14f, 0f), Vector3.up);
        MakeShotCam("Cam_Bay2", new Vector3(2.03f, 0.12f, -0.72f), new Vector3(2.03f, 0.09f, 0f));
        MakeShotCam("Cam_Bay3", new Vector3(4.04f, 0.12f, -0.74f), new Vector3(4.04f, 0.08f, 0f));
        MakeShotCam("Cam_Bay4", new Vector3(6.07f, 0.12f, -0.76f), new Vector3(6.07f, 0.08f, 0f));
        MakeShotCam("Cam_Bay5", new Vector3(8.0f, 0.28f, -0.82f), new Vector3(8.0f, 0.17f, 0f));
        MakeShotCam("Cam_Bay6", new Vector3(10.02f, 0.34f, -0.86f), new Vector3(10.02f, 0.13f, 0.06f));

        Directory.CreateDirectory(Path.GetDirectoryName(PreviewScene));
        EditorSceneManager.SaveScene(scene, PreviewScene);

        sb.AppendLine($"Preview scene saved to {PreviewScene} with Cam_Bay1..Cam_Bay6.");
    }
}
