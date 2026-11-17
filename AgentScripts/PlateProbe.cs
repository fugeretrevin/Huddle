using System.Text;
using UnityEngine;

public static class PlateProbe
{
    public static string Run()
    {
        var sb = new StringBuilder();
        var ws = GameObject.Find("WorkspaceRoot");
        foreach (var n in Object.FindObjectsByType<MuseDesk.Workspace.AgentNameplate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Transform t = n.transform;
            string p = t.name;
            for (Transform q = t.parent; q != null; q = q.parent) p = q.name + "/" + p;
            Vector3 pos = ws != null ? ws.transform.InverseTransformPoint(t.position) : t.position;
            Vector3 fwd = ws != null ? ws.transform.InverseTransformDirection(t.forward) : t.forward;
            sb.AppendLine(p + " active=" + t.gameObject.activeInHierarchy + " pos=" + pos.ToString("F3") + " fwd=" + fwd.ToString("F2")
                          + " localEuler=" + t.localEulerAngles.ToString("F0") + " facing=" + n.FacingActive + " cam=" + (Camera.main != null ? Camera.main.name : "none") + " comps=" + string.Join(",", System.Array.ConvertAll(t.GetComponents<Component>(), c => c.GetType().Name)));
        }
        return sb.ToString();
    }
}
