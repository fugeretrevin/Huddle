using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;
using MuseDesk.Agents;
using MuseDesk.Art;
using MuseDesk.Art.VisualPolish;
using MuseDesk.Documents;
using MuseDesk.Models;
using MuseDesk.Networking;
using MuseDesk.Phase2.Board;
using MuseDesk.Phase2.Contracts;
using MuseDesk.Workspace;

public static class BuildVisualPolishSuite
{
    const string MainScenePath = "Assets/MuseDesk/Scenes/MainMRScene.unity";
    const string PreviewScenePath = "Assets/MuseDesk/Scenes/VisualPolishPreviewScene.unity";
    const string PrefabDir = "Assets/MuseDesk/Art/Prefabs/VisualPolish";
    const string MatsRoot = "Assets/MuseDesk/Art/Materials";
    const string ScreenshotDir = "Screenshots/VisualPolish";

    public static string RunAll()
    {
        var sb = new StringBuilder();
        Directory.CreateDirectory(ScreenshotDir);
        EnsureFolder(PrefabDir);

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var matMap = LoadAllMaterials();

        // 1. Verify mesh bounds on all newly generated FBX models
        sb.AppendLine("=== 1. MESH BOUNDS VERIFICATION ===");
        VerifyNewModelBounds(sb);

        // 2. Build Reusable Visual Polish Prefabs (with URP materials applied)
        sb.AppendLine("\n=== 2. PREFAB GENERATION ===");
        BuildReusablePrefabs(matMap, sb);

        // 3. Measure Baseline Renderer Count in MainMRScene (Read-Only) then copy to VisualPolishPreviewScene
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        int beforeTotalRenderers = CountSceneRenderers(includeInactive: true);
        int beforeActiveRenderers = CountSceneRenderers(includeInactive: false);
        sb.AppendLine($"\n=== 3. SCENE UPGRADE ({PreviewScenePath}) ===");
        sb.AppendLine($"Baseline MainMRScene renderers: {beforeTotalRenderers} total ({beforeActiveRenderers} active)");

        // Copy MainMRScene.unity on disk to PreviewScenePath so MainMRScene.unity is NEVER saved or modified!
        File.Copy(MainScenePath, PreviewScenePath, true);
        AssetDatabase.ImportAsset(PreviewScenePath, ImportAssetOptions.ForceSynchronousImport);
        var previewScene = EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Single);

        ApplyDecisionPaperPolish(matMap, sb);
        ApplyTaskBoardAndCardPolish(matMap, sb);
        ApplySingleMeshPokeButtons(matMap, sb);
        ApplyStampsAndClutterReduction(matMap, sb);
        ApplyStationAffordancesAndOptimizedWorkstations(matMap, sb);
        ApplyMascotFeedbackBadges(matMap, sb);
        BuildAffordanceAndFeedbackShowcaseStrip(matMap, sb);

        // Ensure all URP materials are bound across the scene
        foreach (var root in previewScene.GetRootGameObjects())
            ApplyMaterialsRecursively(root, matMap);

        int afterTotalRenderers = CountSceneRenderers(includeInactive: true);
        int afterActiveRenderers = CountSceneRenderers(includeInactive: false);
        int totalSaved = beforeTotalRenderers - afterTotalRenderers;
        int activeSaved = beforeActiveRenderers - afterActiveRenderers;
        sb.AppendLine($"Polished VisualPolishPreviewScene renderers: {afterTotalRenderers} total (saved {totalSaved}, {(1f - (float)afterTotalRenderers / Mathf.Max(1, beforeTotalRenderers)) * 100f:F1}% reduction), {afterActiveRenderers} active (saved {activeSaved})");

        EditorSceneManager.MarkSceneDirty(previewScene);
        EditorSceneManager.SaveScene(previewScene);
        AssetDatabase.SaveAssets();

        // 4. Capture 1-to-1 Before/After Game View Screenshots
        sb.AppendLine("\n=== 4. GAME VIEW SCREENSHOT CAPTURES ===");
        CaptureAllPreviews(sb);

        // Re-open MainMRScene clean so the Editor is left on the main scene without dirty changes
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

