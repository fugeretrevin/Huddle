using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Wall;
using MuseDesk.Spatial.Wall;

/// <summary>
/// Play Mode sample: does the ART elevator car (what the user sees) stay with the LOGIC cabin (what riders are attached
/// to)? Also each walking avatar's Animator state (the walk fix). Call repeatedly during rides.
///   scripts\drv.ps1 WallArtLiftProbe Sample
/// </summary>
public static class WallArtLiftProbe
{
    public static string Sample()
    {
        var c = Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);
        if (c == null || c.View == null) return "no view";
        var v = c.View;
        var sb = new StringBuilder("t=" + Time.unscaledTime.ToString("F1"));
        var cab = v.Cabin;
        var art = v.ArtElevator;
        Transform pivot = art != null ? art.ElevatorCarPivot : null;
        if (cab != null) sb.Append(" logicCabinY=" + cab.position.y.ToString("F3"));
        if (pivot != null) sb.Append(" artCarY=" + pivot.position.y.ToString("F3") + " gap=" + (pivot.position.y - cab.position.y).ToString("F3") + " artActive=" + pivot.gameObject.activeInHierarchy);
        else sb.Append(" (no art car)");
        sb.AppendLine();
        foreach (HabitatRole r in System.Enum.GetValues(typeof(HabitatRole)))
        {
            Transform a = v.Avatar(r);
            if (a == null) continue;
            var an = a.GetComponentInChildren<Animator>();
            var w = c.Stage != null ? c.Stage.Walker(r) : null;
            string st = an == null ? "-" : an.GetCurrentAnimatorStateInfo(0).shortNameHash == Animator.StringToHash("Walk_Loop") ? "Walk_Loop"
                : an.GetCurrentAnimatorStateInfo(0).shortNameHash == Animator.StringToHash("Walk_Carry_Paper") ? "Walk_Carry_Paper"
                : an.GetCurrentAnimatorStateInfo(0).shortNameHash == Animator.StringToHash("Idle_Breathing") ? "Idle_Breathing" : "other";
            sb.AppendLine($"  {r,-10} walker={(w != null ? w.State.ToString() : "-"),-15} anim={(w != null ? w.Anim.ToString() : "-"),-7} animator={st,-16} MoveSpeed={(an != null ? an.GetFloat("MoveSpeed") : 0):F1} parent={a.parent.name}");
        }
        return sb.ToString();
    }
}
