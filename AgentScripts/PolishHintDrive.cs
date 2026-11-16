using System.Text;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Onboarding;
using MuseDesk.Workspace;

/// <summary>Play Mode checks for the polish pass (DockStatusCopyV1 + the Reset layout hint).
///   scripts\drv.ps1 PolishHintDrive Status     the dock's status line at the desk
///   scripts\drv.ps1 PolishHintDrive Move       carry the first movable panel 8 cm (as a pinch-drag would), then report
///   scripts\drv.ps1 PolishHintDrive Hint       which hint the coach shows now (call ~3 s after Move)
///   scripts\drv.ps1 PolishHintDrive Reset      press Reset layout (MovablePanel.ResetAll) and report the hint again</summary>
public static class PolishHintDrive
{
    static OnboardingHintPresenter P => Object.FindAnyObjectByType<OnboardingHintPresenter>(FindObjectsInactive.Include);

    public static string Status()
    {
        var c = Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);
        return c == null ? "no ModeConsole" : "mode=" + SpatialWorkspaceState.Mode + " status='" + c.StatusShown + "'";
    }

    public static string Move()
    {
        if (!Application.isPlaying) return "enter Play Mode first";
        MovablePanel panel = null;
        for (int i = 0; i < MovablePanel.All.Count; i++) if (MovablePanel.All[i] != null) { panel = MovablePanel.All[i]; break; }
        if (panel == null) return "no MovablePanel";
        Vector3 before = panel.transform.position;
        panel.SimulateDrag(new Vector3(0.08f, 0f, 0f));
        return "moved " + panel.panelId + " by " + (panel.transform.position - before).magnitude.ToString("F3") + " m | " + Hint();
    }

    public static string Hint()
    {
        var p = P;
        if (p == null) return "no presenter";
        var sb = new StringBuilder("presenter active=" + p.Active);
        var coach = p.Coach;
        if (coach == null) return sb.Append(" (no coach: onboardingHints flag off?)").ToString();
        sb.Append(" current=").Append(coach.Current == null ? "none" : coach.Current.Id + " '" + coach.Current.Text + "'");
        sb.Append(" resetPending=").Append(coach.IsPending(HintId.ResetLayout));
        return sb.ToString();
    }

    public static string Reset()
    {
        MovablePanel.ResetAll();
        return "reset | " + Hint();
    }
}
