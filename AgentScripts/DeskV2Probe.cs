using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Play Mode scratch probe for the Desk layout V2 (not saved to the scene): move the Researcher's station block along root z,
/// move a lane point, and print where things are. Used with DeskClearanceDrive.Report / Try in Desk, Floor and Ceiling to pick
/// the numbers that go into DeskChoreographyLayoutV1's V2 tables. Changes made here are lost when Play stops.
///   scripts\drv.ps1 DeskV2Probe Shift 0.05        ResearchStation, its nameplate, InboxTray and the decor cluster, +z by that much
///   scripts\drv.ps1 DeskV2Probe Point "Res_Exit_1;-0.285;-0.047"
///   scripts\drv.ps1 DeskV2Probe Where
/// </summary>
public static class DeskV2Probe
{
    static Transform Root() => Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
        .SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "WorkspaceRoot");
    static Transform Find(Transform root, string n) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != root && t.name == n);

    public static string Shift(string dzText)
    {
        var root = Root(); float dz = float.Parse(dzText, System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        foreach (var n in new[] { "ResearchStation", "Nameplate_Researcher", "InboxTray", "Decor_BookStack", "Decor_MiniPlant", "Decor_DeskLamp" })
        {
            var t = Find(root, n); if (t == null) { sb.Append("MISSING " + n + "; "); continue; }
            t.position += root.TransformVector(new Vector3(0, 0, dz));
            sb.Append(n + " " + root.InverseTransformPoint(t.position).ToString("F3") + "; ");
        }
        return sb.ToString();
    }

    public static string Point(string spec)
    {
        var root = Root(); var f = spec.Split(';'); var c = System.Globalization.CultureInfo.InvariantCulture;
        var t = Find(root, f[0]); if (t == null) return "no " + f[0];
        t.localPosition = new Vector3(float.Parse(f[1], c), 0f, float.Parse(f[2], c));
        return f[0] + " " + t.localPosition.ToString("F3");
    }

    public static string Where()
    {
        var root = Root(); var sb = new StringBuilder();
        foreach (var n in new[] { "ResearchStation", "InboxTray", "DeliveryZone", "Res_Exit_1", "Res_Exit_2", "ResToDev_FromHome_2", "ResToDev_FromDelivery_1", "ResToDev_Stand" })
        { var t = Find(root, n); if (t != null) { var p = root.InverseTransformPoint(t.position); sb.Append(n + " (" + p.x.ToString("F3") + ", " + p.z.ToString("F3") + "); "); } }
        return sb.ToString();
    }
}
