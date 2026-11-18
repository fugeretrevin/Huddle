using System.Text;
using UnityEngine;
using MuseDesk.Phase2;
using MuseDesk.Phase2.Board;

/// <summary>
/// rc22 Play Mode probes for the task board: counter badge, column paging and the arrow keys.
/// scripts\drv.ps1 Rc22BoardProbe AddMany 9   (then wait ~2 s, the demo gateway answers asynchronously)
/// scripts\drv.ps1 Rc22BoardProbe Report
/// scripts\drv.ps1 Rc22BoardProbe ScrollDown 0   (lane 0 Backlog, 1 Working, 2 Needs me, 3 Completed; or a name)
/// scripts\drv.ps1 Rc22BoardProbe SetRows 2      (0 = automatic)
/// Nothing here changes the scene; AddMany creates real demo-gateway tasks (offline: SCRIPTED DEMO cards).
/// </summary>
public static class Rc22BoardProbe
{
    static BoardPresenter P => Object.FindAnyObjectByType<BoardPresenter>();

    static int Lane(string s)
    {
        if (int.TryParse(s, out int n)) return Mathf.Clamp(n, 0, 3);
        string k = (s ?? "").ToLowerInvariant();
        if (k.StartsWith("work")) return 1;
        if (k.StartsWith("need")) return 2;
        if (k.StartsWith("comp")) return 3;
        return 0;
    }

    /// <summary>Create N backlog tasks through the board's own create path (the + New task route).</summary>
    public static string AddMany(string count)
    {
        var boot = Object.FindAnyObjectByType<Phase2Bootstrap>();
        if (boot == null) return "no Phase2Bootstrap (is Play Mode running?)";
        int n = int.TryParse(count, out int v) ? Mathf.Clamp(v, 1, 30) : 9;
        int stamp = Time.frameCount;
        for (int i = 0; i < n; i++) boot.CreateTask("Probe task " + stamp + "-" + (i + 1));
        return "asked for " + n + " tasks; call Report in a couple of seconds";
    }

    public static string ScrollDown(string lane) => Scroll(lane, +1);
    public static string ScrollUp(string lane) => Scroll(lane, -1);

    static string Scroll(string lane, int dir)
    {
        var p = P; if (p == null) return "no BoardPresenter";
        int l = Lane(lane);
        p.ScrollLane(l, dir);
        return LaneLine(p, l);
    }

    public static string SetRows(string rows)
    {
        var p = P; if (p == null) return "no BoardPresenter";
        p.maxRowsOverride = int.TryParse(rows, out int r) ? Mathf.Clamp(r, 0, 8) : 0;
        p.ScrollLane(0, 0);
        return "maxRowsOverride=" + p.maxRowsOverride + "\n" + Report();
    }

    /// <summary>Select the first visible card and press the Remove key twice (arm, then confirm), as a hand would.
    /// The removal is asynchronous; call Report a moment later.</summary>
    public static string RemoveFirst()
    {
        var p = P; if (p == null) return "no BoardPresenter";
        var console = Object.FindAnyObjectByType<CardActionConsole>();
        if (console == null) return "no CardActionConsole";
        TaskCardView first = null;
        foreach (var v in p.ActiveViews)
        {
            if (v == null || v.Data == null || v.ScrolledOut || v.IsLeaving) continue;
            if (first == null || string.CompareOrdinal(v.Data.TaskId, first.Data.TaskId) < 0) first = v;
        }
        if (first == null) return "no visible card to remove";
        string id = first.Data.TaskId, title = first.Data.Title;
        if (!console.SelectCard(id)) return "could not select " + id;
        var key = console.EnsureRemoveKey();
        if (key == null) return "Remove key not built (toolbar keys not wired?)";
        string before = key.Label;
        bool armed = !console.PressRemoveKey() && key.Armed;
        string armedLabel = key.Label;
        bool started = console.PressRemoveKey();
        return "card " + id + " \"" + title + "\": key label " + before + " -> " + armedLabel + " (armed=" + armed + ") -> second press started=" + started
             + " (label now " + key.Label + "). Call Report in a second.";
    }

    /// <summary>Press the board's Clear key twice (arm, then confirm).</summary>
    public static string Clear()
    {
        var p = P; if (p == null) return "no BoardPresenter";
        var key = p.EnsureClearKey();
        if (key == null) return "Clear key not built (no NewTaskButton under the board?)";
        if (!key.Visible) return "Clear key is hidden (the board has no cards)";
        string before = key.Label;
        bool armed = !p.PressClearKey() && key.Armed;
        string armedLabel = key.Label;
        bool started = p.PressClearKey();
        return "Clear key: " + before + " -> " + armedLabel + " (armed=" + armed + ") -> second press started=" + started
             + " (label now " + key.Label + "). Call Report in a second.";
    }

    public static string Report()
    {
        var p = P; if (p == null) return "no BoardPresenter";
        var sb = new StringBuilder();
        sb.Append("pager built=").Append(p.Pager != null && p.Pager.Built).Append(" columns=").Append(p.columns)
          .Append(" maxRowsOverride=").Append(p.maxRowsOverride).Append('\n');
        for (int l = 0; l < 4; l++) sb.Append(LaneLine(p, l)).Append('\n');
        var ck = p.ClearKey;
        sb.Append("clear key: ").Append(ck == null ? "(not built)" : "label=\"" + ck.Label + "\" visible=" + ck.Visible + " armed=" + ck.Armed + " busy=" + ck.Busy).Append('\n');
        var console = Object.FindAnyObjectByType<CardActionConsole>();
        var rk = console != null ? console.RemoveKey : null;
        sb.Append("remove key: ").Append(rk == null ? "(not built)" : "label=\"" + rk.Label + "\" visible=" + rk.Visible + " armed=" + rk.Armed + " busy=" + rk.Busy)
          .Append(" selected=").Append(console != null && console.Selected != null ? console.Selected.TaskId : "none").Append('\n');
        return sb.ToString();
    }

    static string LaneLine(BoardPresenter p, int l)
    {
        string heading = p.laneHeadings != null && l < p.laneHeadings.Length && p.laneHeadings[l] != null ? p.laneHeadings[l].text : "?";
        var pg = p.Pager;
        var sb = new StringBuilder();
        sb.Append(BoardPresenter.LaneName(l)).Append(": heading=\"").Append(heading).Append("\" counter=\"")
          .Append(pg != null ? pg.BadgeText(l) : "(no pager)").Append("\" total=").Append(p.LaneTotal(l))
          .Append(" capacity=").Append(p.LaneCapacity(l)).Append(" offset=").Append(p.LaneOffset(l))
          .Append(" up=").Append(pg != null && pg.UpShown(l)).Append(" down=").Append(pg != null && pg.DownShown(l)).Append(" visible=[");
        int hidden = 0; string mode = "";
        foreach (var v in p.ActiveViews)
        {
            if (v == null || v.Data == null || BoardPresenter.RegionFor(v.Data.TaskState) != (BoardPresenter.BoardRegion)l) continue;
            if (v.ScrolledOut) { hidden++; continue; }
            sb.Append(v.TaskId).Append(' ');
            if (v.sourceLabel != null) mode = v.sourceLabel.text;
        }
        sb.Append("] scrolledOut=").Append(hidden).Append(" cardModeLine=\"").Append(mode).Append('"');
        return sb.ToString();
    }
}
