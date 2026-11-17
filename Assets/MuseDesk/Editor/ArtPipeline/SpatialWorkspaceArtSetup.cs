using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using MuseDesk.Art;

namespace MuseDesk.Editor.ArtPipeline
{
    /// <summary>
    /// Idempotent Editor setup pipeline for the MuseDesk Spatial Workspace Expansion.
    /// Creates materials, configures imported spatial FBXs, builds all 6 Animator Controllers,
    /// assembles all modular & composite prefabs with verified Vector3.one lossyScale sockets,
    /// builds SpatialWorkspaceValidationScene.unity, runs automated validation checks,
    /// captures multi-angle Game View screenshots, and outputs SPATIAL_WORKSPACE_INVENTORY.json.
    /// </summary>
    public static class SpatialWorkspaceArtSetup
    {
        private const string ModelsDir = "Assets/MuseDesk/Art/Models/Spatial";
        private const string MatsDir = "Assets/MuseDesk/Art/Materials";
        private const string ControllersDir = "Assets/MuseDesk/Art/Controllers/Spatial";
        private const string PrefabsDir = "Assets/MuseDesk/Art/Prefabs/Spatial";
        private const string ScenePath = "Assets/MuseDesk/Scenes/SpatialWorkspaceValidationScene.unity";
        private const string ScreenshotDir = "Screenshots/SpatialWorkspace/Unity";
        private const string InventoryPath = "docs/Art/SPATIAL_WORKSPACE_INVENTORY.json";
        private const string InterFontPath = "Assets/MuseDesk/Art/Fonts/Inter SDF.asset";

        [Serializable]
        private class AssetMetrics
        {
            public string prefabName;
            public string prefabPath;
            public int meshCount;
            public int rendererCount;
            public int skinnedRendererCount;
            public int colliderCount;
            public int uniqueMaterialCount;
            public string[] materialNames;
            public int vertexCount;
            public int triangleCount;
            public int socketCount;
            public int missingScripts;
            public bool allSocketsUnitScale;
            public string[] animatorStates;
            public string boundsSize;
        }

        [Serializable]
        private class ValidationInventory
        {
            public string generatedAtUtc;
            public string unityVersion;
            public int totalPrefabs;
            public int totalMissingScripts;
            public int totalBrokenSockets;
            public int totalMissingMaterials;
            public string[] screenshots;
            public List<AssetMetrics> assets = new List<AssetMetrics>();
        }

        [MenuItem("MuseDesk/Art Pipeline/Build Spatial Workspace Expansion (All)")]
        public static void BuildAllMenu()
        {
            BuildAll();
        }

        public static void BuildAll()
        {
            Debug.Log("[SpatialWorkspaceArtSetup] Starting full Spatial Workspace Expansion setup...");
            EnsureFolders();
            Directory.CreateDirectory(ScreenshotDir);
            Directory.CreateDirectory(Path.GetDirectoryName(InventoryPath) ?? "docs/Art");

            // 1. Ensure materials first so FBX OnAssignMaterialModel binds every slot cleanly.
            var matMap = EnsureSpatialMaterials();

            // 2. Reimport all Spatial FBXs so ModelBoundsFixer sets isReadable, clips, and materials.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ReimportAllSpatialModels();

            // 3. Build Animator Controllers for all 6 animated spatial assets.
            var controllers = BuildAllAnimatorControllers();

            // 4. Build all 12 core spatial prefabs + 4 composite reference prefabs.
            var builtPrefabs = BuildAllPrefabs(matMap, controllers);

            // 5. Validate prefabs and collect exact performance/socket metrics.
            var inventory = ValidateAndCollectMetrics(builtPrefabs);

            // 6. Build Validation Scene showcasing all 7 required configurations & capture screenshots.
            var screenshots = BuildValidationSceneAndCapture(builtPrefabs, matMap);
            inventory.screenshots = screenshots.ToArray();

            // 7. Write SPATIAL_WORKSPACE_INVENTORY.json
            string json = JsonUtility.ToJson(inventory, true);
            File.WriteAllText(InventoryPath, json, Encoding.UTF8);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[SpatialWorkspaceArtSetup] Complete! Built {inventory.totalPrefabs} prefabs, " +
                      $"missingScripts={inventory.totalMissingScripts}, brokenSockets={inventory.totalBrokenSockets}, " +
                      $"missingMats={inventory.totalMissingMaterials}. Inventory -> {InventoryPath}");

            if (inventory.totalMissingScripts > 0 || inventory.totalBrokenSockets > 0 || inventory.totalMissingMaterials > 0)
            {
                throw new Exception($"[SpatialWorkspaceArtSetup] Validation failed: missingScripts={inventory.totalMissingScripts}, " +
                                    $"brokenSockets={inventory.totalBrokenSockets}, missingMats={inventory.totalMissingMaterials}");
            }
        }

        // =====================================================================
        // 1. FOLDERS & MATERIALS
        // =====================================================================

        private static void EnsureFolders()
        {
            EnsureFolderPath(ControllersDir);
            EnsureFolderPath(PrefabsDir);
            EnsureFolderPath("Assets/MuseDesk/Art/Shaders");
            EnsureFolderPath("Assets/MuseDesk/Art/Textures");
            EnsureFolderPath("Assets/MuseDesk/Scenes");
        }

