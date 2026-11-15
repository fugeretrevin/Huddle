#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using MuseDesk.Art;

namespace MuseDesk.Editor.ArtPipeline
{
    /// <summary>
    /// Repeatable Unity Editor pipeline for importing all 61 MuseDesk 3D assets,
    /// normalizing FBX Empty/socket scales, generating shared URP materials,
    /// building Animator Controllers (39 character clips + 10 factory clips),
    /// creating interaction-ready prefabs under Assets/MuseDesk/Art/Prefabs/,
    /// assembling MuseDesk_Workspace_Root.prefab, and building ArtShowcaseScene.unity
    /// and MRWorkstationTestScene.unity without modifying MainMRScene.unity.
    /// </summary>
    public static class MuseDeskArtPipeline
    {
        private const string ArtRoot        = "Assets/MuseDesk/Art";
        private const string ModelsRoot     = ArtRoot + "/Models";
        private const string MaterialsDir   = ArtRoot + "/Materials";
        private const string ControllersDir = ArtRoot + "/Controllers";
        private const string PrefabsRoot    = ArtRoot + "/Prefabs";
        private const string ScenesDir      = "Assets/MuseDesk/Scenes";

        private static readonly HashSet<string> LoopingCharacterClips = new HashSet<string>
        {
            "Idle_Breathing", "Idle_Fidget", "Happy_Idle", "Walk_Loop", "Walk_Carry_Paper", "Seated_Idle",
            "Talking", "Listening", "Thinking", "Excited", "Worried",
            "Typing", "Reading", "Writing", "Inspecting", "Using_Workstation",
            "Waiting_For_Input", "Hold_Document", "Offer_Document", "Carry_Package",
            "Blocked", "Cheer", "Waving",
            "Elevator_Queue_Idle", "Elevator_Ride_Idle", "DeskVisit_Perch_Idle",
            "DeskVisit_Listening", "Portal_Converse_Loop", "Manager_Lookout_Idle", "Manager_Review_Plan",
            "Carry_Suspended_Idle", "Researcher_Compare_Papers", "Tester_Note_Take"
        };

        private struct MatSpec
        {
            public string Name;
            public Color Color;
            public float Roughness;
            public float Metallic;
            public bool Emissive;
            public Color EmissionColor;

            public MatSpec(string name, float r, float g, float b, float rough, float metal, bool emissive = false, float er = 0, float eg = 0, float eb = 0)
            {
                Name = name;
                Color = new Color(r, g, b, 1f);
                Roughness = rough;
                Metallic = metal;
                Emissive = emissive;
                EmissionColor = new Color(er, eg, eb, 1f);
            }
        }

        private static readonly MatSpec[] PaletteSpecs = new MatSpec[]
        {
            new MatSpec("Mat_WarmWood",            0.785f, 0.615f, 0.465f, 0.48f, 0.00f),
            new MatSpec("Mat_DarkWood",            0.460f, 0.330f, 0.230f, 0.52f, 0.00f),
            new MatSpec("Mat_CreamCeramic",        0.955f, 0.935f, 0.895f, 0.32f, 0.00f),
            new MatSpec("Mat_WarmBeige",           0.875f, 0.835f, 0.775f, 0.42f, 0.00f),
            new MatSpec("Mat_CharcoalVisor",       0.130f, 0.140f, 0.170f, 0.20f, 0.15f),
            new MatSpec("Mat_DarkBoots",           0.180f, 0.190f, 0.225f, 0.38f, 0.08f),
            new MatSpec("Mat_EyeGlow",             0.990f, 0.985f, 0.940f, 0.10f, 0.00f, true, 1.2f, 1.15f, 1.05f),
            new MatSpec("Mat_CheekBlush",          0.960f, 0.620f, 0.600f, 0.45f, 0.00f, true, 0.35f, 0.18f, 0.18f),
            new MatSpec("Mat_Brass",               0.850f, 0.690f, 0.280f, 0.24f, 0.82f),
            new MatSpec("Mat_BrushedMetal",        0.650f, 0.685f, 0.725f, 0.28f, 0.70f),
            new MatSpec("Mat_LeatherSatchel",      0.520f, 0.350f, 0.230f, 0.55f, 0.00f),
            new MatSpec("Mat_PaperWarm",           0.985f, 0.968f, 0.925f, 0.58f, 0.00f),
            new MatSpec("Mat_PaperManila",         0.930f, 0.850f, 0.680f, 0.55f, 0.00f),
            new MatSpec("Mat_BlueprintBlue",       0.175f, 0.385f, 0.610f, 0.45f, 0.00f),
            new MatSpec("Mat_CardboardBox",        0.760f, 0.580f, 0.400f, 0.62f, 0.00f),
            new MatSpec("Mat_Terracotta",          0.780f, 0.430f, 0.310f, 0.56f, 0.00f),
            new MatSpec("Mat_PlantGreen",          0.340f, 0.690f, 0.440f, 0.40f, 0.00f),
            new MatSpec("Mat_FeltSlate",           0.260f, 0.290f, 0.340f, 0.72f, 0.00f),
            new MatSpec("Mat_ConveyorBelt",        0.200f, 0.215f, 0.250f, 0.65f, 0.05f),
            new MatSpec("Mat_Researcher_Lavender", 0.560f, 0.485f, 0.785f, 0.40f, 0.00f),
            new MatSpec("Mat_Researcher_Teal",     0.280f, 0.735f, 0.680f, 0.28f, 0.05f, true, 0.14f, 0.38f, 0.34f),
            new MatSpec("Mat_Developer_Coral",     0.980f, 0.460f, 0.360f, 0.38f, 0.00f),
            new MatSpec("Mat_Developer_Amber",     1.000f, 0.810f, 0.320f, 0.25f, 0.10f, true, 0.65f, 0.50f, 0.18f),
            new MatSpec("Mat_Tester_Mint",         0.310f, 0.785f, 0.590f, 0.36f, 0.00f),
            new MatSpec("Mat_Tester_SlateBlue",    0.290f, 0.500f, 0.660f, 0.34f, 0.18f),
            new MatSpec("Mat_Tester_LensGlow",     0.420f, 0.920f, 0.820f, 0.12f, 0.00f, true, 0.35f, 0.82f, 0.72f),
            new MatSpec("Mat_StatusGreen",         0.220f, 0.820f, 0.450f, 0.20f, 0.00f, true, 0.22f, 0.85f, 0.45f),
            new MatSpec("Mat_StatusRed",           0.920f, 0.250f, 0.240f, 0.20f, 0.00f, true, 0.92f, 0.25f, 0.24f),
            new MatSpec("Mat_StatusAmber",         0.980f, 0.680f, 0.220f, 0.20f, 0.00f, true, 0.98f, 0.68f, 0.22f),
            new MatSpec("Mat_StatusCyan",          0.250f, 0.820f, 0.950f, 0.20f, 0.00f, true, 0.25f, 0.82f, 0.95f),
            new MatSpec("Mat_ChamberGlass",        0.680f, 0.900f, 0.880f, 0.12f, 0.00f, true, 0.20f, 0.45f, 0.42f),
            new MatSpec("Mat_ScreenCode",          0.120f, 0.260f, 0.400f, 0.14f, 0.00f, true, 0.16f, 0.52f, 0.82f),
            new MatSpec("Mat_LampGlow",            1.000f, 0.950f, 0.800f, 0.15f, 0.00f, true, 1.10f, 1.00f, 0.80f),
            new MatSpec("Mat_StampInk_Approve",    0.102f, 0.420f, 0.239f, 0.72f, 0.00f),
            new MatSpec("Mat_StampInk_Reject",     0.620f, 0.161f, 0.129f, 0.72f, 0.00f),
        };

