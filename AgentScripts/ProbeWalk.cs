using System.Linq;
using UnityEngine;

var anim = Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude).FirstOrDefault(a => a.runtimeAnimatorController != null);
if (anim == null) return "NO ANIMATOR";
anim.SetBool("IsMoving", true);
anim.SetInteger("StateId", 5);
float t0 = Time.time;
await Wait.Until(() => Time.time > t0 + 0.6f);
var ci = anim.GetCurrentAnimatorClipInfo(0);
string clip = ci.Length > 0 ? ci[0].clip.name : "(none)";

var smr = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude).FirstOrDefault();
var arm = smr.bones.FirstOrDefault(b => b.name == "UpperArm_R");
var a0 = arm.localEulerAngles;
float t1 = Time.time;
await Wait.Until(() => Time.time > t1 + 0.3f);
var a1 = arm.localEulerAngles;
float d = (a1 - a0).magnitude;
anim.SetBool("IsMoving", false);
return $"state clip={clip}  UpperArm_R {a0} -> {a1} d={d:0.00} => {(d>0.01f?"ANIMATING":"STATIC")}";
