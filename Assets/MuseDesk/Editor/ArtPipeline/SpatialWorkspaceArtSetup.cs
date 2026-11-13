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

            AssetDatabase.SaveAssets();
            return map;
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

            float platW = isCompact ? 0.56f : 0.68f;
            float platD = isCompact ? 0.36f : 0.44f;

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
            float plaqueZ = isCompact ? 0.2055f : 0.2455f;
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

            // Low-profile collider matching the pod deck
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.020f, 0f);
            bc.size = new Vector3(0.58f, 0.040f, 0.34f);

            // Small front slate status display TMP readout attached cleanly at InfoDisplaySocket
            Transform dispSock = FindChildRecursive(go.transform, "InfoDisplaySocket");
            Transform dispParent = dispSock != null ? dispSock : go.transform;
            Vector3 dispPos = dispSock != null ? new Vector3(0f, 0f, 0.0062f) : new Vector3(0.190f, 0.058f, 0.151f);
            AddWorldTextLabel(dispParent, "Pod_TaskDisplayTMP", "POD READY\nSTANDBY",
                dispPos, new Vector3(-18f, 14f, 0f), 0.0032f,
                new Color(0.82f, 0.94f, 0.92f, 1f), new Vector2(22f, 9f));

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
            Transform dockedPose = FindChildRecursive(mount.transform, "DockedPose");
            if (dockedPose != null) dockedPose.localPosition = new Vector3(0f, -0.376f, 0f);
            Transform inspPose = FindChildRecursive(mount.transform, "InspectionPose");
            if (inspPose != null) inspPose.localPosition = new Vector3(0f, -1.42f, 0f);

            var pod = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["SuspendedOfficePod"], root.transform);
            pod.name = "SuspendedOfficePod";
            pod.transform.localPosition = new Vector3(0f, -0.376f, 0f);

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

            return SavePrefab(root, "Spatial_CeilingWorkspaceAssembly");
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
            CreateStudioBox("Studio_Floor", new Vector3(0f, -0.025f, 0f), new Vector3(6.5f, 0.05f, 5.0f), floorMat);
            CreateStudioBox("Studio_CeilingBeam", new Vector3(0.65f, 2.43f, 0f), new Vector3(2.4f, 0.06f, 0.9f), ceilMat);
            CreateStudioBox("Studio_DeskProxy", new Vector3(-0.05f, 0.37f, 0.68f), new Vector3(1.10f, 0.74f, 0.55f), deskMat);

            // 1. Full Floor Office Complete
            var floorFull = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Spatial_FloorOffice_Complete"]);
            floorFull.name = "Showcase_01_FloorOffice_Complete";
            floorFull.transform.position = new Vector3(-1.05f, 0f, -0.05f);

            // 2. Compact Floor Office Platform
            var floorCompact = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["FloorOfficePlatform_Compact"]);
            floorCompact.name = "Showcase_02_FloorOffice_Compact";
            floorCompact.transform.position = new Vector3(-1.90f, 0f, 0.15f);

            // 3. Retractable Ceiling Workspace — Docked
            var ceilDocked = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Spatial_CeilingPod_Complete"]);
            ceilDocked.name = "Showcase_03_CeilingPod_Docked";
            ceilDocked.transform.position = new Vector3(0.15f, 2.40f, -0.05f);
            var dockCtrl = ceilDocked.GetComponent<CeilingWorkspacePodController>();
            dockCtrl.ConfigureRoomPoses(new Vector3(0.15f, 2.024f, -0.05f), new Vector3(0.15f, 1.00f, -0.05f), Quaternion.identity);
            dockCtrl.SnapToPoseImmediate(false);

            // 4. Retractable Ceiling Workspace — Deployed (with sampled Pod_Deployed_Idle clip!)
            var ceilDeployed = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Spatial_CeilingPod_Complete"]);
            ceilDeployed.name = "Showcase_04_CeilingPod_Deployed";
            ceilDeployed.transform.position = new Vector3(1.00f, 2.40f, -0.05f);
            var depCtrl = ceilDeployed.GetComponent<CeilingWorkspacePodController>();
            depCtrl.ConfigureRoomPoses(new Vector3(1.00f, 2.024f, -0.05f), new Vector3(1.00f, 1.02f, -0.05f), Quaternion.identity);
            depCtrl.SnapToPoseImmediate(true);
            SampleAnimationState(ceilDeployed, "SuspendedOfficePod", $"{ModelsDir}/SuspendedOfficePod.fbx", "Pod_Deployed_Idle");

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
            ghostFloor.transform.position = new Vector3(1.65f, 0.002f, 0.55f);
            var gfCtrl = ghostFloor.GetComponent<PlacementGhostPreviewController>();
            gfCtrl.SetValid(true, "FLOOR PLACEMENT OK");

            var ghostCeil = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["PlacementGhostPreview_Ceiling"]);
            ghostCeil.name = "Showcase_07c_GhostCeiling_Blocked";
            ghostCeil.transform.position = new Vector3(1.65f, 1.48f, 0.55f);
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
                    new Vector3(0.15f, 1.50f, 3.85f), new Vector3(0.15f, 0.92f, 0.25f), 1440, 900),
                CaptureCameraShot(cam, "02_FloorOffice_Complete.png",
                    new Vector3(-1.05f, 0.52f, 0.85f), new Vector3(-1.05f, 0.08f, -0.05f), 1280, 800),
                CaptureCameraShot(cam, "03_CeilingPod_Docked_vs_Deployed.png",
                    new Vector3(0.58f, 1.65f, 2.05f), new Vector3(0.58f, 1.65f, -0.05f), 1280, 800),
                CaptureCameraShot(cam, "04_AttentionBeacon_7States.png",
                    new Vector3(-0.02f, 0.92f, 1.48f), new Vector3(-0.02f, 0.78f, 0.85f), 1280, 720),
                CaptureCameraShot(cam, "05_Courier_And_Perch_Showcase.png",
                    new Vector3(-0.04f, 1.08f, 1.18f), new Vector3(-0.04f, 0.88f, 0.56f), 1280, 720),
                CaptureCameraShot(cam, "06_Mailbox_And_GhostPreviews.png",
                    new Vector3(1.10f, 0.90f, 2.75f), new Vector3(1.10f, 0.72f, 0.55f), 1280, 720)
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
