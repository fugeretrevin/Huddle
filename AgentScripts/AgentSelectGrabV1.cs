using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oculus.Interaction;
using MuseDesk.Spatial.Wall;
using MuseDesk.Wall;

/// <summary>
/// Select Agents "force grab", scene part (AgentSelectGrabV1). Changes ONE component's settings and adds ONE component;
/// it creates no GameObject, no collider, no interactable, and touches no rig object:
///   - WallHabitatController: useSelectGrabV2 = true, the grab/drop tuning, muteHiddenOfficeInteractables = true,
///     muteDistanceGrabWhileSelecting = true, and leftHandVisual / rightHandVisual wired to the rig's hand-tracking
///     visuals by name (OVRHandVisualLeft / OVRHandVisualRight). The rig has four HandVisuals; the old code latched
///     whichever FindObjectsByType returned first.
///   - AgentSelectInteractableMute on the controller's GameObject (it switches the hidden desk office's interactables
///     off while Wall is active, and distance grabs while Select Agents is armed; it gives back exactly what it took).
/// Idempotent; saves the scene. Run through the guard so it lands in MainMRScene:
///   scripts/run_guarded.ps1 -File AgentScripts\AgentSelectGrabV1.cs -Entry AgentSelectGrabV1.Apply
/// Revert(): useSelectGrabV2 = false (the previous Select Agents path exactly), the mute component removed, the hand
/// wiring cleared. The runtime's hand reader still tries every tracked HandVisual (a bug fix, not a feature).
/// Do not re-run older builders for this.
/// </summary>
public static class AgentSelectGrabV1
{
    static WallHabitatController Find()
    {
        var scene = EditorSceneManager.GetActiveScene();
        return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WallHabitatController>(true)).FirstOrDefault();
    }

    static HandVisual HandByName(string name)
    {
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == name)
                {
                    var v = t.GetComponent<HandVisual>();
                    if (v != null) return v;
                }
        return null;
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "AgentSelectGrabV1: stop Play Mode first.";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "AgentSelectGrabV1: no open scene.";
        if (scene.name != "MainMRScene") return "AgentSelectGrabV1: active scene is '" + scene.name + "', expected MainMRScene; nothing changed (use scripts/run_guarded.ps1).";
        var c = Find();
        if (c == null) return "AgentSelectGrabV1: no WallHabitatController in the scene (run BuildWallHabitatV1 first); nothing changed.";

        Undo.RecordObject(c, "AgentSelectGrabV1");
        c.useSelectGrabV2 = true;
        c.selectGrab = new AgentSelectGrabSettings { minTargetDegrees = 4.5f, lockGraceSeconds = 0.35f, minRayDistance = 0.2f };
        c.visitDrop = new VisitDropSettings { perchSnapRadius = 0.30f, nearUserRadius = 0.55f };
        c.muteHiddenOfficeInteractables = true;
        c.muteDistanceGrabWhileSelecting = true;
        c.regrabReach = 0.16f;
        HandVisual left = HandByName("OVRHandVisualLeft"), right = HandByName("OVRHandVisualRight");
        c.leftHandVisual = left;
        c.rightHandVisual = right;
        EditorUtility.SetDirty(c);

        var mute = c.GetComponent<AgentSelectInteractableMute>();
        bool added = false;
        if (mute == null) { mute = Undo.AddComponent<AgentSelectInteractableMute>(c.gameObject); added = true; }
        Undo.RecordObject(mute, "AgentSelectGrabV1");
        mute.owner = c;
        mute.rescanSeconds = 0.5f;
        EditorUtility.SetDirty(mute);

        int officeInteractables = 0;
        Transform office = c.officeRoot;
        if (office != null)
            foreach (var mb in office.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb is IInteractable && !Excluded(mb.transform, c.neverHide)) officeInteractables++;

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        return "AgentSelectGrabV1: " + c.gameObject.name + " useSelectGrabV2=true; mute component " + (added ? "added" : "updated")
             + "; hands wired left=" + (left != null ? left.name : "NOT FOUND") + " right=" + (right != null ? right.name : "NOT FOUND") + " (else every tracked HandVisual is tried at run time)"
             + "; office root=" + (office != null ? office.name : "none (WorkspaceRoot at run time)") + " with " + officeInteractables
             + " ISDK interactables that are hidden-but-live in Wall mode (switched off while Wall is active)"
             + "; scene saved=" + saved;
    }

    static bool Excluded(Transform t, Transform[] exclude)
    {
        if (exclude == null) return false;
        foreach (var e in exclude) if (e != null && t.IsChildOf(e)) return true;
        return false;
    }

    public static string Revert()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "AgentSelectGrabV1.Revert: stop Play Mode first.";
        var scene = EditorSceneManager.GetActiveScene();
        var c = Find();
        if (c == null) return "AgentSelectGrabV1.Revert: no WallHabitatController; nothing to revert.";
        Undo.RecordObject(c, "AgentSelectGrabV1.Revert");
        c.useSelectGrabV2 = false;
        c.leftHandVisual = null;
        c.rightHandVisual = null;
        EditorUtility.SetDirty(c);
        var mute = c.GetComponent<AgentSelectInteractableMute>();
        bool removed = mute != null;
        if (mute != null) Undo.DestroyObjectImmediate(mute);
        EditorSceneManager.MarkSceneDirty(scene);
        return "AgentSelectGrabV1.Revert: useSelectGrabV2=false, hand wiring cleared, mute component " + (removed ? "removed" : "absent") + "; scene saved=" + EditorSceneManager.SaveScene(scene);
    }
}
