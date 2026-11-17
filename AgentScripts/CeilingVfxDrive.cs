using System;
using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Spatial;

/// <summary>
/// Play Mode driver for the ceiling deployment effects (CeilingVfxBridge). Uses the same director
/// calls as SpatialDrive / CeilingPolishDrive for the moves.
///
/// Typical run (call Report between steps; each Step call does ONE thing and returns the report):
///   CeilingVfxDrive.Step   Enable + synthetic room + request Ceiling
///   CeilingVfxDrive.Step   confirm the placement (office goes up to the dock)
///   (wait for Docked)      CeilingVfxDrive.Report
///   CeilingVfxDrive.Step   bring the pod down         (Unlocking, Descending, Stabilizing, DeployedIdle)
///   CeilingVfxDrive.Step   send the pod up            (ClosingShelf, ReturningToDock, DockLocking, DockedIdle)
///   CeilingVfxDrive.Desk   back to Desk               (all effects off)
/// Switches: VfxOff / VfxOn (master), ParticlesOff / ParticlesOn (particles only).
/// Force("Descending") shows one effect phase (a CeilingWorkspacePodController.PodState name) without
/// moving the office; only while the effects are shown. A driver aid, never part of the app.
/// </summary>
public static class CeilingVfxDrive
{
    static T One<T>() where T : UnityEngine.Object => UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var b = One<CeilingVfxBridge>();
        var pod = One<CeilingPodController>();
        string head = $"mode={SpatialWorkspaceState.Mode} transitioning={SpatialWorkspaceState.IsTransitioning} pod={(pod != null ? pod.State.ToString() : "none")} podVisualsShown={(pod != null && pod.VisualsShown)}";
        if (b == null) return head + " | NO CeilingVfxBridge in the scene (run BuildCeilingVfxV1.Apply)";
        return head + "\n" + b.Describe();
    }

    public static string Step()
    {
        if (!Application.isPlaying) return "not playing";
        var d = One<SpatialWorkspaceDirector>();
        var pod = One<CeilingPodController>();
        if (d == null || pod == null) return "no director or pod controller";
        string did;
        if (SpatialWorkspaceState.Mode != WorkspaceMode.Ceiling && !d.IsPreviewing)
        {
            d.SetFlags(true, true); d.UseSyntheticTestRoom(); d.RequestCeiling();
            did = "enabled flags, synthetic room, requested Ceiling";
        }
        else if (d.IsPreviewing) { d.ConfirmPlacement(); did = "confirmed the ceiling placement"; }
        else if (SpatialWorkspaceState.IsTransitioning) did = "office is moving; wait";
        else if (pod.State == CeilingPodState.Docked) { d.BringPodDown(); did = "BringPodDown"; }
        else if (pod.State == CeilingPodState.Engaged) { d.SendPodUp(); did = "SendPodUp"; }
        else if (pod.State == CeilingPodState.Docked || !pod.Active) { d.RequestDesk(); did = "RequestDesk"; }
        else did = "pod is in " + pod.State + "; wait";
        return did + " | " + Report();
    }

    public static string Desk()
    {
        var d = One<SpatialWorkspaceDirector>(); if (d == null) return "no director";
        d.RequestDesk();
        return "RequestDesk | " + Report();
    }

    public static string Force(string phase)
    {
        if (!Application.isPlaying) return "not playing";
        var b = One<CeilingVfxBridge>();
        if (b == null || b.vfx == null) return "no bridge";
        if (!b.Shown) return "effects are not shown (Ceiling mode with the pod visuals on is needed) | " + Report();
        if (!Enum.TryParse(phase, true, out CeilingWorkspacePodController.PodState s)) return "unknown phase '" + phase + "'; use a CeilingWorkspacePodController.PodState name";
        b.vfx.OnPodState(s);
        return "forced " + s + " | " + Report();
    }

    public static string VfxOff() { var b = One<CeilingVfxBridge>(); if (b == null) return "no bridge"; b.vfxEnabled = false; return "vfxEnabled=false (applies next frame)"; }
    public static string VfxOn() { var b = One<CeilingVfxBridge>(); if (b == null) return "no bridge"; b.vfxEnabled = true; return "vfxEnabled=true (applies next frame)"; }
    public static string ParticlesOff() { var b = One<CeilingVfxBridge>(); if (b == null) return "no bridge"; b.particlesEnabled = false; return "particlesEnabled=false (applies next frame)"; }
    public static string ParticlesOn() { var b = One<CeilingVfxBridge>(); if (b == null) return "no bridge"; b.particlesEnabled = true; return "particlesEnabled=true (applies next frame)"; }
}
