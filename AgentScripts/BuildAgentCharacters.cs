using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using MuseDesk.Agents;
using MuseDesk.Documents;

/// <summary>
/// Editor builder: integrates the Antigravity-exported rigged Pipkin-Sprite FBX characters into
/// the live MuseDesk agent system. Configures FBX import (39 clips + loop flags), builds ONE shared
/// Generic Animator Controller (MuseDesk_Agent.controller) driven by StateId/IsMoving/triggers, and
/// assembles a character prefab wired with AgentView + AgentMovement + AgentAnimatorBridge.
/// Run entries via: unity command script run --file AgentScripts/BuildAgentCharacters.cs --entry BuildAgentCharacters.X
/// </summary>
public static class BuildAgentCharacters
{
    const string CharDir = "Assets/MuseDesk/Art/Models/Characters/";
    const string CtrlPath = "Assets/MuseDesk/Art/Controllers/MuseDesk_Agent.controller";
    const string PrefabDir = "Assets/MuseDesk/Prefabs/Agents/";
    const string SceneDir = "Assets/MuseDesk/Scenes/";
    const string ReferenceFbx = CharDir + "Agent_Researcher.fbx"; // clips authored here drive the shared controller

    static readonly HashSet<string> LoopClips = new HashSet<string>
    {
        "Idle_Breathing","Idle_Fidget","Happy_Idle","Walk_Loop","Seated_Idle","Talking","Listening",
        "Thinking","Excited","Worried","Waving","Typing","Reading","Writing","Inspecting",
        "Using_Workstation","Waiting_For_Input","Hold_Document","Carry_Package","Blocked","Cheer",
    };

    // Base states: StateId -> (state name, clip). MUST mirror AgentAnimatorBridge.StateId().
    static readonly (int id, string name, string clip)[] Bases =
    {
        (0, "Idle",        "Idle_Breathing"),
        (1, "Researching", "Reading"),
        (2, "Coding",      "Typing"),
        (3, "Testing",     "Inspecting"),
        (4, "Reporting",   "Talking"),
        (5, "Approaching", "Walk_Loop"),
        (6, "Waiting",     "Waiting_For_Input"),
        (7, "Complete",    "Task_Completed"),
        (8, "Error",       "Error"),
    };

    // One-shot reactions: trigger param -> clip.
    static readonly (string trig, string clip)[] OneShots =
    {
        ("TrOffer",          "Offer_Document"),
        ("TrSuccess",        "Success"),
        ("TrFailure",        "Failure"),
        ("TrError",          "Error"),
        ("TrHandoffGive",    "Exchange_Package"),
        ("TrHandoffReceive", "Receive_Document"),
    };

    // ---- Public entry points ----

    /// <summary>Full researcher pass: import config + shared controller + prefab + integration scene.</summary>
    public static string BuildResearcher()
    {
        ConfigureImport(CharDir + "Agent_Researcher.fbx");
        BuildController();
        var p = BuildPrefab("Agent_Researcher.fbx", "researcher", "Researcher", new Color(0.56f, 0.49f, 0.78f));
        AssetDatabase.SaveAssets();
        return "Researcher prefab: " + p;
    }

