using System.Linq;
using UnityEngine;

var smr = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude).FirstOrDefault();
if (smr == null) return "NO SMR";
var hand = smr.bones.FirstOrDefault(b => b.name == "Hand_R");
var spine = smr.bones.FirstOrDefault(b => b.name == "Spine");
if (hand == null) return "NO Hand_R; bones=" + string.Join(",", smr.bones.Select(b => b.name));
var h0 = hand.localEulerAngles; var s0 = spine.localEulerAngles;
float t0 = Time.time;
await Wait.Until(() => Time.time > t0 + 0.35f);
var h1 = hand.localEulerAngles; var s1 = spine.localEulerAngles;
float dh = (h1 - h0).magnitude; float ds = (s1 - s0).magnitude;
return $"Hand_R d={dh:0.00} ({h0}->{h1})  Spine d={ds:0.00}  => {(dh+ds > 0.01f ? "ANIMATING" : "STATIC/bindpose")}";