        File.WriteAllText(Path.Combine(ScreenshotDir, "unity_polish_build_log.txt"), sb.ToString());
        return sb.ToString();
    }

    static int CountSceneRenderers(bool includeInactive)
    {
        return EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Renderer>(includeInactive))
            .Count(r => !(r is ParticleSystemRenderer) && r.enabled);
    }

    static Dictionary<string, Material> LoadAllMaterials()
    {
        var map = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        var guids = AssetDatabase.FindAssets("t:Material", new[] { MatsRoot });
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m != null) map[m.name] = m;
        }
        return map;
    }

    static void ApplyMaterialsRecursively(GameObject root, Dictionary<string, Material> matMap)
    {
        if (root == null) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.GetComponent<TMP_Text>() != null) continue;
            var shared = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == null) continue;
                string raw = shared[i].name;
                int dot = raw.IndexOf('.');
                if (dot > 0) raw = raw.Substring(0, dot);
                raw = raw.Replace(" (Instance)", "");
                if (matMap.TryGetValue(raw, out var urpMat) && shared[i] != urpMat)
                {
                    shared[i] = urpMat;
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = shared;
        }
    }

    static void VerifyNewModelBounds(StringBuilder sb)
    {
        string[] checkPaths =
        {
            "Assets/MuseDesk/Art/Models/Documents/Doc_DecisionPaper_Small.fbx",
            "Assets/MuseDesk/Art/Models/Documents/Doc_DecisionPaper_Expanded.fbx",
            "Assets/MuseDesk/Art/Models/Documents/Doc_Stamp_Approve.fbx",
            "Assets/MuseDesk/Art/Models/Documents/Doc_Stamp_Reject.fbx",
            "Assets/MuseDesk/Art/Models/Documents/Doc_StampRestPad.fbx",
            "Assets/MuseDesk/Art/Models/Affordances/Affordance_AgentHaloRing.fbx",
            "Assets/MuseDesk/Art/Models/Affordances/Affordance_DeskReceivePad.fbx",
            "Assets/MuseDesk/Art/Models/Affordances/Affordance_CardSocket.fbx",
            "Assets/MuseDesk/Art/Models/Affordances/Affordance_StatePuck.fbx",
            "Assets/MuseDesk/Art/Models/UI/UI_PokeKey_Housing.fbx",
            "Assets/MuseDesk/Art/Models/UI/UI_PokeKey_Cap.fbx",
            "Assets/MuseDesk/Art/Models/Feedback/Feedback_Listening.fbx",
            "Assets/MuseDesk/Art/Models/Feedback/Feedback_Speaking.fbx",
            "Assets/MuseDesk/Art/Models/Feedback/Feedback_Thinking.fbx",
            "Assets/MuseDesk/Art/Models/Feedback/Feedback_NeedsAttention.fbx",
            "Assets/MuseDesk/Art/Models/Feedback/Feedback_Error.fbx",
            "Assets/MuseDesk/Art/Models/Feedback/Feedback_Success.fbx",
            "Assets/MuseDesk/Art/Models/Optimized/Workstation_Research_Optimized.fbx",
            "Assets/MuseDesk/Art/Models/Optimized/Workstation_Developer_Optimized.fbx",
            "Assets/MuseDesk/Art/Models/Optimized/Workstation_Testing_Optimized.fbx",
            "Assets/MuseDesk/Art/Models/Optimized/TaskBoard_KanbanFrame_Optimized.fbx",
        };

        foreach (var path in checkPaths)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToList();
            if (meshes.Count == 0)
            {
                sb.AppendLine($"  WARN: No meshes at {path}");
                continue;
            }
            var maxBounds = meshes.Aggregate(new Bounds(), (acc, m) => { acc.Encapsulate(m.bounds); return acc; });
            sb.AppendLine($"  OK {Path.GetFileName(path)}: {meshes.Count} mesh(es), combinedBounds=({maxBounds.size.x:F3}, {maxBounds.size.y:F3}, {maxBounds.size.z:F3})m");
        }
    }

    static void BuildReusablePrefabs(Dictionary<string, Material> matMap, StringBuilder sb)
    {
        // 1. Small Decision Paper Prefab
        var smallFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Documents/Doc_DecisionPaper_Small.fbx");
        if (smallFbx != null)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(smallFbx);
            inst.name = "DecisionPaper_Small_Polished";
            ApplyMaterialsRecursively(inst, matMap);
            var b = CreateDarkTMP(inst.transform, "Badge",   new Vector3(0f,  0.046f, 0.0042f), 2.0f, new Vector2(7.6f, 1.2f), TextAlignmentOptions.Center, new Color(0.96f, 0.94f, 0.88f), FontStyles.Bold);
            b.text = "DECISION";
            b.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var t = CreateDarkTMP(inst.transform, "Title",   new Vector3(0f,  0.020f, 0.0042f), 2.5f, new Vector2(7.4f, 1.8f), TextAlignmentOptions.Center, new Color(0.10f, 0.11f, 0.14f), FontStyles.Bold);
            t.text = "Use OAuth for sign-in?";
            t.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var s = CreateDarkTMP(inst.transform, "Summary", new Vector3(0f, -0.004f, 0.0042f), 1.8f, new Vector2(7.2f, 2.4f), TextAlignmentOptions.Center, new Color(0.16f, 0.18f, 0.22f), FontStyles.Normal);
            s.text = "Baseline tests pass.";
            s.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var r = CreateDarkTMP(inst.transform, "Reco",    new Vector3(0.012f, -0.030f, 0.0042f), 1.8f, new Vector2(4.8f, 1.0f), TextAlignmentOptions.Center, new Color(0.12f, 0.44f, 0.26f), FontStyles.Bold);
            r.text = "APPROVE";
            r.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            SavePrefab(inst, $"{PrefabDir}/DecisionPaper_Small_Polished.prefab", sb);
        }

        // 2. Affordance Prefabs
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Affordances/Affordance_AgentHaloRing.fbx", $"{PrefabDir}/Affordance_AgentHaloRing.prefab", matMap, sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Affordances/Affordance_DeskReceivePad.fbx", $"{PrefabDir}/Affordance_DeskReceivePad.prefab", matMap, sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Affordances/Affordance_CardSocket.fbx", $"{PrefabDir}/Affordance_CardSocket.prefab", matMap, sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Affordances/Affordance_StatePuck.fbx", $"{PrefabDir}/Affordance_StatePuck.prefab", matMap, sb);

        // Combined Station Assignment Affordance Kit Prefab
        var kitGo = new GameObject("Affordance_StationAssignment_Kit");
        var haloFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Affordances/Affordance_AgentHaloRing.fbx");
        var padFbx  = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Affordances/Affordance_DeskReceivePad.fbx");
        var puckFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Affordances/Affordance_StatePuck.fbx");
        var view = kitGo.AddComponent<AssignmentAffordanceView>();
        if (haloFbx != null)
        {
            var h = (GameObject)PrefabUtility.InstantiatePrefab(haloFbx, kitGo.transform);
            h.name = "AgentHaloRing";
            h.transform.localPosition = new Vector3(0f, 0.002f, 0f);
            view.haloRoot = h;
            view.haloOuterRenderer = h.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("StatusGreen"));
        }
        if (padFbx != null)
        {
            var p = (GameObject)PrefabUtility.InstantiatePrefab(padFbx, kitGo.transform);
            p.name = "DeskReceivePad";
            p.transform.localPosition = new Vector3(0.095f, 0.092f, -0.040f);
            view.receivePadStatusRenderer = p.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("StatusBar"));
        }
        if (puckFbx != null)
        {
            var pk = (GameObject)PrefabUtility.InstantiatePrefab(puckFbx, kitGo.transform);
            pk.name = "StatePuck";
            pk.transform.localPosition = new Vector3(-0.095f, 0.092f, -0.040f);
            view.statePuckCoreRenderer = pk.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("GlowCore"));
        }
        ApplyMaterialsRecursively(kitGo, matMap);
        SavePrefab(kitGo, $"{PrefabDir}/Affordance_StationAssignment_Kit.prefab", sb);

        // 3. Stamps & Rest Pad Prefabs
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Documents/Doc_Stamp_Approve.fbx", $"{PrefabDir}/Doc_Stamp_Approve_Polished.prefab", matMap, sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Documents/Doc_Stamp_Reject.fbx",  $"{PrefabDir}/Doc_Stamp_Reject_Polished.prefab", matMap, sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Documents/Doc_StampRestPad.fbx",  $"{PrefabDir}/Doc_StampRestPad.prefab", matMap, sb);

        // 4. Mascot Feedback Badge Group Prefab
        var fbGroupGo = BuildMascotFeedbackGroupObject("Mascot_FeedbackBadgeGroup", matMap);
        SavePrefab(fbGroupGo, $"{PrefabDir}/Mascot_FeedbackBadgeGroup.prefab", sb);

        // 5. Optimized Workstation & Board Prefabs
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Optimized/Workstation_Research_Optimized.fbx",  $"{PrefabDir}/Workstation_Research_Optimized.prefab", matMap, sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Optimized/Workstation_Developer_Optimized.fbx", $"{PrefabDir}/Workstation_Developer_Optimized.prefab", matMap, sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Optimized/Workstation_Testing_Optimized.fbx",   $"{PrefabDir}/Workstation_Testing_Optimized.prefab", matMap, sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Optimized/TaskBoard_KanbanFrame_Optimized.fbx", $"{PrefabDir}/TaskBoard_KanbanFrame_Optimized.prefab", matMap, sb);
    }

    static GameObject BuildMascotFeedbackGroupObject(string name, Dictionary<string, Material> matMap)
    {
        var root = new GameObject(name);
        var group = root.AddComponent<MascotFeedbackBadgeGroup>();

        GameObject SpawnBadge(string fbxName)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/MuseDesk/Art/Models/Feedback/{fbxName}.fbx");
            if (fbx == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
            go.name = fbxName;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * 1.15f;
            ApplyMaterialsRecursively(go, matMap);
            go.SetActive(false);
            return go;
        }

        group.listeningBadge      = SpawnBadge("Feedback_Listening");
        group.speakingBadge       = SpawnBadge("Feedback_Speaking");
        group.thinkingBadge       = SpawnBadge("Feedback_Thinking");
        group.needsAttentionBadge = SpawnBadge("Feedback_NeedsAttention");
        group.errorBadge          = SpawnBadge("Feedback_Error");
        group.successBadge        = SpawnBadge("Feedback_Success");
        return root;
    }

    // =========================================================================
    // PRIORITY 1: DECISION PAPER REDESIGN IN SCENE + PREFAB
    // =========================================================================

    static void ApplyDecisionPaperPolish(Dictionary<string, Material> matMap, StringBuilder sb)
    {
        var roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
        var doc = roots.FirstOrDefault(r => r.name == "DecisionDocument");
        if (doc == null)
        {
            sb.AppendLine("WARN: DecisionDocument not found in scene");
            return;
        }

        // Remove old Card / ExpandedFrame visuals
        foreach (var childName in new[] { "Card", "Doc_ExpandedFrame", "Doc_DecisionPaper_Expanded", "SmallHeldPreview" })
        {
            var old = doc.transform.Find(childName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        }

        // Instantiate our new Expanded Decision Folio model
        var folioFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Documents/Doc_DecisionPaper_Expanded.fbx");
        if (folioFbx != null)
        {
            var folio = (GameObject)PrefabUtility.InstantiatePrefab(folioFbx, doc.transform);
            folio.name = "Doc_DecisionPaper_Expanded";
            folio.transform.SetAsFirstSibling();
            // DecisionDocument's readable face is -Z; rotate 180 deg around Y so the folio's front faces -Z!
            folio.transform.localPosition = new Vector3(0f, 0f, 0.005f);
            folio.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            folio.transform.localScale = Vector3.one;
            ApplyMaterialsRecursively(folio, matMap);

            // Wire DecisionPanel.modeBand to the folio's TopHeaderBar renderer if desired, or keep TopHeaderBar dark charcoal
            var panel = doc.GetComponentInChildren<DecisionPanel>(true);
            if (panel != null)
            {
                var headerRend = folio.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("TopHeaderBar"));
                if (headerRend != null) panel.modeBand = headerRend;
            }
        }

        // Re-layout all TMP text fields for crisp contrast, clear hierarchy, and zero overlap!
        // Note: On DecisionDocument, front face is -Z (`z = -0.0045f`).
        //   - Above Top Brass Clip (y = +0.168): `Status`
        //   - Region 1 (Top Header Band, y = +0.118): `Badge` ("SCRIPTED DEMO    needs your decision")
        //   - Region 2 (Upper Cream Summary Card, y = +0.020 to +0.092):
        //       `Title` at y = +0.078 ("Use OAuth for sign-in?")
        //       `Summary` at y = +0.048 ("OAuth with an existing provider is less code to own...")
        //       `Reco` at y = +0.024 ("Recommended: Approve")
        //       `Provenance` at y = +0.008 ("Template recommendation. Not live AI research.")
        //   - Region 3 (Lower-Left Details Card, x = -0.044, y = -0.032, width = 17.6 cm):
        //       `Details` root containing `Body` (y = -0.024) and `Answer` (y = -0.058)
        //   - Region 4 (Lower-Right Dedicated Stamp Target Pad, x = +0.096, y = -0.032):
        //       `StampZone` and `StampMark` centered at `(0.096, -0.032, -0.006)`!
        //   - Region 5 (Bottom Single-Row Action Deck, y = -0.114, inside the frame bottom at -0.150):
        //       All 4 buttons in ONE row (`Btn_Approve`, `Btn_Ask_why`, `Btn_Details`, `Btn_Reject`)!

        StyleDocText(doc.transform, "Status",     new Vector3(0.000f,  0.168f, -0.005f), new Vector2(28f, 2.2f), 2.0f, 2.8f, new Color(0.98f, 0.92f, 0.65f), FontStyles.Bold,   TextAlignmentOptions.Center, false);
        StyleDocText(doc.transform, "Badge",      new Vector3(0.000f,  0.118f, -0.005f), new Vector2(26f, 2.0f), 1.9f, 2.5f, new Color(0.99f, 0.97f, 0.90f), FontStyles.Bold,   TextAlignmentOptions.Center, false);
        StyleDocText(doc.transform, "Title",      new Vector3(0.000f,  0.078f, -0.005f), new Vector2(26f, 2.4f), 2.4f, 3.4f, new Color(0.10f, 0.11f, 0.15f), FontStyles.Bold,   TextAlignmentOptions.Center, true);
        StyleDocText(doc.transform, "Summary",    new Vector3(0.000f,  0.048f, -0.005f), new Vector2(26f, 2.5f), 1.8f, 2.4f, new Color(0.16f, 0.18f, 0.22f), FontStyles.Normal, TextAlignmentOptions.Center, true);
        StyleDocText(doc.transform, "Reco",       new Vector3(0.000f,  0.024f, -0.005f), new Vector2(16f, 1.4f), 1.8f, 2.3f, new Color(0.12f, 0.44f, 0.26f), FontStyles.Bold,   TextAlignmentOptions.Center, false);
        StyleDocText(doc.transform, "Provenance", new Vector3(0.000f,  0.008f, -0.005f), new Vector2(26f, 1.2f), 1.3f, 1.7f, new Color(0.32f, 0.34f, 0.38f), FontStyles.Italic, TextAlignmentOptions.Center, false);

        // Details region on the left side (x = -0.044) so the right side (x = +0.096) is 100% dedicated to the Stamp Target!
        var details = doc.transform.Find("Details");
        if (details != null)
        {
            details.localPosition = Vector3.zero;
            StyleDocText(details, "Body",   new Vector3(-0.044f, -0.025f, -0.005f), new Vector2(17.4f, 4.8f), 1.45f, 1.95f, new Color(0.14f, 0.15f, 0.19f), FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
            StyleDocText(details, "Answer", new Vector3(-0.044f, -0.058f, -0.005f), new Vector2(17.4f, 1.8f), 1.40f, 1.85f, new Color(0.18f, 0.32f, 0.54f), FontStyles.Italic, TextAlignmentOptions.Center,  true);
        }
        else
        {
            StyleDocText(doc.transform, "Body",   new Vector3(-0.044f, -0.025f, -0.005f), new Vector2(17.4f, 4.8f), 1.45f, 1.95f, new Color(0.14f, 0.15f, 0.19f), FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
            StyleDocText(doc.transform, "Answer", new Vector3(-0.044f, -0.058f, -0.005f), new Vector2(17.4f, 1.8f), 1.40f, 1.85f, new Color(0.18f, 0.32f, 0.54f), FontStyles.Italic, TextAlignmentOptions.Center,  true);
        }

        // Move StampZone to the dedicated Stamp Target pad on the right (x = +0.096, y = -0.032)
        // and remove the giant primitive cylinder ZoneMarker that overlapped Ask Why!
        var stampZone = doc.transform.Find("StampZone");
        if (stampZone != null)
        {
            stampZone.localPosition = new Vector3(0.096f, -0.032f, -0.005f);
            var oldMarker = stampZone.Find("ZoneMarker");
            if (oldMarker != null) UnityEngine.Object.DestroyImmediate(oldMarker.gameObject);
        }

        // Move StampMark & ApprovalStamp prop to the dedicated Stamp Target zone (x = +0.096, y = -0.032)
        var stampMark = doc.transform.Find("StampMark");
        if (stampMark != null)
        {
            stampMark.localPosition = new Vector3(0.096f, -0.032f, -0.007f);
            stampMark.localRotation = Quaternion.Euler(0f, 0f, -10f);
            stampMark.localScale = Vector3.one * 0.01f;
            var tm = stampMark.GetComponent<TMP_Text>();
            if (tm != null)
            {
                tm.fontSize = 2.4f;
                tm.enableAutoSizing = false;
                tm.rectTransform.sizeDelta = new Vector2(7.5f, 2.2f);
                tm.color = new Color(0.14f, 0.58f, 0.32f);
            }
        }
        var appStamp = doc.transform.Find("ApprovalStamp");
        if (appStamp != null)
        {
            appStamp.localPosition = new Vector3(0.096f, -0.032f, -0.055f);
            appStamp.localScale = Vector3.one * 1.35f;
        }

        // Arrange all 4 action buttons (`Btn_Approve`, `Btn_Ask_why`, `Btn_Details`, `Btn_Reject`)
        // into ONE single horizontal row inside the lower frame deck (`y = -0.114`)!
        // Note: each Btn_* contains `[BuildingBlock] ISDK_PokeInteraction` whose localScale is (0.14, 0.05, 0.10).
        // Setting Btn_* localScale to (0.44, 0.56, 0.50) makes each key 6.16 cm wide x 2.8 cm tall!
        var actions = doc.transform.Find("Actions");
        if (actions != null)
        {
            actions.localPosition = Vector3.zero;
        }

        var btnScale = new Vector3(0.44f, 0.56f, 0.50f);
        PlaceDocButton(doc.transform, "Btn_Approve", new Vector3(-0.105f, -0.114f, -0.004f), btnScale);
        PlaceDocButton(doc.transform, "Btn_Ask_why", new Vector3(-0.035f, -0.114f, -0.004f), btnScale);
        PlaceDocButton(doc.transform, "Btn_Details", new Vector3( 0.035f, -0.114f, -0.004f), btnScale);
        PlaceDocButton(doc.transform, "Btn_Reject",  new Vector3( 0.105f, -0.114f, -0.004f), btnScale);

        // Update BoxCollider on DecisionDocument to match the 32cm x 30cm folio
        var bc = doc.GetComponent<BoxCollider>();
        if (bc != null)
        {
            bc.center = new Vector3(0f, 0f, 0.004f);
            bc.size = new Vector3(0.33f, 0.31f, 0.05f);
        }

        // Save as reusable Expanded Decision Paper prefab
        PrefabUtility.SaveAsPrefabAsset(doc, $"{PrefabDir}/DecisionPaper_Expanded_Polished.prefab");
        sb.AppendLine("DecisionDocument: upgraded to Doc_DecisionPaper_Expanded, high-contrast dark text, dedicated right StampZone, 4 buttons in 1 row inside frame");
    }

    static void StyleDocText(Transform parent, string name, Vector3 localPos, Vector2 sizeDelta,
                             float minFont, float maxFont, Color color, FontStyles style,
                             TextAlignmentOptions align, bool wrap)
    {
        var t = parent.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
        if (t == null) return;
        t.localPosition = localPos;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one * 0.01f;
        var tmp = t.GetComponent<TMP_Text>();
        if (tmp == null) return;
        tmp.rectTransform.sizeDelta = sizeDelta;
        tmp.enableAutoSizing = minFont < maxFont;
        tmp.fontSizeMin = minFont;
        tmp.fontSizeMax = maxFont;
        tmp.fontSize = maxFont;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        EditorUtility.SetDirty(tmp);
    }

    static void PlaceDocButton(Transform docRoot, string btnName, Vector3 localPos, Vector3 localScale)
    {
        var btn = docRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == btnName);
        if (btn == null) return;
        var actions = docRoot.Find("Actions");
        if (actions != null && btn.parent != actions)
            btn.SetParent(actions, false);
        btn.localPosition = localPos;
        btn.localRotation = Quaternion.identity;
        btn.localScale = localScale;
    }

    // =========================================================================
    // PRIORITY 3: TASK BOARD & TASK CARD READABILITY IMPROVEMENTS
    // =========================================================================

    static void ApplyTaskBoardAndCardPolish(Dictionary<string, Material> matMap, StringBuilder sb)
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

        var tb = all.FirstOrDefault(t => t != null && t.name == "TaskBoard");
        if (tb != null)
        {
            // Remove old KanbanFrame_Visual and deactivated fake PreviewCard_* / DependencyLink_* objects
            var toRemove = new List<GameObject>();
            foreach (Transform child in tb)
            {
                if (child.name == "KanbanFrame_Visual" || child.name == "TaskBoard_KanbanFrame" ||
                    child.name == "KanbanFrameVisual" || child.name == "TaskBoard_KanbanFrame_Optimized" ||
                    child.name.StartsWith("PreviewCard_") || child.name.StartsWith("DependencyLink_"))
                {
                    toRemove.Add(child.gameObject);
                }
            }
            foreach (var go in toRemove) UnityEngine.Object.DestroyImmediate(go);

            var optBoardFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Optimized/TaskBoard_KanbanFrame_Optimized.fbx");
            if (optBoardFbx != null)
            {
                var frame = (GameObject)PrefabUtility.InstantiatePrefab(optBoardFbx, tb);
                frame.name = "KanbanFrame_Visual";
                frame.transform.localPosition = new Vector3(0f, -0.21f, 0.018f);
                frame.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                frame.transform.localScale = new Vector3(1.61f, 1.52f, 1.20f);
                ApplyMaterialsRecursively(frame, matMap);
            }

            // Raise the "TASK BOARD" header label onto the new top Title Pediment so it NEVER overlaps "Working" or "Needs me"!
            foreach (var tmp in tb.GetComponentsInChildren<TMP_Text>(true))
            {
                if (tmp.text != null && tmp.text.ToUpperInvariant().Contains("TASK BOARD"))
                {
                    tmp.transform.localPosition = new Vector3(0f, 0.242f, -0.010f);
                    tmp.fontSize = 3.2f;
                    tmp.enableAutoSizing = false;
                    tmp.color = new Color(0.98f, 0.95f, 0.88f);
                    tmp.fontStyle = FontStyles.Bold;
                }
            }
        }

        // Clean up CardTemplate and any active cards so corner sub-props don't overlap text!
        var cardTemplate = all.FirstOrDefault(t => t != null && t.name == "CardTemplate");
        if (cardTemplate != null)
        {
            // Also swap TaskCard_Visual on CardTemplate with our newly exported TaskCard_Grabbable.fbx (with tucked corner indicators)
            var oldVis = cardTemplate.Find("TaskCard_Visual");
            if (oldVis != null) UnityEngine.Object.DestroyImmediate(oldVis.gameObject);
            var cardFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/TaskBoard/TaskCard_Grabbable.fbx");
            if (cardFbx != null)
            {
                var cVis = (GameObject)PrefabUtility.InstantiatePrefab(cardFbx, cardTemplate);
                cVis.name = "TaskCard_Visual";
                cVis.transform.localPosition = new Vector3(0f, 0f, 0.003f);
                cVis.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                cVis.transform.localScale = new Vector3(1.12f, 1.12f, 1.0f);
                ApplyMaterialsRecursively(cVis, matMap);
            }

            CleanCardHierarchy(cardTemplate);
            SavePrefabCopy(cardTemplate.gameObject, $"{PrefabDir}/TaskCard_Grabbable_Polished.prefab", sb);
        }

        foreach (var cardView in UnityEngine.Object.FindObjectsByType<TaskCardView>(FindObjectsInactive.Include))
        {
            CleanCardHierarchy(cardView.transform);
        }
        sb.AppendLine("TaskBoard & TaskCards: upgraded to TaskBoard_KanbanFrame_Optimized, raised TASK BOARD header into top pediment, cleared corner token overlaps on cards");
    }

    static void CleanCardHierarchy(Transform card)
    {
        // Remove the oversized secondary badge sub-objects (`OwnerIcon_Visual`, `PriorityBadge_Visual`, `StatusMarker_Visual`)
        // that were overlapping `SH_Role`, `SH_Priority`, and `SCRIPTED DEMO` in `02_task_board.png`!
        var toDestroy = new List<GameObject>();
        foreach (Transform ch in card)
        {
            if (ch.name.Contains("OwnerIcon") || ch.name.Contains("PriorityBadge") || ch.name.Contains("StatusMarker"))
                toDestroy.Add(ch.gameObject);
        }
        foreach (var go in toDestroy) UnityEngine.Object.DestroyImmediate(go);

        // Ensure high-contrast dark typography on the card labels
        const float z = -0.0055f;
        StyleCardLabel(card, "SH_Role",     new Vector3(-0.011f,  0.0180f, z), new Vector2(4.4f, 0.85f), 3.4f, 4.6f, new Color(0.98f, 0.96f, 0.92f), FontStyles.Bold,   TextAlignmentOptions.Left,   false);
        StyleCardLabel(card, "SH_Priority", new Vector3( 0.016f,  0.0180f, z), new Vector2(1.6f, 0.85f), 3.6f, 4.6f, new Color(0.99f, 0.92f, 0.65f), FontStyles.Bold,   TextAlignmentOptions.Right,  false);
        StyleCardLabel(card, "Title",       new Vector3( 0.000f,  0.0032f, z), new Vector2(6.8f, 1.65f), 4.4f, 6.8f, new Color(0.10f, 0.11f, 0.15f), FontStyles.Bold,   TextAlignmentOptions.Center, true);
        StyleCardLabel(card, "SH_State",    new Vector3( 0.000f, -0.0102f, z), new Vector2(5.2f, 0.80f), 3.2f, 4.8f, new Color(0.20f, 0.23f, 0.28f), FontStyles.Bold,   TextAlignmentOptions.Center, false);
        StyleCardLabel(card, "SH_Source",   new Vector3( 0.000f, -0.0182f, z), new Vector2(5.2f, 0.65f), 2.6f, 3.6f, new Color(0.68f, 0.28f, 0.22f), FontStyles.Bold,   TextAlignmentOptions.Center, false);
    }

    static void StyleCardLabel(Transform card, string name, Vector3 lp, Vector2 box, float minF, float maxF,
                               Color col, FontStyles style, TextAlignmentOptions align, bool wrap)
    {
        var t = card.Find(name);
        if (t == null) return;
        t.localPosition = lp;
        t.localScale = Vector3.one * 0.01f;
        var tmp = t.GetComponent<TMP_Text>();
        if (tmp == null) return;
        tmp.rectTransform.sizeDelta = box;
        tmp.enableAutoSizing = minF < maxF;
        tmp.fontSizeMin = minF;
        tmp.fontSizeMax = maxF;
        tmp.fontSize = maxF;
        tmp.color = col;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
    }

    // =========================================================================
    // PRIORITY 4: SINGLE-MESH 3D POKE BUTTONS (CUTS ~100 RENDERERS!)
    // =========================================================================

    static void ApplySingleMeshPokeButtons(Dictionary<string, Material> matMap, StringBuilder sb)
    {
        var housingFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/UI/UI_PokeKey_Housing.fbx");
        var capFbx     = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/UI/UI_PokeKey_Cap.fbx");
        if (housingFbx == null || capFbx == null)
        {
            sb.AppendLine("WARN: UI_PokeKey FBXs not found");
            return;
        }

        var housingMesh = housingFbx.GetComponentInChildren<MeshFilter>(true)?.sharedMesh;
        var capMesh     = capFbx.GetComponentInChildren<MeshFilter>(true)?.sharedMesh;
        var housingMat  = AssetDatabase.LoadAssetAtPath<Material>("Assets/MuseDesk/Art/Materials/Mat_CharcoalVisor.mat");

        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

        int buttonsUpgraded = 0;
        int renderersSaved = 0;

        foreach (var btn in all)
        {
            if (btn == null) continue;
            if (!btn.GetComponents<MonoBehaviour>().Any(m => m != null && m.GetType().Name == "PokeInteractable")) continue;
            var visual = btn.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == "ButtonVisual");
            if (visual == null) continue;

            // Count old renderers under Housing3D & Cap3D & ButtonVisual quad
            var oldHousing = btn.Find("Housing3D");
            if (oldHousing != null)
            {
                renderersSaved += oldHousing.GetComponentsInChildren<Renderer>(true).Length;
                UnityEngine.Object.DestroyImmediate(oldHousing.gameObject);
            }
            var oldCap = visual.Find("Cap3D");
            if (oldCap != null)
            {
                renderersSaved += oldCap.GetComponentsInChildren<Renderer>(true).Length;
                UnityEngine.Object.DestroyImmediate(oldCap.gameObject);
            }
            var legacyQuadRenderer = visual.GetComponent<MeshRenderer>();
            if (legacyQuadRenderer != null && legacyQuadRenderer.enabled)
            {
                legacyQuadRenderer.enabled = false;
                renderersSaved += 1;
            }

            // Create 1 single-mesh Housing3D (rotated 180 deg around Y so Blender -Y front rim faces Unity -Z!)
            var hGo = new GameObject("Housing3D");
            hGo.transform.SetParent(btn, false);
            hGo.transform.localPosition = Vector3.zero;
            hGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            hGo.transform.localScale = Vector3.one;
            hGo.AddComponent<MeshFilter>().sharedMesh = housingMesh;
            var hr = hGo.AddComponent<MeshRenderer>();
            hr.sharedMaterial = housingMat;
            hr.shadowCastingMode = ShadowCastingMode.Off;
            hr.receiveShadows = false;

            // Create 1 single-mesh Cap3D (rotated 180 deg around Y so cap sits from +0.004 to +0.280 behind ButtonVisual's -0.035 label!)
            string path = (btn.parent != null ? btn.parent.name + "/" : "") + btn.name;
            Material capMat = GetButtonCapMaterial(path, out bool darkCap);

            var cGo = new GameObject("Cap3D");
            cGo.transform.SetParent(visual, false);
            cGo.transform.localPosition = Vector3.zero;
            cGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            cGo.transform.localScale = Vector3.one;
            cGo.AddComponent<MeshFilter>().sharedMesh = capMesh;
            var cr = cGo.AddComponent<MeshRenderer>();
            cr.sharedMaterial = capMat;
            cr.shadowCastingMode = ShadowCastingMode.Off;
            cr.receiveShadows = false;

            // Ensure label rides in front of Cap3D at z = -0.035f with high contrast
            foreach (var tmp in visual.GetComponentsInChildren<TMP_Text>(true))
            {
                var lp = tmp.transform.localPosition;
                tmp.transform.localPosition = new Vector3(0f, 0f, -0.038f);
                tmp.color = darkCap ? new Color(0.98f, 0.97f, 0.94f) : new Color(0.10f, 0.11f, 0.15f);
                tmp.fontStyle = FontStyles.Bold;
                EditorUtility.SetDirty(tmp);
            }

            renderersSaved -= 2; // +2 new renderers (1 housing + 1 cap)
            buttonsUpgraded++;
        }

        sb.AppendLine($"3D Poke Keys: upgraded {buttonsUpgraded} buttons to single-mesh UI_PokeKey_Housing + UI_PokeKey_Cap (net {renderersSaved} renderers eliminated!)");
    }

    static Material GetButtonCapMaterial(string buttonPath, out bool darkCap)
    {
        string p = buttonPath.ToLowerInvariant().Replace("taskboard/", "").Replace("root/", "").Replace("phase6/", "");
        string matName = "Mat_CreamCeramic";
        darkCap = false;
        if (p.Contains("approve")) { matName = "Mat_StatusGreen"; darkCap = false; }
        else if (p.Contains("reject") || p.Contains("close")) { matName = "Mat_Developer_Coral"; darkCap = true; }
        else if (p.Contains("ask") || p.Contains("explain") || p.Contains("new")) { matName = "Mat_Tester_SlateBlue"; darkCap = true; }
        else if (p.Contains("focus")) { matName = "Mat_Researcher_Lavender"; darkCap = true; }
        else if (p.Contains("voice") || p.Contains("priority")) { matName = "Mat_Developer_Amber"; darkCap = false; }
        else if (p.Contains("details") || p.Contains("pause")) { matName = "Mat_CreamCeramic"; darkCap = false; }

        return AssetDatabase.LoadAssetAtPath<Material>($"Assets/MuseDesk/Art/Materials/{matName}.mat")
            ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/MuseDesk/Art/Materials/Mat_CreamCeramic.mat");
    }

    // =========================================================================
    // PRIORITY 4 & 6: REFINED STAMPS & CLUTTER REDUCTION (PHYSICS TRAY REMOVAL)
    // =========================================================================

    static void ApplyStampsAndClutterReduction(Dictionary<string, Material> matMap, StringBuilder sb)
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var ws = all.FirstOrDefault(t => t != null && t.name == "WorkspaceRoot");
        if (ws == null) return;

        // 1. Clutter Reduction (REMAINING_ISSUES.md #57):
        // Remove the oversized gray `PhysicsTray` and giant primitive `Prop_Mug` / `Prop_Block`
        // that cluttered the user's front-left next to the TaskBoard in the preview scene!
        foreach (var clutterName in new[] { "PhysicsTray", "Prop_Mug", "Prop_Block" })
        {
            var obj = all.FirstOrDefault(t => t != null && t.name == clutterName);
            if (obj != null)
            {
                UnityEngine.Object.DestroyImmediate(obj.gameObject);
                sb.AppendLine($"Clutter reduction: removed oversized '{clutterName}' on front-left desk");
            }
        }

        // 2. Place `Doc_StampRestPad` neatly at front-and-center of the desk (`(0f, 0.002f, 0.40f)`)
        // and reskin `StampApprove` & `StampReject` with our ergonomic color-crowned models!
        var restPadFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Documents/Doc_StampRestPad.fbx");
        if (restPadFbx != null)
        {
            var oldPad = ws.Find("Doc_StampRestPad");
            if (oldPad != null) UnityEngine.Object.DestroyImmediate(oldPad.gameObject);
            var pad = (GameObject)PrefabUtility.InstantiatePrefab(restPadFbx, ws);
            pad.name = "Doc_StampRestPad";
            pad.transform.localPosition = new Vector3(0.00f, 0.002f, 0.40f);
            pad.transform.localRotation = Quaternion.identity;
            pad.transform.localScale = Vector3.one * 1.15f;
            ApplyMaterialsRecursively(pad, matMap);
        }

        ReskinStampTool(ws, "StampApprove", "Assets/MuseDesk/Art/Models/Documents/Doc_Stamp_Approve.fbx", new Vector3(-0.032f, 0.012f, 0.40f), matMap);
        ReskinStampTool(ws, "StampReject",  "Assets/MuseDesk/Art/Models/Documents/Doc_Stamp_Reject.fbx",  new Vector3( 0.032f, 0.012f, 0.40f), matMap);
        sb.AppendLine("Stamps: reskinned StampApprove & StampReject with color-crowned models + front orientation marks on Doc_StampRestPad");
    }

    static void ReskinStampTool(Transform ws, string stampName, string fbxPath, Vector3 restPos, Dictionary<string, Material> matMap)
    {
        var stampRoot = ws.Find(stampName);
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (stampRoot == null || fbx == null) return;

        stampRoot.localPosition = restPos;
        stampRoot.localRotation = Quaternion.identity;

        foreach (var oldPart in new[] { "Handle", "Head", "StampVisual" })
        {
            var c = stampRoot.Find(oldPart);
            if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
        }

        var vis = (GameObject)PrefabUtility.InstantiatePrefab(fbx, stampRoot);
        vis.name = "StampVisual";
        vis.transform.localPosition = Vector3.zero;
        vis.transform.localRotation = Quaternion.identity;
        vis.transform.localScale = Vector3.one * 1.15f;
        ApplyMaterialsRecursively(vis, matMap);

        var tool = stampRoot.GetComponent<StampTool>();
        if (tool != null)
        {
            tool.headRenderer = vis.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("Status"));
        }
    }

    // =========================================================================
    // PRIORITY 2, 5 & 8: STATION DROP-ZONE HALOS & OPTIMIZED WORKSTATIONS
    // =========================================================================

    static void ApplyStationAffordancesAndOptimizedWorkstations(Dictionary<string, Material> matMap, StringBuilder sb)
    {
        var ws = GameObject.Find("WorkspaceRoot")?.transform;
        if (ws == null) return;

        var haloFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Affordances/Affordance_AgentHaloRing.fbx");
        var padFbx  = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Affordances/Affordance_DeskReceivePad.fbx");

        var stationSpecs = new (string stationName, string role, string optFbxPath, AssignmentVisualState demoState)[]
        {
            ("ResearchStation", "researcher", "Assets/MuseDesk/Art/Models/Optimized/Workstation_Research_Optimized.fbx",  AssignmentVisualState.ValidTarget),
            ("DevStation",      "developer",  "Assets/MuseDesk/Art/Models/Optimized/Workstation_Developer_Optimized.fbx", AssignmentVisualState.ConfirmedAssignment),
            ("QAStation",       "tester",     "Assets/MuseDesk/Art/Models/Optimized/Workstation_Testing_Optimized.fbx",   AssignmentVisualState.HoveredWorkstation),
        };

        var app = GameObject.Find("AppServices");

        foreach (var (stationName, role, optFbxPath, demoState) in stationSpecs)
        {
            // 1. Wire StationDropZone.halo (fixes REMAINING_ISSUES.md #47!)
            var dz = UnityEngine.Object.FindObjectsByType<StationDropZone>(FindObjectsInactive.Include)
                .FirstOrDefault(z => z.agentId == role);

            if (dz != null && haloFbx != null)
            {
                var oldHalo = dz.transform.Find("AgentHaloRing");
                if (oldHalo != null) UnityEngine.Object.DestroyImmediate(oldHalo.gameObject);

                var haloGo = (GameObject)PrefabUtility.InstantiatePrefab(haloFbx, dz.transform);
                haloGo.name = "AgentHaloRing";
                haloGo.transform.localPosition = new Vector3(0f, 0.003f, 0f);
                haloGo.transform.localRotation = Quaternion.identity;
                haloGo.transform.localScale = Vector3.one;
                ApplyMaterialsRecursively(haloGo, matMap);

                dz.halo = haloGo;
                var affView = dz.GetComponent<AssignmentAffordanceView>() ?? dz.gameObject.AddComponent<AssignmentAffordanceView>();
                affView.agentId = role;
                affView.stationDropZone = dz;
                affView.haloRoot = haloGo;
                affView.haloOuterRenderer = haloGo.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("StatusGreen"));

                // Add miniature DeskReceivePad on the station's desk corner
                if (padFbx != null)
                {
                    var oldPad = dz.transform.Find("DeskReceivePad");
                    if (oldPad != null) UnityEngine.Object.DestroyImmediate(oldPad.gameObject);
                    var padGo = (GameObject)PrefabUtility.InstantiatePrefab(padFbx, dz.transform);
                    padGo.name = "DeskReceivePad";
                    padGo.transform.localPosition = new Vector3(0.088f, 0.089f, 0.042f);
                    padGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    padGo.transform.localScale = Vector3.one * 0.85f;
                    ApplyMaterialsRecursively(padGo, matMap);
                    affView.receivePadStatusRenderer = padGo.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("StatusBar"));
                }

                affView.SetState(demoState);
                haloGo.SetActive(true); // Keep visible in preview scene so before/after comparison shows the halo rings!
                EditorUtility.SetDirty(dz);
            }

            // 2. Swap Workstation multi-part mesh (53-56 renderers) with Reversible Material-Welded Optimized Workstation (16-24 renderers!)
            var st = ws.Find(stationName);
            var wsNode = st != null ? st.Find("Workstation") : null;
            var optFbx = AssetDatabase.LoadAssetAtPath<GameObject>(optFbxPath);
            if (st != null && wsNode != null && optFbx != null)
            {
                var pos = wsNode.localPosition;
                var rot = wsNode.localRotation;
                var scl = wsNode.localScale;

                // Preserve any runtime children like FurnitureCollider
                var keepChildren = new List<Transform>();
                foreach (Transform ch in wsNode)
                {
                    if (ch.name == "FurnitureCollider" || ch.name == "Monitor" || ch.GetComponent<TMP_Text>() != null)
                        keepChildren.Add(ch);
                }
                foreach (var kc in keepChildren) kc.SetParent(st, true);

                int beforeWsRends = wsNode.GetComponentsInChildren<Renderer>(true).Length;
                UnityEngine.Object.DestroyImmediate(wsNode.gameObject);

                var newWs = (GameObject)PrefabUtility.InstantiatePrefab(optFbx, st);
                newWs.name = "Workstation";
                newWs.transform.localPosition = pos;
                newWs.transform.localRotation = rot;
                newWs.transform.localScale = scl;
                ApplyMaterialsRecursively(newWs, matMap);

                foreach (var kc in keepChildren) kc.SetParent(newWs.transform, true);

                var vis = newWs.GetComponent<WorkstationVisualController>() ?? newWs.AddComponent<WorkstationVisualController>();
                vis.ResolveAnchorsAndParts();
                if (stationName == "QAStation" && app != null)
                {
                    var tsc = app.GetComponent<TestingStationController>();
                    if (tsc != null) { tsc.testingStation = vis; EditorUtility.SetDirty(tsc); }
                }

                int afterWsRends = newWs.GetComponentsInChildren<Renderer>(true).Length;
                sb.AppendLine($"  {stationName}/Workstation: swapped to {Path.GetFileName(optFbxPath)} ({beforeWsRends} -> {afterWsRends} renderers)");
            }
        }

        sb.AppendLine("Workstations & DropZones: wired StationDropZone.halo + DeskReceivePad on all 3 stations, swapped to reversible *_Optimized workstation meshes");
    }

    // =========================================================================
    // PRIORITY 7: OPTIONAL MASCOT FEEDBACK BADGES ABOVE AGENTS
    // =========================================================================

    static void ApplyMascotFeedbackBadges(Dictionary<string, Material> matMap, StringBuilder sb)
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

        var specs = new (string agentName, string role, MascotFeedbackState state)[]
        {
            ("Agent_Researcher", "researcher", MascotFeedbackState.NeedsAttention),
            ("Agent_Developer",  "developer",  MascotFeedbackState.Thinking),
            ("Agent_Tester",     "tester",     MascotFeedbackState.Success),
        };

        foreach (var (agentName, role, state) in specs)
        {
            var agent = all.FirstOrDefault(t => t != null && t.name == agentName);
            if (agent == null) continue;

            var old = agent.Find("MascotFeedbackBadges");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            var groupGo = BuildMascotFeedbackGroupObject("MascotFeedbackBadges", matMap);
            groupGo.transform.SetParent(agent, false);
            // Position above the mascot's head (+0.235m local Y) without touching the rig or obstructing the face
            groupGo.transform.localPosition = new Vector3(0.055f, 0.235f, -0.02f);
            groupGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            groupGo.transform.localScale = Vector3.one * 1.15f;

            var comp = groupGo.GetComponent<MascotFeedbackBadgeGroup>();
            comp.agentId = role;
            comp.SetFeedback(state);
        }

        sb.AppendLine("Mascot Feedback: attached optional 3D MascotFeedbackBadges above Researcher, Developer, and Tester without modifying character rigs");
    }

    static void BuildAffordanceAndFeedbackShowcaseStrip(Dictionary<string, Material> matMap, StringBuilder sb)
    {
        var ws = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t != null && t.name == "WorkspaceRoot");
        if (ws == null) return;

        var oldStrip = ws.Find("VisualPolish_GalleryStrip");
        if (oldStrip != null) UnityEngine.Object.DestroyImmediate(oldStrip.gameObject);

        var strip = new GameObject("VisualPolish_GalleryStrip");
        strip.transform.SetParent(ws, false);
        strip.transform.localPosition = new Vector3(-0.20f, 0.012f, 0.39f);
        strip.transform.localRotation = Quaternion.identity;

        // Place a Small Decision Paper next to the Stamp Rest Pad & Focus/Explain buttons so both Small and Expanded Decision Papers are visible!
        var smallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/DecisionPaper_Small_Polished.prefab");
        if (smallPrefab != null)
        {
            var sm = (GameObject)PrefabUtility.InstantiatePrefab(smallPrefab, strip.transform);
            sm.name = "DecisionPaper_Small_Showcase";
            sm.transform.localPosition = new Vector3(0f, 0.008f, 0f);
            sm.transform.localRotation = Quaternion.Euler(-65f, 0f, 0f);
            sm.transform.localScale = Vector3.one;
            ApplyMaterialsRecursively(sm, matMap);
        }

        sb.AppendLine("Showcase: placed DecisionPaper_Small_Polished on right-front desk reading spot");
    }

    // =========================================================================
    // SCREENSHOT CAPTURES (MATCHING 01..05 REVIEW SCREENSHOT ANGLES)
    // =========================================================================

    static void CaptureAllPreviews(StringBuilder sb)
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

        var ws = all.FirstOrDefault(t => t != null && t.name == "WorkspaceRoot");
        var doc = all.FirstOrDefault(t => t != null && t.name == "DecisionDocument");
        var tb = all.FirstOrDefault(t => t != null && t.name == "TaskBoard");

        // Add temporary studio lighting for Game-view renders (since QuestRenderSetup disabled scene lights)
        var lightRoot = new GameObject("__TempPreviewStudioLights");
        var keyLight = new GameObject("KeyLight").AddComponent<Light>();
        keyLight.transform.SetParent(lightRoot.transform, false);
        keyLight.type = LightType.Directional;
        keyLight.color = new Color(1.0f, 0.97f, 0.92f);
        keyLight.intensity = 1.25f;
        keyLight.transform.rotation = Quaternion.Euler(42f, -155f, 0f); // Shining from user's +Z side toward -Z workspace!

        var fillLight = new GameObject("FillLight").AddComponent<Light>();
        fillLight.transform.SetParent(lightRoot.transform, false);
        fillLight.type = LightType.Directional;
        fillLight.color = new Color(0.78f, 0.86f, 0.98f);
        fillLight.intensity = 0.55f;
        fillLight.transform.rotation = Quaternion.Euler(28f, 145f, 0f);

        var prevAmbientMode = RenderSettings.ambientMode;
        var prevAmbientLight = RenderSettings.ambientLight;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.64f, 0.68f);

        bool docWasActive = doc != null && doc.gameObject.activeSelf;
        var tempCards = new List<GameObject>();

        try
        {
            // 1. Populate sample cards on the TaskBoard (using Region_Backlog, Region_Working, Region_NeedsMe, Region_Completed as in Preview.cs)
            var cardTemplate = tb != null ? tb.Find("CardTemplate") : null;
            if (tb != null && cardTemplate != null)
            {
                var samples = new (string region, TaskCardV2 data)[]
                {
                    ("Region_Backlog",   new TaskCardV2 { TaskId = "t2-ab12cd34", Title = "Write the login tests",                TaskState = TaskStates.Queued,           Priority = 2, Revision = 0, SourceMode = "demo" }),
                    ("Region_Backlog",   new TaskCardV2 { TaskId = "t2-ef56ab78", Title = "Investigate flaky token refresh",      TaskState = TaskStates.Blocked,          AgentId = "researcher", Priority = 1, Revision = 3, SourceMode = "demo" }),
                    ("Region_Working",   new TaskCardV2 { TaskId = "t2-0911aa22", Title = "Implement OAuth scaffold",             TaskState = TaskStates.Running,          AgentId = "developer",  Priority = 3, Revision = 5, SourceMode = "live" }),
                    ("Region_NeedsMe",   new TaskCardV2 { TaskId = "t2-77c0de11", Title = "Approve auth approach",                TaskState = TaskStates.AwaitingApproval, AgentId = "researcher", Priority = 3, Revision = 2, SourceMode = "live" }),
                    ("Region_Completed", new TaskCardV2 { TaskId = "t2-5150beef", Title = "Baseline test run",                    TaskState = TaskStates.Completed,        AgentId = "tester",     Priority = 0, Revision = 4, SourceMode = "demo" }),
                };
                var rows = new Dictionary<string, int>();
                foreach (var (regionName, data) in samples)
                {
                    var region = tb.Find(regionName);
                    if (region == null) continue;
                    var go = UnityEngine.Object.Instantiate(cardTemplate.gameObject, region);
                    go.name = "__PreviewCard";
                    rows.TryGetValue(regionName, out int row);
                    rows[regionName] = row + 1;
                    go.transform.localPosition = new Vector3(0f, -row * 0.10f, 0f);
                    go.transform.localRotation = Quaternion.identity;
                    go.SetActive(true);
                    var view = go.GetComponent<TaskCardView>();
                    if (view != null) view.SetData(data);
                    CleanCardHierarchy(go.transform);
                    tempCards.Add(go);
                }
            }

            // Force update all TMP texts in the scene
            RefreshAllTmpText();

            Vector3 wsPos = ws != null ? ws.position : new Vector3(0f, 0.70f, 0.62f);
            Vector3 eye = ws != null ? ws.position + ws.forward * 0.62f + Vector3.up * 0.30f : wsPos + new Vector3(0f, 0.30f, 0.62f);

            // Capture 1: Seated Overview (matches 01_seated_view.png — from user eye looking at workspace)
            if (doc != null) doc.gameObject.SetActive(false);
            RenderCameraToPng(
                eye,
                wsPos + Vector3.up * 0.12f,
                72f, Path.Combine(ScreenshotDir, "After_01_SeatedOverview.png"), sb);

            // Capture 2: Task Board Closeup (matches 02_task_board.png)
            if (tb != null)
            {
                RenderCameraToPng(
                    tb.position - tb.forward * 0.50f,
                    tb.position,
                    50f, Path.Combine(ScreenshotDir, "After_02_TaskBoard.png"), sb);

                // Capture 4: Board 3D Buttons Closeup (matches 04_board_buttons_3d.png)
                var pauseBtn = tb.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "PauseResumeButton") ?? tb;
                RenderCameraToPng(
                    pauseBtn.position - pauseBtn.forward * 0.22f,
                    pauseBtn.position,
                    45f, Path.Combine(ScreenshotDir, "After_04_BoardButtons3D.png"), sb);
            }

            // Capture 3: Decision Paper Closeup (matches 03_decision_paper.png)
            if (doc != null)
            {
                doc.gameObject.SetActive(true);
                foreach (Transform c in doc)
                    c.gameObject.SetActive(c.name != "ApprovalStamp" && c.name != "HoverFrame");

                var panel = doc.GetComponentInChildren<DecisionPanel>(true);
                if (panel != null)
                {
                    var content = new DecisionContent
                    {
                        Title = "Use OAuth for sign-in?",
                        Summary = "OAuth with an existing provider is less code to own than local passwords, and the baseline tests already pass.",
                        Recommendation = "approve",
                        Rationale = "No password storage or reset flow to build and secure. The provider handles MFA.",
                        RiskLevel = "Low. Adds a dependency on the identity provider.",
                        Impact = "The Developer is blocked on this decision.",
                        SourceMode = "demo",
                    };
                    content.Alternatives.Add(new DecisionAlternative { Label = "Local passwords", Tradeoff = "more code to secure" });
                    panel.Bind(content, "demo", "Template recommendation. Not live AI research.");
                    panel.Expand();
                    panel.SetAnswer("OAuth removes the password database, which is the part most often breached.");
                }
                var mark = doc.Find("StampMark");
                if (mark != null) mark.gameObject.SetActive(true);

                doc.position = ws != null ? ws.TransformPoint(new Vector3(-0.05f, 0.24f, 0.30f)) : wsPos + new Vector3(-0.05f, 0.24f, 0.30f);
                doc.rotation = Quaternion.LookRotation(doc.position - eye, Vector3.up);
                RefreshAllTmpText();

                RenderCameraToPng(
                    doc.position - doc.forward * 0.38f,
                    doc.position,
                    48f, Path.Combine(ScreenshotDir, "After_03_DecisionPaper.png"), sb);
            }

            // Capture 5: Desk Buttons, Stamps, Small Decision Paper & Affordances Closeup (matches 05_desk_buttons_3d.png)
            if (doc != null) doc.gameObject.SetActive(false);
            var focusBtn = ws != null ? ws.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "FocusToggle") : null;
            if (focusBtn != null)
            {
                Vector3 camEye = ws.TransformPoint(new Vector3(-0.22f, 0.22f, 0.58f));
                Vector3 target = ws.TransformPoint(new Vector3(-0.18f, 0.04f, 0.38f));
                RenderCameraToPng(
                    camEye,
                    target,
                    52f, Path.Combine(ScreenshotDir, "After_05_StampsAndAffordances.png"), sb);
            }
        }
        finally
        {
            foreach (var tc in tempCards)
                if (tc != null) UnityEngine.Object.DestroyImmediate(tc);
            if (doc != null)
            {
                var mark = doc.Find("StampMark");
                if (mark != null) mark.gameObject.SetActive(false);
                doc.gameObject.SetActive(docWasActive);
            }
            if (lightRoot != null) UnityEngine.Object.DestroyImmediate(lightRoot);
            RenderSettings.ambientMode = prevAmbientMode;
            RenderSettings.ambientLight = prevAmbientLight;
        }
    }

    static void RefreshAllTmpText()
    {
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (tmp != null && tmp.gameObject.activeInHierarchy)
                    tmp.ForceMeshUpdate(true, true);
            }
        }
    }

    static void RenderCameraToPng(Vector3 camPos, Vector3 lookTarget, float fov, string outPath, StringBuilder sb)
    {
        var camGo = new GameObject("__PolishCaptureCam");
        try
        {
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f, 1f);
            camGo.transform.position = camPos;
            camGo.transform.rotation = Quaternion.LookRotation((lookTarget - camPos).normalized, Vector3.up);

            // IMPORTANT: Do NOT set rt.antiAliasing > 1 for offscreen URP ReadPixels in Edit Mode (causes unresolved black readback)
            int w = 1440, h = 960;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            cam.targetTexture = null;
            RenderTexture.active = prevActive;
            byte[] png = ImageConversion.EncodeToPNG(tex);
            File.WriteAllBytes(outPath, png);

            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            sb.AppendLine($"  Captured {outPath} ({w}x{h})");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(camGo);
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    static TMP_Text CreateDarkTMP(Transform parent, string name, Vector3 lp, float fontSize,
                                  Vector2 sizeDelta, TextAlignmentOptions align, Color color, FontStyles style)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * 0.01f;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = name;
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.rectTransform.sizeDelta = sizeDelta;
        return tmp;
    }

    static void SaveModelAsPrefab(string fbxPath, string prefabPath, Dictionary<string, Material> matMap, StringBuilder sb)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbx == null) return;
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        inst.name = Path.GetFileNameWithoutExtension(prefabPath);
        ApplyMaterialsRecursively(inst, matMap);
        SavePrefab(inst, prefabPath, sb);
    }

    static void SavePrefab(GameObject inst, string prefabPath, StringBuilder sb)
    {
        PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
        UnityEngine.Object.DestroyImmediate(inst);
        sb.AppendLine($"  Prefab saved: {prefabPath}");
    }

    static void SavePrefabCopy(GameObject sceneObj, string prefabPath, StringBuilder sb)
    {
        var copy = UnityEngine.Object.Instantiate(sceneObj);
        copy.name = Path.GetFileNameWithoutExtension(prefabPath);
        copy.SetActive(true);
        PrefabUtility.SaveAsPrefabAsset(copy, prefabPath);
        UnityEngine.Object.DestroyImmediate(copy);
        sb.AppendLine($"  Prefab saved: {prefabPath}");
    }

    static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{cur}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
