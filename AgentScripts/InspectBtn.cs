using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Dump a cloned action button's hierarchy + components, and the paper HandGrabInteractable's
/// full field set (incl. base-class privates) so we can find grab-type / pose / press-visual fields.</summary>
public static class InspectBtn
{
    public static string Go()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var sb = new StringBuilder();

        var btn = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name == "Btn_Approve");
        if (btn == null) sb.AppendLine("no Btn_Approve");
        else
        {
            sb.AppendLine("== Btn_Approve tree ==");
            foreach (var t in btn.GetComponentsInChildren<Transform>(true))
            {
                sb.AppendLine($"  [{t.name}] lp={t.localPosition}");
                foreach (var c in t.GetComponents<Component>())
                    sb.AppendLine($"      {c.GetType().Name}");
            }
        }

        var doc = roots.FirstOrDefault(r => r.name == "DecisionDocument");
        var hand = doc?.GetComponents<Component>()
            .FirstOrDefault(c => c.GetType().FullName == "Oculus.Interaction.HandGrab.HandGrabInteractable");
        if (hand != null)
        {
            sb.AppendLine("== HandGrabInteractable fields (all, incl base) ==");
            Type t = hand.GetType();
            while (t != null && t != typeof(MonoBehaviour))
            {
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    sb.AppendLine($"  {t.Name}.{f.Name} : {f.FieldType.Name} = {Val(f.GetValue(hand))}");
                t = t.BaseType;
            }
        }
        return sb.ToString();
    }

    static string Val(object o)
    {
        if (o == null) return "null";
        if (o is UnityEngine.Object uo) return uo ? uo.name + "(" + o.GetType().Name + ")" : "null";
        return o.ToString();
    }
}