    /// <summary>Replicate the proven pipeline to Developer + Tester (controller already shared).</summary>
    public static string BuildDeveloperAndTester()
    {
        var sb = new System.Text.StringBuilder();
        ConfigureImport(CharDir + "Agent_Developer.fbx");
        sb.AppendLine(BuildPrefab("Agent_Developer.fbx", "developer", "Developer", new Color(1f, 0.5f, 0.4f)));
        ConfigureImport(CharDir + "Agent_Tester.fbx");
        sb.AppendLine(BuildPrefab("Agent_Tester.fbx", "tester", "Tester", new Color(0.34f, 0.77f, 0.59f)));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    public static string ConfigureAllImports()
    {
        foreach (var f in new[] { "Agent_Researcher.fbx", "Agent_Developer.fbx", "Agent_Tester.fbx" })
            ConfigureImport(CharDir + f);
        return "Configured imports for 3 characters.";
    }

    /// <summary>
    /// P1 finish: replace the procedural-primitive agents in MainMRScene with the rigged prefabs,
    /// preserving each station slot + agentId, and rewiring AgentRegistry.agents and
    /// HeldPaperController.agentHoldPoint (now the researcher's Document_Attach bone).
    /// MainMRScene is committed clean, so git is the restore point.
    /// </summary>
    public static string SwapAgentsIntoMainScene()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new System.Text.StringBuilder();
        var map = new[]
        {
            ("researcher", "Agent_Researcher"),
            ("developer",  "Agent_Developer"),
            ("tester",     "Agent_Tester"),
        };

        var newViews = new Dictionary<string, AgentView>();
        Transform researcherDocAttach = null;

        foreach (var (id, prefabName) in map)
        {
            var old = Object.FindObjectsByType<AgentView>(FindObjectsInactive.Include).FirstOrDefault(v => v.agentId == id);
            if (old == null) { sb.AppendLine("MISSING old agent: " + id); continue; }
            var oldGo = old.gameObject;
            var parent = oldGo.transform.parent;
            var lp = oldGo.transform.localPosition;
            var lr = oldGo.transform.localRotation;
            string nm = oldGo.name;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + prefabName + ".prefab");
            if (prefab == null) { sb.AppendLine("MISSING prefab: " + prefabName); continue; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.SetParent(parent, false);
            inst.transform.localPosition = lp;
            inst.transform.localRotation = lr;
            inst.name = nm;

            var view = inst.GetComponent<AgentView>();
            view.agentId = id;
            var mv = inst.GetComponent<AgentMovement>();
            var fud = inst.AddComponent<FaceUserWhileDelivering>();
            fud.movement = mv;
            newViews[id] = view;

            if (id == "researcher")
                researcherDocAttach = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Document_Attach");

            Object.DestroyImmediate(oldGo);
            sb.AppendLine($"swapped {id} -> {prefabName} at {parent.name} lp={lp}");
        }

        var reg = Object.FindObjectsByType<AgentRegistry>(FindObjectsInactive.Include).FirstOrDefault();
        if (reg != null)
        {
            reg.agents = new List<AgentView> { newViews.GetValueOrDefault("researcher"), newViews.GetValueOrDefault("developer"), newViews.GetValueOrDefault("tester") };
            EditorUtility.SetDirty(reg);
            sb.AppendLine("AgentRegistry.agents rewired (" + reg.agents.Count(a => a != null) + " set)");
        }
        else sb.AppendLine("NO AgentRegistry");

        var hpc = Object.FindObjectsByType<HeldPaperController>(FindObjectsInactive.Include).FirstOrDefault();
        if (hpc != null && researcherDocAttach != null)
        {
            hpc.agentHoldPoint = researcherDocAttach;
            EditorUtility.SetDirty(hpc);
            sb.AppendLine("HeldPaperController.agentHoldPoint -> researcher Document_Attach");
        }
        else sb.AppendLine("HeldPaperController hold-point NOT rewired (hpc=" + (hpc != null) + " docAttach=" + (researcherDocAttach != null) + ")");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sb.ToString();
    }

    // ---- Steps ----

    public static string ConfigureImport(string fbxPath)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        if (imp == null) return "NO IMPORTER: " + fbxPath;
        imp.animationType = ModelImporterAnimationType.Generic;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; // Generic rig needs an Avatar to drive bones
        imp.importConstraints = false;
        imp.importVisibility = false;

