using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial;

/// <summary>
/// Play Mode driver for the movable panels and the mode console. Every method returns a report.
/// A "drag" here calls MovablePanel.SimulateDrag: the same code path as a pinch-drag of the bar
/// (follow, turn to the head, release constraints, save), without a hand and without easing.
/// Use with AgentScripts/SpatialDrive.cs (Enable, Floor, Confirm, ...) to change the office's mode.
/// </summary>
public static class PanelsDrive
{
    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static MovablePanel[] Panels() => Object.FindObjectsByType<MovablePanel>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
        .OrderBy(p => p.panelId).ToArray();
    static MovablePanel Panel(string id) => Panels().FirstOrDefault(p => p.panelId == id);

    /// <summary>Every panel with its pose, distance to the head, bar and saved state; and the rig.</summary>
    public static string List()
    {
        if (!Application.isPlaying) return "not playing";
        var sb = new StringBuilder();
        var kit = One<NearUserKit>();
        if (kit != null && kit.rig != null)
            sb.AppendLine($"rig pos={kit.rig.position:F3} yaw={kit.rig.eulerAngles.y:F0} active={kit.rig.gameObject.activeSelf} followDesk={kit.followDesk} hasDeskPose={kit.HasDeskPose} detached={kit.Detached} | mode={SpatialWorkspaceState.Mode} transitioning={SpatialWorkspaceState.IsTransitioning}");
        var panels = Panels();
        if (panels.Length == 0) sb.AppendLine("no active MovablePanel (is the rig active? has BuildMovablePanelsV1 run?)");
        foreach (var p in panels) sb.AppendLine(p.Describe());
        var chip = One<PanelResetChip>();
        if (chip != null) sb.AppendLine($"reset chip shown={chip.Shown}");
        return sb.ToString();
    }

    /// <summary>Drag one panel by a world-space delta, as a pinch-drag and release would.</summary>
    public static string Drag(string id, Vector3 worldDelta)
    {
        if (!Application.isPlaying) return "not playing";
        var p = Panel(id);
        if (p == null) return "no active panel '" + id + "'\n" + List();
        string before = p.Describe();
        p.SimulateDrag(worldDelta);
        return "before: " + before + "\nafter:  " + p.Describe();
    }

    /// <summary>Drag a panel's bar by (dx, dy, dz) metres in world axes (dy is up). Reports before and after.</summary>
    public static string DragBy(string id, float dx, float dy, float dz) => Drag(id, new Vector3(dx, dy, dz));

    public static string RaiseDock() => DragBy("dock", 0f, 0.30f, 0f);     // clamps at ~45 cm over the desk
    public static string LowerDock() => DragBy("dock", 0f, -0.10f, 0f);    // clamps at the desk plane (or 5 cm under it away from the desk)
    public static string LowerDockSmall() => DragBy("dock", 0f, -0.03f, 0f);

