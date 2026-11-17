using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Play Mode sample (owner rc9: "the legs play the walk animation for a second then slide"). For each Wall avatar:
/// position, how far it moved since the last call, the Animator's current state and normalized time, its speed, culling
/// mode and whether the renderer counts as visible. Call repeatedly while agents walk (LivingOfficeDrive RunOffline).
///   scripts\drv.ps1 WallWalkAnimProbe Sample
/// </summary>
public static class WallWalkAnimProbe
{
    static readonly string[] Names = { "Walk_Loop", "Walk_Carry_Paper", "Idle_Breathing", "Turn_Left", "Turn_Right", "Offer_Document", "Typing", "Reading", "Inspecting", "Writing", "Waiting_For_Input", "Listening", "Nodding", "Worried", "Thinking", "Talking", "Manager_Lookout_Idle", "Manager_Review_Plan", "Elevator_Ride_Idle" };

    public static string Sample()
    {
        var sb = new StringBuilder("t=" + Time.unscaledTime.ToString("F2") + "\n");
        foreach (var a in Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.gameObject.scene.IsValid() && t.name.StartsWith("Avatar_") && t.gameObject.activeInHierarchy))
        {
            var an = a.GetComponentInChildren<Animator>();
            var smr = a.GetComponentInChildren<SkinnedMeshRenderer>();
            string st = "-"; float nt = 0f;
            if (an != null)
            {
                var info = an.GetCurrentAnimatorStateInfo(0);
                st = Names.FirstOrDefault(n => Animator.StringToHash(n) == info.shortNameHash) ?? info.shortNameHash.ToString();
                nt = info.normalizedTime;
                if (an.IsInTransition(0)) st += ">" + (Names.FirstOrDefault(n => Animator.StringToHash(n) == an.GetNextAnimatorStateInfo(0).shortNameHash) ?? "?");
            }
            string key = "WallWalkAnimProbe." + a.name;
            Vector3 lp = a.localPosition;
            string prev = PlayerPrefs.GetString(key, "");
            float moved = 0f;
            if (prev.Length > 0) { var p = prev.Split(','); moved = (lp - new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]))).magnitude; }
            PlayerPrefs.SetString(key, lp.x + "," + lp.y + "," + lp.z);
            sb.AppendLine($"  {a.name,-18} parent={a.parent.name,-14} moved={moved:F3} state={st,-22} nt={nt:F2} speed={(an != null ? an.speed : 0):F2} cull={(an != null ? an.cullingMode.ToString() : "-")} visible={(smr != null && smr.isVisible)} enabled={(an != null && an.isActiveAndEnabled)}");
        }
        return sb.ToString();
    }
}
