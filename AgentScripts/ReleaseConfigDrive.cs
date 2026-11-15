using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Config;
using MuseDesk.Spatial;
using MuseDesk.Voice;

/// <summary>Play Mode driver for the release config. Applies a runtime copy of the config (the asset is
/// never touched) and reports what is on. Call Report() first, then Set("floor=0 voice=0"), then Reset().</summary>
public static class ReleaseConfigDrive
{
    static MuseDeskReleaseConfig _copy;
    static T[] All<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var sb = new StringBuilder();
        var a = ReleaseConfigApplier.Active;
        sb.Append("active=" + (a != null ? a.Summary() : "none"));
        var d = Object.FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
        if (d != null) sb.Append($" | director floor={d.flags.floorModeEnabled} ceiling={d.flags.ceilingModeEnabled} mode={d.Mode}");
        sb.Append(" | pull enabled=" + string.Join(",", All<CeilingPullGesture>().Select(x => x.enabled)));
        sb.Append(" | vfx particles=" + string.Join(",", All<CeilingDeploymentVFXController>().Select(x => x.particlesEnabled
            + "/playing=" + x.GetComponentsInChildren<ParticleSystem>(true).Count(p => p.isPlaying))));
        sb.Append(" cone=" + string.Join(",", All<CeilingDeploymentVFXController>().Select(x => x.portalConeEnabled)));
        sb.Append(" | ceilingGrid enabled=" + string.Join(",", All<CeilingScanReveal>().Select(x => x.enabled + "/shown=" + x.Shown)));
        sb.Append(" floorGrid enabled=" + string.Join(",", All<FloorScanReveal>().Select(x => x.enabled + "/shown=" + x.Shown)));
        sb.Append(" | voice center=" + string.Join(",", All<VoiceCommandCenter>().Select(x => x.enabled))
            + " wit=" + string.Join(",", All<WitVoiceSource>().Select(x => x.enabled))
            + " gesture=" + string.Join(",", All<VoiceGestureActivator>().Select(x => x.enabled + "/flag=" + x.gestureEnabled))
            + " policy=" + string.Join(",", All<VoiceDecisionController>().Select(x => x.voiceEnabled))
            + " mic=" + string.Join(",", All<VoiceHud>().Select(x => x.Mic)));
        sb.Append(" | hints " + string.Join(",", All<MuseDesk.Workspace.OnboardingHintPresenter>().Select(x => x.Describe())));
        return sb.ToString();
    }

    /// <summary>Spec as BuildReleaseConfigV1.SetFlags (floor ceiling pull particles cone grids voice), applied at once.</summary>
    public static string Set(string spec)
    {
        if (!Application.isPlaying) return "not playing";
        if (_copy == null)
        {
            var src = ReleaseConfigApplier.Active != null ? ReleaseConfigApplier.Active : ScriptableObject.CreateInstance<MuseDeskReleaseConfig>();
            _copy = Object.Instantiate(src);
        }
        foreach (var tok in (spec ?? "").Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = tok.IndexOf('='); if (eq <= 0) return "bad token " + tok;
            string k = tok.Substring(0, eq); bool b = tok.Substring(eq + 1) == "1";
            switch (k)
            {
                case "floor": _copy.floorMode = b; break;
                case "ceiling": _copy.ceilingMode = b; break;
                case "pull": _copy.ceilingPullGesture = b; break;
                case "particles": _copy.ceilingParticles = b; break;
                case "cone": _copy.ceilingPortalCone = b; break;
                case "grids": _copy.placementScanGrids = b; break;
                case "voice": _copy.voice = b; break;
                case "motiontoggle": _copy.reducedMotionToggle = b; break;
                case "hints": _copy.onboardingHints = b; break;
                default: return "unknown key " + k;
            }
        }
        ReleaseConfigApplier.Apply(_copy);
        return Report();
    }

    /// <summary>Pushes the pod through Descending in the VFX controller and reports whether the cone object and any particle system came on.</summary>
    public static string ProbeVfx()
    {
        if (!Application.isPlaying) return "not playing";
        var sb = new StringBuilder();
        foreach (var v in All<CeilingDeploymentVFXController>())
        {
            v.OnPodState(CeilingWorkspacePodController.PodState.Descending);
            v.OnPodState(CeilingWorkspacePodController.PodState.Stabilizing);
            v.OnPodState(CeilingWorkspacePodController.PodState.Descending);
            var cone = v.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "VFX_CeilingPortalCone");
            sb.Append($"vfx particles={v.particlesEnabled} cone={v.portalConeEnabled} playingNow={v.GetComponentsInChildren<ParticleSystem>(true).Count(p => p.isPlaying)} coneActive(before frame)={(cone != null && cone.gameObject.activeSelf)}; ");
        }
        return sb.ToString();
    }

    public static string Reset() { return Set("floor=1 ceiling=1 pull=1 particles=1 cone=1 grids=1 voice=1"); }
}
