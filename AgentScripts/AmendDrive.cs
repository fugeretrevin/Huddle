using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Wall;
using MuseDesk.Spatial.Wall;

/// <summary>
/// rc17 "Apply change": with a committed plan (after LivingOfficeDrive.RunOffline / RunLive in the same Play session) and the
/// Developer perched (PortalFxDrive.Perch arm/pinch/carry/drop), ask for a change, press Apply change, and follow the new step:
///   AmendDrive.AskChange -> AmendDrive.State (Apply change key shown) -> PortalFxDrive.Key visit:Apply_change -> AmendDrive.State
/// </summary>
public static class AmendDrive
{
    static WallHabitatController C => Object.FindFirstObjectByType<WallHabitatController>();

    public static string AskChange()
    {
        var c = C;
        if (c == null) return "no habitat";
        bool asked = c.AskText("Actually, change the password rule to 12 characters.");
        return "asked=" + asked + " | " + State();
    }

    public static string State()
    {
        var c = C;
        if (c == null) return "no habitat";
        var sb = new StringBuilder();
        sb.Append("manager=").Append(c.Manager.State).Append(" plan=").Append(c.Manager.CommittedPlanId).Append(" rev=").Append(c.Manager.Revision)
          .Append(" committed=").Append(c.Manager.CommittedCount).Append(" printed=").Append(c.Manager.PrintedCount)
          .Append(" applied=").Append(c.AmendmentsApplied).Append(" | visit=").Append(c.Visit.State);
        var card = c.VisitCard;
        if (card != null && card.Visible)
        {
            string body = card.Body != null ? card.Body.text.Replace("\n", " / ") : "";
            sb.Append(" | card='").Append(body.Length > 260 ? body.Substring(body.Length - 260) : body).Append("'");
            sb.Append(" keys=[").Append(string.Join(",", Enumerable.Range(0, card.KeyCount).Select(i => card.KeyLabel(i)))).Append("]");
        }
        var change = Enumerable.Range(0, c.Board.Count).Select(i => c.Board.Cards[i]).FirstOrDefault(x => (x.title ?? "").StartsWith("Change:"));
        sb.Append(change != null ? " | change card " + change.taskId + " " + change.agentId + "=" + change.state + "/" + change.delivery + " '" + change.title + "'"
                                 : " | no change card");
        return sb.ToString();
    }
}
