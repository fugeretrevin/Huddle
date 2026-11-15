#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MuseDesk.Editor.ArtPipeline
{
    /// <summary>
    /// Creates and maintains Generic AvatarMasks (Mask_UpperBody, Mask_HeadGaze),
    /// ensures all 14 canonical character sockets (5 legacy _Attach + 9 Section 16 sockets)
    /// exist with unit world scale, and wires MuseDesk_Agent.controller and
    /// MuseDesk_WallAgent.controller with all 77 base-layer states plus optional
    /// UpperBody and HeadGaze overlay layers (weight 0 by default).
    /// </summary>
    public static class CharacterMaskAndSocketSetup
    {
        public const string ControllersDir = "Assets/MuseDesk/Art/Controllers";
        public const string UpperBodyMaskPath = ControllersDir + "/Mask_UpperBody.mask";
        public const string HeadGazeMaskPath = ControllersDir + "/Mask_HeadGaze.mask";
        public const string AgentControllerPath = ControllersDir + "/MuseDesk_Agent.controller";
        public const string WallAgentControllerPath = ControllersDir + "/WallHabitat/MuseDesk_WallAgent.controller";

        private const string ResearcherFbxPath = "Assets/MuseDesk/Art/Models/Characters/Agent_Researcher.fbx";
        private const string ManagerFbxPath = "Assets/MuseDesk/Art/Models/WallHabitat/Agent_Manager.fbx";

        private static readonly string[] CharacterPrefabPaths =
        {
            "Assets/MuseDesk/Art/Prefabs/Characters/Agent_Researcher.prefab",
            "Assets/MuseDesk/Art/Prefabs/Characters/Agent_Developer.prefab",
            "Assets/MuseDesk/Art/Prefabs/Characters/Agent_Tester.prefab",
            "Assets/MuseDesk/Art/Prefabs/WallHabitat/Agent_Manager.prefab",
        };

        public static readonly string[] Canonical77Clips =
        {
            // A. Basic movement (12)
            "Idle_Breathing", "Idle_Fidget", "Happy_Idle",
            "Walk_Start", "Walk_Loop", "Walk_Stop", "Walk_Carry_Paper",
            "Turn_Left", "Turn_Right",
            "Sit_Down", "Seated_Idle", "Stand_Up",
            // B. Communication (12)
            "Talking", "Listening", "Thinking", "Confused", "Excited", "Worried",
            "Pointing", "Waving", "Nodding", "Shaking_Head", "React_Approve", "React_Reject",
            // C. Working (6)
            "Typing", "Reading", "Writing", "Inspecting", "Using_Workstation", "Waiting_For_Input",
            // D. Object interactions & handoffs (12)
            "Pick_Up_Document", "Hold_Document", "Offer_Document_Present", "Offer_Document",
            "Offer_Document_Release", "Receive_Document", "Put_Document_Down",
            "Carry_Package", "Exchange_Package", "Handoff_Give", "Handoff_Receive", "Celebrate_Handoff",
            // E. Result reactions (7)
            "Success", "React_Completion_Cheer", "Failure", "Blocked", "Error", "Task_Completed", "Cheer",
            // F. Wall habitat, elevator, selection, desk visit & portal (16)
            "Elevator_Queue_Idle", "Elevator_Step_Board", "Elevator_Ride_Idle", "Elevator_Step_Exit",
            "Select_Highlighted_Wave",
            "DeskVisit_Perch_Idle", "DeskVisit_Listening", "DeskVisit_Explain",
            "Portal_Emerge_Step", "Portal_Converse_Loop", "Portal_Return_Step",
            "Manager_Lookout_Idle", "Manager_Review_Plan", "Manager_Operate_Printer",
            "Manager_Delegate_Point", "Manager_Receive_Report",
            // G. Living world interaction, reaction & role clips (12)
            "React_Glance", "React_Poke_Surprise", "React_Wave_Greet",
            "React_Interrupt_Listen", "React_Assign_Ack",
            "Carry_Suspended_Idle", "Researcher_Compare_Papers",
            "Developer_Adjust_Keyboard", "Tester_Note_Take",
            "Manager_Welcome_User", "Manager_Acknowledge_Worker", "Return_To_Work",
        };

        public static readonly string[] AutoReturnOneShotClips =
        {
            "React_Approve", "React_Reject", "React_Completion_Cheer",
            "Handoff_Give", "Handoff_Receive", "Walk_Stop", "Turn_Left", "Turn_Right",
            "Elevator_Step_Board", "Elevator_Step_Exit", "Portal_Emerge_Step", "Portal_Return_Step",
            "React_Glance", "React_Poke_Surprise", "React_Wave_Greet",
            "React_Interrupt_Listen", "React_Assign_Ack",
            "Developer_Adjust_Keyboard", "Manager_Welcome_User",
            "Manager_Acknowledge_Worker", "Return_To_Work",
        };

        private static readonly string[] UpperBodyLayerClips =
        {
            "Hold_Document", "Offer_Document_Present", "Offer_Document", "Offer_Document_Release",
            "Carry_Package", "Waving", "Pointing",
            "React_Wave_Greet", "React_Assign_Ack", "React_Interrupt_Listen",
            "Manager_Delegate_Point", "Manager_Acknowledge_Worker", "Manager_Welcome_User",
            "Researcher_Compare_Papers", "Tester_Note_Take",
        };

        private static readonly string[] UpperBodyOneShotClips =
        {
            "Offer_Document_Release", "React_Wave_Greet", "React_Assign_Ack",
            "React_Interrupt_Listen", "Manager_Acknowledge_Worker", "Manager_Welcome_User",
        };

        private static readonly string[] HeadGazeLayerClips =
        {
            "React_Glance", "Nodding", "Shaking_Head",
            "Listening", "React_Interrupt_Listen", "DeskVisit_Listening",
        };

        private static readonly string[] HeadGazeOneShotClips =
        {
            "React_Glance", "Nodding", "Shaking_Head", "React_Interrupt_Listen",
        };

        public struct SocketSpec
        {
            public string Name;
            public string ParentBone;
            public Vector3 BlenderWorldPos;

            public SocketSpec(string name, string parentBone, float bx, float by, float bz)
            {
                Name = name;
                ParentBone = parentBone;
                BlenderWorldPos = new Vector3(bx, by, bz);
            }
        }

        public static readonly SocketSpec[] CanonicalSockets =
        {
            // Legacy 5 attachment Empties
            new SocketSpec("RightHand_Attach", "Hand_R", -0.047f, -0.016f, 0.045f),
            new SocketSpec("LeftHand_Attach",  "Hand_L",  0.047f, -0.016f, 0.045f),
            new SocketSpec("Head_Attach",      "Head",    0.000f,  0.000f, 0.194f),
            new SocketSpec("Back_Attach",      "Chest",   0.000f,  0.040f, 0.095f),
            new SocketSpec("Document_Attach",  "Chest",   0.000f, -0.072f, 0.092f),
            // Section 16 sockets
            new SocketSpec("DocumentHold",           "Chest",  0.000f, -0.056f, 0.090f),
            new SocketSpec("DocumentOffer",          "Chest",  0.000f, -0.088f, 0.104f),
            new SocketSpec("TaskCardHold",           "Hand_R", -0.047f, -0.020f, 0.048f),
            new SocketSpec("ArtifactHold",           "Chest",  0.000f, -0.068f, 0.086f),
            new SocketSpec("HeadLookTarget",         "Head",   0.000f, -0.036f, 0.156f),
            new SocketSpec("ConversationFocus",      "Head",   0.000f, -0.060f, 0.155f),
            new SocketSpec("FootGroundReference",    "Hips",   0.000f,  0.000f, 0.000f),
            new SocketSpec("ElevatorPassengerRoot",  "Hips",   0.000f,  0.000f, 0.000f),
            new SocketSpec("PortalPresentationRoot", "Hips",   0.000f,  0.000f, 0.000f),
        };

        private static readonly HashSet<string> CanonicalSocketNames =
            new HashSet<string>(CanonicalSockets.Select(s => s.Name), StringComparer.Ordinal);

        // Full canonical transform paths relative to the Animator root GameObject.
        private static readonly string[] CanonicalArmatureTransformPaths =
        {
            "",
            "Armature",
            "Armature/Root",
            "Armature/Root/Hips",
            "Armature/Root/Hips/FootGroundReference",
            "Armature/Root/Hips/ElevatorPassengerRoot",
            "Armature/Root/Hips/PortalPresentationRoot",
            "Armature/Root/Hips/Thigh_L",
            "Armature/Root/Hips/Thigh_L/Shin_L",
            "Armature/Root/Hips/Thigh_L/Shin_L/Foot_L",
            "Armature/Root/Hips/Thigh_R",
            "Armature/Root/Hips/Thigh_R/Shin_R",
            "Armature/Root/Hips/Thigh_R/Shin_R/Foot_R",
            "Armature/Root/Hips/Spine",
            "Armature/Root/Hips/Spine/Chest",
            "Armature/Root/Hips/Spine/Chest/Back_Attach",
            "Armature/Root/Hips/Spine/Chest/Document_Attach",
            "Armature/Root/Hips/Spine/Chest/DocumentHold",
            "Armature/Root/Hips/Spine/Chest/DocumentOffer",
            "Armature/Root/Hips/Spine/Chest/ArtifactHold",
            "Armature/Root/Hips/Spine/Chest/Shoulder_L",
            "Armature/Root/Hips/Spine/Chest/Shoulder_L/UpperArm_L",
            "Armature/Root/Hips/Spine/Chest/Shoulder_L/UpperArm_L/Forearm_L",
            "Armature/Root/Hips/Spine/Chest/Shoulder_L/UpperArm_L/Forearm_L/Hand_L",
            "Armature/Root/Hips/Spine/Chest/Shoulder_L/UpperArm_L/Forearm_L/Hand_L/LeftHand_Attach",
            "Armature/Root/Hips/Spine/Chest/Shoulder_R",
            "Armature/Root/Hips/Spine/Chest/Shoulder_R/UpperArm_R",
            "Armature/Root/Hips/Spine/Chest/Shoulder_R/UpperArm_R/Forearm_R",
            "Armature/Root/Hips/Spine/Chest/Shoulder_R/UpperArm_R/Forearm_R/Hand_R",
            "Armature/Root/Hips/Spine/Chest/Shoulder_R/UpperArm_R/Forearm_R/Hand_R/RightHand_Attach",
            "Armature/Root/Hips/Spine/Chest/Shoulder_R/UpperArm_R/Forearm_R/Hand_R/TaskCardHold",
            "Armature/Root/Hips/Spine/Chest/Neck",
            "Armature/Root/Hips/Spine/Chest/Neck/Head",
            "Armature/Root/Hips/Spine/Chest/Neck/Head/Eye_L",
            "Armature/Root/Hips/Spine/Chest/Neck/Head/Eye_R",
            "Armature/Root/Hips/Spine/Chest/Neck/Head/Brow_L",
            "Armature/Root/Hips/Spine/Chest/Neck/Head/Brow_R",
            "Armature/Root/Hips/Spine/Chest/Neck/Head/Accessory_Root",
            "Armature/Root/Hips/Spine/Chest/Neck/Head/Accessory_Root/Accessory_Tip",
            "Armature/Root/Hips/Spine/Chest/Neck/Head/Head_Attach",
            "Armature/Root/Hips/Spine/Chest/Neck/Head/HeadLookTarget",
            "Armature/Root/Hips/Spine/Chest/Neck/Head/ConversationFocus",
        };

        public static bool IsCanonicalCharacterSocket(string transformName)
        {
            return !string.IsNullOrEmpty(transformName) && CanonicalSocketNames.Contains(transformName);
        }

        [MenuItem("MuseDesk/Art Pipeline/Build Masks & Ensure Character Sockets")]
        public static void BuildMasksAndEnsureSockets()
        {
            EnsureFolder(ControllersDir);
            EnsureFolder(ControllersDir + "/WallHabitat");

            EnsureMasksExist(out AvatarMask upperBodyMask, out AvatarMask headGazeMask);

            int updatedPrefabs = 0;
            foreach (string prefabPath in CharacterPrefabPaths)
            {
                if (EnsureSocketsOnCharacterPrefab(prefabPath))
                    updatedPrefabs++;
            }

            Dictionary<string, AnimationClip> researcherClips = LoadClipsWithFallback(ResearcherFbxPath, ManagerFbxPath);
            Dictionary<string, AnimationClip> managerClips = LoadClipsWithFallback(ManagerFbxPath, ResearcherFbxPath);

            EnsureControllerWired(AgentControllerPath, researcherClips, upperBodyMask, headGazeMask);
            EnsureControllerWired(WallAgentControllerPath, managerClips, upperBodyMask, headGazeMask);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[CharacterMaskAndSocketSetup] Built masks ({UpperBodyMaskPath}, {HeadGazeMaskPath}), " +
                      $"verified sockets on {updatedPrefabs} prefabs, and wired 77-clip layers on agent controllers.");
        }

        public static void EnsureMasksExist(out AvatarMask upperBodyMask, out AvatarMask headGazeMask)
        {
            EnsureFolder(ControllersDir);

            List<string> allPaths = CollectMaskTransformPaths();
            upperBodyMask = CreateOrUpdateMask(UpperBodyMaskPath, allPaths, IsUpperBodyPathActive);
            headGazeMask = CreateOrUpdateMask(HeadGazeMaskPath, allPaths, IsHeadGazePathActive);
        }

        public static void ConfigureControllerLayersAndClips(
            AnimatorController controller,
            Dictionary<string, AnimationClip> clipsByName,
            AvatarMask upperBodyMask,
            AvatarMask headGazeMask)
        {
            if (controller == null) return;

            EnsureParameter(controller, "MoveSpeed", AnimatorControllerParameterType.Float);
            EnsureParameter(controller, "IsCarryingPaper", AnimatorControllerParameterType.Bool);

            // Base layer (index 0): preserve existing state machine & ensure all 77 states + transitions exist
            AnimatorStateMachine baseSm = controller.layers[0].stateMachine;
            Dictionary<string, AnimatorState> baseStates = CollectStates(baseSm);

            int idx = baseStates.Count;
            foreach (string clipName in Canonical77Clips)
            {
                clipsByName.TryGetValue(clipName, out AnimationClip clip);
                if (!baseStates.TryGetValue(clipName, out AnimatorState st))
                {
                    st = baseSm.AddState(clipName, new Vector3(260 + (idx % 4) * 230, (idx / 4) * 70, 0));
                    baseStates[clipName] = st;
                    idx++;
                }
                if (clip != null) st.motion = clip;
                if (clipName == "Idle_Breathing") baseSm.defaultState = st;
            }

            // Include any extra clips present in the FBX beyond the canonical 77
            foreach (var kv in clipsByName.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!baseStates.TryGetValue(kv.Key, out AnimatorState st))
                {
                    st = baseSm.AddState(kv.Key, new Vector3(260 + (idx % 4) * 230, (idx / 4) * 70, 0));
                    baseStates[kv.Key] = st;
                    idx++;
                }
                if (kv.Value != null) st.motion = kv.Value;
            }

            WireBaseTransitions(baseStates);

            // Layer 1: UpperBody (weight 0 by default)
            EnsureOverlayLayer(
                controller,
                "UpperBody",
                "UpperBody_Empty",
                upperBodyMask,
                UpperBodyLayerClips,
                UpperBodyOneShotClips,
                clipsByName);

            // Layer 2: HeadGaze (weight 0 by default)
            EnsureOverlayLayer(
                controller,
                "HeadGaze",
                "HeadGaze_Empty",
                headGazeMask,
                HeadGazeLayerClips,
                HeadGazeOneShotClips,
                clipsByName);

            EditorUtility.SetDirty(controller);
        }

        private static void EnsureControllerWired(
            string ctrlPath,
            Dictionary<string, AnimationClip> clipsByName,
            AvatarMask upperBodyMask,
            AvatarMask headGazeMask)
        {
            AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            if (ctrl == null)
            {
                ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            }
            ConfigureControllerLayersAndClips(ctrl, clipsByName, upperBodyMask, headGazeMask);
        }

        private static void EnsureOverlayLayer(
            AnimatorController controller,
            string layerName,
            string emptyStateName,
            AvatarMask mask,
            string[] layerClips,
            string[] oneShotClips,
            Dictionary<string, AnimationClip> clipsByName)
        {
            int layerIdx = -1;
            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].name == layerName)
                {
                    layerIdx = i;
                    break;
                }
            }

            if (layerIdx < 0)
            {
                controller.AddLayer(layerName);
                layers = controller.layers;
                layerIdx = layers.Length - 1;
            }

            layers[layerIdx].avatarMask = mask;
            layers[layerIdx].defaultWeight = 0f;
            layers[layerIdx].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[layerIdx].iKPass = false;
            controller.layers = layers;

            AnimatorStateMachine sm = controller.layers[layerIdx].stateMachine;
            Dictionary<string, AnimatorState> states = CollectStates(sm);

            if (!states.TryGetValue(emptyStateName, out AnimatorState emptyState))
            {
                emptyState = sm.AddState(emptyStateName, new Vector3(260, 0, 0));
                states[emptyStateName] = emptyState;
            }
            emptyState.motion = null;
            sm.defaultState = emptyState;

            for (int i = 0; i < layerClips.Length; i++)
            {
                string clipName = layerClips[i];
                if (!states.TryGetValue(clipName, out AnimatorState st))
                {
                    st = sm.AddState(clipName, new Vector3(520 + (i % 3) * 230, (i / 3) * 70, 0));
                    states[clipName] = st;
                }
                if (clipsByName.TryGetValue(clipName, out AnimationClip clip) && clip != null)
                {
                    st.motion = clip;
                }
            }

            for (int i = 0; i < oneShotClips.Length; i++)
            {
                if (states.TryGetValue(oneShotClips[i], out AnimatorState st))
                {
                    EnsureExitTimeTransition(st, emptyState, 0.95f, 0.15f);
                }
            }
        }

        private static void WireBaseTransitions(Dictionary<string, AnimatorState> states)
        {
            states.TryGetValue("Idle_Breathing", out AnimatorState idleState);
            states.TryGetValue("Walk_Loop", out AnimatorState walkState);

            if (idleState != null && walkState != null)
            {
                if (!HasTransition(idleState, walkState))
                {
                    AnimatorStateTransition toWalk = idleState.AddTransition(walkState);
                    toWalk.hasExitTime = false;
                    toWalk.duration = 0.15f;
                    toWalk.AddCondition(AnimatorConditionMode.Greater, 0.02f, "MoveSpeed");
                }
                if (!HasTransition(walkState, idleState))
                {
                    AnimatorStateTransition toIdle = walkState.AddTransition(idleState);
                    toIdle.hasExitTime = false;
                    toIdle.duration = 0.15f;
                    toIdle.AddCondition(AnimatorConditionMode.Less, 0.015f, "MoveSpeed");
                }
            }

            if (states.TryGetValue("Offer_Document_Present", out AnimatorState offerPresent) &&
                states.TryGetValue("Offer_Document", out AnimatorState offerLoop))
            {
                EnsureExitTimeTransition(offerPresent, offerLoop, 0.95f, 0.12f);
            }
            if (states.TryGetValue("Offer_Document_Release", out AnimatorState offerRelease) && idleState != null)
            {
                EnsureExitTimeTransition(offerRelease, idleState, 0.95f, 0.15f);
            }
            if (states.TryGetValue("Walk_Start", out AnimatorState walkStart) && walkState != null)
            {
                EnsureExitTimeTransition(walkStart, walkState, 0.90f, 0.12f);
            }
            if (walkState != null && states.TryGetValue("Walk_Carry_Paper", out AnimatorState carryWalk))
            {
                if (!HasTransition(walkState, carryWalk))
                {
                    AnimatorStateTransition toCarry = walkState.AddTransition(carryWalk);
                    toCarry.hasExitTime = false;
                    toCarry.duration = 0.15f;
                    toCarry.AddCondition(AnimatorConditionMode.If, 0f, "IsCarryingPaper");
                }
                if (!HasTransition(carryWalk, walkState))
                {
                    AnimatorStateTransition fromCarry = carryWalk.AddTransition(walkState);
                    fromCarry.hasExitTime = false;
                    fromCarry.duration = 0.15f;
                    fromCarry.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsCarryingPaper");
                }
                if (idleState != null && !HasTransition(carryWalk, idleState))
                {
                    AnimatorStateTransition carryToIdle = carryWalk.AddTransition(idleState);
                    carryToIdle.hasExitTime = false;
                    carryToIdle.duration = 0.15f;
                    carryToIdle.AddCondition(AnimatorConditionMode.Less, 0.015f, "MoveSpeed");
                }
            }

            if (idleState != null)
            {
                foreach (string oneShot in AutoReturnOneShotClips)
                {
                    if (states.TryGetValue(oneShot, out AnimatorState st))
                    {
                        EnsureExitTimeTransition(st, idleState, 0.95f, 0.15f);
                    }
                }
            }
        }

        private static bool EnsureSocketsOnCharacterPrefab(string prefabPath)
        {
            if (!File.Exists(prefabPath)) return false;

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) return false;

            bool modified = false;
            try
            {
                for (int i = 0; i < CanonicalSockets.Length; i++)
                {
                    SocketSpec spec = CanonicalSockets[i];
                    Transform socket = FindDeepChild(root.transform, spec.Name);
                    Transform parentBone = FindDeepChild(root.transform, spec.ParentBone);

                    if (socket == null && parentBone != null)
                    {
                        GameObject go = new GameObject(spec.Name);
                        socket = go.transform;
                        socket.SetParent(parentBone, false);
                        // Convert Blender (-Y front, +Z up, +X left) to Unity (+Z front, +Y up, -X left)
                        Vector3 rootSpacePos = new Vector3(-spec.BlenderWorldPos.x, spec.BlenderWorldPos.z, -spec.BlenderWorldPos.y);
                        socket.position = root.transform.TransformPoint(rootSpacePos);
                        socket.rotation = root.transform.rotation;
                        NormalizeSocketScale(socket);
                        modified = true;
                    }
                    else if (socket != null)
                    {
                        if (NeedsScaleOrBasisNormalization(socket))
                        {
                            socket.localRotation = socket.localRotation * Quaternion.Euler(90f, 0f, 0f);
                            NormalizeSocketScale(socket);
                            modified = true;
                        }
                    }
                }

                if (modified)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return modified;
        }

        private static bool NeedsScaleOrBasisNormalization(Transform t)
        {
            Vector3 ls = t.lossyScale;
            return Mathf.Abs(ls.x - 1f) > 0.1f ||
                   Mathf.Abs(ls.y - 1f) > 0.1f ||
                   Mathf.Abs(ls.z - 1f) > 0.1f;
        }

        private static void NormalizeSocketScale(Transform t)
        {
            Vector3 pLossy = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(
                Mathf.Abs(pLossy.x) > 1e-5f ? 1f / pLossy.x : 1f,
                Mathf.Abs(pLossy.y) > 1e-5f ? 1f / pLossy.y : 1f,
                Mathf.Abs(pLossy.z) > 1e-5f ? 1f / pLossy.z : 1f);
        }

        private static List<string> CollectMaskTransformPaths()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();

            foreach (string p in CanonicalArmatureTransformPaths)
            {
                if (seen.Add(p)) ordered.Add(p);
            }

            // Also inspect Agent_Researcher.fbx if present so any body mesh or extra child transforms are explicitly listed
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ResearcherFbxPath);
            if (fbx != null)
            {
                CollectTransformPathsRecursive(fbx.transform, "", seen, ordered);
            }

            return ordered;
        }

        private static void CollectTransformPathsRecursive(
            Transform current,
            string currentPath,
            HashSet<string> seen,
            List<string> ordered)
        {
            if (seen.Add(currentPath)) ordered.Add(currentPath);

            for (int i = 0; i < current.childCount; i++)
            {
                Transform child = current.GetChild(i);
                string childPath = string.IsNullOrEmpty(currentPath) ? child.name : (currentPath + "/" + child.name);
                CollectTransformPathsRecursive(child, childPath, seen, ordered);
            }
        }

        private static AvatarMask CreateOrUpdateMask(
            string maskPath,
            List<string> paths,
            Func<string, bool> isActivePredicate)
        {
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, maskPath);
            }

            mask.transformCount = paths.Count;
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                mask.SetTransformPath(i, path);
                mask.SetTransformActive(i, isActivePredicate(path));
            }

            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static bool IsUpperBodyPathActive(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            // Enable Spine and everything under Spine (Chest, Neck, Head, Eye_*, Brow_*, Accessory_*, Shoulder_*/UpperArm_*/Forearm_*/Hand_*, upper-body sockets).
            // Legs (Thigh_*, Shin_*, Foot_*), Root, Hips, and Hips-parented sockets stay disabled.
            return path == "Armature/Root/Hips/Spine" ||
                   path.StartsWith("Armature/Root/Hips/Spine/", StringComparison.Ordinal);
        }

        private static bool IsHeadGazePathActive(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            const string neckPath = "Armature/Root/Hips/Spine/Chest/Neck";
            return path == neckPath ||
                   path.StartsWith(neckPath + "/", StringComparison.Ordinal);
        }

        private static Dictionary<string, AnimationClip> LoadClipsWithFallback(string primaryFbx, string fallbackFbx)
        {
            var dict = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            LoadClipsInto(fallbackFbx, dict);
            LoadClipsInto(primaryFbx, dict);
            return dict;
        }

        private static void LoadClipsInto(string fbxPath, Dictionary<string, AnimationClip> dest)
        {
            if (string.IsNullOrEmpty(fbxPath) || !File.Exists(fbxPath)) return;
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    string clean = clip.name;
                    int pipe = clean.LastIndexOf('|');
                    if (pipe >= 0 && pipe < clean.Length - 1) clean = clean.Substring(pipe + 1);
                    dest[clean] = clip;
                }
            }
        }

        private static Dictionary<string, AnimatorState> CollectStates(AnimatorStateMachine sm)
        {
            var dict = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            foreach (ChildAnimatorState child in sm.states)
            {
                if (child.state != null) dict[child.state.name] = child.state;
            }
            return dict;
        }

        private static void EnsureParameter(AnimatorController controller, string paramName, AnimatorControllerParameterType type)
        {
            foreach (AnimatorControllerParameter p in controller.parameters)
            {
                if (p.name == paramName && p.type == type) return;
            }
            controller.AddParameter(paramName, type);
        }

        private static bool HasTransition(AnimatorState src, AnimatorState dst)
        {
            foreach (AnimatorStateTransition t in src.transitions)
            {
                if (t.destinationState == dst) return true;
            }
            return false;
        }

        private static void EnsureExitTimeTransition(AnimatorState src, AnimatorState dst, float exitTime, float duration)
        {
            if (src == null || dst == null || HasTransition(src, dst)) return;
            AnimatorStateTransition t = src.AddTransition(dst);
            t.hasExitTime = true;
            t.exitTime = exitTime;
            t.duration = duration;
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

        private static void EnsureFolder(string path)
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
    }
}
#endif