        [MenuItem("MuseDesk/Art Pipeline/Rebuild Characters & Agent Controller")]
        public static void RebuildCharactersAndAgentController()
        {
            EnsureDirectories();
            AssetDatabase.Refresh();

            Dictionary<string, Material> matMap = LoadExistingMaterialsOrCreate();
            ConfigureAllModelImporters(ModelsRoot + "/Characters");
            AnimatorController agentController = BuildAgentAnimatorController();

            string[] names = { "Agent_Researcher", "Agent_Developer", "Agent_Tester" };
            for (int i = 0; i < names.Length; i++)
            {
                string fbxPath = $"{ModelsRoot}/Characters/{names[i]}.fbx";
                GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (modelAsset == null) continue;

                GameObject fbxInst = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                GameObject instance;
                if (fbxInst.transform.localScale.x > 10f || Quaternion.Angle(fbxInst.transform.localRotation, Quaternion.identity) > 1f)
                {
                    instance = new GameObject(names[i]);
                    fbxInst.name = names[i] + "_Model";
                    fbxInst.transform.SetParent(instance.transform, false);
                }
                else
                {
                    instance = fbxInst;
                    instance.name = names[i];
                }
                NormalizeFbxHierarchyScales(instance, true);
                ApplySharedMaterialsToHierarchy(instance, matMap);

                Animator anim = instance.GetComponentInChildren<Animator>();
                if (anim == null) anim = instance.AddComponent<Animator>();
                anim.runtimeAnimatorController = agentController;

                AgentAnimatorBridge bridge = instance.AddComponent<AgentAnimatorBridge>();
                bridge.ResolveSockets();

                CapsuleCollider col = instance.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, 0.115f, 0f);
                col.radius = 0.052f;
                col.height = 0.23f;

                string prefabPath = $"{PrefabsRoot}/Characters/{names[i]}.prefab";
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                UnityEngine.Object.DestroyImmediate(instance);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MuseDeskArtPipeline] Rebuilt character prefabs & updated MuseDesk_Agent.controller in-place.");
        }

        [MenuItem("MuseDesk/Art Pipeline/Build Stamp Juice (P3)")]
        public static void BuildStampJuice()
        {
            EnsureDirectories();
            string visualPolishDir = PrefabsRoot + "/VisualPolish";
            if (!Directory.Exists(visualPolishDir)) Directory.CreateDirectory(visualPolishDir);
            AssetDatabase.Refresh();

            Dictionary<string, Material> matMap = LoadExistingMaterialsOrCreate();

            string docsModels = ModelsRoot + "/Documents";
            string[] fbxNames = {
                "Doc_Stamp_Approve", "Doc_Stamp_Reject",
                "Doc_StampInkMark_Approve", "Doc_StampInkMark_Reject"
            };
            for (int i = 0; i < fbxNames.Length; i++)
            {
                string fbxPath = $"{docsModels}/{fbxNames[i]}.fbx";
                ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer == null) continue;
                bool isAnim = fbxNames[i].StartsWith("Doc_Stamp_");
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.importAnimation = isAnim;
                if (isAnim)
                {
                    ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
                    if (defaults != null && defaults.Length > 0)
                    {
                        ModelImporterClipAnimation[] custom = new ModelImporterClipAnimation[defaults.Length];
                        for (int c = 0; c < defaults.Length; c++)
                        {
                            ModelImporterClipAnimation clip = defaults[c];
                            string cleanName = clip.takeName;
                            int pipeIdx = cleanName.LastIndexOf('|');
                            if (pipeIdx >= 0 && pipeIdx < cleanName.Length - 1)
                            {
                                cleanName = cleanName.Substring(pipeIdx + 1);
                            }
                            if (cleanName.Contains("Stamp_Impact_Squash")) cleanName = "Stamp_Impact_Squash";
                            clip.name = cleanName;
                            clip.loopTime = false;
                            custom[c] = clip;
                        }
                        importer.clipAnimations = custom;
                    }
                }
                importer.SaveAndReimport();
            }

            // Build or update MuseDesk_Stamp.controller in-place
            string stampCtrlPath = $"{ControllersDir}/MuseDesk_Stamp.controller";
            AnimatorController stampCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(stampCtrlPath);
            if (stampCtrl == null)
            {
                stampCtrl = AnimatorController.CreateAnimatorControllerAtPath(stampCtrlPath);
            }
            for (int p = stampCtrl.parameters.Length - 1; p >= 0; p--) stampCtrl.RemoveParameter(p);
            AnimatorStateMachine sm = stampCtrl.layers[0].stateMachine;
            foreach (ChildAnimatorState child in sm.states) sm.RemoveState(child.state);
            for (int t = sm.anyStateTransitions.Length - 1; t >= 0; t--) sm.RemoveAnyStateTransition(sm.anyStateTransitions[t]);

            stampCtrl.AddParameter("Impact", AnimatorControllerParameterType.Trigger);
            AnimatorState idleState = sm.AddState("Stamp_Idle");
            sm.defaultState = idleState;

            AnimationClip squashClip = null;
            UnityEngine.Object[] stampSubAssets = AssetDatabase.LoadAllAssetsAtPath($"{docsModels}/Doc_Stamp_Approve.fbx");
            for (int i = 0; i < stampSubAssets.Length; i++)
            {
                AnimationClip c = stampSubAssets[i] as AnimationClip;
                if (c != null && !c.name.StartsWith("__preview__"))
                {
                    squashClip = c;
                    break;
                }
            }