        var takes = imp.importedTakeInfos;
        var clips = new ModelImporterClipAnimation[takes.Length];
        for (int i = 0; i < takes.Length; i++)
        {
            var t = takes[i];
            clips[i] = new ModelImporterClipAnimation
            {
                name = t.name,
                takeName = t.name,
                firstFrame = Mathf.RoundToInt(t.startTime * t.sampleRate),
                lastFrame = Mathf.RoundToInt(t.stopTime * t.sampleRate),
                loopTime = LoopClips.Contains(t.name),
                wrapMode = WrapMode.Default,
                keepOriginalOrientation = true,
                keepOriginalPositionY = true,
                keepOriginalPositionXZ = true,
            };
        }
        imp.clipAnimations = clips;
        EditorUtility.SetDirty(imp);
        imp.SaveAndReimport();
        return "Configured " + takes.Length + " clips (" + clips.Count(c => c.loopTime) + " looping): " + fbxPath;
    }

    public static string BuildController()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CtrlPath));
        AssetDatabase.DeleteAsset(CtrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);

        ctrl.AddParameter("StateId", AnimatorControllerParameterType.Int);
        ctrl.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        foreach (var os in OneShots)
            ctrl.AddParameter(os.trig, AnimatorControllerParameterType.Trigger);

        var clipMap = LoadClips(ReferenceFbx);
        var sm = ctrl.layers[0].stateMachine;

        var baseStates = new Dictionary<int, AnimatorState>();
        foreach (var b in Bases)
        {
            var st = sm.AddState(b.name);
            st.motion = Clip(clipMap, b.clip);
            baseStates[b.id] = st;
        }
        sm.defaultState = baseStates[0];

        var walk = sm.AddState("Walk");
        walk.motion = Clip(clipMap, "Walk_Loop");

        // Base <-> base (StateId-driven, only while NOT moving) and base -> walk (while moving).
        foreach (var bi in Bases)
        {
            var from = baseStates[bi.id];
            foreach (var bj in Bases)
            {
                if (bj.id == bi.id) continue;
                var tr = from.AddTransition(baseStates[bj.id]);
                Setup(tr, 0.12f);
                tr.AddCondition(AnimatorConditionMode.Equals, bj.id, "StateId");
                tr.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");
            }
            var tw = from.AddTransition(walk);
            Setup(tw, 0.10f);
            tw.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");
        }
        foreach (var bj in Bases)
        {
            var tr = walk.AddTransition(baseStates[bj.id]);
            Setup(tr, 0.12f);
            tr.AddCondition(AnimatorConditionMode.Equals, bj.id, "StateId");
            tr.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");
        }

        // One-shot reaction states: entered from AnyState on a trigger, return to StateId after exit.
        foreach (var os in OneShots)
        {
            var st = sm.AddState("OS_" + os.trig.Substring(2));
            st.motion = Clip(clipMap, os.clip);

            var at = sm.AddAnyStateTransition(st);
            Setup(at, 0.08f);
            at.canTransitionToSelf = false;
            at.AddCondition(AnimatorConditionMode.If, 0f, os.trig);

            foreach (var bj in Bases)
            {
                var r = st.AddTransition(baseStates[bj.id]);
                Setup(r, 0.12f, exitTime: 0.85f);
                r.AddCondition(AnimatorConditionMode.Equals, bj.id, "StateId");
                r.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");
            }
            var rw = st.AddTransition(walk);
            Setup(rw, 0.10f, exitTime: 0.85f);
            rw.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");
        }

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        int tr2 = baseStates.Values.Sum(s => s.transitions.Length) + walk.transitions.Length
                  + sm.anyStateTransitions.Length;
        return "Controller built: " + Bases.Length + " base + Walk + " + OneShots.Length
               + " one-shots; transitions=" + tr2;
    }

    public static string BuildPrefab(string fbxFile, string agentId, string displayName, Color accent)
    {
        string fbxPath = CharDir + fbxFile;
        var fbx = AssetDatabase.LoadMainAssetAtPath(fbxPath) as GameObject;
        if (fbx == null) return "NO FBX: " + fbxPath;

        var root = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        root.name = "Agent_" + displayName;

        var anim = root.GetComponent<Animator>();
        if (anim == null) anim = root.AddComponent<Animator>();
        anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CtrlPath);
        anim.avatar = LoadAvatar(fbxPath);
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var view = root.AddComponent<AgentView>();
        view.agentId = agentId;
        view.displayName = displayName;
        view.bob = null; view.bodyRenderer = null; view.armL = null; view.armR = null; // Animator drives visuals

        // Name tag (TMP world text; forward=false because TMP's readable face is -Z)
        var tag = MakeLabel("NameTag", root.transform, new Vector3(0f, 0.30f, 0f), displayName, 6f, Color.white);
        view.label = tag.GetComponent<TMP_Text>();

        // Alert exclamation (hidden until a decision is pending)
        var alert = MakeLabel("Alert", root.transform, new Vector3(0f, 0.40f, 0f), "!", 14f, new Color(1f, 0.75f, 0.2f));
        alert.SetActive(false);
        view.alert = alert;

        var mv = root.AddComponent<AgentMovement>();

        var bridge = root.AddComponent<AgentAnimatorBridge>();
        bridge.animator = anim; bridge.view = view; bridge.movement = mv;

        var cap = root.AddComponent<CapsuleCollider>();
        cap.center = new Vector3(0f, 0.105f, 0f);
        cap.height = 0.21f;
        cap.radius = 0.05f;

        Directory.CreateDirectory(PrefabDir);
        string prefabPath = PrefabDir + "Agent_" + displayName + ".prefab";
        var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool ok);
        Object.DestroyImmediate(root);
        return (ok ? "OK " : "FAIL ") + prefabPath + " (saved=" + (saved != null) + ")";
    }

    /// <summary>Isolated validation scene: plain camera + light + researcher prefab (no XR rig).</summary>
    public static string BuildIntegrationScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
        camGo.transform.position = new Vector3(0f, 0.12f, 0.5f);
        camGo.transform.LookAt(new Vector3(0f, 0.11f, 0f));

        var names = new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" };
        float[] xs = { -0.3f, 0f, 0.3f };
        int placed = 0;
        for (int i = 0; i < names.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + names[i] + ".prefab");
            if (prefab == null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.position = new Vector3(xs[i], 0f, 0f);
            placed++;
        }
        if (placed == 0) return "NO PREFABS — run BuildResearcher first.";
        // Frame all placed characters
        camGo.transform.position = new Vector3(0f, 0.14f, 0.62f);
        camGo.transform.LookAt(new Vector3(0f, 0.11f, 0f));

        Directory.CreateDirectory(SceneDir);
        string scenePath = SceneDir + "CharacterIntegrationTest.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        return "Integration scene: " + scenePath;
    }

    // ---- helpers ----

    static void Setup(AnimatorStateTransition t, float dur, float exitTime = -1f)
    {
        t.hasFixedDuration = true;
        t.duration = dur;
        if (exitTime >= 0f) { t.hasExitTime = true; t.exitTime = exitTime; }
        else { t.hasExitTime = false; }
    }

    static Dictionary<string, AnimationClip> LoadClips(string fbxPath)
    {
        var d = new Dictionary<string, AnimationClip>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__"))
                d[c.name] = c;
        return d;
    }

    static Motion Clip(Dictionary<string, AnimationClip> map, string name)
        => map.TryGetValue(name, out var c) ? c : null;

    static Avatar LoadAvatar(string fbxPath)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (o is Avatar a) return a;
        return null;
    }

    static GameObject MakeLabel(string name, Transform parent, Vector3 localPos, string text, float fontSize, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        var rt = tmp.rectTransform;
        rt.sizeDelta = new Vector2(2f, 0.6f);
        go.transform.localScale = Vector3.one * 0.03f;
        var face = go.AddComponent<MuseDesk.Workspace.FaceUser>();
        face.forward = false; // TMP readable face is -Z
        return go;
    }
}
