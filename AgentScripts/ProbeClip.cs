using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

const string fbx = "Assets/MuseDesk/Art/Models/Characters/Agent_Researcher.fbx";
var clip = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
    .FirstOrDefault(c => c.name == "Idle_Breathing");
if (clip == null) return "NO CLIP";
var sb = new StringBuilder();
var binds = AnimationUtility.GetCurveBindings(clip);
sb.AppendLine($"clip={clip.name} len={clip.length:0.00} frameRate={clip.frameRate} empty={clip.empty} bindings={binds.Length}");
foreach (var b in binds.Take(8))
    sb.AppendLine($"  path='{b.path}' type={b.type.Name} prop={b.propertyName}");

// Walk_Loop should have bigger motion — check a Hand curve range
var walk = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => c.name == "Walk_Loop");
if (walk != null)
{
    var wb = AnimationUtility.GetCurveBindings(walk).FirstOrDefault(x => x.path.Contains("Hand_R") || x.path.Contains("UpperArm_R"));
    if (!string.IsNullOrEmpty(wb.path))
    {
        var curve = AnimationUtility.GetEditorCurve(walk, wb);
        var ks = curve.keys;
        float min = ks.Min(k => k.value), max = ks.Max(k => k.value);
        sb.AppendLine($"Walk arm curve '{wb.path}'.{wb.propertyName} keys={ks.Length} range={min:0.00}..{max:0.00}");
    }
}
return sb.ToString();