        private static void EnsureFolderPath(string path)
        {
            string norm = path.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(norm)) return;
            string[] parts = norm.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        private static Dictionary<string, Material> EnsureSpatialMaterials()
        {
            var map = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MatsDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m != null) map[m.name] = m;
            }

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            // Mat_Spatial_SlateIcon (opaque matte slate display face)
            if (!map.TryGetValue("Mat_Spatial_SlateIcon", out Material slateMat) || slateMat == null)
            {
                slateMat = new Material(urpLit) { name = "Mat_Spatial_SlateIcon" };
                slateMat.SetColor("_BaseColor", new Color(0.122f, 0.145f, 0.188f, 1f));
                slateMat.SetFloat("_Metallic", 0.05f);
                slateMat.SetFloat("_Smoothness", 0.42f);
                AssetDatabase.CreateAsset(slateMat, $"{MatsDir}/Mat_Spatial_SlateIcon.mat");
                map["Mat_Spatial_SlateIcon"] = slateMat;
            }

            // Mat_Spatial_Ghost (URP Lit Transparent, MPB-tintable via _BaseColor and _EmissionColor)
            string ghostPath = $"{MatsDir}/Mat_Spatial_Ghost.mat";
            Material ghostMat = AssetDatabase.LoadAssetAtPath<Material>(ghostPath);
            if (ghostMat == null)
            {
                ghostMat = new Material(urpLit) { name = "Mat_Spatial_Ghost" };
                AssetDatabase.CreateAsset(ghostMat, ghostPath);
            }
            ConfigureTransparentUrpLit(ghostMat, new Color(0.184f, 0.639f, 0.604f, 0.36f));
            EditorUtility.SetDirty(ghostMat);
            map["Mat_Spatial_Ghost"] = ghostMat;

            // Radial soft particle glow texture
            string glowTexPath = "Assets/MuseDesk/Art/Textures/Tex_SoftGlowDot.png";
            Texture2D glowTex = EnsureSoftGlowTexture(glowTexPath);

            // Mat_Spatial_PortalCone (custom Quest-3 stereo-safe additive holographic beam & halo ring shader)
            Shader portalShader = Shader.Find("MuseDesk/Spatial/PortalCone") ?? Shader.Find("Universal Render Pipeline/Unlit");
            string portalMatPath = $"{MatsDir}/Mat_Spatial_PortalCone.mat";
            Material portalMat = AssetDatabase.LoadAssetAtPath<Material>(portalMatPath);
            if (portalMat == null)
            {
                portalMat = new Material(portalShader) { name = "Mat_Spatial_PortalCone" };
                AssetDatabase.CreateAsset(portalMat, portalMatPath);
            }
            else if (portalMat.shader != portalShader)
            {
                portalMat.shader = portalShader;
            }
            portalMat.SetColor("_BaseColor", new Color(0.184f, 0.78f, 0.76f, 0.42f));
            portalMat.SetFloat("_Intensity", 1.25f);
            portalMat.SetFloat("_Cull", 0f);
            portalMat.SetFloat("_LiteMode", 0f);
            portalMat.DisableKeyword("_LITE_MODE");
            EditorUtility.SetDirty(portalMat);
            map["Mat_Spatial_PortalCone"] = portalMat;

            // Mat_Spatial_PortalCone_Lite (Reversible Quest 3 optimization variant: Cull Back + _LITE_MODE skips Fresnel pow & trig bands)
            string portalLitePath = $"{MatsDir}/Mat_Spatial_PortalCone_Lite.mat";
            Material portalLiteMat = AssetDatabase.LoadAssetAtPath<Material>(portalLitePath);
            if (portalLiteMat == null)
            {
                portalLiteMat = new Material(portalShader) { name = "Mat_Spatial_PortalCone_Lite" };
                AssetDatabase.CreateAsset(portalLiteMat, portalLitePath);
            }
            else if (portalLiteMat.shader != portalShader)
            {
                portalLiteMat.shader = portalShader;
            }
            portalLiteMat.SetColor("_BaseColor", new Color(0.184f, 0.78f, 0.76f, 0.42f));
            portalLiteMat.SetFloat("_Intensity", 1.25f);
            portalLiteMat.SetFloat("_Cull", (float)CullMode.Back);
            portalLiteMat.SetFloat("_LiteMode", 1f);
            portalLiteMat.EnableKeyword("_LITE_MODE");
            EditorUtility.SetDirty(portalLiteMat);
            map["Mat_Spatial_PortalCone_Lite"] = portalLiteMat;

            // Mat_Spatial_SoftParticle (URP Particles/Unlit Additive with soft circular radial texture)
            Shader partShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            string partMatPath = $"{MatsDir}/Mat_Spatial_SoftParticle.mat";
            Material partMat = AssetDatabase.LoadAssetAtPath<Material>(partMatPath);
            if (partMat == null)
            {
                partMat = new Material(partShader) { name = "Mat_Spatial_SoftParticle" };
                AssetDatabase.CreateAsset(partMat, partMatPath);
            }
            else if (partMat.shader != partShader)
            {
                partMat.shader = partShader;
            }
            if (glowTex != null) partMat.SetTexture("_BaseMap", glowTex);
            partMat.SetColor("_BaseColor", Color.white);
            partMat.SetFloat("_Surface", 1f);
            partMat.SetFloat("_Blend", 2f); // Additive
            partMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            partMat.SetFloat("_DstBlend", (float)BlendMode.One);
            partMat.SetFloat("_ZWrite", 0f);
            partMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            partMat.SetOverrideTag("RenderType", "Transparent");
            partMat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(partMat);
            map["Mat_Spatial_SoftParticle"] = partMat;

            AssetDatabase.SaveAssets();
            return map;
        }

        private static Texture2D EnsureSoftGlowTexture(string assetPath)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float falloff = Mathf.Clamp01(1f - r);
                    float a = Mathf.Pow(falloff, 2.2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Default;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static void ConfigureTransparentUrpLit(Material mat, Color baseColor)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)CullMode.Back);
            mat.SetFloat("_Metallic", 0.0f);
            mat.SetFloat("_Smoothness", 0.35f);
            mat.SetColor("_BaseColor", baseColor);
            mat.SetColor("_EmissionColor", new Color(baseColor.r, baseColor.g, baseColor.b, 1f) * 0.25f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_EMISSION");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }

        private static void ReimportAllSpatialModels()
        {
            if (!Directory.Exists(ModelsDir)) return;
            string[] fbxFiles = Directory.GetFiles(ModelsDir, "*.fbx");
            foreach (string f in fbxFiles)
            {
                string ap = f.Replace("\\", "/");
                AssetDatabase.ImportAsset(ap, ImportAssetOptions.ForceUpdate);
            }
        }

        // =====================================================================
        // 2. ANIMATOR CONTROLLERS
        // =====================================================================

        private static Dictionary<string, RuntimeAnimatorController> BuildAllAnimatorControllers()
        {
            var dict = new Dictionary<string, RuntimeAnimatorController>();

            // CeilingMount (4 clips)
            dict["CeilingMount"] = CreateStateController(
                $"{ControllersDir}/MuseDesk_CeilingMount.controller",
                $"{ModelsDir}/CeilingMount.fbx",
                "Mount_Docked_Idle",
                new[] { "Mount_Docked_Idle", "Mount_Unlock", "Mount_Spool_Spin", "Mount_Lock" });

            // SuspendedOfficePod (all 13 local mechanical clips)
            dict["SuspendedOfficePod"] = CreateStateController(
                $"{ControllersDir}/MuseDesk_CeilingPod.controller",
                $"{ModelsDir}/SuspendedOfficePod.fbx",
                "Pod_Docked_Idle",
                new[]
                {
                    "Pod_Docked_Idle",
                    "Pod_Attention_Ack",
                    "Pod_Unlock",
                    "Pod_Begin_Descent",
                    "Pod_Descending",
                    "Pod_Stabilize",
                    "Pod_Shelf_Open",
                    "Pod_Deployed_Idle",
                    "Pod_Shelf_Close",
                    "Pod_Begin_Ascent",
                    "Pod_Returning_Dock",
                    "Pod_Dock_Lock",
                    "Pod_Mechanical_Settle"
                });

            // WorkspaceAttentionBeacon (7 clips)
            dict["WorkspaceAttentionBeacon"] = CreateStateController(
                $"{ControllersDir}/MuseDesk_AttentionBeacon.controller",
                $"{ModelsDir}/WorkspaceAttentionBeacon.fbx",
                "Beacon_Idle",
                new[]
                {
                    "Beacon_Idle",
                    "Beacon_Working",
                    "Beacon_Completed",
                    "Beacon_Suggestion",
                    "Beacon_Blocking_Decision",
                    "Beacon_Error",
                    "Beacon_Incoming"
                });

            // FloorCourierStation (4 clips)
            dict["FloorCourierStation"] = CreateStateController(
                $"{ControllersDir}/MuseDesk_FloorCourierStation.controller",
                $"{ModelsDir}/FloorCourierStation.fbx",
                "Station_Idle",
                new[] { "Station_Idle", "Station_Load_Lift", "Station_Dispatch_Release", "Station_Dock_Receive" });

            // DocumentCourier (6 clips)
            dict["DocumentCourier"] = CreateStateController(
                $"{ControllersDir}/MuseDesk_DocumentCourier.controller",
                $"{ModelsDir}/DocumentCourier.fbx",
                "Courier_Docked_Idle",
                new[]
                {
                    "Courier_Docked_Idle",
                    "Courier_Load_Document",
                    "Courier_Fly_Loop",
                    "Courier_Arrive_Present",
                    "Courier_Waiting_Idle",
                    "Courier_Return_Dock"
                });

            // AgentConversationPerch (5 clips + trigger parameters matching AgentConversationPerchController)
            var perchCtrl = CreateStateController(
                $"{ControllersDir}/MuseDesk_ConversationPerch.controller",
                $"{ModelsDir}/AgentConversationPerch.fbx",
                "Perch_Folded_Idle",
                new[]
                {
                    "Perch_Folded_Idle",
                    "Perch_Unfold_Arrive",
                    "Perch_Listening_Idle",
                    "Perch_Speaking_Pulse",
                    "Perch_Fold_Return"
                }) as AnimatorController;
            WirePerchTransitions(perchCtrl);
            dict["AgentConversationPerch"] = perchCtrl;

            AssetDatabase.SaveAssets();
            return dict;
        }

        private static RuntimeAnimatorController CreateStateController(
            string ctrlPath,
            string fbxPath,
            string defaultStateName,
            string[] expectedClips)
        {
            if (File.Exists(ctrlPath))
                AssetDatabase.DeleteAsset(ctrlPath);

            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var sm = ctrl.layers[0].stateMachine;

            var clipsByName = LoadClipMap(fbxPath);
            AnimatorState defaultState = null;

            for (int i = 0; i < expectedClips.Length; i++)
            {
                string sName = expectedClips[i];
                var state = sm.AddState(sName, new Vector3(260 + (i % 3) * 230, (i / 3) * 75, 0));
                if (clipsByName.TryGetValue(sName, out AnimationClip clip))
                {
                    state.motion = clip;
                }
                else
                {
                    Debug.LogWarning($"[SpatialWorkspaceArtSetup] Clip '{sName}' not found in {fbxPath}. " +
                                     $"Available: {string.Join(", ", clipsByName.Keys)}");
                }

                if (sName == defaultStateName)
                    defaultState = state;
            }

            if (defaultState != null)
                sm.defaultState = defaultState;

            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        private static void WirePerchTransitions(AnimatorController ctrl)
        {
            if (ctrl == null) return;
            ctrl.AddParameter("Deployed", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("UnfoldArrive", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("FoldReturn", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("SpeakPulse", AnimatorControllerParameterType.Trigger);

            var sm = ctrl.layers[0].stateMachine;
            var states = sm.states.ToDictionary(s => s.state.name, s => s.state);

            if (states.TryGetValue("Perch_Folded_Idle", out var folded) &&
                states.TryGetValue("Perch_Unfold_Arrive", out var unfold))
            {
                var t = folded.AddTransition(unfold);
                t.AddCondition(AnimatorConditionMode.If, 0f, "UnfoldArrive");
                t.duration = 0.12f;
                t.hasExitTime = false;
            }

            if (states.TryGetValue("Perch_Unfold_Arrive", out var u2) &&
                states.TryGetValue("Perch_Listening_Idle", out var listening))
            {
                var t = u2.AddTransition(listening);
                t.hasExitTime = true;
                t.exitTime = 0.92f;
                t.duration = 0.12f;
            }

            if (states.TryGetValue("Perch_Listening_Idle", out var listen2) &&
                states.TryGetValue("Perch_Speaking_Pulse", out var speak))
            {
                var t1 = listen2.AddTransition(speak);
                t1.AddCondition(AnimatorConditionMode.If, 0f, "SpeakPulse");
                t1.duration = 0.08f;
                t1.hasExitTime = false;

                var t2 = speak.AddTransition(listen2);
                t2.hasExitTime = true;
                t2.exitTime = 0.90f;
                t2.duration = 0.10f;
            }

            if (states.TryGetValue("Perch_Listening_Idle", out var listen3) &&
                states.TryGetValue("Perch_Fold_Return", out var foldRet))
            {
                var t = listen3.AddTransition(foldRet);
                t.AddCondition(AnimatorConditionMode.If, 0f, "FoldReturn");
                t.duration = 0.10f;
                t.hasExitTime = false;
            }

            if (states.TryGetValue("Perch_Fold_Return", out var f2) &&
                states.TryGetValue("Perch_Folded_Idle", out var folded2))
            {
                var t = f2.AddTransition(folded2);
                t.hasExitTime = true;
                t.exitTime = 0.92f;
                t.duration = 0.10f;
            }

            EditorUtility.SetDirty(ctrl);
        }

        private static Dictionary<string, AnimationClip> LoadClipMap(string fbxPath)
        {
            var map = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
            foreach (UnityEngine.Object obj in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    string clean = clip.name;
                    int pipe = clean.IndexOf('|');
                    if (pipe >= 0 && pipe + 1 < clean.Length)
                        clean = clean.Substring(pipe + 1);
                    map[clean] = clip;
                    map[clip.name] = clip;
                }
            }
            return map;
        }

        // =====================================================================
        // 3. PREFAB CONSTRUCTION
        // =====================================================================

        private static Dictionary<string, GameObject> BuildAllPrefabs(
            Dictionary<string, Material> matMap,
            Dictionary<string, RuntimeAnimatorController> controllers)
        {
            var prefabs = new Dictionary<string, GameObject>();

            prefabs["FloorOfficePlatform"] = BuildFloorPlatformPrefab(
                "FloorOfficePlatform", isCompact: false, matMap);
            prefabs["FloorOfficePlatform_Compact"] = BuildFloorPlatformPrefab(
                "FloorOfficePlatform_Compact", isCompact: true, matMap);

            prefabs["CeilingMount"] = BuildSimpleAnimatedPrefab(
                "CeilingMount", controllers["CeilingMount"], matMap, addBoxCollider: true,
                colliderCenter: new Vector3(0f, -0.04f, 0f), colliderSize: new Vector3(0.24f, 0.10f, 0.24f));

            prefabs["CeilingInspectionShelf"] = BuildStaticPrefab(
                "CeilingInspectionShelf", matMap, addBoxCollider: true,
                colliderCenter: new Vector3(0f, 0.008f, 0f), colliderSize: new Vector3(0.25f, 0.024f, 0.13f));

            prefabs["SuspendedOfficePod"] = BuildSuspendedPodPrefab(controllers["SuspendedOfficePod"], matMap);

            prefabs["WorkspaceAttentionBeacon"] = BuildBeaconPrefab(controllers["WorkspaceAttentionBeacon"], matMap);
            prefabs["SpatialMailbox"] = BuildMailboxPrefab(matMap);
            prefabs["FloorCourierStation"] = BuildSimpleAnimatedPrefab(
                "FloorCourierStation", controllers["FloorCourierStation"], matMap, addBoxCollider: true,
                colliderCenter: new Vector3(0f, 0.015f, 0f), colliderSize: new Vector3(0.15f, 0.03f, 0.16f));

            prefabs["DocumentCourier"] = BuildCourierPrefab(controllers["DocumentCourier"], matMap);
            prefabs["AgentConversationPerch"] = BuildPerchPrefab(controllers["AgentConversationPerch"], matMap);

            prefabs["PlacementGhostPreview_Floor"] = BuildGhostPreviewPrefab(
                "PlacementGhostPreview_Floor", "READY TO PLACE", new Vector3(0f, 0.025f, 0.34f), matMap);
            prefabs["PlacementGhostPreview_Ceiling"] = BuildGhostPreviewPrefab(
                "PlacementGhostPreview_Ceiling", "CEILING READY", new Vector3(0f, -0.02f, 0.24f), matMap);

            // Composite turnkey assemblies for immediate deployment or validation reference
            prefabs["Spatial_FloorCourierSystem"] = BuildCompositeCourierSystem(prefabs);
            prefabs["Spatial_CeilingWorkspaceAssembly"] = BuildCompositeCeilingAssembly(prefabs, matMap);
            prefabs["Spatial_FloorOffice_Complete"] = BuildCompositeFloorComplete(prefabs);
            prefabs["Spatial_CeilingPod_Complete"] = BuildCompositeCeilingComplete(prefabs);

            return prefabs;
        }

        private static GameObject InstantiateModelClean(string assetName, Dictionary<string, Material> matMap)
        {
            string fbxPath = $"{ModelsDir}/{assetName}.fbx";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (src == null) throw new FileNotFoundException($"Missing spatial FBX: {fbxPath}");

            var inst = UnityEngine.Object.Instantiate(src);
            inst.name = assetName;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;

            ApplyMaterialsRecursively(inst, matMap);
            NormalizeSkinnedSockets(inst.transform);
            return inst;
        }

        private static void ApplyMaterialsRecursively(GameObject root, Dictionary<string, Material> matMap)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string clean = mats[i].name;
                    int dot = clean.IndexOf('.');
                    if (dot > 0) clean = clean.Substring(0, dot);
                    if (matMap.TryGetValue(clean, out var urpMat) && urpMat != null && mats[i] != urpMat)
                    {
                        mats[i] = urpMat;
                        changed = true;
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// Ensures every Blender socket empty preserves world orientation (+Z front, +Y up) and
        /// exact lossyScale == Vector3.one. Never mutates Armature or Bone_* transforms!
        /// </summary>
        private static void NormalizeSkinnedSockets(Transform root)
        {
            var sockets = new List<Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                if (t.name.StartsWith("Bone_") || t.name.EndsWith("_Armature")) continue;
                if (t.GetComponent<Renderer>() != null) continue;
                if (IsSocketName(t.name)) sockets.Add(t);
            }

            foreach (Transform t in sockets)
            {
                bool isBoneChild = t.parent != null && t.parent.name.StartsWith("Bone_");
                if (!isBoneChild)
                {
                    // Reparent directly to asset root so Armature scale (100) / -90 X / animation evaluation
                    // can never warp socket scale or flip mounted characters onto their backs.
                    t.SetParent(root, worldPositionStays: true);
                    t.localRotation = Quaternion.identity;
                    t.localScale = Vector3.one;
                }
                else
                {
                    Vector3 pLossy = t.parent.lossyScale;
                    t.localScale = new Vector3(Inv(pLossy.x), Inv(pLossy.y), Inv(pLossy.z));
                    t.rotation = Quaternion.identity;
                }
            }
        }

        private static bool IsSocketName(string name)
        {
            return name.EndsWith("Socket") ||
                   name.EndsWith("Pose") ||
                   name.EndsWith("Point") ||
                   name == "WorkspaceRoot" ||
                   name == "FloorContact" ||
                   name == "NavigationBounds" ||
                   name == "CeilingAnchor" ||
                   name == "CeilingMountRoot" ||
                   name == "SuspensionTop" ||
                   name == "SuspensionBottom" ||
                   name == "PodRoot" ||
                   name == "InspectionShelf" ||
                   name == "DockingConnector" ||
                   name == "PresentationColliderCenter" ||
                   name == "ConversationIndicator";
        }

        private static bool IsInsideMountedChildAsset(Transform t, Transform root)
        {
            Transform cur = t.parent;
            while (cur != null && cur != root)
            {
                string n = cur.name;
                if (n.StartsWith("Workstation_") ||
                    n.StartsWith("SoftwareFactory") ||
                    n.StartsWith("Agent_") ||
                    n.StartsWith("Doc_") ||
                    n.StartsWith("SpatialMailbox") ||
                    n.StartsWith("WorkspaceAttentionBeacon"))
                {
                    return true;
                }
                cur = cur.parent;
            }
            return false;
        }

        private static float Inv(float v) => Mathf.Abs(v) > 1e-5f ? 1f / v : 1f;

        private static GameObject SavePrefab(GameObject go, string prefabName)
        {
            string path = $"{PrefabsDir}/{prefabName}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            return saved;
        }

        private static GameObject BuildStaticPrefab(
            string assetName,
            Dictionary<string, Material> matMap,
            bool addBoxCollider,
            Vector3 colliderCenter,
            Vector3 colliderSize)
        {
            var go = InstantiateModelClean(assetName, matMap);
            if (addBoxCollider)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = colliderCenter;
                bc.size = colliderSize;
            }
            return SavePrefab(go, assetName);
        }

        private static GameObject BuildSimpleAnimatedPrefab(
            string assetName,
            RuntimeAnimatorController ctrl,
            Dictionary<string, Material> matMap,
            bool addBoxCollider,
            Vector3 colliderCenter,
            Vector3 colliderSize)
        {
            var go = InstantiateModelClean(assetName, matMap);
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (addBoxCollider)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = colliderCenter;
                bc.size = colliderSize;
            }
            return SavePrefab(go, assetName);
        }

        private static GameObject BuildFloorPlatformPrefab(
            string assetName,
            bool isCompact,
            Dictionary<string, Material> matMap)
        {
            var go = InstantiateModelClean(assetName, matMap);

            float platW = isCompact ? 0.56f : 0.764f;
            float platD = isCompact ? 0.36f : 0.466f;

            // Main surface collider (thin, cleanly covering bottom platform slab)
            var baseCol = go.AddComponent<BoxCollider>();
            baseCol.center = new Vector3(0f, 0.017f, 0f);
            baseCol.size = new Vector3(platW, 0.034f, platD);

            // NavigationBounds collider (trigger volume defining safe walking zone for mascots)
            Transform navBounds = FindChildRecursive(go.transform, "NavigationBounds");
            if (navBounds != null)
            {
                var navCol = navBounds.gameObject.AddComponent<BoxCollider>();
                navCol.isTrigger = true;
                navCol.center = new Vector3(0f, 0.045f, 0f);
                navCol.size = new Vector3(platW - 0.06f, 0.09f, platD - 0.07f);
            }

            // Crisp miniature bottom-front plaque label ("MUSEDESK LAB") following design system TMP rule
            float plaqueY = 0.0185f;
            float plaqueZ = isCompact ? 0.2055f : 0.2505f;
            AddWorldTextLabel(go.transform, "Floor_NameplateTMP", "MUSEDESK LAB",
                new Vector3(0f, plaqueY, plaqueZ), new Vector3(-18f, 0f, 0f), 0.0036f,
                new Color(0.969f, 0.949f, 0.890f, 1f), new Vector2(18f, 3f));

            var ctrl = go.AddComponent<FloorOfficePlatformController>();
            ctrl.ResolveSockets();
            ctrl.SetStatusStripColor(FloorOfficePlatformController.DefaultTeal, 0.25f);

            return SavePrefab(go, assetName);
        }

        private static GameObject BuildSuspendedPodPrefab(
            RuntimeAnimatorController ctrl,
            Dictionary<string, Material> matMap)
        {
            var go = InstantiateModelClean("SuspendedOfficePod", matMap);
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Low-profile collider matching the pod deck (0.700 x 0.407 m centered at z = +0.0125)
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.020f, 0.0125f);
            bc.size = new Vector3(0.700f, 0.040f, 0.407f);

            // Small front slate status display TMP readout attached cleanly at InfoDisplaySocket
            Transform dispSock = FindChildRecursive(go.transform, "InfoDisplaySocket");
            Transform dispParent = dispSock != null ? dispSock : go.transform;
            Vector3 dispPos = dispSock != null ? new Vector3(0f, 0f, 0.0065f) : new Vector3(0.190f, 0.058f, 0.151f);
            Vector3 dispEuler = dispSock != null ? Vector3.zero : new Vector3(-18f, 14f, 0f);
            AddWorldTextLabel(dispParent, "Pod_TaskDisplayTMP", "POD READY\nSTANDBY",
                dispPos, dispEuler, 0.0026f,
                new Color(0.82f, 0.94f, 0.92f, 1f), new Vector2(28f, 12f));

            return SavePrefab(go, "SuspendedOfficePod");
        }

        private static GameObject BuildBeaconPrefab(
            RuntimeAnimatorController ctrl,
            Dictionary<string, Material> matMap)
        {
            var go = InstantiateModelClean("WorkspaceAttentionBeacon", matMap);
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var sc = go.AddComponent<SphereCollider>();
            sc.center = new Vector3(0f, 0.045f, 0f);
            sc.radius = 0.055f;

            var beaconCtrl = go.AddComponent<SpatialBeaconVisualController>();
            beaconCtrl.ResolveReferences();
            beaconCtrl.SetState(SpatialBeaconVisualController.BeaconState.WorkingNormally, immediate: true);

            return SavePrefab(go, "WorkspaceAttentionBeacon");
        }

        private static GameObject BuildMailboxPrefab(Dictionary<string, Material> matMap)
        {
            var go = InstantiateModelClean("SpatialMailbox", matMap);
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.042f, 0f);
            bc.size = new Vector3(0.145f, 0.085f, 0.095f);

            // Numeric counter TMP placed 0.5mm proud of the front slate card face (z = +0.0505, tilted -20 deg)
            AddWorldTextLabel(go.transform, "Mailbox_CountTMP", "0",
                new Vector3(0f, 0.0245f, 0.0505f), new Vector3(-20f, 0f, 0f), 0.0072f,
                new Color(0.969f, 0.949f, 0.890f, 1f), new Vector2(8f, 3f));

            var mbCtrl = go.AddComponent<SpatialMailboxController>();
            mbCtrl.ResolveReferences();
            mbCtrl.UpdateCounts(0, 0, 0);

            return SavePrefab(go, "SpatialMailbox");
        }

        private static GameObject BuildCourierPrefab(
            RuntimeAnimatorController ctrl,
            Dictionary<string, Material> matMap)
        {
            var go = InstantiateModelClean("DocumentCourier", matMap);
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Attach clear interaction collider centered at PresentationColliderCenter
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.038f, 0f);
            bc.size = new Vector3(0.135f, 0.075f, 0.135f);

            return SavePrefab(go, "DocumentCourier");
        }

        private static GameObject BuildPerchPrefab(
            RuntimeAnimatorController ctrl,
            Dictionary<string, Material> matMap)
        {
            var go = InstantiateModelClean("AgentConversationPerch", matMap);
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.015f, 0f);
            bc.size = new Vector3(0.19f, 0.032f, 0.17f);

            var perchCtrl = go.AddComponent<AgentConversationPerchController>();
            perchCtrl.ResolveReferences();

            return SavePrefab(go, "AgentConversationPerch");
        }

        private static GameObject BuildGhostPreviewPrefab(
            string assetName,
            string defaultText,
            Vector3 badgePos,
            Dictionary<string, Material> matMap)
        {
            var go = InstantiateModelClean(assetName, matMap);
            AddWorldTextLabel(go.transform, "Preview_StatusBadgeTMP", defaultText,
                badgePos, new Vector3(-25f, 0f, 0f), 0.008f,
                new Color(0.969f, 0.949f, 0.890f, 1f), new Vector2(28f, 4f));

            var ctrl = go.AddComponent<PlacementGhostPreviewController>();
            ctrl.ResolveReferences();
            ctrl.SetValid(true, defaultText);

            return SavePrefab(go, assetName);
        }

        // ---- COMPOSITE PREFABS ----

        private static GameObject BuildCompositeCourierSystem(Dictionary<string, GameObject> prefabs)
        {
            var root = new GameObject("Spatial_FloorCourierSystem");

            var station = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["FloorCourierStation"], root.transform);
            station.name = "FloorCourierStation";

            var courier = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["DocumentCourier"], root.transform);
            courier.name = "CourierRoot";

            var dockPt = FindChildRecursive(station.transform, "CourierDockPoint");
            if (dockPt != null)
                courier.transform.SetPositionAndRotation(dockPt.position, dockPt.rotation);

            var ctrl = root.AddComponent<DocumentCourierController>();
            // Wire serialized stationAnimator / courierAnimator cleanly via SerializedObject
            SetPrivateField(ctrl, "stationAnimator", station.GetComponent<Animator>());
            SetPrivateField(ctrl, "courierAnimator", courier.GetComponent<Animator>());
            SetPrivateField(ctrl, "courierRoot", courier.transform);
            ctrl.ResolveReferences();

            return SavePrefab(root, "Spatial_FloorCourierSystem");
        }

        private static GameObject BuildCompositeCeilingAssembly(
            Dictionary<string, GameObject> prefabs,
            Dictionary<string, Material> matMap)
        {
            var root = new GameObject("Spatial_CeilingWorkspaceAssembly");

            var mount = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["CeilingMount"], root.transform);
            mount.name = "CeilingMount";

            // Position CeilingMount DockedPose & InspectionPose sockets relative to CeilingAnchor
            // DockedPose (-0.388m) sits Pod's top DockingConnector (+0.258m) at -0.130m right beneath the closed 4-leaf Iris bay!
            Transform dockedPose = FindChildRecursive(mount.transform, "DockedPose");
            if (dockedPose != null) dockedPose.localPosition = new Vector3(0f, -0.388f, 0f);
            Transform inspPose = FindChildRecursive(mount.transform, "InspectionPose");
            if (inspPose != null) inspPose.localPosition = new Vector3(0f, -1.42f, 0f);

            var pod = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["SuspendedOfficePod"], root.transform);
            pod.name = "SuspendedOfficePod";
            pod.transform.localPosition = new Vector3(0f, -0.388f, 0f);

            // Dynamic visual suspension tether cylinder (height = 2m default primitive -> tetherBaseLength = 2f)
            var rodGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rodGo.name = "DynamicTetherRod";
            UnityEngine.Object.DestroyImmediate(rodGo.GetComponent<Collider>());
            rodGo.transform.SetParent(root.transform, false);
            rodGo.transform.localScale = new Vector3(0.0085f, 0.08f, 0.0085f);
            if (matMap.TryGetValue("Mat_CharcoalVisor", out var tetherMat))
                rodGo.GetComponent<Renderer>().sharedMaterial = tetherMat;

            // Mount AttentionBeacon onto pod's AttentionBeaconSocket
            Transform beaconSocket = FindChildRecursive(pod.transform, "AttentionBeaconSocket");
            var beacon = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WorkspaceAttentionBeacon"], root.transform);
            beacon.name = "WorkspaceAttentionBeacon";
            if (beaconSocket != null)
                SpatialScaleUtility.MountPreservingWorldScale(beacon.transform, beaconSocket, Vector3.one * 0.85f);

            // Ceiling Ascension/Descension Portal & Particle VFX Rig (shared URP shaders + MPB + Quest-safe particles)
            BuildCeilingDeploymentVfxChildren(root.transform, pod.transform, matMap);

            var ctrl = root.AddComponent<CeilingWorkspacePodController>();
            ctrl.ResolveTransforms();
            SetPrivateField(ctrl, "mountAnimator", mount.GetComponent<Animator>());
            SetPrivateField(ctrl, "podAnimator", pod.GetComponent<Animator>());
            SetPrivateField(ctrl, "podRoot", pod.transform);
            SetPrivateField(ctrl, "dockedPose", dockedPose);
            SetPrivateField(ctrl, "inspectionPose", inspPose);
            SetPrivateField(ctrl, "dynamicTetherRod", rodGo.transform);
            SetPrivateField(ctrl, "tetherBaseLength", 2f);
            ctrl.SnapToPoseImmediate(deployed: false);

            var vfxCtrl = root.AddComponent<CeilingDeploymentVFXController>();
            vfxCtrl.ResolveReferences();
            if (matMap.TryGetValue("Mat_Spatial_PortalCone", out var fullPortalMat))
                SetPrivateField(vfxCtrl, "portalConeFullMaterial", fullPortalMat);
            if (matMap.TryGetValue("Mat_Spatial_PortalCone_Lite", out var litePortalMat))
                SetPrivateField(vfxCtrl, "portalConeLiteMaterial", litePortalMat);
            vfxCtrl.ApplySnapshotImmediate(CeilingWorkspacePodController.PodState.DockedIdle, 0f);

            return SavePrefab(root, "Spatial_CeilingWorkspaceAssembly");
        }

        private static void BuildCeilingDeploymentVfxChildren(
            Transform root,
            Transform podRoot,
            Dictionary<string, Material> matMap)
        {
            BuildOrLoadVfxMeshes(out Mesh coneMesh, out Mesh ringMesh);
            matMap.TryGetValue("Mat_Spatial_PortalCone", out Material portalMat);
            matMap.TryGetValue("Mat_Spatial_SoftParticle", out Material particleMat);

            // 1. Volumetric holographic light cone spanning ceiling bay -> pod yoke
            var coneGo = new GameObject("VFX_CeilingPortalCone");
            coneGo.transform.SetParent(root, false);
            coneGo.AddComponent<MeshFilter>().sharedMesh = coneMesh;
            var coneRend = coneGo.AddComponent<MeshRenderer>();
            coneRend.sharedMaterial = portalMat;
            coneRend.shadowCastingMode = ShadowCastingMode.Off;
            coneRend.receiveShadows = false;

            // 2. Steady ceiling halo + expanding lock/unlock ripple flush beneath drywall
            var haloGo = new GameObject("VFX_CeilingProjectionHalo");
            haloGo.transform.SetParent(root, false);
            haloGo.transform.localPosition = new Vector3(0f, -0.004f, 0f);
            haloGo.transform.localScale = new Vector3(0.28f, 1f, 0.28f);
            haloGo.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            var haloRend = haloGo.AddComponent<MeshRenderer>();
            haloRend.sharedMaterial = portalMat;
            haloRend.shadowCastingMode = ShadowCastingMode.Off;
            haloRend.receiveShadows = false;

            var rippleGo = new GameObject("VFX_CeilingRippleRing");
            rippleGo.transform.SetParent(root, false);
            rippleGo.transform.localPosition = new Vector3(0f, -0.008f, 0f);
            rippleGo.transform.localScale = new Vector3(0.30f, 1f, 0.30f);
            rippleGo.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            var rippleRend = rippleGo.AddComponent<MeshRenderer>();
            rippleRend.sharedMaterial = portalMat;
            rippleRend.shadowCastingMode = ShadowCastingMode.Off;
            rippleRend.receiveShadows = false;

            // 3. Air-cushion landing halo beneath the suspended pod
            var cushionGo = new GameObject("VFX_AirCushionRing");
            cushionGo.transform.SetParent(root, false);
            cushionGo.transform.localPosition = new Vector3(0f, -0.015f, 0f);
            cushionGo.transform.localScale = new Vector3(0.35f, 1f, 0.24f);
            cushionGo.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            var cushionRend = cushionGo.AddComponent<MeshRenderer>();
            cushionRend.sharedMaterial = portalMat;
            cushionRend.shadowCastingMode = ShadowCastingMode.Off;
            cushionRend.receiveShadows = false;

            // 4. ParticleSystem: Ceiling bay orbiting motes
            var apGo = new GameObject("VFX_CeilingApertureParticles");
            apGo.transform.SetParent(root, false);
            apGo.transform.localPosition = new Vector3(0f, -0.045f, 0f);
            apGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var apPs = ConfigureBaseParticleSystem(apGo, particleMat, maxParticles: 28, rateOverTime: 18f,
                startLifetime: 1.15f, startSpeed: 0.045f, startSizeMin: 0.014f, startSizeMax: 0.026f,
                new Color(0.32f, 0.90f, 0.94f, 0.90f));
            var apShape = apPs.shape;
            apShape.shapeType = ParticleSystemShapeType.Circle;
            apShape.radius = 0.145f;
            apShape.radiusThickness = 0.2f;
            var apVel = apPs.velocityOverLifetime;
            apVel.enabled = true;
            apVel.orbitalZ = new ParticleSystem.MinMaxCurve(1.4f);

            // 5. ParticleSystem: Bidirectional tether flow motes (down when descending, up when retracting)
            var tfGo = new GameObject("VFX_TetherFlowParticles");
            tfGo.transform.SetParent(root, false);
            tfGo.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            tfGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var tfPs = ConfigureBaseParticleSystem(tfGo, particleMat, maxParticles: 36, rateOverTime: 26f,
                startLifetime: 1.25f, startSpeed: 0.35f, startSizeMin: 0.012f, startSizeMax: 0.022f,
                new Color(0.42f, 0.95f, 0.94f, 0.95f));
            var tfShape = tfPs.shape;
            tfShape.shapeType = ParticleSystemShapeType.ConeVolume;
            tfShape.angle = 3f;
            tfShape.radius = 0.085f;
            tfShape.length = 0.85f;
            var tfCol = tfPs.colorOverLifetime;
            tfCol.enabled = true;
            var tfGrad = new Gradient();
            tfGrad.SetKeys(
                new[] { new GradientColorKey(new Color(0.25f, 0.92f, 0.92f), 0f), new GradientColorKey(Color.white, 0.5f), new GradientColorKey(new Color(0.25f, 0.92f, 0.92f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.25f), new GradientAlphaKey(0.85f, 0.75f), new GradientAlphaKey(0f, 1f) });
            tfCol.color = tfGrad;

            // 6. ParticleSystem: Arrival / Docking radial burst mist
            var brGo = new GameObject("VFX_ArrivalBurstParticles");
            brGo.transform.SetParent(root, false);
            brGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var brPs = ConfigureBaseParticleSystem(brGo, particleMat, maxParticles: 24, rateOverTime: 0f,
                startLifetime: 0.75f, startSpeed: 0.28f, startSizeMin: 0.014f, startSizeMax: 0.028f,
                new Color(0.95f, 0.88f, 0.65f, 0.90f));
            var brShape = brPs.shape;
            brShape.shapeType = ParticleSystemShapeType.Donut;
            brShape.radius = 0.16f;
            brShape.donutRadius = 0.02f;
        }

        private static ParticleSystem ConfigureBaseParticleSystem(
            GameObject go,
            Material mat,
            int maxParticles,
            float rateOverTime,
            float startLifetime,
            float startSpeed,
            float startSizeMin,
            float startSizeMax,
            Color startColor)
        {
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = maxParticles;
            main.startLifetime = startLifetime;
            main.startSpeed = startSpeed;
            main.startSize = new ParticleSystem.MinMaxCurve(startSizeMin, startSizeMax);
            main.startColor = startColor;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var em = ps.emission;
            em.rateOverTime = rateOverTime;

            var sizeLife = ps.sizeOverLifetime;
            sizeLife.enabled = true;
            sizeLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.2f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f)));

            var rend = go.GetComponent<ParticleSystemRenderer>();
            if (rend != null)
            {
                rend.sharedMaterial = mat;
                rend.renderMode = ParticleSystemRenderMode.Billboard;
                rend.shadowCastingMode = ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }
            return ps;
        }

        private static void BuildOrLoadVfxMeshes(out Mesh coneMesh, out Mesh ringMesh)
        {
            string conePath = $"{ModelsDir}/VFX_PortalConeMesh.asset";
            string ringPath = $"{ModelsDir}/VFX_HoloRingMesh.asset";

            coneMesh = AssetDatabase.LoadAssetAtPath<Mesh>(conePath);
            if (coneMesh == null)
            {
                coneMesh = new Mesh { name = "VFX_PortalConeMesh" };
                const int segs = 28;
                var verts = new Vector3[(segs + 1) * 2];
                var norms = new Vector3[(segs + 1) * 2];
                var uvs = new Vector2[(segs + 1) * 2];
                var tris = new int[segs * 6];
                const float rTop = 0.135f;
                const float rBot = 0.225f;
                for (int i = 0; i <= segs; i++)
                {
                    float t = (float)i / segs;
                    float ang = t * Mathf.PI * 2f;
                    float cx = Mathf.Cos(ang);
                    float sz = Mathf.Sin(ang);
                    Vector3 n = new Vector3(cx, 0.08f, sz).normalized;
                    verts[i * 2]     = new Vector3(cx * rTop, 0f, sz * rTop);
                    verts[i * 2 + 1] = new Vector3(cx * rBot, -1f, sz * rBot);
                    norms[i * 2] = norms[i * 2 + 1] = n;
                    uvs[i * 2]     = new Vector2(t, 0f);
                    uvs[i * 2 + 1] = new Vector2(t, 1f);
                }
                for (int i = 0; i < segs; i++)
                {
                    int v0 = i * 2;
                    tris[i * 6] = v0; tris[i * 6 + 1] = v0 + 2; tris[i * 6 + 2] = v0 + 1;
                    tris[i * 6 + 3] = v0 + 2; tris[i * 6 + 4] = v0 + 3; tris[i * 6 + 5] = v0 + 1;
                }
                coneMesh.vertices = verts;
                coneMesh.normals = norms;
                coneMesh.uv = uvs;
                coneMesh.triangles = tris;
                coneMesh.RecalculateBounds();
                AssetDatabase.CreateAsset(coneMesh, conePath);
            }

            ringMesh = AssetDatabase.LoadAssetAtPath<Mesh>(ringPath);
            if (ringMesh == null)
            {
                ringMesh = new Mesh { name = "VFX_HoloRingMesh" };
                const int segs = 36;
                var verts = new Vector3[(segs + 1) * 2];
                var norms = new Vector3[(segs + 1) * 2];
                var uvs = new Vector2[(segs + 1) * 2];
                var tris = new int[segs * 6];
                const float rIn = 0.68f;
                const float rOut = 1.0f;
                for (int i = 0; i <= segs; i++)
                {
                    float t = (float)i / segs;
                    float ang = t * Mathf.PI * 2f;
                    float cx = Mathf.Cos(ang);
                    float sz = Mathf.Sin(ang);
                    verts[i * 2]     = new Vector3(cx * rIn, 0f, sz * rIn);
                    verts[i * 2 + 1] = new Vector3(cx * rOut, 0f, sz * rOut);
                    norms[i * 2] = norms[i * 2 + 1] = Vector3.up;
                    uvs[i * 2]     = new Vector2(t, 0f);
                    uvs[i * 2 + 1] = new Vector2(t, 1f);
                }
                for (int i = 0; i < segs; i++)
                {
                    int v0 = i * 2;
                    tris[i * 6] = v0; tris[i * 6 + 1] = v0 + 1; tris[i * 6 + 2] = v0 + 2;
                    tris[i * 6 + 3] = v0 + 2; tris[i * 6 + 4] = v0 + 1; tris[i * 6 + 5] = v0 + 3;
                }
                ringMesh.vertices = verts;
                ringMesh.normals = norms;
                ringMesh.uv = uvs;
                ringMesh.triangles = tris;
                ringMesh.RecalculateBounds();
                AssetDatabase.CreateAsset(ringMesh, ringPath);
            }
        }

        private static GameObject BuildCompositeFloorComplete(Dictionary<string, GameObject> prefabs)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["FloorOfficePlatform"]);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            root.name = "Spatial_FloorOffice_Complete";

            var ctrl = root.GetComponent<FloorOfficePlatformController>();
            ctrl.ResolveSockets();

            // Mount optimized workstations + factory + mascots cleanly into sockets (existing assets natively face +Z at yaw 0)
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Research_Optimized.prefab",
                ctrl.ResearchStationSocket, Vector3.one * 0.62f, 0f, "Workstation_Research");
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Developer_Optimized.prefab",
                ctrl.DeveloperStationSocket, Vector3.one * 0.62f, 0f, "Workstation_Developer");
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Testing_Optimized.prefab",
                ctrl.TesterStationSocket, Vector3.one * 0.62f, 0f, "Workstation_Testing");
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Factory/SoftwareFactory_Complete.prefab",
                ctrl.FactorySocket, Vector3.one * 0.60f, 0f, "SoftwareFactory_Complete");

            // Mount 3 mascots seated/standing at stations facing +Z toward the user
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Characters/Agent_Researcher.prefab",
                ctrl.ResearchStationSocket, Vector3.one * 0.68f, 0f, "Agent_Researcher", new Vector3(0f, 0f, -0.050f));
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Characters/Agent_Developer.prefab",
                ctrl.DeveloperStationSocket, Vector3.one * 0.68f, 0f, "Agent_Developer", new Vector3(0f, 0f, -0.050f));
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Characters/Agent_Tester.prefab",
                ctrl.TesterStationSocket, Vector3.one * 0.68f, 0f, "Agent_Tester", new Vector3(0f, 0f, -0.050f));

            // Mount Spatial Courier System, Attention Beacon, and Mailbox
            if (ctrl.DispatchStationSocket != null)
            {
                var courierSys = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Spatial_FloorCourierSystem"], root.transform);
                SpatialScaleUtility.MountPreservingWorldScale(courierSys.transform, ctrl.DispatchStationSocket, Vector3.one);
            }
            if (ctrl.StatusBeaconSocket != null)
            {
                var beacon = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WorkspaceAttentionBeacon"], root.transform);
                SpatialScaleUtility.MountPreservingWorldScale(beacon.transform, ctrl.StatusBeaconSocket, Vector3.one * 0.90f);
            }
            if (ctrl.MailboxSocket != null)
            {
                var mailbox = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["SpatialMailbox"], root.transform);
                SpatialScaleUtility.MountPreservingWorldScale(mailbox.transform, ctrl.MailboxSocket, Vector3.one * 0.90f);
            }

            return SavePrefab(root, "Spatial_FloorOffice_Complete");
        }

        private static GameObject BuildCompositeCeilingComplete(Dictionary<string, GameObject> prefabs)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Spatial_CeilingWorkspaceAssembly"]);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            root.name = "Spatial_CeilingPod_Complete";

            var ctrl = root.GetComponent<CeilingWorkspacePodController>();
            ctrl.ResolveTransforms();
            Transform outerPod = FindChildRecursive(root.transform, "SuspendedOfficePod") ?? FindChildRecursive(root.transform, "PodRoot");
            if (outerPod != null) SetPrivateField(ctrl, "podRoot", outerPod);

            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Research_Optimized.prefab",
                ctrl.ResearchStationSocket, Vector3.one * 0.52f, 0f, "Workstation_Research");
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Developer_Optimized.prefab",
                ctrl.DeveloperStationSocket, Vector3.one * 0.52f, 0f, "Workstation_Developer");
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Testing_Optimized.prefab",
                ctrl.TesterStationSocket, Vector3.one * 0.52f, 0f, "Workstation_Testing");
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Factory/SoftwareFactory_Complete.prefab",
                ctrl.FactorySocket, Vector3.one * 0.45f, 0f, "SoftwareFactory_Complete");

            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Characters/Agent_Researcher.prefab",
                ctrl.ResearchStationSocket, Vector3.one * 0.58f, 0f, "Agent_Researcher", new Vector3(0f, 0f, -0.040f));
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Characters/Agent_Developer.prefab",
                ctrl.DeveloperStationSocket, Vector3.one * 0.58f, 0f, "Agent_Developer", new Vector3(0f, 0f, -0.040f));
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Characters/Agent_Tester.prefab",
                ctrl.TesterStationSocket, Vector3.one * 0.58f, 0f, "Agent_Tester", new Vector3(0f, 0f, -0.040f));

            if (ctrl.MailboxSocket != null)
            {
                var mailbox = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["SpatialMailbox"], root.transform);
                SpatialScaleUtility.MountPreservingWorldScale(mailbox.transform, ctrl.MailboxSocket, Vector3.one * 0.72f);
            }

            return SavePrefab(root, "Spatial_CeilingPod_Complete");
        }

        private static void MountExistingPrefab(
            string assetPath,
            Transform socket,
            Vector3 desiredWorldScale,
            float yawDeg,
            string instanceName,
            Vector3 localOffset = default)
        {
            if (socket == null) return;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (src == null)
            {
                Debug.LogWarning($"[SpatialWorkspaceArtSetup] Optional reference asset missing: {assetPath}");
                return;
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, socket);
            inst.name = instanceName;
            SpatialScaleUtility.MountPreservingWorldScale(inst.transform, socket, desiredWorldScale, resetLocalPose: true);
            inst.transform.localPosition = localOffset;
            inst.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
        }

        // =====================================================================
        // 4. TEXTMESHPRO & UTILITY HELPERS
        // =====================================================================

        private static void AddWorldTextLabel(
            Transform parent,
            string name,
            string text,
            Vector3 localPos,
            Vector3 localEuler,
            float scale,
            Color col,
            Vector2 rectSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            // Note from SPATIAL_INTERACTION_GUIDELINES.md: TMP readable front is -Z, so add 180 Y to face +Z user.
            go.transform.localRotation = Quaternion.Euler(localEuler) * Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = Vector3.one * scale;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 36f;
            tmp.color = col;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(InterFontPath);
            if (font != null) tmp.font = font;

            var rt = go.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = rectSize;
        }

        private static Transform FindChildRecursive(Transform root, string targetName)
        {
            if (root == null) return null;
            if (root.name == targetName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildRecursive(root.GetChild(i), targetName);
                if (found != null) return found;
            }
            return null;
        }

        private static void SetPrivateField(object target, string fieldName, object val)
        {
            if (target == null) return;
            var f = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(target, val);
        }

        // =====================================================================
        // 5. VALIDATION & PERFORMANCE METRICS
        // =====================================================================

        private static ValidationInventory ValidateAndCollectMetrics(Dictionary<string, GameObject> prefabs)
        {
            var inv = new ValidationInventory
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                totalPrefabs = prefabs.Count
            };

            foreach (var kv in prefabs)
            {
                var go = UnityEngine.Object.Instantiate(kv.Value);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;

                var m = new AssetMetrics
                {
                    prefabName = kv.Key,
                    prefabPath = AssetDatabase.GetAssetPath(kv.Value),
                };

                // Missing scripts check
                int missing = 0;
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                    if (c == null) missing++;
                m.missingScripts = missing;
                inv.totalMissingScripts += missing;

                // Mesh / Triangle / Vertex / Material audit
                var matNames = new HashSet<string>();
                int tris = 0, verts = 0, meshes = 0;
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                m.rendererCount = renderers.Length;
                m.skinnedRendererCount = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
                m.colliderCount = go.GetComponentsInChildren<Collider>(true).Length;

                Bounds combined = new Bounds(go.transform.position, Vector3.zero);
                bool hasBounds = false;

                foreach (var r in renderers)
                {
                    Mesh shared = null;
                    if (r is SkinnedMeshRenderer smr) shared = smr.sharedMesh;
                    else if (r is MeshRenderer)
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf != null) shared = mf.sharedMesh;
                    }

                    if (shared != null)
                    {
                        meshes++;
                        verts += shared.vertexCount;
                        for (int s = 0; s < shared.subMeshCount; s++)
                            tris += (int)(shared.GetIndexCount(s) / 3);
                    }

                    foreach (var mat in r.sharedMaterials)
                    {
                        if (mat == null) inv.totalMissingMaterials++;
                        else matNames.Add(mat.name);
                    }

                    if (!hasBounds) { combined = r.bounds; hasBounds = true; }
                    else combined.Encapsulate(r.bounds);
                }

                m.meshCount = meshes;
                m.vertexCount = verts;
                m.triangleCount = tris;
                m.uniqueMaterialCount = matNames.Count;
                m.materialNames = matNames.OrderBy(s => s).ToArray();
                m.boundsSize = hasBounds
                    ? $"{combined.size.x:F3} x {combined.size.y:F3} x {combined.size.z:F3} m"
                    : "empty";

                // Socket scale verification (only verify top-level spatial sockets, not internal sub-sockets
                // inside intentionally scaled miniature child prefabs like Workstations or Factory)
                int sockCount = 0;
                bool unitSocketsOk = true;
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (!IsSocketName(t.name)) continue;
                    if (IsInsideMountedChildAsset(t, go.transform)) continue;
                    sockCount++;
                    Vector3 ls = t.lossyScale;
                    if (Mathf.Abs(ls.x - 1f) > 0.02f || Mathf.Abs(ls.y - 1f) > 0.02f || Mathf.Abs(ls.z - 1f) > 0.02f)
                    {
                        unitSocketsOk = false;
                        inv.totalBrokenSockets++;
                        Debug.LogError($"[SpatialWorkspaceArtSetup] Socket '{t.name}' on '{kv.Key}' has non-unit lossyScale={ls}");
                    }
                }
                m.socketCount = sockCount;
                m.allSocketsUnitScale = unitSocketsOk;

                // Animator states verification
                var animStates = new List<string>();
                var anim = go.GetComponentInChildren<Animator>(true);
                if (anim != null && anim.runtimeAnimatorController is AnimatorController ac)
                {
                    foreach (var cs in ac.layers[0].stateMachine.states)
                    {
                        if (cs.state.motion == null)
                            Debug.LogError($"[SpatialWorkspaceArtSetup] Animator state '{cs.state.name}' in {kv.Key} has NULL motion clip!");
                        animStates.Add(cs.state.name);
                    }
                }
                m.animatorStates = animStates.ToArray();

                inv.assets.Add(m);
                UnityEngine.Object.DestroyImmediate(go);
            }

            return inv;
        }

        // =====================================================================
        // 6. VALIDATION SCENE & MULTI-ANGLE GAME VIEW SCREENSHOTS
        // =====================================================================

        private static List<string> BuildValidationSceneAndCapture(
            Dictionary<string, GameObject> prefabs,
            Dictionary<string, Material> matMap)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Clean neutral studio lighting
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.78f, 0.85f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.62f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.30f, 0.28f, 1f);

            var sunGo = new GameObject("Studio_KeyLight");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1.0f, 0.97f, 0.92f);
            sunGo.transform.rotation = Quaternion.Euler(46f, 145f, 0f);

            var fillGo = new GameObject("Studio_FillLight");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.color = new Color(0.82f, 0.90f, 1.0f);
            fillGo.transform.rotation = Quaternion.Euler(35f, -45f, 0f);

            // Studio Floor (y = 0) + Ceiling Reference Slab (y = 2.40) + Seated Desk Proxy (y = 0.74)
            matMap.TryGetValue("Mat_FeltSlate", out var floorMat);
            matMap.TryGetValue("Mat_WarmBeige", out var ceilMat);
            matMap.TryGetValue("Mat_WarmWood", out var deskMat);
            CreateStudioBox("Studio_Floor", new Vector3(0f, -0.025f, 0f), new Vector3(8.5f, 0.05f, 5.5f), floorMat);
            CreateStudioBox("Studio_CeilingBeam", new Vector3(0.85f, 2.43f, 0f), new Vector3(2.8f, 0.06f, 0.9f), ceilMat);
            CreateStudioBox("Studio_DeskProxy", new Vector3(-0.05f, 0.37f, 0.68f), new Vector3(1.10f, 0.74f, 0.55f), deskMat);

            // 1. Full Floor Office Complete
            var floorFull = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Spatial_FloorOffice_Complete"]);
            floorFull.name = "Showcase_01_FloorOffice_Complete";
            floorFull.transform.position = new Vector3(-1.05f, 0f, -0.05f);

            // 2. Compact Floor Office Platform
            var floorCompact = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["FloorOfficePlatform_Compact"]);
            floorCompact.name = "Showcase_02_FloorOffice_Compact";
            floorCompact.transform.position = new Vector3(-1.90f, 0f, 0.15f);

            // 3. Retractable Ceiling Workspace — Docked (closed 4-leaf Iris bay, snug zero-clip cradle)
            var ceilDocked = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Spatial_CeilingPod_Complete"]);
            ceilDocked.name = "Showcase_03_CeilingPod_Docked";
            ceilDocked.transform.position = new Vector3(0.05f, 2.40f, -0.05f);
            var dockCtrl = ceilDocked.GetComponent<CeilingWorkspacePodController>();
            dockCtrl.ConfigureRoomPoses(new Vector3(0.05f, 2.012f, -0.05f), new Vector3(0.05f, 1.00f, -0.05f), Quaternion.identity);
            dockCtrl.SnapToPoseImmediate(false);
            ceilDocked.GetComponent<CeilingDeploymentVFXController>()?.ApplySnapshotImmediate(CeilingWorkspacePodController.PodState.DockedIdle, 0f);

            // 4a. Retractable Ceiling Workspace — Active Descension VFX Showcase (Iris open + Portal Cone + Tether Motes + Ripple)
            var ceilDesc = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Spatial_CeilingPod_Complete"]);
            ceilDesc.name = "Showcase_04b_CeilingPod_DescendingVFX";
            ceilDesc.transform.position = new Vector3(0.85f, 2.40f, -0.05f);
            var descCtrl = ceilDesc.GetComponent<CeilingWorkspacePodController>();
            descCtrl.ConfigureRoomPoses(new Vector3(0.85f, 2.012f, -0.05f), new Vector3(0.85f, 1.56f, -0.05f), Quaternion.identity);
            descCtrl.SnapToPoseImmediate(true);
            SampleAnimationState(ceilDesc, "CeilingMount", $"{ModelsDir}/CeilingMount.fbx", "Mount_Spool_Spin");
            SampleAnimationState(ceilDesc, "SuspendedOfficePod", $"{ModelsDir}/SuspendedOfficePod.fbx", "Pod_Descending");
            ceilDesc.GetComponent<CeilingDeploymentVFXController>()?.ApplySnapshotImmediate(CeilingWorkspacePodController.PodState.Descending, 0.35f);

            // 4b. Retractable Ceiling Workspace — Deployed (Shelf open at comfortable desk height)
            var ceilDeployed = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Spatial_CeilingPod_Complete"]);
            ceilDeployed.name = "Showcase_04_CeilingPod_Deployed";
            ceilDeployed.transform.position = new Vector3(1.65f, 2.40f, -0.05f);
            var depCtrl = ceilDeployed.GetComponent<CeilingWorkspacePodController>();
            depCtrl.ConfigureRoomPoses(new Vector3(1.65f, 2.012f, -0.05f), new Vector3(1.65f, 1.02f, -0.05f), Quaternion.identity);
            depCtrl.SnapToPoseImmediate(true);
            SampleAnimationState(ceilDeployed, "CeilingMount", $"{ModelsDir}/CeilingMount.fbx", "Mount_Spool_Spin");
            SampleAnimationState(ceilDeployed, "SuspendedOfficePod", $"{ModelsDir}/SuspendedOfficePod.fbx", "Pod_Deployed_Idle");
            ceilDeployed.GetComponent<CeilingDeploymentVFXController>()?.ApplySnapshotImmediate(CeilingWorkspacePodController.PodState.DeployedIdle, 0f);

            // 4c. Full vs LowCostNoParticles vs MinimalIrisHalo VFX comparison strip (separate beam away from main trio)
            CreateStudioBox("Studio_CeilingBeam_VfxCompare", new Vector3(3.45f, 2.43f, 0f), new Vector3(2.9f, 0.06f, 0.9f), ceilMat);
            var ceilFullCmp = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Spatial_CeilingPod_Complete"]);
            ceilFullCmp.name = "Showcase_04c0_CeilingPod_VFX_Full";
            ceilFullCmp.transform.position = new Vector3(2.65f, 2.40f, -0.05f);
            var fullCmpCtrl = ceilFullCmp.GetComponent<CeilingWorkspacePodController>();
            fullCmpCtrl.ConfigureRoomPoses(new Vector3(2.65f, 2.012f, -0.05f), new Vector3(2.65f, 1.56f, -0.05f), Quaternion.identity);
            fullCmpCtrl.SnapToPoseImmediate(true);
            SampleAnimationState(ceilFullCmp, "CeilingMount", $"{ModelsDir}/CeilingMount.fbx", "Mount_Spool_Spin");
            SampleAnimationState(ceilFullCmp, "SuspendedOfficePod", $"{ModelsDir}/SuspendedOfficePod.fbx", "Pod_Descending");
            ceilFullCmp.GetComponent<CeilingDeploymentVFXController>()?.ApplySnapshotImmediate(CeilingWorkspacePodController.PodState.Descending, 0.35f);

            var ceilLite = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Spatial_CeilingPod_Complete"]);
            ceilLite.name = "Showcase_04c_CeilingPod_VFX_LowCost";
            ceilLite.transform.position = new Vector3(3.45f, 2.40f, -0.05f);
            var liteCtrl = ceilLite.GetComponent<CeilingWorkspacePodController>();
            liteCtrl.ConfigureRoomPoses(new Vector3(3.45f, 2.012f, -0.05f), new Vector3(3.45f, 1.56f, -0.05f), Quaternion.identity);
            liteCtrl.SnapToPoseImmediate(true);
            SampleAnimationState(ceilLite, "CeilingMount", $"{ModelsDir}/CeilingMount.fbx", "Mount_Spool_Spin");
            SampleAnimationState(ceilLite, "SuspendedOfficePod", $"{ModelsDir}/SuspendedOfficePod.fbx", "Pod_Descending");
            var liteVfx = ceilLite.GetComponent<CeilingDeploymentVFXController>();
            if (liteVfx != null)
            {
                liteVfx.SetQualityPreset(CeilingVfxQualityPreset.LowCostNoParticles);
                liteVfx.ApplySnapshotImmediate(CeilingWorkspacePodController.PodState.Descending, 0.35f);
            }

            var ceilMin = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Spatial_CeilingPod_Complete"]);
            ceilMin.name = "Showcase_04d_CeilingPod_VFX_MinimalHalo";
            ceilMin.transform.position = new Vector3(4.25f, 2.40f, -0.05f);
            var minCtrl = ceilMin.GetComponent<CeilingWorkspacePodController>();
            minCtrl.ConfigureRoomPoses(new Vector3(4.25f, 2.012f, -0.05f), new Vector3(4.25f, 1.56f, -0.05f), Quaternion.identity);
            minCtrl.SnapToPoseImmediate(true);
            SampleAnimationState(ceilMin, "CeilingMount", $"{ModelsDir}/CeilingMount.fbx", "Mount_Spool_Spin");
            SampleAnimationState(ceilMin, "SuspendedOfficePod", $"{ModelsDir}/SuspendedOfficePod.fbx", "Pod_Descending");
            var minVfx = ceilMin.GetComponent<CeilingDeploymentVFXController>();
            if (minVfx != null)
            {
                minVfx.SetQualityPreset(CeilingVfxQualityPreset.MinimalIrisHalo);
                minVfx.ApplySnapshotImmediate(CeilingWorkspacePodController.PodState.Descending, 0.35f);
            }

            // 5. Attention Beacon 7-State Showcase Row (on desk height shelf for close-up inspection)
            var beaconStates = (SpatialBeaconVisualController.BeaconState[])Enum.GetValues(
                typeof(SpatialBeaconVisualController.BeaconState));
            for (int i = 0; i < beaconStates.Length; i++)
            {
                var b = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WorkspaceAttentionBeacon"]);
                b.name = $"Showcase_05_Beacon_{beaconStates[i]}";
                b.transform.position = new Vector3(-0.45f + i * 0.145f, 0.74f, 0.85f);
                var bc = b.GetComponent<SpatialBeaconVisualController>();
                bc.ResolveReferences();
                bc.SetState(beaconStates[i], immediate: true);
            }

            // 6. Active Courier + Agent Conversation Perch Duo (near seated desk height)
            var perchGo = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["AgentConversationPerch"]);
            perchGo.name = "Showcase_06_ConversationPerch";
            perchGo.transform.position = new Vector3(-0.32f, 0.86f, 0.56f);
            SampleAnimationState(perchGo, null, $"{ModelsDir}/AgentConversationPerch.fbx", "Perch_Listening_Idle");
            var perchCtrl = perchGo.GetComponent<AgentConversationPerchController>();
            perchCtrl.ResolveReferences();
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Characters/Agent_Researcher.prefab",
                perchCtrl.AgentStandSocket, Vector3.one * 0.78f, 0f, "Perch_Agent_Researcher");
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Documents/Doc_ApprovalPaper.prefab",
                perchCtrl.DocumentPresentationSocket, Vector3.one * 0.55f, 0f, "Perch_Doc");

            var courierGo = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["DocumentCourier"]);
            courierGo.name = "Showcase_06b_PresentingCourier";
            courierGo.transform.position = new Vector3(0.24f, 0.90f, 0.56f);
            SampleAnimationState(courierGo, null, $"{ModelsDir}/DocumentCourier.fbx", "Courier_Waiting_Idle");
            Transform docSocket = FindChildRecursive(courierGo.transform, "DocumentAttachSocket");
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Documents/Doc_ResearchDocument.prefab",
                docSocket, Vector3.one * 0.75f, 180f, "Courier_CarriedDoc");

            // 7. Mailbox + Placement Ghost Previews (Valid vs Invalid) placed cleanly in unobstructed showcase view
            var mbGo = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["SpatialMailbox"]);
            mbGo.name = "Showcase_07_SpatialMailbox";
            mbGo.transform.position = new Vector3(0.46f, 0.74f, 0.55f);
            var mbCtrl = mbGo.GetComponent<SpatialMailboxController>();
            mbCtrl.ResolveReferences();
            mbCtrl.UpdateCounts(3, 2, 1);

            var ghostFloor = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["PlacementGhostPreview_Floor"]);
            ghostFloor.name = "Showcase_07b_GhostFloor_Valid";
            ghostFloor.transform.position = new Vector3(2.40f, 0.002f, 0.55f);
            var gfCtrl = ghostFloor.GetComponent<PlacementGhostPreviewController>();
            gfCtrl.SetValid(true, "FLOOR PLACEMENT OK");

            var ghostCeil = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["PlacementGhostPreview_Ceiling"]);
            ghostCeil.name = "Showcase_07c_GhostCeiling_Blocked";
            ghostCeil.transform.position = new Vector3(2.40f, 1.48f, 0.55f);
            var gcCtrl = ghostCeil.GetComponent<PlacementGhostPreviewController>();
            gcCtrl.SetValid(false, "OBSTRUCTED CEILING");

            // Validation Main Camera
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.21f, 0.24f, 0.29f, 1f);
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 25f;

            // Diagnostic check: warn if any non-studio renderer blows up > 2.0m
            foreach (var r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)))
            {
                if (r.name.StartsWith("Studio_")) continue;
                if (r.bounds.size.magnitude > 2.2f)
                    Debug.LogWarning($"[OversizeCheck] {r.transform.root.name}/{r.name}: center={r.bounds.center}, size={r.bounds.size}, lossyScale={r.transform.lossyScale}");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Warmup render pass so URP initializes shaders/buffers cleanly before first PNG capture
            CaptureCameraShot(cam, "_warmup.png", new Vector3(-0.10f, 1.55f, 3.15f), new Vector3(-0.10f, 0.90f, 0.15f), 256, 256);
            string warmupPath = Path.Combine(ScreenshotDir, "_warmup.png");
            if (File.Exists(warmupPath)) File.Delete(warmupPath);

            // Capture 6 validation views via RenderTexture
            var captured = new List<string>
            {
                CaptureCameraShot(cam, "01_Validation_Overview.png",
                    new Vector3(0.30f, 1.50f, 4.35f), new Vector3(0.30f, 0.92f, 0.25f), 1440, 900),
                CaptureCameraShot(cam, "02_FloorOffice_Complete.png",
                    new Vector3(-1.05f, 0.52f, 0.85f), new Vector3(-1.05f, 0.08f, -0.05f), 1280, 800),
                CaptureCameraShot(cam, "03_CeilingPod_Docked_vs_Deployed.png",
                    new Vector3(0.85f, 1.68f, 2.55f), new Vector3(0.85f, 1.68f, -0.05f), 1280, 800),
                CaptureCameraShot(cam, "04_AttentionBeacon_7States.png",
                    new Vector3(-0.02f, 0.92f, 1.48f), new Vector3(-0.02f, 0.78f, 0.85f), 1280, 720),
                CaptureCameraShot(cam, "05_Courier_And_Perch_Showcase.png",
                    new Vector3(-0.04f, 1.08f, 1.18f), new Vector3(-0.04f, 0.88f, 0.56f), 1280, 720),
                CaptureCameraShot(cam, "06_Mailbox_And_GhostPreviews.png",
                    new Vector3(1.55f, 0.90f, 3.05f), new Vector3(1.55f, 0.72f, 0.55f), 1280, 720),
                CaptureCameraShot(cam, "07_CeilingVFX_Full_vs_LowCost.png",
                    new Vector3(3.45f, 1.88f, 2.75f), new Vector3(3.45f, 1.88f, -0.05f), 1280, 720)
            };

            // Reset Main Camera to clean overview pose before saving final scene
            cam.transform.position = new Vector3(0.15f, 1.50f, 3.85f);
            cam.transform.LookAt(new Vector3(0.15f, 0.92f, 0.25f));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            return captured;
        }

        private static void CreateStudioBox(string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static void SampleAnimationState(GameObject root, string targetChild, string fbxPath, string clipName)
        {
            Transform target = string.IsNullOrEmpty(targetChild) ? root.transform : FindChildRecursive(root.transform, targetChild);
            if (target == null) return;
            var map = LoadClipMap(fbxPath);
            if (map.TryGetValue(clipName, out var clip) && clip != null)
            {
                clip.SampleAnimation(target.gameObject, 0f);
            }
        }

        private static string CaptureCameraShot(
            Camera cam,
            string fileName,
            Vector3 pos,
            Vector3 lookAt,
            int width,
            int height)
        {
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            cam.targetTexture = null;
            RenderTexture.active = prev;
            UnityEngine.Object.DestroyImmediate(rt);

            string fullPath = Path.Combine(ScreenshotDir, fileName).Replace("\\", "/");
            File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            return fullPath;
        }
    }
}
