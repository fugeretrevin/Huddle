using System.Text;
using UnityEngine;

/// <summary>
/// Play Mode driver for the text-fit pass. Report() measures the LIVE text (whatever the cards,
/// nameplates, gaze chips and dock currently say, not samples) against the rects and the faces they
/// sit on. ReportWorstCase() temporarily writes the longest realistic strings and restores the text.
/// Both use BuildTextFitV1's measurement, so the numbers match the builder's report.
/// </summary>
public static class TextFitDrive
{
    /// <summary>Live text: label | text | rect | text at min size | font mm | rendered | margin to face | verdict.</summary>
    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var targets = BuildTextFitV1.CollectTargets();
        var sb = new StringBuilder("=== TextFitDrive.Report (live text) ===\n");
        sb.AppendLine(targets.Count + " text owners. Pooled cards appear here once the board has them; run again after tasks exist.");
        sb.Append(BuildTextFitV1.MeasureTargets(targets, false));
        return sb.ToString();
    }

    /// <summary>The same table with the longest realistic strings written in (restored afterwards).</summary>
    public static string ReportWorstCase()
    {
        if (!Application.isPlaying) return "not playing";
        var targets = BuildTextFitV1.CollectTargets();
        var sb = new StringBuilder("=== TextFitDrive.ReportWorstCase (longest realistic strings) ===\n");
        sb.Append(BuildTextFitV1.MeasureTargets(targets, true));
        return sb.ToString();
    }
}
