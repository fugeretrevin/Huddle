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
using MuseDesk.Art.VisualPolish;
using MuseDesk.Documents;
using MuseDesk.Phase2.Board;
using MuseDesk.Phase2.Contracts;

public static class BuildVisualPolishSuite
{
    const string MainScenePath = "Assets/MuseDesk/Scenes/MainMRScene.unity";
    const string PreviewScenePath = "Assets/MuseDesk/Scenes/VisualPolishPreviewScene.unity";
    const string PrefabDir = "Assets/MuseDesk/Art/Prefabs/VisualPolish";
    const string ScreenshotDir = "Screenshots/VisualPolish";

    public static string RunAll()
    {
        var sb = new StringBuilder();
        Directory.CreateDirectory(ScreenshotDir);
        EnsureFolder(PrefabDir);

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        // 1. Verify mesh bounds on all newly generated FBX models
        sb.AppendLine("=== 1. MESH BOUNDS VERIFICATION ===");
        VerifyNewModelBounds(sb);

        // 2. Build Reusable Visual Polish Prefabs
        sb.AppendLine("\n=== 2. PREFAB GENERATION ===");
        BuildReusablePrefabs(sb);

        // 3. Measure Baseline Renderer Count in MainMRScene (Read-Only) then copy to VisualPolishPreviewScene
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        int beforeRenderers = CountSceneRenderers();
        sb.AppendLine($"\n=== 3. SCENE UPGRADE ({PreviewScenePath}) ===");
        sb.AppendLine($"Baseline MainMRScene total renderers: {beforeRenderers}");

        // Copy MainMRScene.unity on disk to PreviewScenePath so MainMRScene.unity is NEVER saved or modified!
        File.Copy(MainScenePath, PreviewScenePath, true);
        AssetDatabase.ImportAsset(PreviewScenePath, ImportAssetOptions.ForceSynchronousImport);
        var previewScene = EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Single);

        ApplyDecisionPaperPolish(sb);
        ApplyTaskBoardAndCardPolish(sb);
        ApplySingleMeshPokeButtons(sb);
        ApplyStampsAndClutterReduction(sb);
        ApplyStationAffordancesAndOptimizedWorkstations(sb);
        ApplyMascotFeedbackBadges(sb);
        BuildAffordanceAndFeedbackShowcaseStrip(sb);

        int afterRenderers = CountSceneRenderers();
        sb.AppendLine($"Polished VisualPolishPreviewScene total renderers: {afterRenderers} (Saved {beforeRenderers - afterRenderers} renderers, {(1f - (float)afterRenderers / Mathf.Max(1, beforeRenderers)) * 100f:F1}% reduction!)");

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

    static int CountSceneRenderers()
    {
        return EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
            .Count(r => !(r is ParticleSystemRenderer));
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

    static void BuildReusablePrefabs(StringBuilder sb)
    {
        // 1. Small Decision Paper Prefab
        var smallFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Documents/Doc_DecisionPaper_Small.fbx");
        if (smallFbx != null)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(smallFbx);
            inst.name = "DecisionPaper_Small_Polished";
            CreateDarkTMP(inst.transform, "Badge",   new Vector3(0f,  0.046f, -0.0042f), 2.2f, new Vector2(7.6f, 1.2f), TextAlignmentOptions.Center, new Color(0.96f, 0.94f, 0.88f), FontStyles.Bold);
            CreateDarkTMP(inst.transform, "Title",   new Vector3(0f,  0.022f, -0.0042f), 2.8f, new Vector2(7.6f, 1.8f), TextAlignmentOptions.Center, new Color(0.10f, 0.11f, 0.14f), FontStyles.Bold);
            CreateDarkTMP(inst.transform, "Summary", new Vector3(0f, -0.002f, -0.0042f), 2.1f, new Vector2(7.4f, 2.6f), TextAlignmentOptions.Center, new Color(0.16f, 0.18f, 0.22f), FontStyles.Normal);
            CreateDarkTMP(inst.transform, "Reco",    new Vector3(-0.012f, -0.030f, -0.0042f), 1.9f, new Vector2(5.0f, 1.0f), TextAlignmentOptions.Center, new Color(0.14f, 0.44f, 0.26f), FontStyles.Bold);
            SavePrefab(inst, $"{PrefabDir}/DecisionPaper_Small_Polished.prefab", sb);
        }

        // 2. Affordance Prefabs
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Affordances/Affordance_AgentHaloRing.fbx", $"{PrefabDir}/Affordance_AgentHaloRing.prefab", sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Affordances/Affordance_DeskReceivePad.fbx", $"{PrefabDir}/Affordance_DeskReceivePad.prefab", sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Affordances/Affordance_CardSocket.fbx", $"{PrefabDir}/Affordance_CardSocket.prefab", sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Affordances/Affordance_StatePuck.fbx", $"{PrefabDir}/Affordance_StatePuck.prefab", sb);

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
        SavePrefab(kitGo, $"{PrefabDir}/Affordance_StationAssignment_Kit.prefab", sb);

