using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Renderers inside a WorkspaceRoot-local box (x0,x1,y0,y1,z0,z1): what sits between the seated eye and a panel?</summary>
public static class InFront
{
    public static string Run(float x0, float x1, float y0, float y1, float z0, float z1)
    {
        var ws = GameObject.Find("WorkspaceRoot").transform;
        var sb = new StringBuilder();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            Vector3 c = ws.InverseTransformPoint(r.bounds.center);
            if (c.x < x0 || c.x > x1 || c.y < y0 || c.y > y1 || c.z < z0 || c.z > z1) continue;
            Vector3 top = ws.InverseTransformPoint(r.bounds.center + Vector3.up * r.bounds.extents.y);
            string p = r.name; int depth = 0;
            for (Transform q = r.transform.parent; q != null && depth < 4; q = q.parent, depth++) p = q.name + "/" + p;
            sb.AppendLine(p + " centre=" + c.ToString("F3") + " topY=" + top.y.ToString("F3"));
        }
        return sb.Length > 0 ? sb.ToString() : "nothing in the box";
    }
}
