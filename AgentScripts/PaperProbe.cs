using UnityEngine;
using MuseDesk.Wall;

/// <summary>Read-only: the Manager plan paper's live state (why rows / stamp may be empty).</summary>
public static class PaperProbe
{
    public static string State()
    {
        var c = Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);
        if (c == null) return "no controller";
        var p = c.Paper;
        string s = "manager=" + c.Manager.State + " planOpen=" + (c.PlanCard != null && c.PlanCard.Visible);
        if (p == null) return s + " paper=null";
        s += " paperVisible=" + p.Visible + " rows=" + p.RowCount + " stamp='" + p.StampWord + "' zone=" + p.ZoneMode
           + " cardRootActive=" + (p.card.root != null && p.card.root.gameObject.activeInHierarchy) + " sameCard=" + ReferenceEquals(p.card, c.PlanCard);
        return s;
    }
}