        // 3. Stamps & Rest Pad Prefabs
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Documents/Doc_Stamp_Approve.fbx", $"{PrefabDir}/Doc_Stamp_Approve_Polished.prefab", sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Documents/Doc_Stamp_Reject.fbx",  $"{PrefabDir}/Doc_Stamp_Reject_Polished.prefab", sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Documents/Doc_StampRestPad.fbx",  $"{PrefabDir}/Doc_StampRestPad.prefab", sb);

        // 4. Mascot Feedback Badge Group Prefab
        var fbGroupGo = BuildMascotFeedbackGroupObject("Mascot_FeedbackBadgeGroup");
        SavePrefab(fbGroupGo, $"{PrefabDir}/Mascot_FeedbackBadgeGroup.prefab", sb);

        // 5. Optimized Workstation & Board Prefabs
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Optimized/Workstation_Research_Optimized.fbx",  $"{PrefabDir}/Workstation_Research_Optimized.prefab", sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Optimized/Workstation_Developer_Optimized.fbx", $"{PrefabDir}/Workstation_Developer_Optimized.prefab", sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Optimized/Workstation_Testing_Optimized.fbx",   $"{PrefabDir}/Workstation_Testing_Optimized.prefab", sb);
        SaveModelAsPrefab("Assets/MuseDesk/Art/Models/Optimized/TaskBoard_KanbanFrame_Optimized.fbx", $"{PrefabDir}/TaskBoard_KanbanFrame_Optimized.prefab", sb);
    }

    static GameObject BuildMascotFeedbackGroupObject(string name)
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

    static void ApplyDecisionPaperPolish(StringBuilder sb)
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
            // Orient front of folio (-Z in Unity) toward the user (-Z of DecisionDocument is front face)
            folio.transform.localPosition = new Vector3(0f, 0f, 0.005f);
            folio.transform.localRotation = Quaternion.identity;
            folio.transform.localScale = Vector3.one;
        }

        // Re-layout all TMP text fields for crisp contrast, clear hierarchy, and zero overlap!
        // Note: On DecisionDocument, front face is -Z (`z = -0.004f`).
        // Notice in `03_decision_paper.png`:
        //   - Region 1 (Top Dark Header Bar, y = +0.118): `Badge` ("[BLOCKING] [DEMO]") in crisp cream/amber text!
        //   - Above Top Clip (y = +0.168): `Status` ("Approved. The developer is continuing.")
        //   - Region 2 (Upper Cream Summary Card, y = +0.020 to +0.092):
        //       `Title` at y = +0.078 (Large bold dark charcoal: "Deploy auth service to staging?")
        //       `Summary` at y = +0.048 (Dark readable body text: "OAuth login flow passed 12/12 tests.")
        //       `Reco` at y = +0.024 (Bold emerald/amber pill text: "Recommended: APPROVE")
        //   - Region 3 (Lower-Left Details Card, x = -0.044, y = -0.032, width = 18.0 cm):
        //       `Details` root containing `Body` (y = -0.022) and `Answer` (y = -0.058)
        //   - Region 4 (Lower-Right Dedicated Stamp Target Pad, x = +0.096, y = -0.032):
        //       `StampZone` and `StampMark` centered at `(0.096, -0.032, -0.006)`!
        //   - Region 5 (Bottom Single-Row Action Deck, y = -0.114, inside the frame bottom at -0.150):
        //       All 4 buttons in ONE row:
        //         `Btn_Approve` at `(-0.105, -0.114, -0.004)`
        //         `Btn_Ask_why` at `(-0.035, -0.114, -0.004)`
        //         `Btn_Details` at `(+0.035, -0.114, -0.004)`
        //         `Btn_Reject`  at `(+0.105, -0.114, -0.004)`

        StyleDocText(doc.transform, "Status",  new Vector3(0.000f,  0.168f, -0.005f), new Vector2(28f, 2.4f), 2.4f, 3.2f, new Color(0.98f, 0.92f, 0.65f), FontStyles.Bold,   TextAlignmentOptions.Center, false);
        StyleDocText(doc.transform, "Badge",   new Vector3(0.000f,  0.118f, -0.005f), new Vector2(26f, 2.0f), 2.2f, 2.8f, new Color(0.98f, 0.95f, 0.86f), FontStyles.Bold,   TextAlignmentOptions.Center, false);
        StyleDocText(doc.transform, "Title",   new Vector3(0.000f,  0.078f, -0.005f), new Vector2(26f, 2.6f), 2.8f, 3.8f, new Color(0.10f, 0.11f, 0.15f), FontStyles.Bold,   TextAlignmentOptions.Center, true);
        StyleDocText(doc.transform, "Summary", new Vector3(0.000f,  0.048f, -0.005f), new Vector2(26f, 2.6f), 2.2f, 2.9f, new Color(0.16f, 0.18f, 0.22f), FontStyles.Normal, TextAlignmentOptions.Center, true);
        StyleDocText(doc.transform, "Reco",    new Vector3(0.000f,  0.024f, -0.005f), new Vector2(16f, 1.5f), 2.0f, 2.5f, new Color(0.14f, 0.46f, 0.28f), FontStyles.Bold,   TextAlignmentOptions.Center, false);

        // Details region on the left side (x = -0.044) so the right side (x = +0.096) is 100% dedicated to the Stamp Target!
        var details = doc.transform.Find("Details");
        if (details != null)
        {
            details.localPosition = Vector3.zero;
            StyleDocText(details, "Body",   new Vector3(-0.044f, -0.024f, -0.005f), new Vector2(17.8f, 5.0f), 1.6f, 2.2f, new Color(0.14f, 0.15f, 0.19f), FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
            StyleDocText(details, "Answer", new Vector3(-0.044f, -0.058f, -0.005f), new Vector2(17.8f, 2.0f), 1.6f, 2.1f, new Color(0.20f, 0.34f, 0.56f), FontStyles.Italic, TextAlignmentOptions.Center,  true);
        }
        else
        {
            StyleDocText(doc.transform, "Body",   new Vector3(-0.044f, -0.024f, -0.005f), new Vector2(17.8f, 5.0f), 1.6f, 2.2f, new Color(0.14f, 0.15f, 0.19f), FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
            StyleDocText(doc.transform, "Answer", new Vector3(-0.044f, -0.058f, -0.005f), new Vector2(17.8f, 2.0f), 1.6f, 2.1f, new Color(0.20f, 0.34f, 0.56f), FontStyles.Italic, TextAlignmentOptions.Center,  true);
        }

        // Move StampZone to the dedicated Stamp Target pad on the right (x = +0.096, y = -0.032)
        // and remove the giant ugly primitive cylinder ZoneMarker that overlapped Ask Why!
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
                tm.fontSize = 2.6f;
                tm.enableAutoSizing = false;
                tm.rectTransform.sizeDelta = new Vector2(7.5f, 2.2f);
                tm.color = new Color(0.16f, 0.64f, 0.36f);
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
        var actions = doc.transform.Find("Actions");
        if (actions != null)
        {
            actions.localPosition = Vector3.zero;
        }

        PlaceDocButton(doc.transform, "Btn_Approve", new Vector3(-0.105f, -0.114f, -0.004f), new Vector3(0.048f, 0.036f, 0.040f));
        PlaceDocButton(doc.transform, "Btn_Ask_why", new Vector3(-0.035f, -0.114f, -0.004f), new Vector3(0.048f, 0.036f, 0.040f));
        PlaceDocButton(doc.transform, "Btn_Details", new Vector3( 0.035f, -0.114f, -0.004f), new Vector3(0.048f, 0.036f, 0.040f));
        PlaceDocButton(doc.transform, "Btn_Reject",  new Vector3( 0.105f, -0.114f, -0.004f), new Vector3(0.048f, 0.036f, 0.040f));

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

    static void ApplyTaskBoardAndCardPolish(StringBuilder sb)
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

        var tb = all.FirstOrDefault(t => t.name == "TaskBoard");
        if (tb != null)
        {
            // Swap KanbanFrame visual to TaskBoard_KanbanFrame_Optimized (which has the dedicated top Title Pediment!)
            var oldFrame = tb.Find("TaskBoard_KanbanFrame") ?? tb.Find("KanbanFrameVisual");
            if (oldFrame != null) UnityEngine.Object.DestroyImmediate(oldFrame.gameObject);

            var optBoardFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Optimized/TaskBoard_KanbanFrame_Optimized.fbx");
            if (optBoardFbx != null)
            {
                var frame = (GameObject)PrefabUtility.InstantiatePrefab(optBoardFbx, tb);
                frame.name = "TaskBoard_KanbanFrame_Optimized";
                frame.transform.localPosition = new Vector3(0f, -0.22f, 0.015f);
                frame.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                frame.transform.localScale = new Vector3(1.55f, 1.55f, 1.55f);
            }

            // Raise the "TASK BOARD" header label onto the new top Title Pediment so it NEVER overlaps "Working" or "Needs me"!
            foreach (var tmp in tb.GetComponentsInChildren<TMP_Text>(true))
            {
                if (tmp.text != null && tmp.text.ToUpperInvariant().Contains("TASK BOARD"))
                {
                    tmp.transform.localPosition = new Vector3(0f, 0.236f, -0.008f);
                    tmp.fontSize = 3.2f;
                    tmp.enableAutoSizing = false;
                    tmp.color = new Color(0.98f, 0.95f, 0.88f);
                    tmp.fontStyle = FontStyles.Bold;
                }
            }
        }

        // Clean up CardTemplate and any active cards so corner sub-props don't overlap text!
        var cardTemplate = all.FirstOrDefault(t => t.name == "CardTemplate");
        if (cardTemplate != null)
        {
            CleanCardHierarchy(cardTemplate);
            SavePrefabCopy(cardTemplate.gameObject, $"{PrefabDir}/TaskCard_Grabbable_Polished.prefab", sb);
        }

        foreach (var cardView in UnityEngine.Object.FindObjectsByType<TaskCardView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            CleanCardHierarchy(cardView.transform);
        }
        sb.AppendLine("TaskBoard & TaskCards: upgraded to TaskBoard_KanbanFrame_Optimized, raised TASK BOARD header into top pediment, cleared corner token overlaps on cards");
    }

    static void CleanCardHierarchy(Transform card)
    {
        // Remove the oversized secondary badge sub-objects (`TaskBoard_OwnerIcon`, `TaskBoard_PriorityBadge`, `TaskBoard_StatusMarker`)
        // that were overlapping `SH_Role`, `SH_Priority`, and `SCRIPTED DEMO` in `02_task_board.png`!
        // `TaskCard_Grabbable` already has clean tucked corner indicators built into its bezel!
        foreach (var extraName in new[] { "OwnerIcon", "PriorityBadge", "StatusMarker", "TaskBoard_OwnerIcon", "TaskBoard_PriorityBadge", "TaskBoard_StatusMarker" })
        {
            var extra = card.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != card && t.name.Contains(extraName));
            if (extra != null) UnityEngine.Object.DestroyImmediate(extra.gameObject);
        }

        // Ensure high-contrast dark typography on the card labels
        const float z = -0.0055f;
        StyleCardLabel(card, "SH_Role",     new Vector3(-0.010f,  0.0185f, z), new Vector2(4.4f, 0.85f), 3.4f, 4.6f, new Color(0.98f, 0.96f, 0.92f), FontStyles.Bold,   TextAlignmentOptions.Left,   false);
        StyleCardLabel(card, "SH_Priority", new Vector3( 0.018f,  0.0185f, z), new Vector2(1.6f, 0.85f), 3.6f, 4.6f, new Color(0.99f, 0.92f, 0.65f), FontStyles.Bold,   TextAlignmentOptions.Right,  false);
        StyleCardLabel(card, "Title",       new Vector3( 0.000f,  0.0035f, z), new Vector2(6.8f, 1.65f), 4.4f, 6.8f, new Color(0.10f, 0.11f, 0.15f), FontStyles.Bold,   TextAlignmentOptions.Center, true);
        StyleCardLabel(card, "SH_State",    new Vector3( 0.000f, -0.0100f, z), new Vector2(5.4f, 0.80f), 3.2f, 4.8f, new Color(0.22f, 0.25f, 0.30f), FontStyles.Bold,   TextAlignmentOptions.Center, false);
        StyleCardLabel(card, "SH_Source",   new Vector3( 0.000f, -0.0180f, z), new Vector2(5.4f, 0.65f), 2.6f, 3.6f, new Color(0.72f, 0.30f, 0.24f), FontStyles.Bold,   TextAlignmentOptions.Center, false);
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
    // PRIORITY 4: SINGLE-MESH 3D POKE BUTTONS (CUTS ~64+ RENDERERS!)
    // =========================================================================

    static void ApplySingleMeshPokeButtons(StringBuilder sb)
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
            // Also disable the legacy flat quad MeshRenderer directly on ButtonVisual if present (saves 1 more renderer per key!)
            var legacyQuadRenderer = visual.GetComponent<MeshRenderer>();
            if (legacyQuadRenderer != null && legacyQuadRenderer.enabled)
            {
                legacyQuadRenderer.enabled = false;
                renderersSaved += 1;
            }

            // Create 1 single-mesh Housing3D
            var hGo = new GameObject("Housing3D");
            hGo.transform.SetParent(btn, false);
            hGo.transform.localPosition = Vector3.zero;
            hGo.transform.localRotation = Quaternion.identity;
            hGo.transform.localScale = Vector3.one;
            hGo.AddComponent<MeshFilter>().sharedMesh = housingMesh;
            var hr = hGo.AddComponent<MeshRenderer>();
            hr.sharedMaterial = housingMat;
            hr.shadowCastingMode = ShadowCastingMode.Off;
            hr.receiveShadows = false;

            // Create 1 single-mesh Cap3D
            string path = (btn.parent != null ? btn.parent.name + "/" : "") + btn.name;
            Material capMat = GetButtonCapMaterial(path);

            var cGo = new GameObject("Cap3D");
            cGo.transform.SetParent(visual, false);
            cGo.transform.localPosition = Vector3.zero;
            cGo.transform.localRotation = Quaternion.identity;
            cGo.transform.localScale = Vector3.one;
            cGo.AddComponent<MeshFilter>().sharedMesh = capMesh;
            var cr = cGo.AddComponent<MeshRenderer>();
            cr.sharedMaterial = capMat;
            cr.shadowCastingMode = ShadowCastingMode.Off;
            cr.receiveShadows = false;

            renderersSaved -= 2; // +2 new renderers (1 housing + 1 cap)
            buttonsUpgraded++;
        }

        sb.AppendLine($"3D Poke Keys: upgraded {buttonsUpgraded} buttons to single-mesh UI_PokeKey_Housing + UI_PokeKey_Cap (net {renderersSaved} renderers eliminated!)");
    }

    static Material GetButtonCapMaterial(string buttonPath)
    {
        string p = buttonPath.ToLowerInvariant().Replace("taskboard/", "").Replace("root/", "").Replace("phase6/", "");
        string matName = "Mat_CreamCeramic";
        if (p.Contains("approve")) matName = "Mat_Tester_Mint";
        else if (p.Contains("reject") || p.Contains("close")) matName = "Mat_Developer_Coral";
        else if (p.Contains("ask") || p.Contains("explain") || p.Contains("new")) matName = "Mat_Tester_SlateBlue";
        else if (p.Contains("focus")) matName = "Mat_Researcher_Lavender";
        else if (p.Contains("voice") || p.Contains("priority")) matName = "Mat_Developer_Amber";
        else if (p.Contains("details") || p.Contains("pause")) matName = "Mat_WarmBeige";

        return AssetDatabase.LoadAssetAtPath<Material>($"Assets/MuseDesk/Art/Materials/{matName}.mat")
            ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/MuseDesk/Art/Materials/Mat_CreamCeramic.mat");
    }

    // =========================================================================
    // PRIORITY 4 & 6: REFINED STAMPS & CLUTTER REDUCTION (PHYSICS TRAY REMOVAL)
    // =========================================================================

    static void ApplyStampsAndClutterReduction(StringBuilder sb)
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var ws = all.FirstOrDefault(t => t.name == "WorkspaceRoot");
        if (ws == null) return;

        // 1. Clutter Reduction (REMAINING_ISSUES.md #57):
        // Deactivate the oversized gray `PhysicsTray` and giant primitive `Prop_Mug` / `Prop_Block`
        // that cluttered the user's front-left next to the TaskBoard!
        foreach (var clutterName in new[] { "PhysicsTray", "Prop_Mug", "Prop_Block" })
        {
            var obj = all.FirstOrDefault(t => t != null && t.name == clutterName);
            if (obj != null)
            {
                obj.gameObject.SetActive(false);
                sb.AppendLine($"Clutter reduction: deactivated oversized '{clutterName}' on front-left desk");
            }
        }

        // 2. Place `Doc_StampRestPad` neatly on the right-front of the desk and reskin `StampApprove` & `StampReject`
        // with our ergonomic color-crowned `Doc_Stamp_Approve` and `Doc_Stamp_Reject` models!
        var restPadFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Documents/Doc_StampRestPad.fbx");
        if (restPadFbx != null)
        {
            var oldPad = ws.Find("Doc_StampRestPad");
            if (oldPad != null) UnityEngine.Object.DestroyImmediate(oldPad.gameObject);
            var pad = (GameObject)PrefabUtility.InstantiatePrefab(restPadFbx, ws);
            pad.name = "Doc_StampRestPad";
            pad.transform.localPosition = new Vector3(-0.20f, 0.002f, 0.38f);
            pad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            pad.transform.localScale = Vector3.one;
        }

        ReskinStampTool(ws, "StampApprove", "Assets/MuseDesk/Art/Models/Documents/Doc_Stamp_Approve.fbx", new Vector3(-0.228f, 0.011f, 0.38f));
        ReskinStampTool(ws, "StampReject",  "Assets/MuseDesk/Art/Models/Documents/Doc_Stamp_Reject.fbx",  new Vector3(-0.172f, 0.011f, 0.38f));
        sb.AppendLine("Stamps: reskinned StampApprove & StampReject with color-crowned models + front orientation marks on Doc_StampRestPad");
    }

    static void ReskinStampTool(Transform ws, string stampName, string fbxPath, Vector3 restPos)
    {
        var stampRoot = ws.Find(stampName);
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (stampRoot == null || fbx == null) return;

        stampRoot.localPosition = restPos;
        stampRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);

        foreach (var oldPart in new[] { "Handle", "Head", "StampVisual" })
        {
            var c = stampRoot.Find(oldPart);
            if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
        }

        var vis = (GameObject)PrefabUtility.InstantiatePrefab(fbx, stampRoot);
        vis.name = "StampVisual";
        vis.transform.localPosition = Vector3.zero;
        vis.transform.localRotation = Quaternion.identity;
        vis.transform.localScale = Vector3.one;

        var tool = stampRoot.GetComponent<StampTool>();
        if (tool != null)
        {
            tool.headRenderer = vis.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("Status"));
        }
    }

    // =========================================================================
    // PRIORITY 2, 5 & 8: STATION DROP-ZONE HALOS & OPTIMIZED WORKSTATIONS
    // =========================================================================

    static void ApplyStationAffordancesAndOptimizedWorkstations(StringBuilder sb)
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

        var haloFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Affordances/Affordance_AgentHaloRing.fbx");
        var padFbx  = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Affordances/Affordance_DeskReceivePad.fbx");

        var stationSpecs = new (string role, string optFbxPath, AssignmentVisualState demoState)[]
        {
            ("researcher", "Assets/MuseDesk/Art/Models/Optimized/Workstation_Research_Optimized.fbx",  AssignmentVisualState.ValidTarget),
            ("developer",  "Assets/MuseDesk/Art/Models/Optimized/Workstation_Developer_Optimized.fbx", AssignmentVisualState.ConfirmedAssignment),
            ("tester",     "Assets/MuseDesk/Art/Models/Optimized/Workstation_Testing_Optimized.fbx",   AssignmentVisualState.HoveredWorkstation),
        };

        foreach (var (role, optFbxPath, demoState) in stationSpecs)
        {
            // 1. Wire StationDropZone.halo (fixes REMAINING_ISSUES.md #47!)
            var dz = UnityEngine.Object.FindObjectsByType<StationDropZone>(FindObjectsInactive.Include, FindObjectsSortMode.None)
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
                    affView.receivePadStatusRenderer = padGo.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("StatusBar"));
                }

                affView.SetState(demoState);
                haloGo.SetActive(true); // Keep visible in preview scene so before/after comparison shows the halo rings!
                EditorUtility.SetDirty(dz);
            }

            // 2. Swap Workstation multi-part mesh (53-56 renderers) with Reversible Material-Welded Optimized Workstation (16-24 renderers!)
            var optFbx = AssetDatabase.LoadAssetAtPath<GameObject>(optFbxPath);
            if (optFbx == null) continue;

            foreach (var wsNode in all.Where(t => t != null && t.name == "Workstation" && t.parent != null && t.parent.name.ToLowerInvariant().Contains(role)).ToList())
            {
                var parent = wsNode.parent;
                var pos = wsNode.localPosition;
                var rot = wsNode.localRotation;
                var scl = wsNode.localScale;

                // Preserve any runtime children like Monitor TMP or FurnitureCollider
                var keepChildren = new List<Transform>();
                foreach (Transform ch in wsNode)
                {
                    if (ch.name == "Monitor" || ch.name == "FurnitureCollider" || ch.GetComponent<TMP_Text>() != null)
                        keepChildren.Add(ch);
                }
                foreach (var kc in keepChildren) kc.SetParent(parent, true);

                UnityEngine.Object.DestroyImmediate(wsNode.gameObject);

                var newWs = (GameObject)PrefabUtility.InstantiatePrefab(optFbx, parent);
                newWs.name = "Workstation";
                newWs.transform.localPosition = pos;
                newWs.transform.localRotation = rot;
                newWs.transform.localScale = scl;

                foreach (var kc in keepChildren) kc.SetParent(newWs.transform, true);
            }
        }

        sb.AppendLine("Workstations & DropZones: wired StationDropZone.halo + DeskReceivePad on all 3 stations, swapped to reversible *_Optimized workstation meshes");
    }

    // =========================================================================
    // PRIORITY 7: OPTIONAL MASCOT FEEDBACK BADGES ABOVE AGENTS
    // =========================================================================

    static void ApplyMascotFeedbackBadges(StringBuilder sb)
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

            var groupGo = BuildMascotFeedbackGroupObject("MascotFeedbackBadges");
            groupGo.transform.SetParent(agent, false);
            // Position above the mascot's head (+0.285m local Y) without touching the rig or obstructing the face
            groupGo.transform.localPosition = new Vector3(0.055f, 0.235f, -0.02f);
            groupGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            groupGo.transform.localScale = Vector3.one * 1.1f;

            var comp = groupGo.GetComponent<MascotFeedbackBadgeGroup>();
            comp.agentId = role;
            comp.SetFeedback(state);
        }

        sb.AppendLine("Mascot Feedback: attached optional 3D MascotFeedbackBadges above Researcher, Developer, and Tester without modifying character rigs");
    }

    static void BuildAffordanceAndFeedbackShowcaseStrip(StringBuilder sb)
    {
        var ws = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name == "WorkspaceRoot");
        if (ws == null) return;

        var oldStrip = ws.Find("VisualPolish_GalleryStrip");
        if (oldStrip != null) UnityEngine.Object.DestroyImmediate(oldStrip.gameObject);

        var strip = new GameObject("VisualPolish_GalleryStrip");
        strip.transform.SetParent(ws, false);
        strip.transform.localPosition = new Vector3(-0.38f, 0.002f, 0.36f);
        strip.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        // Place a Small Decision Paper next to the Stamp Rest Pad so both Small and Expanded Decision Papers are visible in the preview scene!
        var smallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/DecisionPaper_Small_Polished.prefab");
        if (smallPrefab != null)
        {
            var sm = (GameObject)PrefabUtility.InstantiatePrefab(smallPrefab, strip.transform);
            sm.name = "DecisionPaper_Small_Showcase";
            sm.transform.localPosition = new Vector3(0f, 0.004f, 0f);
            sm.transform.localRotation = Quaternion.Euler(78f, 0f, -8f);
            sm.transform.localScale = Vector3.one;
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

        var ws = all.FirstOrDefault(t => t.name == "WorkspaceRoot");
        var doc = all.FirstOrDefault(t => t.name == "DecisionDocument");
        var tb = all.FirstOrDefault(t => t.name == "TaskBoard");

        // Ensure WorkspaceRoot and DecisionDocument are active for visual inspection captures
        bool docWasActive = doc != null && doc.gameObject.activeSelf;
        if (doc != null)
        {
            doc.gameObject.SetActive(true);
            // Populate sample decision content so the screenshot shows real typography & stamp mark
            var panel = doc.GetComponentInChildren<DecisionPanel>(true);
            if (panel != null)
            {
                if (panel.badge) panel.badge.text = "[BLOCKING]  [DEMO]";
                if (panel.title) panel.title.text = "Deploy auth service to staging?";
                if (panel.summary) panel.summary.text = "OAuth login flow passed 12/12 tests.";
                if (panel.recommendation) panel.recommendation.text = "Recommended: APPROVE";
                if (panel.body) panel.body.text = "Why: All 12 integration checks succeeded.\nOptions: Deploy now (fast) • Hold for load test (safe)\nRisk: Low (staging only)\nImpact: Unblocks QA verification.";
                if (panel.answer) panel.answer.text = "“Staging matches prod config and rollback takes <10s.”";
                if (panel.detailsRoot) panel.detailsRoot.SetActive(true);
                if (panel.actionsRoot) panel.actionsRoot.SetActive(true);
            }
            var mark = doc.Find("StampMark");
            if (mark != null) mark.gameObject.SetActive(true);
        }

        // Populate sample cards on the TaskBoard if Play mode isn't active so 02_task_board comparison has live cards!
        var tempCards = new List<GameObject>();
        var cardTemplate = all.FirstOrDefault(t => t.name == "CardTemplate");
        if (tb != null && cardTemplate != null)
        {
            var sampleSpecs = new (string colName, int slot, string role, string prio, string title, string state, string src)[]
            {
                ("Col_Backlog",   0, "Unassigned", "P2", "Write the login tests",                "Queued",   "SCRIPTED DEMO"),
                ("Col_Backlog",   1, "Researcher", "P1", "Investigate flaky token refresh",      "Blocked",  "SCRIPTED DEMO"),
                ("Col_Working",   0, "Developer",  "P3", "Implement OAuth scaffold",             "Running",  "LIVE"),
                ("Col_NeedsMe",   0, "Researcher", "P3", "Approve auth approach",                "Needs me", "LIVE"),
                ("Col_Completed", 0, "Tester",     "P0", "Baseline test run",                    "Completed","SCRIPTED DEMO"),
            };
            foreach (var (colName, slot, role, prio, title, state, src) in sampleSpecs)
            {
                var col = tb.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == colName);
                if (col == null) continue;
                var c = UnityEngine.Object.Instantiate(cardTemplate.gameObject, col);
                c.SetActive(true);
                c.transform.localPosition = new Vector3(0f, -slot * 0.088f, -0.006f);
                c.transform.localRotation = Quaternion.identity;
                c.transform.localScale = Vector3.one * 1.5f;
                SetCardText(c.transform, "SH_Role", role);
                SetCardText(c.transform, "SH_Priority", prio);
                SetCardText(c.transform, "Title", title);
                SetCardText(c.transform, "SH_State", state);
                SetCardText(c.transform, "SH_Source", src);
                tempCards.Add(c);
            }
        }

        Vector3 wsPos = ws != null ? ws.position : new Vector3(0f, 0.70f, 0.62f);

        // 1. Seated Overview (matches 01_seated_view.png)
        if (doc != null)
        {
            // Park DecisionDocument slightly to the upper-left for the seated overview so all 3 stations & halos are clear
            doc.transform.position = wsPos + new Vector3(0.0f, 0.36f, 0.18f);
            doc.transform.rotation = Quaternion.Euler(8f, 180f, 0f);
        }
        RenderCameraToPng(
            wsPos + new Vector3(0.00f, 0.42f, 0.72f),
            wsPos + new Vector3(0.00f, 0.10f, 0.02f),
            48f, Path.Combine(ScreenshotDir, "After_01_SeatedOverview.png"), sb);

        // 2. Task Board Closeup (matches 02_task_board.png)
        if (tb != null)
        {
            Vector3 tbPos = tb.position;
            Vector3 tbFwd = tb.forward; // Board front is -tb.forward (toward +Z)
            RenderCameraToPng(
                tbPos - tbFwd * 0.56f + Vector3.up * 0.01f,
                tbPos + Vector3.up * 0.00f,
                45f, Path.Combine(ScreenshotDir, "After_02_TaskBoard.png"), sb);

            // 4. Board 3D Buttons Closeup (matches 04_board_buttons_3d.png)
            Vector3 btnFocus = tbPos + new Vector3(-0.10f, -0.19f, 0.0f);
            RenderCameraToPng(
                btnFocus - tbFwd * 0.25f + Vector3.up * 0.01f,
                btnFocus,
                42f, Path.Combine(ScreenshotDir, "After_04_BoardButtons3D.png"), sb);
        }

        // 3. Decision Paper Closeup (matches 03_decision_paper.png)
        if (doc != null)
        {
            doc.transform.position = wsPos + new Vector3(0.0f, 0.28f, 0.22f);
            doc.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Vector3 docPos = doc.transform.position;
            RenderCameraToPng(
                docPos + new Vector3(0.0f, 0.005f, 0.44f),
                docPos + new Vector3(0.0f, 0.005f, 0.0f),
                44f, Path.Combine(ScreenshotDir, "After_03_DecisionPaper.png"), sb);
        }

        // 5. Desk Stamps, Small Decision Paper, Halo Rings & Mascot Feedback Closeup
        RenderCameraToPng(
            wsPos + new Vector3(-0.22f, 0.26f, 0.54f),
            wsPos + new Vector3(-0.18f, 0.08f, 0.12f),
            45f, Path.Combine(ScreenshotDir, "After_05_StampsAndAffordances.png"), sb);

        // Clean up temporary preview cards and restore DecisionDocument state
        foreach (var tc in tempCards)
            if (tc != null) UnityEngine.Object.DestroyImmediate(tc);
        if (doc != null)
        {
            var mark = doc.Find("StampMark");
            if (mark != null) mark.gameObject.SetActive(false);
            doc.gameObject.SetActive(docWasActive);
        }
    }

    static void SetCardText(Transform card, string childName, string text)
    {
        var t = card.Find(childName);
        var tmp = t != null ? t.GetComponent<TMP_Text>() : null;
        if (tmp != null) tmp.text = text;
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

            int w = 1440, h = 960;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
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

    static void SaveModelAsPrefab(string fbxPath, string prefabPath, StringBuilder sb)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbx == null) return;
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        inst.name = Path.GetFileNameWithoutExtension(prefabPath);
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
