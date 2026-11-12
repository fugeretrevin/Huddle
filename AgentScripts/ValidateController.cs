using System.Linq;
using System.Text;
using UnityEngine;

var anim = Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude).FirstOrDefault(a => a.runtimeAnimatorController != null);
if (anim == null) return "NO ANIMATOR";
var sb = new StringBuilder();

async System.Threading.Tasks.Task<string> After(float s)
{
    float t = Time.time; await Wait.Until(() => Time.time > t + s);
    var ci = anim.GetCurrentAnimatorClipInfo(0);
    return ci.Length > 0 ? ci[0].clip.name : "(none)";
}

anim.SetBool("IsMoving", false);
foreach (var (id, label) in new[] { (0,"idle"),(1,"researching"),(2,"coding"),(3,"testing"),(4,"reporting"),(6,"waiting"),(7,"complete") })
{
    anim.SetInteger("StateId", id);
    sb.AppendLine($"StateId={id} ({label}) -> clip={await After(0.4f)}");
}
// one-shots: return to idle base afterwards
anim.SetInteger("StateId", 0);
foreach (var tr in new[] { "TrOffer","TrSuccess","TrFailure","TrHandoffGive","TrHandoffReceive" })
{
    anim.SetTrigger(tr);
    sb.AppendLine($"{tr} -> clip={await After(0.2f)}");
    await Wait.Until(() => true);
    float t = Time.time; await Wait.Until(() => Time.time > t + 1.6f); // let one-shot finish + return
}
sb.AppendLine("after one-shots, clip=" + await After(0.3f));
return sb.ToString();
