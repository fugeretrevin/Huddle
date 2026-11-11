using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Check for doubled hand meshes: list every HandVisual + any OVRHand mesh renderers,
/// with active/enabled state and parent chain, so we can tell if two hand meshes overlap.</summary>
public static class InspectHandVis
{
    public static string Go()
    {
        var sb = new StringBuilder();
        var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);

        var visuals = all.Where(c => c != null && c.GetType().Name == "HandVisual").ToList();
        sb.AppendLine($"HandVisual components: {visuals.Count}");
        foreach (var v in visuals)
        {
            var go = v.gameObject;
            var r = go.GetComponent<Renderer>() ?? go.GetComponentInChildren<Renderer>(true);
            sb.AppendLine($"  [{go.name}] activeInHierarchy={go.activeInHierarchy} " +
                          $"behaviourEnabled={((Behaviour)v).enabled} " +
                          $"renderer={(r ? r.GetType().Name + " enabled=" + r.enabled + " actInHier=" + r.gameObject.activeInHierarchy : "none")} " +
                          $"parent={(go.transform.parent ? go.transform.parent.name : "root")}");
        }

        // Also any SkinnedMeshRenderer that looks like a hand (OVR hand mesh)
        var skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include)
            .Where(s => s.name.ToLower().Contains("hand") || (s.transform.parent && s.transform.parent.name.ToLower().Contains("hand")));
        sb.AppendLine("Hand-ish SkinnedMeshRenderers:");
        foreach (var s in skins)
            sb.AppendLine($"  [{s.name}] enabled={s.enabled} activeInHier={s.gameObject.activeInHierarchy} parent={(s.transform.parent ? s.transform.parent.name : "root")}");

        return sb.ToString();
    }
}
