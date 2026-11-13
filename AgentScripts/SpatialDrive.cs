using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using MuseDesk.Spatial;

/// <summary>Play Mode driver for the room-aware workspace modes, using a synthetic room.</summary>
public static class SpatialDrive
{
    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static Transform Root() => Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
        .SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "WorkspaceRoot");

    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var d = One<SpatialWorkspaceDirector>(); var ws = Root();
        var cam = Camera.main;
        var sb = new StringBuilder();
        sb.Append($"mode={d.Mode} previewing={d.IsPreviewing} status='{d.StatusText}' phase={d.Core.Phase} candidates={d.Core.CandidateCount} mover={d.Core.Mover.Phase} pod={d.Core.Pod.State}");
        sb.Append($" | state: mode={SpatialWorkspaceState.Mode} remote={SpatialWorkspaceState.IsRemote} transitioning={SpatialWorkspaceState.IsTransitioning} held={SpatialWorkspaceState.TransitionHeld} nearUser={SpatialWorkspaceState.HasNearUserPose}");
        sb.Append($" | root pos={ws.position:F2} yaw={ws.eulerAngles.y:F0} scale={ws.localScale.x:F2} head={(cam != null ? cam.transform.position.ToString("F2") : "none")} distHeadToRoot={(cam != null ? Vector3.Distance(cam.transform.position, ws.position).ToString("F2") : "?")}");
        var del = One<RemoteDocumentDeliveryController>();
        if (del != null) sb.Append(" | delivery: " + del.DriverReport());
        var beacon = One<AttentionBeacon>();
        if (beacon != null) sb.Append($" | beacon office={beacon.OfficeCueShown} mirror={beacon.MirrorCueShown} level={beacon.ShownLevel}");
        var kit = One<NearUserKit>();
        if (kit != null && cam != null)
        {
            sb.Append($" | kit detached={kit.Detached} stampsShown={kit.DecisionItemsShown}");
            foreach (var t in kit.items) if (t != null && (t.name == "TaskDock" || t.name == "StampApprove")) sb.Append($" {t.name}@{Vector3.Distance(cam.transform.position, t.position):F2}m");
        }
        var sc = One<SummonController>(); if (sc != null) sb.Append($" keysShown={sc.KeysShown}");
        return sb.ToString();
    }

    public static string Enable() { var d = One<SpatialWorkspaceDirector>(); d.SetFlags(true, true); d.UseSyntheticTestRoom(); return "flags on, synthetic room"; }
    public static string Floor() { One<SpatialWorkspaceDirector>().RequestFloor(); return Report(); }
    public static string Ceiling() { One<SpatialWorkspaceDirector>().RequestCeiling(); return Report(); }
    public static string Desk() { One<SpatialWorkspaceDirector>().RequestDesk(); return Report(); }
    public static string Next() { One<SpatialWorkspaceDirector>().NextCandidate(); return Report(); }
    public static string Confirm() { One<SpatialWorkspaceDirector>().ConfirmPlacement(); return Report(); }
    public static string PodDown() { One<SpatialWorkspaceDirector>().BringPodDown(); return Report(); }
    public static string PodUp() { One<SpatialWorkspaceDirector>().SendPodUp(); return Report(); }
    public static string Hold() { SpatialWorkspaceState.AcquireTransitionHold(); return "hold acquired"; }
    public static string Unhold() { SpatialWorkspaceState.ReleaseTransitionHold(); return "hold released"; }
    public static string Bring() { One<SummonController>().PressBring(); return Report(); }
    public static string Status() { One<SummonController>().PressStatus(); return Report(); }
    public static string Stop() { One<SpatialWorkspaceDirector>().StopMove(); return Report(); }
    public static string Off() { One<SpatialWorkspaceDirector>().SetFlags(false, false); return Report(); }
}