            if (squashClip != null)
            {
                AnimatorState squashState = sm.AddState("Stamp_Impact_Squash");
                squashState.motion = squashClip;

                AnimatorStateTransition anyToSquash = sm.AddAnyStateTransition(squashState);
                anyToSquash.hasExitTime = false;
                anyToSquash.duration = 0.02f;
                anyToSquash.canTransitionToSelf = true;
                anyToSquash.AddCondition(AnimatorConditionMode.If, 0f, "Impact");

                AnimatorStateTransition squashToIdle = squashState.AddTransition(idleState);
                squashToIdle.hasExitTime = true;
                squashToIdle.exitTime = 0.95f;
                squashToIdle.duration = 0.05f;
            }
            EditorUtility.SetDirty(stampCtrl);

            string[] stampAssets = { "Doc_Stamp_Approve", "Doc_Stamp_Reject" };
            for (int i = 0; i < stampAssets.Length; i++)
            {
                GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>($"{docsModels}/{stampAssets[i]}.fbx");
                if (modelAsset == null) continue;

                GameObject fbxInst = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                GameObject instance;
                if (fbxInst.transform.localScale.x > 10f || Quaternion.Angle(fbxInst.transform.localRotation, Quaternion.identity) > 1f)
                {
                    instance = new GameObject(stampAssets[i] + "_Polished");
                    fbxInst.name = stampAssets[i] + "_Model";
                    fbxInst.transform.SetParent(instance.transform, false);
                }
                else
                {
                    instance = fbxInst;
                    instance.name = stampAssets[i] + "_Polished";
                    NormalizeFbxHierarchyScales(instance, false);
                }
                ApplySharedMaterialsToHierarchy(instance, matMap);

                Animator anim = fbxInst.GetComponent<Animator>();
                if (anim == null) anim = fbxInst.AddComponent<Animator>();
                anim.runtimeAnimatorController = stampCtrl;
                anim.applyRootMotion = false;

                string prefabPath = $"{visualPolishDir}/{stampAssets[i]}_Polished.prefab";
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                UnityEngine.Object.DestroyImmediate(instance);
            }