    /// <summary>Each panel's pivot and base height above the desk plane (mm), saved flag.</summary>
    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var sb = new StringBuilder();
        foreach (var p in Panels())
        {
            var frame = p.transform.parent;
            float deskY = frame != null ? frame.position.y : 0f;
            float pivot = p.PivotWorld.y;
            sb.AppendLine($"{p.panelId}: pivot y={pivot:F3} (+{(pivot - deskY) * 1000f:F0} mm over desk), base +{(pivot - p.baseBelowPivot - deskY) * 1000f:F0} mm, band -{p.lowestBelowDesk * 1000f:F0}..+{p.highestAboveDesk * 1000f:F0} mm, saved={(string.IsNullOrEmpty(p.SavedText()) ? "no" : "yes")}");
        }
        return sb.ToString();
    }

    static Vector3 UserLeft()
    {
        var kit = One<NearUserKit>();
        return kit != null && kit.rig != null ? kit.rig.right : Vector3.right;      // root +X is the user's left
    }

    // Parameterless entries (8 cm to the user's left and 5 cm up).
    public static string DragDock() => Drag("dock", UserLeft() * 0.08f + Vector3.up * 0.05f);
    public static string DragPlaque() => Drag("plaque", UserLeft() * 0.08f + Vector3.up * 0.05f);
    public static string DragMode() => Drag("mode", UserLeft() * 0.08f + Vector3.up * 0.05f);
    public static string DragReceipt() => Drag("receipt", UserLeft() * 0.08f + Vector3.up * 0.05f);

    /// <summary>Reach clamp: push the mode panel 1.5 m away from the head; it must come back within 0.75 m.</summary>
    public static string DragModeFar()
    {
        var cam = Camera.main; var p = Panel("mode");
        if (cam == null || p == null) return "no camera or no mode panel";
        Vector3 away = p.PivotWorld - cam.transform.position; away.y = 0f;
        return Drag("mode", away.normalized * 1.5f);
    }

    /// <summary>"Never behind": put the mode panel 0.5 m behind the head; it must end up in front.</summary>
    public static string DragModeBehind()
    {
        var cam = Camera.main; var p = Panel("mode");
        if (cam == null || p == null) return "no camera or no mode panel";
        Vector3 f = cam.transform.forward; f.y = 0f;
        Vector3 target = cam.transform.position - f.normalized * 0.5f + Vector3.down * 0.2f;
        return Drag("mode", target - p.PivotWorld);
    }

    /// <summary>Desk clamp: push the mode panel 20 cm down; its base must end on or above the desk plane.</summary>
    public static string SinkMode() => Drag("mode", Vector3.down * 0.20f);

    /// <summary>Overlap: drop the mode panel onto the dock; it must be nudged clear.</summary>
    public static string DragModeOntoDock()
    {
        MovablePanel a = Panel("mode"), b = Panel("dock");
        if (a == null || b == null) return "mode or dock panel missing";
        return Drag("mode", b.transform.TransformPoint(b.centreLocal) - a.transform.TransformPoint(a.centreLocal));
    }

    /// <summary>Put every panel back and forget the saved layout (what the Reset layout chip does).</summary>
    public static string Reset()
    {
        if (!Application.isPlaying) return "not playing";
        MovablePanel.ResetAll();
        return "ResetAll called (panels ease back over about 0.2 s; call List again)\n" + List();
    }

    /// <summary>Show the Reset layout chip above the dock's bar, as a 1.5 s still pinch would.</summary>
    public static string LongPinch()
    {
        var p = Panel("dock") ?? Panels().FirstOrDefault();
        if (p == null) return "no panel";
        p.SimulateLongPinch();
        var chip = One<PanelResetChip>();
        return chip != null ? $"chip shown={chip.Shown} at {chip.transform.position:F3}" : "no PanelResetChip in the scene";
    }

    /// <summary>Press the Reset layout chip (it must be on show: call LongPinch first).</summary>
    public static string PressResetChip()
    {
        var chip = One<PanelResetChip>();
        if (chip == null) return "no PanelResetChip in the scene";
        bool shown = chip.Shown;
        chip.Press();
        return (shown ? "pressed" : "not shown, press ignored") + "\n" + List();
    }

    /// <summary>The saved pose text of every panel (PlayerPrefs).</summary>
    public static string Saved()
    {
        var sb = new StringBuilder();
        foreach (var p in Panels()) sb.AppendLine($"{PanelLayoutMath.PrefsKey(p.panelId)} = '{p.SavedText()}'");
        return sb.ToString();
    }

    // ───────────────────────── mode console ─────────────────────────

    /// <summary>The console's keys (shown / disabled / pokeable / words) and status line.</summary>
    public static string Console()
    {
        if (!Application.isPlaying) return "not playing";
        var c = One<ModeConsole>();
        if (c == null) return "no ModeConsole in the scene";
        var strip = One<SpatialModeStrip>();
        var d = One<SpatialWorkspaceDirector>();
        return c.Describe() + $"\n  strip.console set={(strip != null && strip.console == c)} cancelKeyStops={(strip != null && strip.CancelKeyStops)} | director mode={(d != null ? d.Mode.ToString() : "none")} previewing={(d != null && d.IsPreviewing)} status='{(d != null ? d.StatusText : "")}'";
    }

    /// <summary>Press one console key by name: Desk, Floor, Ceiling, Next, Confirm, Cancel, Stop, Pod, Bring, Status.</summary>
    public static string Press(string key)
    {
        if (!Application.isPlaying) return "not playing";
        var c = One<ModeConsole>();
        if (c == null) return "no ModeConsole in the scene";
        return (c.PressByName(key) ? "pressed " + key : "unknown key " + key) + "\n" + Console();
    }

    public static string PressDesk() => Press("Desk");
    public static string PressFloor() => Press("Floor");
    public static string PressCeiling() => Press("Ceiling");
    public static string PressNext() => Press("Next");
    public static string PressConfirm() => Press("Confirm");
    public static string PressCancel() => Press("Cancel");
    public static string PressStop() => Press("Stop");
    public static string PressPod() => Press("Pod");
    public static string PressBring() => Press("Bring");
    public static string PressStatus() => Press("Status");

    /// <summary>
    /// Press every console key once, in an order that is safe in any state: the keys that do not
    /// apply flash "refused" and do nothing. It ends by pressing Cancel and Stop so nothing is left
    /// previewing because of this call. For a real walk through the modes use the single presses
    /// with waits between them (Floor, Confirm, wait, Bring, ...).
    /// </summary>
    public static string PressAllConsoleKeys()
    {
        if (!Application.isPlaying) return "not playing";
        var c = One<ModeConsole>();
        if (c == null) return "no ModeConsole in the scene";
        var sb = new StringBuilder();
        foreach (var k in new[] { "Status", "Bring", "Pod", "Next", "Floor", "Next", "Cancel", "Ceiling", "Cancel", "Desk", "Confirm", "Stop" })
        {
            c.PressByName(k);
            var d = One<SpatialWorkspaceDirector>();
            sb.AppendLine($"{k}: mode={(d != null ? d.Mode.ToString() : "?")} previewing={(d != null && d.IsPreviewing)} status='{(d != null ? d.StatusText : "")}'");
        }
        return sb + Console();
    }
}
