using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Idempotent scene + art polish builder addressing all 10 Quest 3 on-head feedback items:
///   1. Stamps: replaces primitive StampApprove/StampReject handles & heads with Doc_Stamp_Approve.fbx,
///      Doc_Stamp_Reject.fbx, and Doc_StampRestPad.fbx cleanly seated at the front desk.
///   2. Touch Highlights: converts Hover_Frame, Hover_Ring, and DropZone_Halo to URP Unlit Transparent
///      with subtle alpha (0.22-0.36) and updates GrabHoverHighlight / StationDropZone colors.
///   3. Agent-Carried Papers & Tokens: sets compact HeldPaperController.heldScale (0.18) and
///      reparents Collab_ArtifactToken out of the 100x rig bone so carried items stay miniature (~3-6cm).
///   4. Button Overlaps: separates FiledKey off AssignDock/Dock_Developer, moves ExplainToggle off
///      StartKey, separates TipsKey from ArtifactPanel/ClosePoke, compacts CardTemplate card keys,
///      and positions Btn_Voice cleanly below the 5 action buttons on DecisionDocument.
///   5. Floating Button Faces: installs PokeVisualRestGuard on every ButtonVisual so button faces
///      always return flush to their base after release & resync on scale changes.
///   6. Grabbable Physics Props: adds Grabbable + OneGrabFreeTransformer + HandGrabInteractable +
///      DistanceHandGrabInteractable + GrabbableRigidbody + subtle GrabHoverHighlight to all 11
///      PushableProps under Phase6 and OfficeProps_Collection.
///   7. Task Assignment: ensures CardTemplate has explicit OneGrabFreeTransformer, clean BoxCollider,
///      generous snapRadius (0.15), and Phase2Bootstrap registers all 6 StationDropZones.
///   8. Stability: disables Locomotor (FirstPersonLocomotor/CharacterController gravity) and disables
///      autoReplaceOnRecenter on WorkspacePlacement so the diorama never falls or keeps jumping.
///   9. Tactile 3D Button Assets: upgrades every Cap3D to UI_PokeKey_Cap.fbx (KeyCap_Body), points
///      InteractableColorVisual._editor directly to Cap3D's MaterialPropertyBlockEditor, and hides
///      the flat ButtonVisual Quad MeshRenderer.
///  10. Voice + Agent Physical Touch/Tilt/Arm Push: enables Btn_Voice on DecisionDocument, wires
///      VoiceDecisionController.panel, enables OVRManager microphone startup prompt, and wires
///      AgentTilt (Chest/Spine) + AgentArmNudge (UpperArm_L/UpperArm_R + scale-compensated trigger
///      capsules) onto all 3 rigged agents.
/// </summary>
public static class ApplyFeedbackFixes
{
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
        .FirstOrDefault(t => t.Name == name);

    static GameObject[] _all;

    static void RefreshAll(UnityEngine.SceneManagement.Scene scene)
    {
        _all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToArray();
    }

    static GameObject Find(string name) => _all.FirstOrDefault(g => g != null && g.name == name);

    static TC GetOrAdd<TC>(GameObject go) where TC : Component
    {
        var c = go.GetComponent<TC>();
        return c != null ? c : go.AddComponent<TC>();
    }

    static Component GetOrAdd(GameObject go, Type t)
    {
        var c = go.GetComponent(t);
        return c != null ? c : go.AddComponent(t);
    }

    public static string Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();
        RefreshAll(scene);

        var matMap = BuildMaterialMap();

        // 1. Stability: disable Locomotor + lock placement against re-adjusting
        FixRigAndPlacement(sb);

        // 2. Carried paper & artifact token scale
        FixCarriedPaperAndToken(sb);

        // 3. Stamps reskin with Doc_Stamp_Approve, Doc_Stamp_Reject & Doc_StampRestPad
        FixStamps(matMap, sb);

        // 4. Transparent highlights & drop halos
        RefreshAll(scene);
        FixHighlightsAndHalos(sb);

        // 5. Voice button & controller wiring
        FixVoice(sb);

        // 6. Button overlaps + beveled 3D KeyCap_Body upgrade + PokeVisualRestGuard
        RefreshAll(scene);
        FixButtonsAndOverlaps(sb);

        // 7. Make all PushableProps grabbable + hover-highlighted
        RefreshAll(scene);
        MakeAllPhysicsPropsGrabbable(sb);

        // 8. CardTemplate grab transformer + collider + Phase2Bootstrap 6 dropZones
        RefreshAll(scene);
        FixTaskAssignment(sb);