            string[] markAssets = { "Doc_StampInkMark_Approve", "Doc_StampInkMark_Reject" };
            for (int i = 0; i < markAssets.Length; i++)
            {
                GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>($"{docsModels}/{markAssets[i]}.fbx");
                if (modelAsset == null) continue;

                GameObject fbxInst = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                GameObject instance;
                if (fbxInst.transform.localScale.x > 10f || Quaternion.Angle(fbxInst.transform.localRotation, Quaternion.identity) > 1f)
                {
                    instance = new GameObject(markAssets[i]);
                    fbxInst.name = markAssets[i] + "_Model";
                    fbxInst.transform.SetParent(instance.transform, false);
                }
                else
                {
                    instance = fbxInst;
                    instance.name = markAssets[i];
                    NormalizeFbxHierarchyScales(instance, false);
                }
                ApplySharedMaterialsToHierarchy(instance, matMap);

                string prefabPath = $"{PrefabsRoot}/Documents/{markAssets[i]}.prefab";
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                UnityEngine.Object.DestroyImmediate(instance);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MuseDeskArtPipeline] Priority 3 Stamp Juice built & verified.");
        }

        private static Dictionary<string, Material> LoadExistingMaterialsOrCreate()
        {
            Dictionary<string, Material> map = new Dictionary<string, Material>();
            bool createdAny = false;
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) urpLit = Shader.Find("Standard");

            for (int i = 0; i < PaletteSpecs.Length; i++)
            {
                MatSpec spec = PaletteSpecs[i];
                string matPath = $"{MaterialsDir}/{spec.Name}.mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(urpLit);
                    float smoothness = Mathf.Clamp01(1f - spec.Roughness);
                    if (mat.HasProperty("_BaseColor"))  mat.SetColor("_BaseColor", spec.Color);
                    if (mat.HasProperty("_Color"))      mat.SetColor("_Color", spec.Color);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
                    if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
                    if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", spec.Metallic);
                    if (spec.Emissive)
                    {
                        mat.EnableKeyword("_EMISSION");
                        if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", spec.EmissionColor);
                    }
                    AssetDatabase.CreateAsset(mat, matPath);
                    createdAny = true;
                }
                map[spec.Name] = mat;
            }
            if (createdAny) AssetDatabase.SaveAssets();
            return map;
        }

        [MenuItem("MuseDesk/Art Pipeline/Build Complete Phase 2 Art Library & Scenes")]
        public static void BuildAll()
        {
            EnsureDirectories();
            AssetDatabase.Refresh();

            Dictionary<string, Material> matMap = CreateOrUpdateMaterials();
            ConfigureAllModelImporters();

            AnimatorController agentController   = BuildAgentAnimatorController();
            AnimatorController factoryController = BuildFactoryAnimatorController();

            Dictionary<string, GameObject> prefabs = GenerateAllPrefabs(matMap, agentController, factoryController);
            GameObject workspaceRootPrefab = BuildWorkspaceRootPrefab(prefabs);

            BuildArtShowcaseScene(prefabs, workspaceRootPrefab);
            BuildMRWorkstationTestScene(workspaceRootPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MuseDeskArtPipeline] Rebuilt all prefabs, normalized FBX Empty scales, and updated showcase scenes!");
        }

        private static void EnsureDirectories()
        {
            string[] dirs = new string[]
            {
                MaterialsDir,
                ControllersDir,
                PrefabsRoot,
                PrefabsRoot + "/Characters",
                PrefabsRoot + "/Workstations",
                PrefabsRoot + "/TaskBoard",
                PrefabsRoot + "/Documents",
                PrefabsRoot + "/Factory",
                PrefabsRoot + "/Diagrams",
                PrefabsRoot + "/Decorations",
                PrefabsRoot + "/Workspace",
                ScenesDir,
            };
            for (int i = 0; i < dirs.Length; i++)
            {
                if (!Directory.Exists(dirs[i]))
                {
                    Directory.CreateDirectory(dirs[i]);
                }
            }
        }

        private static Dictionary<string, Material> CreateOrUpdateMaterials()
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) urpLit = Shader.Find("Standard");

            Dictionary<string, Material> map = new Dictionary<string, Material>();
            for (int i = 0; i < PaletteSpecs.Length; i++)
            {
                MatSpec spec = PaletteSpecs[i];
                string matPath = $"{MaterialsDir}/{spec.Name}.mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(urpLit);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else
                {
                    mat.shader = urpLit;
                }

                float smoothness = Mathf.Clamp01(1f - spec.Roughness);
                if (mat.HasProperty("_BaseColor"))  mat.SetColor("_BaseColor", spec.Color);
                if (mat.HasProperty("_Color"))      mat.SetColor("_Color", spec.Color);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
                if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", spec.Metallic);

                if (spec.Emissive)
                {
                    mat.EnableKeyword("_EMISSION");
                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.SetColor("_EmissionColor", spec.EmissionColor);
                    }
                }
                EditorUtility.SetDirty(mat);
                map[spec.Name] = mat;
            }
            AssetDatabase.SaveAssets();
            return map;
        }

        private static void ConfigureAllModelImporters(string searchRoot = null)
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { string.IsNullOrEmpty(searchRoot) ? ModelsRoot : searchRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;

                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                bool isAnimated = path.Contains("/Characters/") || path.Contains("/Factory/") || path.Contains("Doc_Stamp_");
                bool changed = false;

                if (importer.animationType != ModelImporterAnimationType.Generic)
                {
                    importer.animationType = ModelImporterAnimationType.Generic;
                    changed = true;
                }
                if (importer.importAnimation != isAnimated)
                {
                    importer.importAnimation = isAnimated;
                    changed = true;
                }

                if (isAnimated)
                {
                    ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
                    if (defaults != null && defaults.Length > 0)
                    {
                        ModelImporterClipAnimation[] custom = new ModelImporterClipAnimation[defaults.Length];
                        for (int c = 0; c < defaults.Length; c++)
                        {
                            ModelImporterClipAnimation clip = defaults[c];
                            string cleanName = clip.takeName;
                            int pipeIdx = cleanName.LastIndexOf('|');
                            if (pipeIdx >= 0 && pipeIdx < cleanName.Length - 1)
                            {
                                cleanName = cleanName.Substring(pipeIdx + 1);
                            }
                            clip.name = cleanName;
                            clip.loopTime = LoopingCharacterClips.Contains(cleanName) || cleanName == "3_Artifact_Assemble" || cleanName == "5_Artifact_Transport";
                            custom[c] = clip;
                        }
                        importer.clipAnimations = custom;
                        changed = true;
                    }
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        private static AnimatorController BuildAgentAnimatorController()
        {
            string controllerPath = $"{ControllersDir}/MuseDesk_Agent.controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            }

            // Preserve existing controller asset & GUID by resetting layers/parameters in-place
            for (int p = controller.parameters.Length - 1; p >= 0; p--) controller.RemoveParameter(p);
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in sm.states) sm.RemoveState(child.state);
            for (int t = sm.anyStateTransitions.Length - 1; t >= 0; t--) sm.RemoveAnyStateTransition(sm.anyStateTransitions[t]);

            controller.AddParameter("MoveSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsCarryingPaper", AnimatorControllerParameterType.Bool);

            string fbxPath = $"{ModelsRoot}/Characters/Agent_Researcher.fbx";
            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);

            var clipsByName = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            var states = new Dictionary<string, AnimatorState>();
            for (int i = 0; i < subAssets.Length; i++)
            {
                AnimationClip clip = subAssets[i] as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__")) continue;

                string stateName = clip.name;
                int pipeIdx = stateName.LastIndexOf('|');
                if (pipeIdx >= 0 && pipeIdx < stateName.Length - 1)
                {
                    stateName = stateName.Substring(pipeIdx + 1);
                }

                clipsByName[stateName] = clip;
                AnimatorState st = sm.AddState(stateName);
                st.motion = clip;
                states[stateName] = st;

                if (stateName == "Idle_Breathing") sm.defaultState = st;
            }

            states.TryGetValue("Idle_Breathing", out AnimatorState idleState);
            states.TryGetValue("Walk_Loop", out AnimatorState walkState);

            if (idleState != null && walkState != null)
            {
                AnimatorStateTransition toWalk = idleState.AddTransition(walkState);
                toWalk.hasExitTime = false;
                toWalk.duration = 0.15f;
                toWalk.AddCondition(AnimatorConditionMode.Greater, 0.02f, "MoveSpeed");

                AnimatorStateTransition toIdle = walkState.AddTransition(idleState);
                toIdle.hasExitTime = false;
                toIdle.duration = 0.15f;
                toIdle.AddCondition(AnimatorConditionMode.Less, 0.015f, "MoveSpeed");
            }

            if (states.TryGetValue("Offer_Document_Present", out var offerPresent) &&
                states.TryGetValue("Offer_Document", out var offerLoop))
            {
                var toHold = offerPresent.AddTransition(offerLoop);
                toHold.hasExitTime = true;
                toHold.exitTime = 0.95f;
                toHold.duration = 0.12f;
            }
            if (states.TryGetValue("Offer_Document_Release", out var offerRelease) && idleState != null)
            {
                var releaseToIdle = offerRelease.AddTransition(idleState);
                releaseToIdle.hasExitTime = true;
                releaseToIdle.exitTime = 0.95f;
                releaseToIdle.duration = 0.15f;
            }

            string[] autoReturnReactions = CharacterMaskAndSocketSetup.AutoReturnOneShotClips;
            for (int r = 0; r < autoReturnReactions.Length; r++)
            {
                if (states.TryGetValue(autoReturnReactions[r], out var reactState) && idleState != null)
                {
                    var toIdle = reactState.AddTransition(idleState);
                    toIdle.hasExitTime = true;
                    toIdle.exitTime = 0.95f;
                    toIdle.duration = 0.15f;
                }
            }

            if (states.TryGetValue("Walk_Start", out var walkStartState) && walkState != null)
            {
                var startToWalk = walkStartState.AddTransition(walkState);
                startToWalk.hasExitTime = true;
                startToWalk.exitTime = 0.90f;
                startToWalk.duration = 0.12f;
            }
            if (walkState != null && states.TryGetValue("Walk_Carry_Paper", out var carryWalkState))
            {
                var toCarry = walkState.AddTransition(carryWalkState);
                toCarry.hasExitTime = false;
                toCarry.duration = 0.15f;
                toCarry.AddCondition(AnimatorConditionMode.If, 0f, "IsCarryingPaper");

                var fromCarry = carryWalkState.AddTransition(walkState);
                fromCarry.hasExitTime = false;
                fromCarry.duration = 0.15f;
                fromCarry.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsCarryingPaper");

                if (idleState != null)
                {
                    var carryToIdle = carryWalkState.AddTransition(idleState);
                    carryToIdle.hasExitTime = false;
                    carryToIdle.duration = 0.15f;
                    carryToIdle.AddCondition(AnimatorConditionMode.Less, 0.015f, "MoveSpeed");
                }
            }

            CharacterMaskAndSocketSetup.EnsureMasksExist(out AvatarMask upperBodyMask, out AvatarMask headGazeMask);
            CharacterMaskAndSocketSetup.ConfigureControllerLayersAndClips(controller, clipsByName, upperBodyMask, headGazeMask);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorController BuildFactoryAnimatorController()
        {
            string controllerPath = $"{ControllersDir}/MuseDesk_Factory.controller";
            if (File.Exists(controllerPath))
            {
                AssetDatabase.DeleteAsset(controllerPath);
            }
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            string fbxPath = $"{ModelsRoot}/Factory/SoftwareFactory_Complete.fbx";
            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            for (int i = 0; i < subAssets.Length; i++)
            {
                AnimationClip clip = subAssets[i] as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__")) continue;

                string stateName = clip.name;
                int pipeIdx = stateName.LastIndexOf('|');
                if (pipeIdx >= 0 && pipeIdx < stateName.Length - 1)
                {
                    stateName = stateName.Substring(pipeIdx + 1);
                }

                AnimatorState st = sm.AddState(stateName);
                st.motion = clip;
                if (stateName == "3_Artifact_Assemble")
                {
                    sm.defaultState = st;
                }
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static Transform FindDeepChild(Transform parent, string targetName)
        {
            if (parent == null) return null;
            Transform[] all = parent.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == targetName) return all[i];
            }
            return null;
        }

        /// <summary>
        /// Normalizes Blender FBX Empty/Socket transforms that import with lossyScale=(100,100,100)
        /// and Z-up basis while preserving mesh world geometry at exact 1:1 meter scale.
        /// </summary>
        private static void NormalizeFbxHierarchyScales(GameObject root, bool isRigged)
        {
            root.transform.localScale = Vector3.one;
            if (isRigged)
            {
                // For rigged armatures, normalize all attachment Empty sockets (_Attach, _Socket, and Section 16 sockets)
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t.name.EndsWith("_Attach") || t.name.EndsWith("_Socket") || CharacterMaskAndSocketSetup.IsCanonicalCharacterSocket(t.name))
                    {
                        Vector3 ls = t.lossyScale;
                        if (Mathf.Abs(ls.x - 1f) > 0.1f || Mathf.Abs(ls.y - 1f) > 0.1f || Mathf.Abs(ls.z - 1f) > 0.1f)
                        {
                            t.localRotation = t.localRotation * Quaternion.Euler(90f, 0f, 0f);
                            Vector3 pLossy = t.parent != null ? t.parent.lossyScale : Vector3.one;
                            t.localScale = new Vector3(
                                Mathf.Abs(pLossy.x) > 1e-5f ? 1f / pLossy.x : 1f,
                                Mathf.Abs(pLossy.y) > 1e-5f ? 1f / pLossy.y : 1f,
                                Mathf.Abs(pLossy.z) > 1e-5f ? 1f / pLossy.z : 1f);
                        }
                    }
                }
                return;
            }

            // For non-rigged props/workstations, any leaf transform without a Renderer is an Empty anchor/socket.
            // Convert its local basis from Blender Z-up to Unity Y-up (+90 X) and normalize its world lossyScale to 1.
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform t = children[i];
                if (t == root.transform) continue;
                if (t.GetComponentsInChildren<Renderer>(true).Length == 0)
                {
                    t.localRotation = t.localRotation * Quaternion.Euler(90f, 0f, 0f);
                    Vector3 pLossy = t.parent != null ? t.parent.lossyScale : Vector3.one;
                    t.localScale = new Vector3(
                        Mathf.Abs(pLossy.x) > 1e-5f ? 1f / pLossy.x : 1f,
                        Mathf.Abs(pLossy.y) > 1e-5f ? 1f / pLossy.y : 1f,
                        Mathf.Abs(pLossy.z) > 1e-5f ? 1f / pLossy.z : 1f);
                }
            }
        }

        private static void ApplySharedMaterialsToHierarchy(GameObject root, Dictionary<string, Material> matMap)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                Material[] shared = r.sharedMaterials;
                for (int m = 0; m < shared.Length; m++)
                {
                    if (shared[m] == null) continue;
                    string rawName = shared[m].name;
                    int dotIdx = rawName.IndexOf('.');
                    if (dotIdx > 0) rawName = rawName.Substring(0, dotIdx);

                    Material mapped;
                    if (matMap.TryGetValue(rawName, out mapped))
                    {
                        shared[m] = mapped;
                    }
                }
                r.sharedMaterials = shared;
            }
        }

        private static TextMeshPro AddDynamicWorldText(Transform parent, string objName, string initialText, float fontSize, Vector2 rectSize, Color textColor, Vector3 localOffset, Vector3 localEuler)
        {
            if (parent == null) return null;
            GameObject go = new GameObject(objName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localOffset;
            go.transform.localRotation = Quaternion.Euler(localEuler);

            // Compensate for any parent scale so world scale of the TMP transform is exactly 0.002
            float parentLossy = Mathf.Max(0.0001f, parent.lossyScale.x);
            go.transform.localScale = Vector3.one * (0.002f / parentLossy);

            TextMeshPro tmp = go.AddComponent<TextMeshPro>();
            tmp.text = initialText;
            tmp.fontSize = fontSize;
            tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;

            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.sizeDelta = rectSize;
            }
            return tmp;
        }

        private static Dictionary<string, GameObject> GenerateAllPrefabs(
            Dictionary<string, Material> matMap,
            AnimatorController agentController,
            AnimatorController factoryController)
        {
            Dictionary<string, GameObject> prefabMap = new Dictionary<string, GameObject>();
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { ModelsRoot });

            for (int i = 0; i < guids.Length; i++)
            {
                string fbxPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!fbxPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;

                GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (modelAsset == null) continue;

                string assetName = Path.GetFileNameWithoutExtension(fbxPath);
                string category = "Decorations";
                if (fbxPath.Contains("/Characters/"))        category = "Characters";
                else if (fbxPath.Contains("/Workstations/")) category = "Workstations";
                else if (fbxPath.Contains("/TaskBoard/"))    category = "TaskBoard";
                else if (fbxPath.Contains("/Documents/"))    category = "Documents";
                else if (fbxPath.Contains("/Factory/"))      category = "Factory";
                else if (fbxPath.Contains("/Diagrams/"))     category = "Diagrams";

                bool isRigged = (category == "Characters" || category == "Factory");
                GameObject fbxInst = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                GameObject instance;
                if (fbxInst.transform.localScale.x > 10f || Quaternion.Angle(fbxInst.transform.localRotation, Quaternion.identity) > 1f)
                {
                    instance = new GameObject(assetName);
                    fbxInst.name = assetName + "_Model";
                    fbxInst.transform.SetParent(instance.transform, false);
                }
                else
                {
                    instance = fbxInst;
                    instance.name = assetName;
                }
                NormalizeFbxHierarchyScales(instance, isRigged);
                ApplySharedMaterialsToHierarchy(instance, matMap);

                Vector3 facePrefabFront = new Vector3(0f, 180f, 0f);

                if (category == "Characters")
                {
                    Animator anim = instance.GetComponentInChildren<Animator>();
                    if (anim == null) anim = instance.AddComponent<Animator>();
                    anim.runtimeAnimatorController = agentController;

                    AgentAnimatorBridge bridge = instance.AddComponent<AgentAnimatorBridge>();
                    bridge.ResolveSockets();

                    CapsuleCollider col = instance.AddComponent<CapsuleCollider>();
                    col.center = new Vector3(0f, 0.115f, 0f);
                    col.radius = 0.052f;
                    col.height = 0.23f;
                }
                else if (category == "Workstations")
                {
                    WorkstationVisualController wvc = instance.AddComponent<WorkstationVisualController>();
                    wvc.ResolveAnchorsAndParts();

                    BoxCollider col = instance.AddComponent<BoxCollider>();
                    col.center = new Vector3(0f, 0.048f, -0.045f);
                    col.size = new Vector3(0.26f, 0.096f, 0.14f);

                    if (assetName == "Workstation_Developer")
                    {
                        AddDynamicWorldText(wvc.MonitorScreenLeft,  "MonitorLeft_DynamicTMP",  "<b>MuseDesk IDE</b>\n<color=#48E5C2>build: pass</color>\n<color=#FFD166>tasks: 3</color>", 4.5f, new Vector2(36f, 20f), Color.white, new Vector3(0f, 0f, 0.004f), facePrefabFront);
                        AddDynamicWorldText(wvc.MonitorScreenRight, "MonitorRight_DynamicTMP", "<b>Logs</b>\n[OK] Auth\n[OK] API\n[OK] DB", 3.8f, new Vector2(14f, 21f), new Color(0.75f, 0.95f, 1f), new Vector3(0f, 0f, 0.004f), facePrefabFront);
                    }
                    else if (assetName == "Workstation_Testing")
                    {
                        Transform screen = wvc.GetAnchor("Testing_DisplayPanelScreen");
                        if (screen != null)
                        {
                            GameObject screenAnchor = new GameObject("QA_Screen_Anchor");
                            screenAnchor.transform.SetParent(instance.transform, false);
                            screenAnchor.transform.position = screen.position;
                            screenAnchor.transform.rotation = screen.rotation * Quaternion.Euler(90f, 0f, 0f);
                            AddDynamicWorldText(screenAnchor.transform, "QA_Screen_DynamicTMP", "<b>QA SUITE</b>\n<color=#38E58B>14/14 PASS</color>", 4.0f, new Vector2(18f, 12f), Color.white, new Vector3(0f, 0f, 0.004f), facePrefabFront);
                        }
                    }
                }
                else if (category == "Factory")
                {
                    Animator anim = instance.GetComponentInChildren<Animator>();
                    if (anim == null) anim = instance.AddComponent<Animator>();
                    anim.runtimeAnimatorController = factoryController;

                    SoftwareFactoryVisualController fvc = instance.AddComponent<SoftwareFactoryVisualController>();
                    fvc.ResolveReferences();

                    BoxCollider col = instance.AddComponent<BoxCollider>();
                    col.center = new Vector3(-0.02f, 0.06f, 0f);
                    col.size = new Vector3(0.66f, 0.12f, 0.12f);
                }
                else if (category == "TaskBoard")
                {
                    if (assetName == "TaskBoard_KanbanFrame")
                    {
                        BoxCollider col = instance.AddComponent<BoxCollider>();
                        col.center = new Vector3(0f, 0.145f, 0f);
                        col.size = new Vector3(0.43f, 0.28f, 0.03f);

                        string[] cols = { "Queued", "InProgress", "Blocked", "Completed" };
                        string[] labels = { "QUEUED", "IN PROGRESS", "BLOCKED", "COMPLETED" };
                        for (int c = 0; c < cols.Length; c++)
                        {
                            Transform anchor = FindDeepChild(instance.transform, $"HeaderLabel_{cols[c]}_Anchor");
                            AddDynamicWorldText(anchor, $"TMP_{cols[c]}", $"<b>{labels[c]}</b>", 5.0f, new Vector2(40f, 12f), Color.white, new Vector3(0f, 0f, 0.002f), facePrefabFront);
                        }
                    }
                    else if (assetName == "TaskCard_Grabbable")
                    {
                        BoxCollider col = instance.AddComponent<BoxCollider>();
                        col.center = Vector3.zero;
                        col.size = new Vector3(0.072f, 0.044f, 0.008f);

                        Transform titleAnchor = FindDeepChild(instance.transform, "Card_Title_Anchor");
                        Transform subAnchor   = FindDeepChild(instance.transform, "Card_Subtitle_Anchor");
                        AddDynamicWorldText(titleAnchor, "CardTitle_TMP",    "<b>OAuth2 Flow</b>", 3.5f, new Vector2(30f, 8f), new Color(0.15f, 0.16f, 0.20f), new Vector3(0f, 0f, 0.001f), facePrefabFront);
                        AddDynamicWorldText(subAnchor,   "CardSubtitle_TMP", "Dev • P1",          2.8f, new Vector2(30f, 6f), new Color(0.35f, 0.38f, 0.45f), new Vector3(0f, 0f, 0.001f), facePrefabFront);
                    }
                }
                else if (category == "Documents")
                {
                    BoxCollider col = instance.AddComponent<BoxCollider>();
                    if (assetName == "Doc_ExpandedFrame")
                    {
                        col.size = new Vector3(0.26f, 0.32f, 0.016f);
                        Transform surf = FindDeepChild(instance.transform, "Content_Surface_Anchor");
                        AddDynamicWorldText(surf, "ExpandedDoc_TMP", "<b>ARCHITECTURE APPROVAL</b>\n\n• Service Mesh: Ready\n• Token Vault: Verified\n• Quest 3 Passthrough: 72 FPS\n\n<i>Tap Approve or Stamp below</i>", 5.5f, new Vector2(105f, 115f), new Color(0.15f, 0.16f, 0.20f), new Vector3(0f, 0f, 0.002f), facePrefabFront);
                    }
                    else
                    {
                        col.size = new Vector3(0.065f, 0.015f, 0.082f);
                    }
                }
                else if (category == "Diagrams")
                {
                    Transform labelAnchor = FindDeepChild(instance.transform, "Label_Anchor");
                    if (labelAnchor != null)
                    {
                        string shortTitle = assetName.Replace("Diagram_", "").Replace("Node", "");
                        AddDynamicWorldText(labelAnchor, "NodeLabel_TMP", $"<b>{shortTitle}</b>", 4.0f, new Vector2(26f, 8f), Color.white, new Vector3(0f, 0f, 0.001f), facePrefabFront);
                    }
                }

                string prefabPath = $"{PrefabsRoot}/{category}/{assetName}.prefab";
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                prefabMap[assetName] = savedPrefab;
                UnityEngine.Object.DestroyImmediate(instance);
            }

            // Build composite SpatialDiagram_Demo_Layout.prefab (Frontend -> API -> Auth -> Database)
            GameObject diagDemoRoot = new GameObject("SpatialDiagram_Demo_Layout");
            SpawnChildPrefab(prefabMap, "Diagram_DisplayPlatform", diagDemoRoot.transform, Vector3.zero, Vector3.zero);
            SpawnChildPrefab(prefabMap, "Diagram_FrontendNode",    diagDemoRoot.transform, new Vector3( 0.12f, 0.016f, 0f), new Vector3(0f, 180f, 0f));
            SpawnChildPrefab(prefabMap, "Diagram_ConnectorArrow",  diagDemoRoot.transform, new Vector3( 0.08f, 0.018f, 0f), new Vector3(0f, 180f, 0f));
            SpawnChildPrefab(prefabMap, "Diagram_ApiGatewayNode",  diagDemoRoot.transform, new Vector3( 0.04f, 0.016f, 0f), new Vector3(0f, 180f, 0f));
            SpawnChildPrefab(prefabMap, "Diagram_ConnectorArrow",  diagDemoRoot.transform, new Vector3( 0.00f, 0.018f, 0f), new Vector3(0f, 180f, 0f));
            SpawnChildPrefab(prefabMap, "Diagram_AuthNode",        diagDemoRoot.transform, new Vector3(-0.04f, 0.016f, 0f), new Vector3(0f, 180f, 0f));
            SpawnChildPrefab(prefabMap, "Diagram_ConnectorArrow",  diagDemoRoot.transform, new Vector3(-0.08f, 0.018f, 0f), new Vector3(0f, 180f, 0f));
            SpawnChildPrefab(prefabMap, "Diagram_DatabaseNode",    diagDemoRoot.transform, new Vector3(-0.12f, 0.016f, 0f), new Vector3(0f, 180f, 0f));

            string diagDemoPath = $"{PrefabsRoot}/Diagrams/SpatialDiagram_Demo_Layout.prefab";
            prefabMap["SpatialDiagram_Demo_Layout"] = PrefabUtility.SaveAsPrefabAsset(diagDemoRoot, diagDemoPath);
            UnityEngine.Object.DestroyImmediate(diagDemoRoot);

            return prefabMap;
        }

        private static GameObject SpawnChildPrefab(Dictionary<string, GameObject> prefabMap, string key, Transform parent, Vector3 localPos, Vector3 localEuler)
        {
            GameObject prefab;
            if (!prefabMap.TryGetValue(key, out prefab) || prefab == null) return null;

            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            inst.name = key;
            inst.transform.localPosition = localPos;
            inst.transform.localRotation = Quaternion.Euler(localEuler);
            inst.transform.localScale = Vector3.one;
            return inst;
        }

        private static GameObject BuildWorkspaceRootPrefab(Dictionary<string, GameObject> prefabs)
        {
            // Section 11: Global Scene Composition (~1.1m wide x 0.55m deep, real-world desk scale)
            // Note: In Unity, the user sits at -Z looking toward +Z, so objects rotated 180° around Y face -Z toward the user!
            GameObject root = new GameObject("MuseDesk_Workspace_Root");
            Vector3 faceUser = new Vector3(0f, 180f, 0f);

            // 1. Research Workstation on the Left (-X = -0.34m) + Researcher Agent
            GameObject resGroup = new GameObject("ResearchStation_Group");
            resGroup.transform.SetParent(root.transform, false);
            resGroup.transform.localPosition = new Vector3(-0.34f, 0f, 0.00f);
            SpawnChildPrefab(prefabs, "Workstation_Research", resGroup.transform, Vector3.zero, faceUser);
            SpawnChildPrefab(prefabs, "Agent_Researcher",     resGroup.transform, new Vector3(0f, 0f, 0.03f), faceUser);

            // 2. Developer Workstation in the Center (X = 0.00m) + Developer Agent
            GameObject devGroup = new GameObject("DeveloperStation_Group");
            devGroup.transform.SetParent(root.transform, false);
            devGroup.transform.localPosition = new Vector3(0.00f, 0f, -0.02f);
            SpawnChildPrefab(prefabs, "Workstation_Developer", devGroup.transform, Vector3.zero, faceUser);
            SpawnChildPrefab(prefabs, "Agent_Developer",       devGroup.transform, new Vector3(0f, 0f, 0.03f), faceUser);

            // 3. Testing Workstation on the Right (+X = +0.34m) + Tester Agent
            GameObject qaGroup = new GameObject("TestingStation_Group");
            qaGroup.transform.SetParent(root.transform, false);
            qaGroup.transform.localPosition = new Vector3(0.34f, 0f, 0.00f);
            SpawnChildPrefab(prefabs, "Workstation_Testing", qaGroup.transform, Vector3.zero, faceUser);
            SpawnChildPrefab(prefabs, "Agent_Tester",        qaGroup.transform, new Vector3(0f, 0f, 0.03f), faceUser);

            // 4. Physical Kanban Task Board behind the stations (Z = +0.24m) + Sample Task Cards
            GameObject boardInst = SpawnChildPrefab(prefabs, "TaskBoard_KanbanFrame", root.transform, new Vector3(0f, 0f, 0.24f), faceUser);
            if (boardInst != null)
            {
                string[] sampleSockets = { "Socket_Queued_1", "Socket_InProgress_1", "Socket_Blocked_1", "Socket_Completed_1" };
                for (int s = 0; s < sampleSockets.Length; s++)
                {
                    Transform sock = FindDeepChild(boardInst.transform, sampleSockets[s]);
                    if (sock != null)
                    {
                        SpawnChildPrefab(prefabs, "TaskCard_Grabbable", sock, Vector3.zero, Vector3.zero);
                    }
                }
            }

            // 5. Miniature Software Factory connecting the workstations (Z = -0.18m)
            SpawnChildPrefab(prefabs, "SoftwareFactory_Complete", root.transform, new Vector3(0f, 0f, -0.18f), faceUser);

            // 6. Document Inbox & Approval Zone near User's Side (X = -0.22m, Z = -0.28m)
            GameObject inboxZone = new GameObject("UserDocumentInbox_Zone");
            inboxZone.transform.SetParent(root.transform, false);
            inboxZone.transform.localPosition = new Vector3(-0.22f, 0f, -0.28f);
            SpawnChildPrefab(prefabs, "Doc_InboxTray",     inboxZone.transform, new Vector3( 0.00f, 0f, 0f), faceUser);
            SpawnChildPrefab(prefabs, "Doc_ApprovalPaper", inboxZone.transform, new Vector3( 0.00f, 0.006f, 0f), faceUser);
            SpawnChildPrefab(prefabs, "Doc_ApprovalStamp", inboxZone.transform, new Vector3( 0.065f, 0f, 0f), faceUser);
            SpawnChildPrefab(prefabs, "Doc_SignaturePen",  inboxZone.transform, new Vector3( 0.095f, 0f, 0f), faceUser);
            SpawnChildPrefab(prefabs, "Doc_ExpandedFrame", inboxZone.transform, new Vector3(-0.22f, 0.16f, 0.12f), new Vector3(10f, 155f, 0f));

            // 7. Completed-Project Trophy & Archive Shelf behind Testing Station (X = +0.36m, Z = +0.22m)
            GameObject shelfZone = new GameObject("CompletedProjectShelf_Zone");
            shelfZone.transform.SetParent(root.transform, false);
            shelfZone.transform.localPosition = new Vector3(0.36f, 0f, 0.22f);
            SpawnChildPrefab(prefabs, "Decor_ProjectDisplayShelf", shelfZone.transform, Vector3.zero, faceUser);
            SpawnChildPrefab(prefabs, "Decor_Trophy",              shelfZone.transform, new Vector3(-0.035f, 0.088f, 0f), faceUser);
            SpawnChildPrefab(prefabs, "Decor_ProgressAward",       shelfZone.transform, new Vector3( 0.035f, 0.088f, 0f), faceUser);
            SpawnChildPrefab(prefabs, "Doc_DocumentArchive",       shelfZone.transform, new Vector3( 0.13f,  0.000f, 0f), faceUser);

            // 8. Spatial Architecture Diagram Platform (X = +0.30m, Z = -0.28m)
            SpawnChildPrefab(prefabs, "SpatialDiagram_Demo_Layout", root.transform, new Vector3(0.30f, 0f, -0.28f), Vector3.zero);

            // 9. Cozy Room-Aware Decorations around the office perimeter
            SpawnChildPrefab(prefabs, "Decor_FilingCabinet",   root.transform, new Vector3(-0.42f, 0f,  0.22f), new Vector3(0f, 160f, 0f));
            SpawnChildPrefab(prefabs, "Decor_MiniPlant",       root.transform, new Vector3(-0.42f, 0.086f, 0.22f), faceUser);
            SpawnChildPrefab(prefabs, "Decor_Toolbox",         root.transform, new Vector3( 0.18f, 0f,  0.14f), new Vector3(0f, 165f, 0f));
            SpawnChildPrefab(prefabs, "Decor_StatusIndicator", root.transform, new Vector3(-0.18f, 0f,  0.14f), faceUser);

            string rootPath = $"{PrefabsRoot}/Workspace/MuseDesk_Workspace_Root.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, rootPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        private static void BuildArtShowcaseScene(Dictionary<string, GameObject> prefabs, GameObject workspaceRootPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject keyLightGo = new GameObject("Directional_KeyLight");
            Light keyLight = keyLightGo.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1.0f, 0.97f, 0.92f);
            keyLight.intensity = 1.35f;
            keyLight.shadows = LightShadows.Soft;
            keyLightGo.transform.rotation = Quaternion.Euler(46f, -32f, 0f);

            GameObject fillLightGo = new GameObject("Directional_FillLight");
            Light fillLight = fillLightGo.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.82f, 0.90f, 1.0f);
            fillLight.intensity = 0.55f;
            fillLight.shadows = LightShadows.None;
            fillLightGo.transform.rotation = Quaternion.Euler(35f, 145f, 0f);

            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.14f, 0.16f, 0.20f, 1f);
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.02f;
            camGo.transform.position = new Vector3(-0.08f, 0.46f, -0.84f);
            camGo.transform.LookAt(new Vector3(0.0f, 0.10f, -0.02f));

            if (workspaceRootPrefab != null)
            {
                GameObject officeInst = (GameObject)PrefabUtility.InstantiatePrefab(workspaceRootPrefab);
                officeInst.name = "MuseDesk_Workspace_Root";
                officeInst.transform.position = Vector3.zero;
            }

            GameObject galleryRoot = new GameObject("AssetLibrary_GalleryRows");
            galleryRoot.transform.position = new Vector3(0f, 0f, 0.55f);

            int idx = 0;
            foreach (KeyValuePair<string, GameObject> kvp in prefabs)
            {
                if (kvp.Key.StartsWith("Workstation_") || kvp.Key.StartsWith("Agent_") || kvp.Key == "SoftwareFactory_Complete" || kvp.Key == "TaskBoard_KanbanFrame" || kvp.Key == "SpatialDiagram_Demo_Layout")
                {
                    continue;
                }
                int col = idx % 10;
                int row = idx / 10;
                Vector3 pos = new Vector3((col - 4.5f) * 0.14f, 0f, row * 0.14f);
                SpawnChildPrefab(prefabs, kvp.Key, galleryRoot.transform, pos, new Vector3(0f, 180f, 0f));
                idx++;
            }

            string scenePath = $"{ScenesDir}/ArtShowcaseScene.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            RenderCameraToPng(cam, "Screenshots/ArtPreviews/Unity_ArtGallery_capture.png", 1600, 1000);
        }

        private static void BuildMRWorkstationTestScene(GameObject workspaceRootPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject keyLightGo = new GameObject("Directional Light");
            Light keyLight = keyLightGo.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1.0f, 0.97f, 0.92f);
            keyLight.intensity = 1.25f;
            keyLightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.15f, 0.19f, 1f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.02f;
            camGo.transform.position = new Vector3(-0.06f, 0.44f, -0.80f);
            camGo.transform.LookAt(new Vector3(0.0f, 0.10f, -0.02f));

            if (workspaceRootPrefab != null)
            {
                GameObject officeInst = (GameObject)PrefabUtility.InstantiatePrefab(workspaceRootPrefab);
                officeInst.name = "MuseDesk_Workspace_Root";
                officeInst.transform.position = Vector3.zero;
            }

            string scenePath = $"{ScenesDir}/MRWorkstationTestScene.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            RenderCameraToPng(cam, "Screenshots/ArtPreviews/Unity_OfficeShowcase_capture.png", 1600, 1000);
        }

        private static void RenderCameraToPng(Camera cam, string relativePath, int width, int height)
        {
            if (cam == null) return;
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture prevTarget = cam.targetTexture;

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            UnityEngine.Object.DestroyImmediate(rt);

            byte[] pngBytes = ImageConversion.EncodeToPNG(tex);
            UnityEngine.Object.DestroyImmediate(tex);

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllBytes(fullPath, pngBytes);
        }
    }
}
#endif
