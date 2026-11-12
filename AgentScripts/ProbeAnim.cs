using System.Linq;
using UnityEngine;

var anim = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)
    .FirstOrDefault(a => a.runtimeAnimatorController != null);
if (anim == null) return "NO ANIMATOR";
var st = anim.GetCurrentAnimatorStateInfo(0);
var ci = anim.GetCurrentAnimatorClipInfo(0);
string clip = ci.Length > 0 ? ci[0].clip.name : "(none)";
return $"frame={Time.frameCount} controller={anim.runtimeAnimatorController.name} " +
       $"StateId={anim.GetInteger("StateId")} IsMoving={anim.GetBool("IsMoving")} " +
       $"clip={clip} normTime={st.normalizedTime:0.00} playing={anim.isActiveAndEnabled} " +
       $"avatar={(anim.avatar ? anim.avatar.name : "null")}";