        // 9. Interactive Agent Tilt + Arm Nudge on all 3 rigged agents
        RefreshAll(scene);
        WireInteractiveAgents(sb);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("APPLY FEEDBACK FIXES COMPLETE.");
        return sb.ToString();
    }

    static Dictionary<string, Material> BuildMaterialMap()
    {
        var map = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/MuseDesk/Art/Materials" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null && !map.ContainsKey(m.name)) map[m.name] = m;
        }
        return map;
    }

    static void ApplyMaterials(GameObject root, Dictionary<string, Material> matMap)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string key = mats[i].name.Replace(" (Instance)", "");
                if (matMap.TryGetValue(key, out var replacement))
                {
                    mats[i] = replacement;
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    static void FixRigAndPlacement(StringBuilder sb)
    {
        var loc = Find("Locomotor");
        if (loc != null)
        {
            loc.SetActive(false);
            foreach (var b in loc.GetComponentsInChildren<Behaviour>(true))
                if (b.GetType().Name.Contains("Locomotor")) b.enabled = false;
            foreach (var cc in loc.GetComponentsInChildren<CharacterController>(true))
                cc.enabled = false;
            EditorUtility.SetDirty(loc);
            sb.AppendLine("Disabled Locomotor / FirstPersonLocomotor / CharacterController (stops infinite falling)");
        }

        var services = Find("AppServices");
        if (services != null)
        {
            var wp = services.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "WorkspacePlacement");
            if (wp != null)
            {
                var so = new SerializedObject(wp);
                var pAuto = so.FindProperty("autoReplaceOnRecenter");
                if (pAuto != null) pAuto.boolValue = false;
                var pSettle = so.FindProperty("settleTimeout");
                if (pSettle != null) pSettle.floatValue = 4.0f;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(wp);
                sb.AppendLine("WorkspacePlacement: autoReplaceOnRecenter=false, settleTimeout=4.0s");
            }
        }

        var rig = Find("[BuildingBlock] Camera Rig");
        if (rig != null)
        {
            var ovr = rig.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "OVRManager");
            if (ovr != null)
            {
                var so = new SerializedObject(ovr);
                var pMic = so.FindProperty("requestRecordAudioPermissionOnStartup");
                if (pMic != null) pMic.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(ovr);
                sb.AppendLine("OVRManager: requestRecordAudioPermissionOnStartup=true");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    static void FixCarriedPaperAndToken(StringBuilder sb)
    {
        var doc = Find("DecisionDocument");
        if (doc != null)
        {
            var hpc = doc.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "HeldPaperController");
            if (hpc != null)
            {
                var so = new SerializedObject(hpc);
                var hs = so.FindProperty("heldScale");
                if (hs != null) hs.floatValue = 0.18f;
                var ex = so.FindProperty("expandSeconds");
                if (ex != null) ex.floatValue = 0.35f;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(hpc);
                sb.AppendLine("HeldPaperController: heldScale=0.18, expandSeconds=0.35 (with 100x bone compensation in code)");
            }
        }

        var tok = Find("Collab_ArtifactToken");
        var services = Find("AppServices");
        if (tok != null && services != null)
        {
            tok.transform.SetParent(services.transform, false);
            tok.transform.localPosition = Vector3.zero;
            tok.transform.localRotation = Quaternion.identity;
            tok.transform.localScale = Vector3.one;
            tok.SetActive(false);
            EditorUtility.SetDirty(tok);
            sb.AppendLine("Collab_ArtifactToken: reparented to AppServices at scale (1,1,1) (~3.6cm real size, inactive until handoff)");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    static void FixStamps(Dictionary<string, Material> matMap, StringBuilder sb)
    {
        var ws = Find("WorkspaceRoot")?.transform;
        if (ws == null) return;

        // Place Doc_StampRestPad neatly at front center; move GuideSign slightly behind it so zero overlap.
        var restPadFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Documents/Doc_StampRestPad.fbx");
        if (restPadFbx != null)
        {
            var oldPad = ws.Find("Doc_StampRestPad");
            if (oldPad != null) UnityEngine.Object.DestroyImmediate(oldPad.gameObject);
            var pad = (GameObject)PrefabUtility.InstantiatePrefab(restPadFbx, ws);
            pad.name = "Doc_StampRestPad";
            pad.transform.localPosition = new Vector3(0.00f, 0.002f, 0.415f);
            pad.transform.localRotation = Quaternion.identity;
            pad.transform.localScale = Vector3.one * 1.15f;
            ApplyMaterials(pad, matMap);
            EditorUtility.SetDirty(pad);
        }

        var guide = ws.Find("GuideSign");
        if (guide != null)
        {
            guide.localPosition = new Vector3(0.00f, 0.105f, 0.345f);
            EditorUtility.SetDirty(guide);
        }

        ReskinOneStamp(ws, "StampApprove", "Assets/MuseDesk/Art/Models/Documents/Doc_Stamp_Approve.fbx",
            new Vector3(-0.036f, 0.014f, 0.415f), matMap, "Mat_StatusGreen");
        ReskinOneStamp(ws, "StampReject", "Assets/MuseDesk/Art/Models/Documents/Doc_Stamp_Reject.fbx",
            new Vector3(0.036f, 0.014f, 0.415f), matMap, "Mat_StatusRed");
        sb.AppendLine("Stamps: reskinned StampApprove & StampReject with Doc_Stamp_* + Doc_StampRestPad at front center");
    }

    static void ReskinOneStamp(Transform ws, string stampName, string fbxPath, Vector3 restPos,
        Dictionary<string, Material> matMap, string crownMatFallback)
    {
        var root = ws.Find(stampName);
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (root == null || fbx == null) return;

        root.localPosition = restPos;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;
        int fcLayer = LayerMask.NameToLayer("FingerCollide");
        if (fcLayer >= 0) SetLayerRecursive(root.gameObject, fcLayer);

        foreach (var oldPart in new[] { "Handle", "Head", "StampVisual" })
        {
            var c = root.Find(oldPart);
            if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
        }

        var vis = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root);
        vis.name = "StampVisual";
        vis.transform.localPosition = new Vector3(0f, 0.002f, 0f);
        vis.transform.localRotation = Quaternion.identity;
        vis.transform.localScale = Vector3.one * 1.15f;
        ApplyMaterials(vis, matMap);

        var inkHead = root.Find("InkHead");
        if (inkHead != null) inkHead.localPosition = new Vector3(0f, -0.004f, 0f);

        var bc = root.GetComponent<BoxCollider>();
        if (bc != null)
        {
            bc.center = new Vector3(0f, 0.025f, 0f);
            bc.size = new Vector3(0.048f, 0.068f, 0.048f);
        }

        var rends = vis.GetComponentsInChildren<Renderer>(true);
        var crown = rends.FirstOrDefault(r => r.name.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0)
            ?? rends.FirstOrDefault(r => r.name.IndexOf("Mint", StringComparison.OrdinalIgnoreCase) >= 0)
            ?? rends.FirstOrDefault();
        if (crown != null && crown.sharedMaterial == null && matMap.TryGetValue(crownMatFallback, out var m))
            crown.sharedMaterial = m;

        var tool = root.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "StampTool");
        if (tool != null && crown != null)
        {
            var so = new SerializedObject(tool);
            so.FindProperty("headRenderer").objectReferenceValue = crown;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tool);
        }

        var ghh = root.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "GrabHoverHighlight");
        if (ghh != null)
        {
            var so = new SerializedObject(ghh);
            var arr = so.FindProperty("brighten");
            if (arr != null)
            {
                arr.arraySize = rends.Length;
                for (int i = 0; i < rends.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = rends[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ghh);
        }
        EditorUtility.SetDirty(root);
    }

    // ─────────────────────────────────────────────────────────────────────────
    static void FixHighlightsAndHalos(StringBuilder sb)
    {
        int matsConverted = 0;
        foreach (var path in new[]
        {
            "Assets/MuseDesk/Art/Materials/UI/Hover_Frame.mat",
            "Assets/MuseDesk/Art/Materials/UI/Hover_Ring.mat",
            "Assets/MuseDesk/Art/Materials/UI/DropZone_Halo.mat"
        })
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;
            MakeMaterialTransparent(mat, path.Contains("DropZone") ? 0.25f : 0.22f);
            matsConverted++;
        }

        int ghhCount = 0;
        foreach (var ghh in _all.SelectMany(g => g.GetComponents<MonoBehaviour>()).Where(m => m != null && m.GetType().Name == "GrabHoverHighlight"))
        {
            var so = new SerializedObject(ghh);
            so.FindProperty("hoverColor").colorValue = new Color(0.35f, 0.85f, 0.95f, 0.22f);
            so.FindProperty("heldColor").colorValue = new Color(0.98f, 0.82f, 0.30f, 0.36f);
            so.FindProperty("hoverBrightness").floatValue = 1.15f;
            var ind = so.FindProperty("indicator").objectReferenceValue as GameObject;
            var r = ind != null ? ind.GetComponent<Renderer>() : null;
            if (r != null && r.sharedMaterial != null) MakeMaterialTransparent(r.sharedMaterial, 0.22f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ghh);
            ghhCount++;
        }

        int sdzCount = 0;
        foreach (var sdz in _all.SelectMany(g => g.GetComponents<MonoBehaviour>()).Where(m => m != null && m.GetType().Name == "StationDropZone"))
        {
            var so = new SerializedObject(sdz);
            so.FindProperty("validColor").colorValue = new Color(0.30f, 0.80f, 0.45f, 0.24f);
            so.FindProperty("invalidColor").colorValue = new Color(0.85f, 0.35f, 0.30f, 0.26f);
            var halo = so.FindProperty("halo").objectReferenceValue as GameObject;
            var r = halo != null ? halo.GetComponent<Renderer>() : null;
            if (r != null && r.sharedMaterial != null) MakeMaterialTransparent(r.sharedMaterial, 0.24f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(sdz);
            sdzCount++;
        }
        sb.AppendLine($"Highlights/Halos: converted {matsConverted} UI mats to URP Transparent, updated {ghhCount} GrabHoverHighlights + {sdzCount} StationDropZones");
    }

    static void MakeMaterialTransparent(Material mat, float alpha)
    {
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit != null && mat.shader != unlit) mat.shader = unlit;
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        if (mat.HasProperty("_BaseColor"))
        {
            var c = mat.GetColor("_BaseColor");
            c.a = Mathf.Min(c.a, alpha);
            mat.SetColor("_BaseColor", c);
        }
        if (mat.HasProperty("_Color"))
        {
            var c = mat.GetColor("_Color");
            c.a = Mathf.Min(c.a, alpha);
            mat.SetColor("_Color", c);
        }
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
    }

    // ─────────────────────────────────────────────────────────────────────────
    static void FixVoice(StringBuilder sb)
    {
        var doc = Find("DecisionDocument");
        var services = Find("AppServices");
        if (doc != null && services != null)
        {
            var vdc = services.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "VoiceDecisionController");
            var panel = doc.GetComponentInChildren<MonoBehaviour>(true);
            var dp = doc.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(m => m != null && m.GetType().Name == "DecisionPanel");
            if (vdc != null)
            {
                var so = new SerializedObject(vdc);
                so.FindProperty("voiceEnabled").boolValue = true;
                so.FindProperty("requireLiveMode").boolValue = false;
                if (dp != null) so.FindProperty("panel").objectReferenceValue = dp;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(vdc);
            }

            var actions = doc.transform.Find("Actions");
            var voiceBtn = actions != null ? actions.Find("Btn_Voice") : null;
            if (voiceBtn != null)
            {
                voiceBtn.gameObject.SetActive(true);
                voiceBtn.localPosition = new Vector3(0f, -0.194f, -0.004f);
                voiceBtn.localRotation = Quaternion.identity;
                voiceBtn.localScale = Vector3.one * 0.60f;
                // Deduplicate extra TMP Label child if one is empty
                var labels = voiceBtn.GetComponentsInChildren<TMP_Text>(true);
                if (labels.Length > 1)
                {
                    foreach (var l in labels.Skip(1))
                        if (string.IsNullOrEmpty(l.text)) UnityEngine.Object.DestroyImmediate(l.gameObject);
                }
                if (labels.Length > 0 && labels[0] != null) labels[0].text = "Voice";
                EditorUtility.SetDirty(voiceBtn.gameObject);
                sb.AppendLine("Voice: enabled Btn_Voice at bottom-center of DecisionDocument & wired VoiceDecisionController.panel");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    static void FixButtonsAndOverlaps(StringBuilder sb)
    {
        // 1. Reposition overlapping Phase6 & ArtifactPanel keys with generous clearance (>= 8.5cm pitch)
        var p6 = Find("Phase6")?.transform;
        if (p6 != null)
        {
            // Smooth front-left ergonomic arc + symmetric front-right FiledKey (completely clear of AssignDock!)
            var ws = Find("WorkspaceRoot")?.transform;
            Vector3 lookTarget = new Vector3(0f, 0.35f, 1.0f);
            PlaceButtonOnArc(p6.Find("TipsKey"),       new Vector3(-0.540f, 0.055f, 0.335f), lookTarget);
            PlaceButtonOnArc(p6.Find("FocusToggle"),   new Vector3(-0.435f, 0.055f, 0.400f), lookTarget);
            PlaceButtonOnArc(p6.Find("StartKey"),      new Vector3(-0.325f, 0.055f, 0.440f), lookTarget);
            PlaceButtonOnArc(p6.Find("ReplayKey"),     new Vector3(-0.215f, 0.055f, 0.460f), lookTarget);
            PlaceButtonOnArc(p6.Find("FiledKey"),      new Vector3( 0.215f, 0.055f, 0.460f), lookTarget);
            PlaceButtonOnArc(p6.Find("ExplainToggle"), new Vector3(-0.325f, 0.055f, 0.540f), lookTarget);
        }

        // Lift ArtifactPanel slightly higher and forward-clear so ClosePoke is >= 18cm away from TipsKey
        var ap = Find("ArtifactPanel");
        if (ap != null)
        {
            ap.transform.localPosition = new Vector3(-0.520f, 0.440f, 0.210f);
            EditorUtility.SetDirty(ap);
        }

        // Separate CardTemplate's two mini poke buttons cleanly along the right card margin
        var ct = Find("CardTemplate")?.transform;
        if (ct != null)
        {
            var p1 = ct.Find("SH_PokePauseResume");
            var p2 = ct.Find("SH_PokePriority");
            if (p1 != null)
            {
                p1.localPosition = new Vector3(0.052f, 0.015f, -0.004f);
                p1.localScale = Vector3.one * 0.018f;
                EditorUtility.SetDirty(p1.gameObject);
            }
            if (p2 != null)
            {
                p2.localPosition = new Vector3(0.052f, -0.015f, -0.004f);
                p2.localScale = Vector3.one * 0.018f;
                EditorUtility.SetDirty(p2.gameObject);
            }
        }

        // 2. Upgrade ALL PokeInteractable buttons with UI_PokeKey_Cap.fbx + PokeVisualRestGuard
        var capFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/UI/UI_PokeKey_Cap.fbx");
        var housingFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/UI/UI_PokeKey_Housing.fbx");
        Mesh capMesh = capFbx != null ? capFbx.GetComponentInChildren<MeshFilter>(true)?.sharedMesh : null;
        Mesh housingMesh = housingFbx != null ? housingFbx.GetComponentInChildren<MeshFilter>(true)?.sharedMesh : null;
        var guardType = T("PokeVisualRestGuard");
        var mpbeType = T("MaterialPropertyBlockEditor");
        var icvType = T("InteractableColorVisual");

        int upgraded = 0;
        foreach (var pi in _all.SelectMany(g => g.GetComponents<MonoBehaviour>()).Where(m => m != null && m.GetType().Name == "PokeInteractable"))
        {
            var btnRoot = pi.transform;
            var pokeVis = btnRoot.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(m => m != null && m.GetType().Name == "PokeInteractableVisual");
            if (pokeVis == null) continue;

            var bv = pokeVis.transform;
            // Ensure consistent resting offset along local -Z
            bv.localPosition = new Vector3(0f, 0f, -0.300f);
            bv.localRotation = Quaternion.identity;
            bv.localScale = Vector3.one;

            // Hide harsh raw Quad mesh on ButtonVisual so beveled Cap3D IS the visible keycap!
            var quadMr = bv.GetComponent<MeshRenderer>();
            Material capTintSourceMat = null;
            if (quadMr != null)
            {
                capTintSourceMat = quadMr.sharedMaterial;
                quadMr.enabled = false;
            }

            // Upgrade Cap3D to crisp beveled KeyCap_Body mesh
            var capTf = bv.Find("Cap3D");
            if (capTf != null && capMesh != null)
            {
                var capMf = capTf.GetComponent<MeshFilter>();
                if (capMf != null) capMf.sharedMesh = capMesh;
                capTf.localPosition = new Vector3(0f, 0f, 0.142f);
                capTf.localRotation = Quaternion.identity;
                capTf.localScale = Vector3.one;   // KeyCap_Body is already (0.98, 0.98, 0.276)

                // Let InteractableColorVisual drive Cap3D's material color on hover/press!
                var icv = bv.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "InteractableColorVisual");
                if (icv != null && mpbeType != null)
                {
                    var capEditor = capTf.GetComponent(mpbeType);
                    if (capEditor == null) capEditor = capTf.gameObject.AddComponent(mpbeType);
                    var capMr = capTf.GetComponent<MeshRenderer>();
                    if (capMr != null)
                    {
                        var edSo = new SerializedObject(capEditor);
                        var pRend = edSo.FindProperty("_renderers");
                        if (pRend != null)
                        {
                            pRend.arraySize = 1;
                            pRend.GetArrayElementAtIndex(0).objectReferenceValue = capMr;
                        }
                        edSo.ApplyModifiedPropertiesWithoutUndo();
                    }
                    var icvSo = new SerializedObject(icv);
                    var pEd = icvSo.FindProperty("_editor");
                    if (pEd != null) pEd.objectReferenceValue = capEditor;
                    // Also make NormalColorState match the cap's material color so it doesn't wash out!
                    var capMat = capTf.GetComponent<MeshRenderer>()?.sharedMaterial;
                    if (capMat != null && capMat.HasProperty("_BaseColor"))
                    {
                        Color baseC = capMat.GetColor("_BaseColor");
                        SetIcvColor(icvSo, "_normalColorState", baseC);
                        SetIcvColor(icvSo, "_hoverColorState", Color.Lerp(baseC, Color.white, 0.25f));
                        SetIcvColor(icvSo, "_selectColorState", Color.Lerp(baseC, Color.black, 0.15f));
                    }
                    icvSo.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // Ensure Housing3D uses crisp Housing_CharcoalVisor mesh
            var housingTf = btnRoot.Find("Housing3D");
            if (housingTf != null && housingMesh != null)
            {
                var hMf = housingTf.GetComponent<MeshFilter>();
                if (hMf != null) hMf.sharedMesh = housingMesh;
                housingTf.localPosition = new Vector3(0f, 0f, -0.1648f);
                housingTf.localRotation = Quaternion.identity;
                housingTf.localScale = Vector3.one;
            }

            // Pull label neatly in front of KeyCap_Body front surface (-0.300 + 0.142 - 0.138 = -0.296; so local lp.z = -0.020 on ButtonVisual -> -0.320)
            foreach (var lbl in bv.GetComponentsInChildren<TMP_Text>(true))
            {
                var p = lbl.transform.localPosition;
                p.z = -0.025f;
                lbl.transform.localPosition = p;
            }

            // Add / update PokeVisualRestGuard on ButtonVisual
            if (guardType != null)
            {
                var guard = bv.GetComponent(guardType);
                if (guard == null) guard = bv.gameObject.AddComponent(guardType);
                var gSo = new SerializedObject(guard);
                gSo.FindProperty("restLocalPosition").vector3Value = new Vector3(0f, 0f, -0.300f);
                gSo.FindProperty("hasAuthoredRest").boolValue = true;
                gSo.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(btnRoot.gameObject);
            upgraded++;
        }
        sb.AppendLine($"Buttons: separated Phase6/ArtifactPanel/CardTemplate overlaps & upgraded {upgraded} buttons to KeyCap_Body + PokeVisualRestGuard");
    }

    static void SetIcvColor(SerializedObject icvSo, string stateName, Color col)
    {
        var st = icvSo.FindProperty(stateName);
        if (st == null) return;
        var pCol = st.FindPropertyRelative("Color");
        if (pCol != null) pCol.colorValue = col;
    }

    static void PlaceButtonOnArc(Transform t, Vector3 localPos, Vector3 localLookTarget)
    {
        if (t == null) return;
        t.localPosition = localPos;
        Vector3 dir = localPos - localLookTarget;
        dir.y = 0f;
        if (dir.sqrMagnitude > 1e-4f)
        {
            // Button +Z points into surface, -Z faces user; dir = button - user points AWAY from user, so +Z = dir!
            t.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
        EditorUtility.SetDirty(t.gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────────
    static void MakeAllPhysicsPropsGrabbable(StringBuilder sb)
    {
        var grabbableType = T("Grabbable");
        var oneGrabType = T("OneGrabFreeTransformer");
        var hgiType = T("HandGrabInteractable");
        var dgiType = T("DistanceHandGrabInteractable");
        var grabRbType = T("GrabbableRigidbody");
        var reregType = T("ReRegisterInteractable");
        var ghhType = T("GrabHoverHighlight");
        var pushType = T("PushableProp");

        if (grabbableType == null || hgiType == null || grabRbType == null)
        {
            sb.AppendLine("WARN: ISDK Grab types missing; skipped PushableProps");
            return;
        }

        // Copy grab rules from Prop_Block if available so GrabbingRule matches ISDK defaults cleanly
        var propBlock = Find("Prop_Block");
        var templateHgi = propBlock != null ? propBlock.GetComponent(hgiType) : null;

        int madeGrabbable = 0;
        var pushProps = _all.SelectMany(g => g.GetComponents<MonoBehaviour>())
            .Where(m => m != null && m.GetType() == pushType)
            .Select(m => m.gameObject).Distinct().ToArray();

        foreach (var go in pushProps)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) continue;

            // Ensure Collider exists sized cleanly to mesh bounds
            var col = go.GetComponent<Collider>();
            if (col == null)
            {
                var bc = go.AddComponent<BoxCollider>();
                var rends = go.GetComponentsInChildren<Renderer>(true);
                if (rends.Length > 0)
                {
                    Bounds wb = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) wb.Encapsulate(rends[i].bounds);
                    bc.center = go.transform.InverseTransformPoint(wb.center);
                    Vector3 s = go.transform.lossyScale;
                    bc.size = new Vector3(
                        Mathf.Max(0.025f, wb.size.x / Mathf.Max(1e-4f, Mathf.Abs(s.x))),
                        Mathf.Max(0.025f, wb.size.y / Mathf.Max(1e-4f, Mathf.Abs(s.y))),
                        Mathf.Max(0.025f, wb.size.z / Mathf.Max(1e-4f, Mathf.Abs(s.z))));
                }
                else
                {
                    bc.size = Vector3.one * 0.04f;
                }
                bc.isTrigger = false;
            }

            var oneGrab = oneGrabType != null ? GetOrAdd(go, oneGrabType) : null;

            var grab = go.GetComponent(grabbableType);
            if (grab == null) grab = go.AddComponent(grabbableType);
            {
                var gSo = new SerializedObject(grab);
                var pRb = gSo.FindProperty("_rigidbody");
                if (pRb != null) pRb.objectReferenceValue = rb;
                var pOne = gSo.FindProperty("_oneGrabTransformer");
                if (pOne != null && oneGrab != null) pOne.objectReferenceValue = oneGrab;
                var pKin = gSo.FindProperty("_kinematicWhileSelected");
                if (pKin != null) pKin.boolValue = true;
                gSo.ApplyModifiedPropertiesWithoutUndo();
            }

            var hgi = go.GetComponent(hgiType);
            if (hgi == null) hgi = go.AddComponent(hgiType);
            if (templateHgi != null) EditorUtility.CopySerialized(templateHgi, hgi);
            {
                var hSo = new SerializedObject(hgi);
                hSo.FindProperty("_rigidbody").objectReferenceValue = rb;
                hSo.FindProperty("_pointableElement").objectReferenceValue = grab;
                hSo.ApplyModifiedPropertiesWithoutUndo();
            }

            if (dgiType != null)
            {
                var dgi = go.GetComponent(dgiType);
                if (dgi == null) dgi = go.AddComponent(dgiType);
                var dSo = new SerializedObject(dgi);
                dSo.FindProperty("_rigidbody").objectReferenceValue = rb;
                dSo.FindProperty("_pointableElement").objectReferenceValue = grab;
                dSo.FindProperty("_supportedGrabTypes").intValue = 3; // Pinch | Palm
                dSo.ApplyModifiedPropertiesWithoutUndo();
            }

            var grb = go.GetComponent(grabRbType);
            if (grb == null) grb = go.AddComponent(grabRbType);
            {
                var grbSo = new SerializedObject(grb);
                grbSo.FindProperty("grabbable").objectReferenceValue = grab;
                grbSo.ApplyModifiedPropertiesWithoutUndo();
            }

            if (ghhType != null)
            {
                var ghh = go.GetComponent(ghhType);
                if (ghh == null) ghh = go.AddComponent(ghhType);
                var ghhSo = new SerializedObject(ghh);
                ghhSo.FindProperty("interactable").objectReferenceValue = hgi;
                var mrList = go.GetComponentsInChildren<Renderer>(true);
                var pBr = ghhSo.FindProperty("brighten");
                if (pBr != null)
                {
                    pBr.arraySize = mrList.Length;
                    for (int i = 0; i < mrList.Length; i++) pBr.GetArrayElementAtIndex(i).objectReferenceValue = mrList[i];
                }
                ghhSo.FindProperty("hoverBrightness").floatValue = 1.18f;
                ghhSo.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(go);
            madeGrabbable++;
        }
        sb.AppendLine($"Physics props: {madeGrabbable} PushableProps are now full HandGrab + DistanceHandGrab + GrabbableRigidbody + HoverHighlight");
    }

    // ─────────────────────────────────────────────────────────────────────────
    static void FixTaskAssignment(StringBuilder sb)
    {
        var ct = Find("CardTemplate");
        if (ct != null)
        {
            var bc = ct.GetComponent<BoxCollider>();
            if (bc != null)
            {
                bc.center = Vector3.zero;
                bc.size = new Vector3(0.088f, 0.058f, 0.036f);
                bc.isTrigger = true;
            }
            var grabbableType = T("Grabbable");
            var oneGrabType = T("OneGrabFreeTransformer");
            var grab = grabbableType != null ? ct.GetComponent(grabbableType) : null;
            if (grab != null && oneGrabType != null)
            {
                var one = GetOrAdd(ct, oneGrabType);
                var so = new SerializedObject(grab);
                so.FindProperty("_oneGrabTransformer").objectReferenceValue = one;
                so.FindProperty("_kinematicWhileSelected").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var drag = ct.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "TaskDragController");
            if (drag != null)
            {
                var dSo = new SerializedObject(drag);
                dSo.FindProperty("snapRadius").floatValue = 0.15f;
                dSo.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(ct);
        }

        // Register ALL 6 StationDropZones on Phase2Bootstrap for clarity/keep-alive
        var services = Find("AppServices");
        if (services != null)
        {
            var p2 = services.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "Phase2Bootstrap");
            if (p2 != null)
            {
                var zones = _all.SelectMany(g => g.GetComponents<MonoBehaviour>())
                    .Where(m => m != null && m.GetType().Name == "StationDropZone").ToArray();
                var so = new SerializedObject(p2);
                var arr = so.FindProperty("dropZones");
                if (arr != null)
                {
                    arr.arraySize = zones.Length;
                    for (int i = 0; i < zones.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = zones[i];
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(p2);
                sb.AppendLine($"Task Assignment: CardTemplate OneGrabFreeTransformer + 0.15m snapRadius + Phase2Bootstrap dropZones={zones.Length}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    static void WireInteractiveAgents(StringBuilder sb)
    {
        var tiltType = T("AgentTilt");
        var nudgeType = T("AgentArmNudge");
        var rbrType = T("RubberBandReturn");
        var amType = T("AgentMovement");

        int agents = 0, arms = 0;
        foreach (var agName in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" })
        {
            var root = Find(agName);
            if (root == null) continue;
            var mv = amType != null ? root.GetComponent(amType) : null;

            // Kinematic RB + root trigger sphere
            var rb = GetOrAdd<Rigidbody>(root);
            rb.isKinematic = true;
            rb.useGravity = false;
            var sphere = root.GetComponents<SphereCollider>().FirstOrDefault(c => c.isTrigger);
            if (sphere == null) sphere = root.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.center = new Vector3(0f, 0.08f, 0f);
            sphere.radius = 0.09f;

            if (rbrType != null)
            {
                var rbr = GetOrAdd(root, rbrType);
                var so = new SerializedObject(rbr);
                so.FindProperty("movement").objectReferenceValue = mv;
                so.FindProperty("pushRadius").floatValue = 0.075f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var chest = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Chest")
                ?? root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Spine")
                ?? root.transform;

            if (tiltType != null)
            {
                var tilt = GetOrAdd(root, tiltType);
                var so = new SerializedObject(tilt);
                so.FindProperty("bob").objectReferenceValue = chest;
                so.FindProperty("movement").objectReferenceValue = mv;
                so.FindProperty("maxTilt").floatValue = 16f;
                so.FindProperty("touchRadius").floatValue = 0.085f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            foreach (var armBoneName in new[] { "UpperArm_L", "UpperArm_R" })
            {
                var arm = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == armBoneName);
                if (arm == null) continue;

                var arb = GetOrAdd<Rigidbody>(arm.gameObject);
                arb.isKinematic = true;
                arb.useGravity = false;

                // Compensate collider local dimensions for the armature's 100x lossyScale!
                float invScale = 1f / Mathf.Max(1f, Mathf.Abs(arm.lossyScale.x));
                var trig = arm.GetComponents<CapsuleCollider>().FirstOrDefault(c => c.isTrigger);
                if (trig == null) trig = arm.gameObject.AddComponent<CapsuleCollider>();
                trig.isTrigger = true;
                trig.radius = 0.022f * invScale;
                trig.height = 0.070f * invScale;
                trig.direction = 1;
                trig.center = new Vector3(0f, -0.025f * invScale, 0f);

                if (nudgeType != null)
                {
                    var nudge = GetOrAdd(arm.gameObject, nudgeType);
                    var so = new SerializedObject(nudge);
                    so.FindProperty("movement").objectReferenceValue = mv;
                    so.FindProperty("maxNudge").floatValue = 28f;
                    so.FindProperty("touchRadius").floatValue = 0.065f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                arms++;
            }
            EditorUtility.SetDirty(root);
            agents++;
        }
        sb.AppendLine($"Interactive Agents: wired AgentTilt + AgentArmNudge + scale-compensated arm triggers on {agents} agents / {arms} arms");
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform ch in go.transform) SetLayerRecursive(ch.gameObject, layer);
    }
}
