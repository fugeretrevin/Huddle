using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MuseDesk.Art;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MuseDesk.Editor.ArtPipeline
{
    /// <summary>
    /// Phase 3 Workshop Treehouse Wall Habitat art setup, material binding,
    /// AnimatorController builder, validated prefab assembler, preview scene builder,
    /// multi-angle Unity renderer, and automated socket/scale/material validator.
    /// </summary>
    public static class WallHabitatArtSetup
    {
        private const string ModelsDir = "Assets/MuseDesk/Art/Models/WallHabitat";
        private const string MatsDir = "Assets/MuseDesk/Art/Materials";
        private const string ControllersDir = "Assets/MuseDesk/Art/Controllers/WallHabitat";
        private const string PrefabsDir = "Assets/MuseDesk/Art/Prefabs/WallHabitat";
        private const string ScenePath = "Assets/MuseDesk/Scenes/WallHabitatPreviewScene.unity";
        private const string ScreenshotDir = "Screenshots/WallHabitat/Unity";
        private const string InventoryPath = "docs/Art/WALL_HABITAT_INVENTORY.json";
        private const string InterFontPath = "Assets/MuseDesk/Art/Fonts/Inter-Medium SDF.asset";

        [Serializable]
        private class AssetMetrics
        {
            public string prefabName;
            public string prefabPath;
            public int meshCount;
            public int rendererCount;
            public int skinnedRendererCount;
            public int colliderCount;
            public int vertexCount;
            public int triangleCount;
            public int uniqueMaterialCount;
            public string[] materialNames;
            public int socketCount;
            public bool allSocketsUnitScale;
            public int missingScripts;
            public string boundsSize;
            public string[] animatorStates;
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
            public int missingRequired3TierSockets;
            public int missingRequired2TierSockets;
            public List<AssetMetrics> assets = new List<AssetMetrics>();
            public List<string> screenshots = new List<string>();
        }

        private static readonly string[] StaticModuleNames =
        {
            "WallHabitat_Backplate_Full",
            "WallHabitat_Backplate_Compact",
            "WallHabitat_CubicleBay",
            "WallHabitat_CubicleBay_Compact",
            "WallHabitat_HallwaySegment",
            "WallHabitat_MidPlanningNook",
            "WallHabitat_ManagerLookout",
            "WallHabitat_PlacementGhost",
            "WallHabitat_TaskCard_Researcher",
            "WallHabitat_TaskCard_Developer",
            "WallHabitat_TaskCard_Tester",
            "WallHabitat_TaskCard_Manager",
            "WallHabitat_SelectionHalo",
        };

        private static readonly string[] AnimatedModuleNames =
        {
            "WallHabitat_ElevatorSpine_3Tier",
            "WallHabitat_ElevatorSpine_2Tier",
            "WallHabitat_ElevatorCar",
            "WallHabitat_TaskPrinter",
            "WallHabitat_DeskVisitPerch",
            "WallHabitat_LargeAgentPortal",
            "Agent_Manager",
        };

        [MenuItem("MuseDesk/Art Pipeline/Build Wall Habitat Assets & Preview")]
        public static void BuildAll()
        {
            EnsureFolders();

            // 1. Force reimport all WallHabitat FBXs first so ModelBoundsFixer metadata/clips are up to date
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string modName in StaticModuleNames.Concat(AnimatedModuleNames))
            {
                string fbxPath = $"{ModelsDir}/{modName}.fbx";
                if (File.Exists(fbxPath))
                    AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }

            // 2. Ensure shared URP materials + single-pass Mat_Wall_PortalRim + SoftGlow particle mat
            Dictionary<string, Material> matMap = EnsureWallHabitatMaterials();

            // 3. Build Animator Controllers
            Dictionary<string, RuntimeAnimatorController> controllers = BuildAllControllers();

            // 4. Build individual module prefabs
            var prefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

            foreach (string staticName in StaticModuleNames)
            {
                var p = BuildIndividualPrefab(staticName, animated: false, matMap, controllers);
                if (p != null) prefabs[staticName] = p;
            }

            foreach (string animName in AnimatedModuleNames)
            {
                var p = BuildIndividualPrefab(animName, animated: true, matMap, controllers);
                if (p != null) prefabs[animName] = p;
            }

            // 5. Assemble Composite Reference Prefabs
            prefabs["WallHabitat_Complete_3Tier"] = BuildCompositeHabitatPrefab(prefabs, isCompact: false);
            prefabs["WallHabitat_Complete_2Tier"] = BuildCompositeHabitatPrefab(prefabs, isCompact: true);
            prefabs["WallHabitat_DeskVisit_Complete"] = BuildCompositeDeskVisitPrefab(prefabs);
            prefabs["WallHabitat_LargePortal_WithProxy"] = BuildCompositeLargePortalPrefab(prefabs);

            // 6. Validate metrics, socket scales, materials, and required contract coverage
            ValidationInventory inventory = ValidateAndCollectMetrics(prefabs);

            // 7. Build Unity Preview Scene & multi-angle renders
            inventory.screenshots = BuildPreviewSceneAndCapture(prefabs, matMap);

            // 8. Save Inventory JSON
            string json = JsonUtility.ToJson(inventory, true);
            File.WriteAllText(InventoryPath, json, Encoding.UTF8);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[WallHabitatArtSetup] Complete! Built {inventory.totalPrefabs} prefabs, " +
                      $"missingScripts={inventory.totalMissingScripts}, brokenSockets={inventory.totalBrokenSockets}, " +
                      $"missingMats={inventory.totalMissingMaterials}, req3TierMissing={inventory.missingRequired3TierSockets}, " +
                      $"req2TierMissing={inventory.missingRequired2TierSockets}. Inventory -> {InventoryPath}");

            if (inventory.totalMissingScripts > 0 ||
                inventory.totalBrokenSockets > 0 ||
                inventory.totalMissingMaterials > 0 ||
                inventory.missingRequired3TierSockets > 0 ||
                inventory.missingRequired2TierSockets > 0)
            {
                throw new Exception($"[WallHabitatArtSetup] Validation failed: missingScripts={inventory.totalMissingScripts}, " +
                                    $"brokenSockets={inventory.totalBrokenSockets}, missingMats={inventory.totalMissingMaterials}, " +
                                    $"req3TierMissing={inventory.missingRequired3TierSockets}, req2TierMissing={inventory.missingRequired2TierSockets}");
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
            Directory.CreateDirectory(ScreenshotDir);
            Directory.CreateDirectory(Path.GetDirectoryName(InventoryPath) ?? "docs/Art");
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

        private static Dictionary<string, Material> EnsureWallHabitatMaterials()
        {
            var map = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MatsDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m != null) map[m.name] = m;
            }

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            // Mat_Wall_PortalRim (single-pass restrained Quest-safe transparent rim shimmer)
            string portalRimPath = $"{MatsDir}/Mat_Wall_PortalRim.mat";
            Material portalRim = AssetDatabase.LoadAssetAtPath<Material>(portalRimPath);
            if (portalRim == null)
            {
                portalRim = new Material(urpLit) { name = "Mat_Wall_PortalRim" };
                AssetDatabase.CreateAsset(portalRim, portalRimPath);
            }
            ConfigureSinglePassTransparentUrpLit(
                portalRim,
                new Color(0.26f, 0.86f, 0.80f, 0.44f),
                new Color(0.18f, 0.72f, 0.68f, 1f) * 0.90f);
            EditorUtility.SetDirty(portalRim);
            map["Mat_Wall_PortalRim"] = portalRim;

            // Ensure Mat_BlueprintBlue exists as opaque matte accent
            if (!map.ContainsKey("Mat_BlueprintBlue"))
            {
                var bp = new Material(urpLit) { name = "Mat_BlueprintBlue" };
                bp.SetColor("_BaseColor", new Color(0.24f, 0.42f, 0.64f, 1f));
                bp.SetFloat("_Smoothness", 0.35f);
                AssetDatabase.CreateAsset(bp, $"{MatsDir}/Mat_BlueprintBlue.mat");
                map["Mat_BlueprintBlue"] = bp;
            }

            AssetDatabase.SaveAssets();
            return map;
        }

        private static void ConfigureSinglePassTransparentUrpLit(Material mat, Color baseCol, Color emissionCol)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)CullMode.Back);
            mat.SetFloat("_Smoothness", 0.62f);
            mat.SetColor("_BaseColor", baseCol);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emissionCol);
            mat.renderQueue = 3000;
        }

        private static void ApplySharedMaterials(GameObject root, Dictionary<string, Material> matMap)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] == null) continue;
                    string key = shared[i].name;
                    int dot = key.IndexOf('.');
                    if (dot > 0) key = key.Substring(0, dot);
                    if (matMap.TryGetValue(key, out Material mapped) && mapped != null)
                    {
                        shared[i] = mapped;
                        changed = true;
                    }
                }
                if (changed) r.sharedMaterials = shared;
            }
        }

        // =====================================================================
        // 2. ANIMATOR CONTROLLERS
        // =====================================================================

        private static Dictionary<string, RuntimeAnimatorController> BuildAllControllers()
        {
            var dict = new Dictionary<string, RuntimeAnimatorController>(StringComparer.OrdinalIgnoreCase);

            dict["WallHabitat_ElevatorSpine_3Tier"] = CreateStateController(
                $"{ControllersDir}/MuseDesk_WallElevatorSpine_3Tier.controller",
                $"{ModelsDir}/WallHabitat_ElevatorSpine_3Tier.fbx",
                "ElevatorDoors_Idle_Closed",
                new[]
                {
                    "ElevatorDoors_Idle_Closed",
                    "ElevatorDoors_Open_Lower",
                    "ElevatorDoors_Close_Lower",
                    "ElevatorDoors_Open_Mid",
                    "ElevatorDoors_Close_Mid",
                    "ElevatorDoors_Open_Manager",
                    "ElevatorDoors_Close_Manager",
                    "ElevatorWinch_Spin",
                });

            dict["WallHabitat_ElevatorSpine_2Tier"] = CreateStateController(
                $"{ControllersDir}/MuseDesk_WallElevatorSpine_2Tier.controller",
                $"{ModelsDir}/WallHabitat_ElevatorSpine_2Tier.fbx",
                "ElevatorDoors_Idle_Closed",
                new[]
                {
                    "ElevatorDoors_Idle_Closed",
                    "ElevatorDoors_Open_Lower",
                    "ElevatorDoors_Close_Lower",
                    "ElevatorDoors_Open_Manager",
                    "ElevatorDoors_Close_Manager",
                    "ElevatorWinch_Spin",
                });

            dict["WallHabitat_ElevatorCar"] = CreateStateController(
                $"{ControllersDir}/MuseDesk_WallElevatorCar.controller",
                $"{ModelsDir}/WallHabitat_ElevatorCar.fbx",
                "Cabin_Idle_Closed",
                new[]
                {
                    "Cabin_Idle_Closed",
                    "Cabin_Gate_Open",
                    "Cabin_Gate_Close",
                    "Cabin_Travel_Bob",
                    "Cabin_Arrive_Chime",
                });

            dict["WallHabitat_TaskPrinter"] = CreateStateController(
                $"{ControllersDir}/MuseDesk_WallTaskPrinter.controller",
                $"{ModelsDir}/WallHabitat_TaskPrinter.fbx",
                "Printer_Idle",
                new[]
                {
                    "Printer_Idle",
                    "Printer_Print_TaskCard",
                    "Printer_Dispatch_Card",
                    "Printer_Queue_Pulse",
                });

            dict["WallHabitat_DeskVisitPerch"] = CreateStateController(
                $"{ControllersDir}/MuseDesk_WallDeskPerch.controller",
                $"{ModelsDir}/WallHabitat_DeskVisitPerch.fbx",
                "Perch_Active_Idle",
                new[]
                {
                    "Perch_Active_Idle",
                    "Perch_Deploy",
                    "Perch_Conversing_Idle",
                    "Perch_Dismiss",
                });

            dict["WallHabitat_LargeAgentPortal"] = CreateStateController(
                $"{ControllersDir}/MuseDesk_WallLargePortal.controller",
                $"{ModelsDir}/WallHabitat_LargeAgentPortal.fbx",
                "Portal_Active_Idle",
                new[]
                {
                    "Portal_Active_Idle",
                    "Portal_Hidden_Idle",
                    "Portal_Open_Expand",
                    "Portal_Close_Collapse",
                });

            // Shared Wall + Manager + Phase 3 Agent Controller (all 55 clips from Agent_Manager.fbx)
            dict["Agent_Manager"] = BuildWallAgentController(
                $"{ControllersDir}/MuseDesk_WallAgent.controller",
                $"{ModelsDir}/Agent_Manager.fbx");

            AssetDatabase.SaveAssets();
            return dict;
        }

        private static AnimatorController BuildWallAgentController(string ctrlPath, string fbxPath)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            if (ctrl == null)
            {
                ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            }

            // Preserve existing controller GUID by resetting layers/parameters in-place
            for (int p = ctrl.parameters.Length - 1; p >= 0; p--) ctrl.RemoveParameter(p);
            for (int l = ctrl.layers.Length - 1; l >= 1; l--) ctrl.RemoveLayer(l);
            var sm = ctrl.layers[0].stateMachine;
            foreach (ChildAnimatorState child in sm.states) sm.RemoveState(child.state);
            for (int t = sm.anyStateTransitions.Length - 1; t >= 0; t--) sm.RemoveAnyStateTransition(sm.anyStateTransitions[t]);

            ctrl.AddParameter("MoveSpeed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("IsCarryingPaper", AnimatorControllerParameterType.Bool);

            // Load Agent_Manager clips, falling back to Agent_Researcher for any newly added clips if Manager FBX is not yet re-exported
            var clipsByName = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            const string researcherFbx = "Assets/MuseDesk/Art/Models/Characters/Agent_Researcher.fbx";
            if (File.Exists(researcherFbx))
            {
                foreach (var kv in LoadClipMap(researcherFbx)) clipsByName[kv.Key] = kv.Value;
            }
            foreach (var kv in LoadClipMap(fbxPath)) clipsByName[kv.Key] = kv.Value;

            var states = new Dictionary<string, AnimatorState>();
            int idx = 0;

            foreach (var kv in clipsByName.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                AnimatorState st = sm.AddState(kv.Key, new Vector3(260 + (idx % 4) * 230, (idx / 4) * 70, 0));
                st.motion = kv.Value;
                states[kv.Key] = st;
                if (kv.Key == "Idle_Breathing")
                {
                    sm.defaultState = st;
                }
                idx++;
            }

            states.TryGetValue("Idle_Breathing", out AnimatorState idleState);
            states.TryGetValue("Walk_Loop", out AnimatorState walkState);

            if (idleState != null && walkState != null)
            {
                var toWalk = idleState.AddTransition(walkState);
                toWalk.hasExitTime = false;
                toWalk.duration = 0.15f;
                toWalk.AddCondition(AnimatorConditionMode.Greater, 0.02f, "MoveSpeed");

                var toIdle = walkState.AddTransition(idleState);
                toIdle.hasExitTime = false;
                toIdle.duration = 0.15f;
                toIdle.AddCondition(AnimatorConditionMode.Less, 0.015f, "MoveSpeed");
            }

            if (states.TryGetValue("Offer_Document_Present", out var op) && states.TryGetValue("Offer_Document", out var ol))
            {
                var t = op.AddTransition(ol); t.hasExitTime = true; t.exitTime = 0.95f; t.duration = 0.12f;
            }
            if (states.TryGetValue("Offer_Document_Release", out var orl) && idleState != null)
            {
                var t = orl.AddTransition(idleState); t.hasExitTime = true; t.exitTime = 0.95f; t.duration = 0.15f;
            }
            if (states.TryGetValue("Walk_Start", out var ws) && walkState != null)
            {
                var t = ws.AddTransition(walkState); t.hasExitTime = true; t.exitTime = 0.90f; t.duration = 0.12f;
            }
            if (walkState != null && states.TryGetValue("Walk_Carry_Paper", out var wcp))
            {
                var t1 = walkState.AddTransition(wcp); t1.hasExitTime = false; t1.duration = 0.15f; t1.AddCondition(AnimatorConditionMode.If, 0f, "IsCarryingPaper");
                var t2 = wcp.AddTransition(walkState); t2.hasExitTime = false; t2.duration = 0.15f; t2.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsCarryingPaper");
                if (idleState != null)
                {
                    var t3 = wcp.AddTransition(idleState); t3.hasExitTime = false; t3.duration = 0.15f; t3.AddCondition(AnimatorConditionMode.Less, 0.015f, "MoveSpeed");
                }
            }
            foreach (string rs in CharacterMaskAndSocketSetup.AutoReturnOneShotClips)
            {
                if (states.TryGetValue(rs, out var st) && idleState != null)
                {
                    var t = st.AddTransition(idleState); t.hasExitTime = true; t.exitTime = 0.95f; t.duration = 0.15f;
                }
            }

            CharacterMaskAndSocketSetup.EnsureMasksExist(out AvatarMask upperBodyMask, out AvatarMask headGazeMask);
            CharacterMaskAndSocketSetup.ConfigureControllerLayersAndClips(ctrl, clipsByName, upperBodyMask, headGazeMask);

            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        private static RuntimeAnimatorController CreateStateController(
            string ctrlPath,
            string fbxPath,
            string defaultStateName,
            string[] expectedClips)
        {
            if (File.Exists(ctrlPath)) AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var sm = ctrl.layers[0].stateMachine;

            var clipsByName = LoadClipMap(fbxPath);
            AnimatorState defaultState = null;

            for (int i = 0; i < expectedClips.Length; i++)
            {
                string sName = expectedClips[i];
                var state = sm.AddState(sName, new Vector3(260 + (i % 3) * 230, (i / 3) * 75, 0));
                if (clipsByName.TryGetValue(sName, out AnimationClip clip))
                    state.motion = clip;
                else
                    Debug.LogWarning($"[WallHabitatArtSetup] Clip '{sName}' not found in {fbxPath}");

                if (sName == defaultStateName) defaultState = state;
            }

            if (defaultState != null) sm.defaultState = defaultState;
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        private static Dictionary<string, AnimationClip> LoadClipMap(string fbxPath)
        {
            var dict = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (UnityEngine.Object obj in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    string clean = clip.name;
                    int pipe = clean.LastIndexOf('|');
                    if (pipe >= 0 && pipe < clean.Length - 1) clean = clean.Substring(pipe + 1);
                    dict[clean] = clip;
                }
            }
            return dict;
        }

        // =====================================================================
        // 3. INDIVIDUAL PREFAB BUILDERS
        // =====================================================================

        private static GameObject BuildIndividualPrefab(
            string assetName,
            bool animated,
            Dictionary<string, Material> matMap,
            Dictionary<string, RuntimeAnimatorController> controllers)
        {
            string fbxPath = $"{ModelsDir}/{assetName}.fbx";
            var srcFbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (srcFbx == null)
            {
                Debug.LogWarning($"[WallHabitatArtSetup] Missing FBX at {fbxPath}");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(srcFbx);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = assetName;

            ApplySharedMaterials(instance, matMap);
            NormalizeWallSockets(instance, isRiggedCharacter: assetName == "Agent_Manager");

            if (animated && controllers.TryGetValue(assetName, out var ctrl))
            {
                var anim = instance.GetComponentInChildren<Animator>(true);
                if (anim == null) anim = instance.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
            }

            ConfigureModuleSpecifics(instance, assetName, matMap);

            string outPath = $"{PrefabsDir}/{assetName}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(instance, outPath);
            UnityEngine.Object.DestroyImmediate(instance);
            return saved;
        }

        private static bool HasBoneChild(Transform t)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                string childName = t.GetChild(i).name;
                if (childName.IndexOf("Bone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    childName.IndexOf("Armature", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void NormalizeWallSockets(GameObject root, bool isRiggedCharacter)
        {
            root.transform.localScale = Vector3.one;
            var targets = new List<Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform) continue;
                bool isAttachment = isRiggedCharacter
                    ? (t.name.EndsWith("_Attach", StringComparison.Ordinal) || t.name.EndsWith("_Socket", StringComparison.Ordinal))
                    : t.GetComponentsInChildren<Renderer>(true).Length == 0
                      && t.GetComponentsInChildren<ParticleSystem>(true).Length == 0
                      && !t.name.Contains("Bone")
                      && !t.name.Contains("Armature")
                      && !HasBoneChild(t);

                if (isAttachment) targets.Add(t);
            }

            foreach (Transform t in targets)
            {
                if (isRiggedCharacter)
                {
                    t.localRotation = t.localRotation * Quaternion.Euler(90f, 0f, 0f);
                    Vector3 pLossy = t.parent != null ? t.parent.lossyScale : Vector3.one;
                    t.localScale = new Vector3(
                        Mathf.Abs(pLossy.x) > 1e-5f ? 1f / pLossy.x : 1f,
                        Mathf.Abs(pLossy.y) > 1e-5f ? 1f / pLossy.y : 1f,
                        Mathf.Abs(pLossy.z) > 1e-5f ? 1f / pLossy.z : 1f);
                }
                else
                {
                    if (t.parent != root.transform)
                    {
                        t.SetParent(root.transform, worldPositionStays: true);
                    }
                    t.localRotation = Quaternion.identity;
                    t.localScale = Vector3.one;
                }
            }
        }

        private static void ConfigureModuleSpecifics(GameObject go, string assetName, Dictionary<string, Material> matMap)
        {
            if (assetName == "Agent_Manager")
            {
                var anim = go.GetComponentInChildren<Animator>(true);
                var bridge = go.AddComponent<AgentAnimatorBridge>();
                bridge.ResolveSockets();
                if (anim != null) SetPrivateField(bridge, "characterAnimator", anim);

                var cap = go.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(0f, 0.115f, 0f);
                cap.radius = 0.052f;
                cap.height = 0.23f;

                AddAndResolveSocketRegistry(go, isCompact: false);
                return;
            }

            if (assetName == "WallHabitat_PlacementGhost")
            {
                var ghostCtrl = go.AddComponent<WallHabitatPlacementGhostController>();
                ghostCtrl.ResolveReferences();
                ghostCtrl.SetStatus(WallPlacementStatus.ValidTeal);

                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.0f, 0.125f);
                box.size = new Vector3(0.98f, 0.76f, 0.25f);
                box.isTrigger = true;
                return;
            }

            if (assetName == "WallHabitat_SelectionHalo")
            {
                var selCtrl = go.AddComponent<WallHabitatAgentSelectionVisual>();
                selCtrl.ResolveReferences();
                selCtrl.ApplyState(AgentPresentationVisualState.Selected);
                return;
            }

            if (assetName == "WallHabitat_LargeAgentPortal")
            {
                // Add ultra-restrained sparse motes at lower threshold ring (Quest comfort-safe & toggleable)
                if (matMap.TryGetValue("Mat_Spatial_SoftParticle", out var softPartMat))
                {
                    var moteGo = new GameObject("VFX_PortalSparseMotes");
                    moteGo.transform.SetParent(go.transform, false);
                    moteGo.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                    moteGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    var ps = moteGo.AddComponent<ParticleSystem>();
                    var main = ps.main;
                    main.loop = true;
                    main.duration = 2.0f;
                    main.startLifetime = 1.6f;
                    main.startSpeed = 0.12f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.014f, 0.024f);
                    main.startColor = new Color(0.32f, 0.90f, 0.85f, 0.55f);
                    main.maxParticles = 14;
                    var em = ps.emission;
                    em.rateOverTime = 5f;
                    var sh = ps.shape;
                    sh.shapeType = ParticleSystemShapeType.Circle;
                    sh.radius = 0.22f;
                    sh.radiusThickness = 0.15f;
                    var psr = moteGo.GetComponent<ParticleSystemRenderer>();
                    psr.sharedMaterial = softPartMat;
                    psr.shadowCastingMode = ShadowCastingMode.Off;
                    psr.receiveShadows = false;
                }

                var portalCtrl = go.AddComponent<WallHabitatLargePortalVisualController>();
                portalCtrl.ResolveReferences();

                SampleAnimationState(go, null, $"{ModelsDir}/WallHabitat_LargeAgentPortal.fbx", "Portal_Active_Idle");

                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.015f, 0f);
                box.size = new Vector3(0.55f, 0.03f, 0.55f);

                AddAndResolveSocketRegistry(go, isCompact: false);
                return;
            }

            if (assetName == "WallHabitat_ElevatorSpine_3Tier" || assetName == "WallHabitat_ElevatorSpine_2Tier")
            {
                var elevCtrl = go.AddComponent<WallHabitatElevatorVisualController>();
                elevCtrl.ResolveReferences();
                AddAndResolveSocketRegistry(go, isCompact: assetName.EndsWith("2Tier", StringComparison.Ordinal));
                return;
            }

            if (assetName == "WallHabitat_DeskVisitPerch")
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.012f, 0f);
                box.size = new Vector3(0.145f, 0.025f, 0.145f);
                AddAndResolveSocketRegistry(go, isCompact: false);
                return;
            }

            if (assetName.StartsWith("WallHabitat_TaskCard_", StringComparison.Ordinal))
            {
                string role = assetName.Replace("WallHabitat_TaskCard_", "");
                var box = go.AddComponent<BoxCollider>();
                box.center = Vector3.zero;
                box.size = new Vector3(0.088f, 0.056f, 0.008f);

                Transform titleSock = FindChildRecursive(go.transform, "CardTitleAnchor");
                if (titleSock != null)
                {
                    AddWorldTextLabel(titleSock, "CardTitle_TMP", $"<b>{role.ToUpperInvariant()} TASK</b>",
                        new Vector3(0f, 0f, 0.0015f), Vector3.zero, 0.0018f, new Color(0.16f, 0.18f, 0.24f), new Vector2(36f, 10f));
                }
                Transform subSock = FindChildRecursive(go.transform, "CardSubtitleAnchor");
                if (subSock != null)
                {
                    AddWorldTextLabel(subSock, "CardSubtitle_TMP", "<i>Ready • Queued</i>",
                        new Vector3(0f, 0f, 0.0015f), Vector3.zero, 0.0014f, new Color(0.36f, 0.40f, 0.48f), new Vector2(36f, 8f));
                }
                return;
            }

            // Static architectural bays / backplates / printer
            bool isCompact = assetName.Contains("Compact");
            AddAndResolveSocketRegistry(go, isCompact);
        }

        private static WallHabitatSocketRegistry AddAndResolveSocketRegistry(GameObject go, bool isCompact)
        {
            var reg = go.GetComponent<WallHabitatSocketRegistry>();
            if (reg == null) reg = go.AddComponent<WallHabitatSocketRegistry>();
            reg.IsCompactLayout = isCompact;
            reg.ResolveAllSockets();
            return reg;
        }

        // =====================================================================
        // 4. COMPOSITE PREFAB ASSEMBLERS
        // =====================================================================

        private static GameObject BuildCompositeHabitatPrefab(
            Dictionary<string, GameObject> prefabs,
            bool isCompact)
        {
            string prefabName = isCompact ? "WallHabitat_Complete_2Tier" : "WallHabitat_Complete_3Tier";
            string backplateKey = isCompact ? "WallHabitat_Backplate_Compact" : "WallHabitat_Backplate_Full";
            string bayKey = isCompact ? "WallHabitat_CubicleBay_Compact" : "WallHabitat_CubicleBay";
            string spineKey = isCompact ? "WallHabitat_ElevatorSpine_2Tier" : "WallHabitat_ElevatorSpine_3Tier";
            float agentScale = 0.56f;
            float deskScale = isCompact ? 0.50f : 0.62f;

            var root = new GameObject(prefabName);

            var backplate = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[backplateKey], root.transform);
            backplate.name = backplateKey;

            // Mirror contract-level WallContact directly at composite root so root pivot is wall-flush (0,0,0)
            var wallContact = new GameObject("WallContact").transform;
            wallContact.SetParent(root.transform, false);
            wallContact.localPosition = Vector3.zero;
            wallContact.localRotation = Quaternion.identity;

            // 1. Mount Cubicle Bays on Lower Tier
            MountCubicleRole(prefabs, backplate.transform, bayKey, "ResearchCubicleSocket",
                "Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Research_Optimized.prefab",
                "Assets/MuseDesk/Art/Prefabs/Characters/Agent_Researcher.prefab",
                "Bay_Researcher", deskScale, agentScale);

            MountCubicleRole(prefabs, backplate.transform, bayKey, "DeveloperCubicleSocket",
                "Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Developer_Optimized.prefab",
                "Assets/MuseDesk/Art/Prefabs/Characters/Agent_Developer.prefab",
                "Bay_Developer", deskScale, agentScale);

            MountCubicleRole(prefabs, backplate.transform, bayKey, "TesterCubicleSocket",
                "Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Testing_Optimized.prefab",
                "Assets/MuseDesk/Art/Prefabs/Characters/Agent_Tester.prefab",
                "Bay_Tester", deskScale, agentScale);

            // 2. Mount Mid Planning Nook (3-Tier Full mode only)
            if (!isCompact)
            {
                Transform midSock = FindChildRecursive(backplate.transform, "SharedMeetingSocket");
                if (midSock != null && prefabs.TryGetValue("WallHabitat_MidPlanningNook", out var midPrefab))
                {
                    var midGo = (GameObject)PrefabUtility.InstantiatePrefab(midPrefab, midSock);
                    midGo.name = "WallHabitat_MidPlanningNook";
                    SpatialScaleUtility.MountPreservingWorldScale(midGo.transform, midSock, Vector3.one);
                }
            }

            // 3. Mount Top Manager Lookout + Manager Mascot + Manager Workstation + TaskPrinter
            Transform mgrSock = FindChildRecursive(backplate.transform, "ManagerOfficeSocket");
            if (mgrSock != null && prefabs.TryGetValue("WallHabitat_ManagerLookout", out var mgrOfficePrefab))
            {
                var mgrOffice = (GameObject)PrefabUtility.InstantiatePrefab(mgrOfficePrefab, mgrSock);
                mgrOffice.name = "WallHabitat_ManagerLookout";
                SpatialScaleUtility.MountPreservingWorldScale(mgrOffice.transform, mgrSock, Vector3.one);

                Transform mgrWorkSock = FindChildRecursive(mgrOffice.transform, "WorkstationSocket");
                MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/VisualPolish/Workstation_Research_Optimized.prefab",
                    mgrWorkSock, Vector3.one * 0.56f, 0f, "Manager_TerminalProp");

                Transform mgrPose = FindChildRecursive(mgrOffice.transform, "ManagerDeskSocket")
                                    ?? FindChildRecursive(mgrOffice.transform, "AgentHomePose");
                if (mgrPose != null && prefabs.TryGetValue("Agent_Manager", out var mgrAgentPrefab))
                {
                    var mgrAgent = (GameObject)PrefabUtility.InstantiatePrefab(mgrAgentPrefab, mgrPose);
                    mgrAgent.name = "Agent_Manager";
                    SpatialScaleUtility.MountPreservingWorldScale(mgrAgent.transform, mgrPose, Vector3.one * agentScale);
                    SampleAnimationState(mgrAgent, null, $"{ModelsDir}/Agent_Manager.fbx", "Manager_Lookout_Idle");
                }
            }

            Transform printerSock = FindChildRecursive(backplate.transform, "TaskPrinterSocket");
            if (printerSock != null && prefabs.TryGetValue("WallHabitat_TaskPrinter", out var printerPrefab))
            {
                var printer = (GameObject)PrefabUtility.InstantiatePrefab(printerPrefab, printerSock);
                printer.name = "WallHabitat_TaskPrinter";
                SpatialScaleUtility.MountPreservingWorldScale(printer.transform, printerSock, Vector3.one);
            }

            // 4. Mount Elevator Spine + Car inside ElevatorCarSocket
            Transform spineSock = FindChildRecursive(backplate.transform, "ElevatorSpineSocket");
            if (spineSock != null && prefabs.TryGetValue(spineKey, out var spinePrefab))
            {
                var spine = (GameObject)PrefabUtility.InstantiatePrefab(spinePrefab, spineSock);
                spine.name = spineKey;
                SpatialScaleUtility.MountPreservingWorldScale(spine.transform, spineSock, Vector3.one);

                Transform carSock = FindChildRecursive(spine.transform, "ElevatorCarSocket");
                if (carSock != null && prefabs.TryGetValue("WallHabitat_ElevatorCar", out var carPrefab))
                {
                    var car = (GameObject)PrefabUtility.InstantiatePrefab(carPrefab, carSock);
                    car.name = "WallHabitat_ElevatorCar";
                    SpatialScaleUtility.MountPreservingWorldScale(car.transform, carSock, Vector3.one);
                }

                var elevCtrl = spine.GetComponent<WallHabitatElevatorVisualController>();
                if (elevCtrl != null)
                {
                    elevCtrl.ResolveReferences();
                    elevCtrl.MoveCabToStop("ElevatorStop_Lower", instant: true);
                }
            }

            // 5. Mount Attention Beacon & Courier on Right Porch Wing
            Transform beaconSock = FindChildRecursive(backplate.transform, "AttentionBeaconSocket");
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Spatial/WorkspaceAttentionBeacon.prefab",
                beaconSock, Vector3.one * 0.72f, 0f, "WallAttentionBeacon");

            Transform courierSock = FindChildRecursive(backplate.transform, "CourierDispatchSocket");
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Spatial/DocumentCourier.prefab",
                courierSock, Vector3.one * 0.65f, 0f, "WallCourierDock");

            // 6. Add aggregate BoxCollider covering habitat envelope & resolve top-level SocketRegistry
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.0f, 0.125f);
            col.size = isCompact ? new Vector3(0.74f, 0.52f, 0.25f) : new Vector3(0.98f, 0.76f, 0.25f);
            col.isTrigger = true;

            AddAndResolveSocketRegistry(root, isCompact);

            string outPath = $"{PrefabsDir}/{prefabName}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, outPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        private static void MountCubicleRole(
            Dictionary<string, GameObject> prefabs,
            Transform backplateTransform,
            string bayKey,
            string socketName,
            string workstationPrefabPath,
            string agentPrefabPath,
            string instanceLabel,
            float deskScale,
            float agentScale)
        {
            Transform baySock = FindChildRecursive(backplateTransform, socketName);
            if (baySock == null || !prefabs.TryGetValue(bayKey, out var bayPrefab)) return;

            var bay = (GameObject)PrefabUtility.InstantiatePrefab(bayPrefab, baySock);
            bay.name = instanceLabel;
            SpatialScaleUtility.MountPreservingWorldScale(bay.transform, baySock, Vector3.one);

            Transform wsSock = FindChildRecursive(bay.transform, "WorkstationSocket");
            MountExistingPrefab(workstationPrefabPath, wsSock, Vector3.one * deskScale, 0f, $"{instanceLabel}_Workstation");

            Transform agentSock = FindChildRecursive(bay.transform, "AgentHomePose");
            MountExistingPrefab(agentPrefabPath, agentSock, Vector3.one * agentScale, 0f, $"{instanceLabel}_Agent");
        }

        private static GameObject BuildCompositeDeskVisitPrefab(Dictionary<string, GameObject> prefabs)
        {
            var root = new GameObject("WallHabitat_DeskVisit_Complete");
            var perch = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WallHabitat_DeskVisitPerch"], root.transform);
            perch.name = "WallHabitat_DeskVisitPerch";

            Transform agentPose = FindChildRecursive(perch.transform, "AgentHomePose");
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Characters/Agent_Researcher.prefab",
                agentPose, Vector3.one * 0.78f, 0f, "DeskVisit_AgentProxy");

            if (agentPose != null && prefabs.TryGetValue("WallHabitat_SelectionHalo", out var haloPrefab))
            {
                var halo = (GameObject)PrefabUtility.InstantiatePrefab(haloPrefab, agentPose);
                halo.name = "DeskVisit_SelectionHalo";
                SpatialScaleUtility.MountPreservingWorldScale(halo.transform, agentPose, Vector3.one * 0.78f);
                halo.GetComponent<WallHabitatAgentSelectionVisual>()?.ApplyState(AgentPresentationVisualState.DeskVisit);
            }

            AddAndResolveSocketRegistry(root, isCompact: false);

            string outPath = $"{PrefabsDir}/WallHabitat_DeskVisit_Complete.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, outPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        private static GameObject BuildCompositeLargePortalPrefab(Dictionary<string, GameObject> prefabs)
        {
            var root = new GameObject("WallHabitat_LargePortal_WithProxy");
            var portal = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WallHabitat_LargeAgentPortal"], root.transform);
            portal.name = "WallHabitat_LargeAgentPortal";
            SampleAnimationState(portal, null, $"{ModelsDir}/WallHabitat_LargeAgentPortal.fbx", "Portal_Active_Idle");

            // Mount ~1.01m tall presentation proxy (native 0.23m * 4.35f = ~1.00m within 0.9-1.2m spec)
            Transform agentPose = FindChildRecursive(portal.transform, "AgentHomePose");
            MountExistingPrefab("Assets/MuseDesk/Art/Prefabs/Characters/Agent_Developer.prefab",
                agentPose, Vector3.one * 4.35f, 0f, "LargePortal_AgentProxy");

            // Mount off-to-the-side DialoguePanelSocket card preview so text never overlaps avatar torso/head
            Transform dlgSock = FindChildRecursive(portal.transform, "DialoguePanelSocket");
            if (dlgSock != null && prefabs.TryGetValue("WallHabitat_TaskCard_Developer", out var cardPrefab))
            {
                var card = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab, dlgSock);
                card.name = "LargePortal_SideDialogueCard";
                SpatialScaleUtility.MountPreservingWorldScale(card.transform, dlgSock, Vector3.one * 2.4f);
                card.transform.localRotation = Quaternion.Euler(-90f, 180f, 0f);
            }

            var ctrl = portal.GetComponent<WallHabitatLargePortalVisualController>();
            ctrl?.ResolveReferences();

            AddAndResolveSocketRegistry(root, isCompact: false);

            string outPath = $"{PrefabsDir}/WallHabitat_LargePortal_WithProxy.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, outPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        private static void MountExistingPrefab(
            string assetPath,
            Transform socket,
            Vector3 desiredWorldScale,
            float yawDeg,
            string instanceName)
        {
            if (socket == null) return;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (src == null)
            {
                Debug.LogWarning($"[WallHabitatArtSetup] Optional reference asset missing: {assetPath}");
                return;
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, socket);
            inst.name = instanceName;
            SpatialScaleUtility.MountPreservingWorldScale(inst.transform, socket, desiredWorldScale, resetLocalPose: true);
            inst.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
        }

        // =====================================================================
        // 5. VALIDATION & CONTRACT COVERAGE AUDIT
        // =====================================================================

        private static ValidationInventory ValidateAndCollectMetrics(Dictionary<string, GameObject> prefabs)
        {
            var inv = new ValidationInventory
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                totalPrefabs = prefabs.Count,
            };

            foreach (var kv in prefabs.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var go = UnityEngine.Object.Instantiate(kv.Value);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;

                var m = new AssetMetrics
                {
                    prefabName = kv.Key,
                    prefabPath = AssetDatabase.GetAssetPath(kv.Value),
                };

                int missing = 0;
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                    if (c == null) missing++;
                m.missingScripts = missing;
                inv.totalMissingScripts += missing;

                var matNames = new HashSet<string>(StringComparer.Ordinal);
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
                m.materialNames = matNames.OrderBy(s => s, StringComparer.Ordinal).ToArray();
                m.boundsSize = hasBounds
                    ? $"{combined.size.x:F3} x {combined.size.y:F3} x {combined.size.z:F3} m"
                    : "empty";

                // Verify top-level habitat/agent sockets are ~unit lossyScale
                var reg = go.GetComponent<WallHabitatSocketRegistry>();
                if (reg != null)
                {
                    reg.ResolveAllSockets();
                    bool ok = reg.ValidateHabitatAssembly(out var missingSocks, out var nonUnitSocks);
                    m.socketCount = reg.Entries.Count;
                    m.allSocketsUnitScale = nonUnitSocks.Count == 0;
                    if (nonUnitSocks.Count > 0)
                    {
                        inv.totalBrokenSockets += nonUnitSocks.Count;
                        Debug.LogError($"[WallHabitatArtSetup] Non-unit sockets in {kv.Key}: {string.Join(", ", nonUnitSocks)}");
                    }

                    if (kv.Key == "WallHabitat_Complete_3Tier" && !ok)
                    {
                        inv.missingRequired3TierSockets = missingSocks.Count;
                        Debug.LogError($"[WallHabitatArtSetup] 3Tier missing required sockets: {string.Join(", ", missingSocks)}");
                    }
                    else if (kv.Key == "WallHabitat_Complete_2Tier" && !ok)
                    {
                        inv.missingRequired2TierSockets = missingSocks.Count;
                        Debug.LogError($"[WallHabitatArtSetup] 2Tier missing required sockets: {string.Join(", ", missingSocks)}");
                    }
                }
                else
                {
                    m.allSocketsUnitScale = true;
                }

                var animStates = new List<string>();
                var anim = go.GetComponent<Animator>();
                if (anim != null && anim.runtimeAnimatorController is AnimatorController ac)
                {
                    foreach (var cs in ac.layers[0].stateMachine.states)
                    {
                        if (cs.state.motion == null)
                            Debug.LogError($"[WallHabitatArtSetup] Animator state '{cs.state.name}' in {kv.Key} has NULL motion clip!");
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
        // 6. PREVIEW SCENE & MULTI-ANGLE STUDIO RENDERING
        // =====================================================================

        private static List<string> BuildPreviewSceneAndCapture(
            Dictionary<string, GameObject> prefabs,
            Dictionary<string, Material> matMap)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.76f, 0.80f, 0.86f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.60f, 0.64f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.35f, 0.33f, 0.30f, 1f);

            var sunGo = new GameObject("Studio_KeyLight");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1.0f, 0.97f, 0.92f);
            sunGo.transform.rotation = Quaternion.Euler(38f, 145f, 0f);

            var fillGo = new GameObject("Studio_FillLight");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.48f;
            fill.color = new Color(0.84f, 0.91f, 1.0f);
            fillGo.transform.rotation = Quaternion.Euler(28f, -45f, 0f);

            matMap.TryGetValue("Mat_FeltSlate", out var floorMat);
            matMap.TryGetValue("Mat_WarmBeige", out var wallMat);
            matMap.TryGetValue("Mat_WarmWood", out var deskMat);

            CreateStudioBox("Studio_Floor", new Vector3(2.2f, -0.025f, 0.5f), new Vector3(13.0f, 0.05f, 4.5f), floorMat);
            CreateStudioBox("Studio_MockWall", new Vector3(2.2f, 1.45f, -0.02f), new Vector3(10.0f, 2.9f, 0.04f), wallMat);
            CreateStudioBox("Studio_DeskProxy", new Vector3(-0.95f, 0.37f, 0.85f), new Vector3(0.95f, 0.74f, 0.50f), deskMat);

            // 1. Full 3-Tier Workshop Treehouse mounted flush on wall at comfortable eye height (center y = 1.48m)
            var full3Tier = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WallHabitat_Complete_3Tier"]);
            full3Tier.name = "Showcase_01_WallHabitat_3Tier";
            full3Tier.transform.position = new Vector3(-0.75f, 1.48f, 0f);

            // 2. Compact 2-Tier Workshop Treehouse beside it
            var compact2Tier = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WallHabitat_Complete_2Tier"]);
            compact2Tier.name = "Showcase_02_WallHabitat_2TierCompact";
            compact2Tier.transform.position = new Vector3(0.45f, 1.38f, 0f);

            // 3. 3-State Wall Placement Ghosts (Valid Teal, Review Amber, Invalid Red)
            SpawnPlacementGhost(prefabs["WallHabitat_PlacementGhost"], "Showcase_03a_Ghost_ValidTeal",
                new Vector3(1.60f, 1.48f, 0f), WallPlacementStatus.ValidTeal);
            SpawnPlacementGhost(prefabs["WallHabitat_PlacementGhost"], "Showcase_03b_Ghost_ReviewAmber",
                new Vector3(2.70f, 1.48f, 0f), WallPlacementStatus.ReviewAmber);
            SpawnPlacementGhost(prefabs["WallHabitat_PlacementGhost"], "Showcase_03c_Ghost_InvalidRed",
                new Vector3(3.80f, 1.48f, 0f), WallPlacementStatus.InvalidRed);

            // 4. Desk Visit Perch (/btw proxy on desk) + Role TaskCards row
            var deskVisit = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WallHabitat_DeskVisit_Complete"]);
            deskVisit.name = "Showcase_04_DeskVisit_Complete";
            deskVisit.transform.position = new Vector3(-0.95f, 0.74f, 0.85f);

            string[] roles = { "Researcher", "Developer", "Tester", "Manager" };
            for (int i = 0; i < roles.Length; i++)
            {
                if (prefabs.TryGetValue($"WallHabitat_TaskCard_{roles[i]}", out var cardPrefab))
                {
                    var card = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab);
                    card.name = $"Showcase_04b_TaskCard_{roles[i]}";
                    card.transform.position = new Vector3(-0.74f + i * 0.085f, 0.772f, 0.90f);
                    card.transform.rotation = Quaternion.Euler(-68f, 180f, 0f);
                }
            }

            // 5. Large-Agent Restrained Portal (~1.0m presentation avatar + offset side DialoguePanelSocket card)
            var largePortal = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WallHabitat_LargePortal_WithProxy"]);
            largePortal.name = "Showcase_05_LargePortal_WithProxy";
            largePortal.transform.position = new Vector3(5.60f, 0f, 0.85f);

            // Camera + captures
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.20f, 0.23f, 0.28f, 1f);
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 25f;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Warmup render pass
            CaptureCameraShot(cam, "_warmup.png", new Vector3(-0.15f, 1.35f, 2.4f), new Vector3(-0.15f, 1.35f, 0f), 256, 256);
            string warmup = Path.Combine(ScreenshotDir, "_warmup.png");
            if (File.Exists(warmup)) File.Delete(warmup);

            var shots = new List<string>
            {
                CaptureCameraShot(cam, "01_WallHabitat_3Tier_Front.png",
                    new Vector3(-0.75f, 1.48f, 1.42f), new Vector3(-0.75f, 1.46f, 0.06f), 1440, 960),
                CaptureCameraShot(cam, "02_WallHabitat_3Tier_ThreeQuarter.png",
                    new Vector3(-1.55f, 1.68f, 1.25f), new Vector3(-0.75f, 1.44f, 0.08f), 1440, 960),
                CaptureCameraShot(cam, "03_WallHabitat_2Tier_Compact.png",
                    new Vector3(0.45f, 1.40f, 1.18f), new Vector3(0.45f, 1.36f, 0.06f), 1280, 800),
                CaptureCameraShot(cam, "04_WallHabitat_DeskVisit_And_Cards.png",
                    new Vector3(-0.70f, 1.00f, 1.36f), new Vector3(-0.74f, 0.80f, 0.86f), 1280, 800),
                CaptureCameraShot(cam, "05_WallHabitat_LargePortal_Front.png",
                    new Vector3(5.68f, 0.70f, 2.95f), new Vector3(5.68f, 0.62f, 0.85f), 1280, 900),
                CaptureCameraShot(cam, "06_WallHabitat_PlacementGhost_States.png",
                    new Vector3(2.70f, 1.48f, 2.15f), new Vector3(2.70f, 1.48f, 0.05f), 1440, 720),
            };

            cam.transform.position = new Vector3(-0.15f, 1.38f, 2.45f);
            cam.transform.LookAt(new Vector3(-0.15f, 1.35f, 0.15f));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            return shots;
        }

        private static void SpawnPlacementGhost(GameObject prefab, string name, Vector3 pos, WallPlacementStatus status)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            g.name = name;
            g.transform.position = pos;
            var ctrl = g.GetComponent<WallHabitatPlacementGhostController>();
            ctrl?.SetStatus(status);
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
                clip.SampleAnimation(target.gameObject, 0f);
        }

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
