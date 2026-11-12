using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Networking;

/// <summary>
/// Editor builder for the miniature Software Factory (P5). Imports the factory FBX (Generic + Avatar,
/// clip-split), builds a by-name Animator Controller the SoftwareFactoryVisualController crossfades
/// into, and places it in MainMRScene wired to real backend events via FactoryEventController.
/// Run entries via: unity command script run --file AgentScripts/BuildFactory.cs --entry BuildFactory.X
/// </summary>
public static class BuildFactory
{
    const string FactoryFbx = "Assets/MuseDesk/Art/Models/Factory/SoftwareFactory_Complete.fbx";
    const string CtrlPath = "Assets/MuseDesk/Art/Controllers/MuseDesk_Factory.controller";

    /// <summary>Import + controller only (NOT scene) so take names can be confirmed first.</summary>
    public static string Build()
    {
        string a = ConfigureImport();
        string b = BuildController();
        AssetDatabase.SaveAssets();
        return a + "\n" + b;
    }

    public static string ConfigureImport()
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(FactoryFbx);
        if (imp == null) return "NO IMPORTER: " + FactoryFbx;
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
                loopTime = false, // factory stages are discrete, event-triggered one-shots (not looping)
                wrapMode = WrapMode.Default,
            };
        }
        imp.clipAnimations = clips;
        EditorUtility.SetDirty(imp);
        imp.SaveAndReimport();
        // The returned list lets the parent confirm these match SoftwareFactoryVisualController's
        // Clip* constants ("1_Blueprint_Print".."10_Completed_Delivery"); if they differ, the
        // crossfade HasState() check fails silently and the controller states must be renamed.
        return "Factory takes (" + takes.Length + "): " + string.Join(", ", takes.Select(t => t.name));
    }

    public static string BuildController()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CtrlPath));
        AssetDatabase.DeleteAsset(CtrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
        var sm = ctrl.layers[0].stateMachine;

        var clips = LoadClips(FactoryFbx);
        bool first = true;
        foreach (var kv in clips)
        {
            var st = sm.AddState(kv.Key);   // state name == clip name (CrossFadeInFixedTime matches by hash)
            st.motion = kv.Value;
            if (first) { sm.defaultState = st; first = false; }
        }
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return "Factory controller: states=[" + string.Join(", ", clips.Keys) + "]";
    }

    public static string BuildIntoScene()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var fbx = AssetDatabase.LoadMainAssetAtPath(FactoryFbx) as GameObject;
        if (fbx == null) return "NO FBX";

        // Idempotent: remove any previous factory instance.
        var existing = Object.FindObjectsByType<SoftwareFactoryVisualController>(FindObjectsInactive.Include).FirstOrDefault();
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        var wsRoot = GameObject.Find("WorkspaceRoot");
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        inst.name = "SoftwareFactory";
        if (wsRoot != null) inst.transform.SetParent(wsRoot.transform, false);
        inst.transform.localPosition = new Vector3(0.5f, 0f, 0.6f); //TUNE placement on the desk
        inst.transform.localScale = Vector3.one;                    //TUNE scale (factory FBX size unknown)

        var anim = inst.GetComponent<Animator>();
        if (anim == null) anim = inst.AddComponent<Animator>();
        anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CtrlPath);
        anim.avatar = LoadAvatar(FactoryFbx);
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var fvc = inst.AddComponent<SoftwareFactoryVisualController>();  // Awake() calls ResolveReferences()
        var fec = inst.AddComponent<FactoryEventController>();
        fec.factory = fvc;
        fec.controller = Object.FindObjectsByType<SessionController>(FindObjectsInactive.Include).FirstOrDefault();

        EditorUtility.SetDirty(fec);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Factory placed under " + (wsRoot != null ? "WorkspaceRoot" : "<root>") +
               "; controllerWired=" + (fec.controller != null) + " pos=" + inst.transform.localPosition;
    }

    static Dictionary<string, AnimationClip> LoadClips(string fbxPath)
    {
        var d = new Dictionary<string, AnimationClip>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__"))
                d[c.name] = c;
        return d;
    }

    static Avatar LoadAvatar(string fbxPath)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (o is Avatar a) return a;
        return null;
    }
}
