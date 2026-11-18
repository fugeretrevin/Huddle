using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only: lists the habitat art prefab's screen-like renderers. Entry: Rc23HabitatScreenList.Run</summary>
public static class Rc23HabitatScreenList
{
    public static string Run()
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Prefabs/WallHabitat/WallHabitat_Complete_3Tier.prefab");
        if (p == null) return "prefab missing";
        var sb = new StringBuilder();
        foreach (var r in p.GetComponentsInChildren<Renderer>(true))
            if (r.transform.parent != null && r.transform.parent.name == "Bay_Developer_Workstation")
            { sb.Append("DEVPART ").Append(r.name).Append(" :"); foreach (var m in r.sharedMaterials) sb.Append(m != null ? m.name : "null").Append(","); sb.Append("\n"); }
        foreach (var r in p.GetComponentsInChildren<Renderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>();
            var mats = r.sharedMaterials;
            bool hit = r.name.IndexOf("Screen", System.StringComparison.OrdinalIgnoreCase) >= 0 || r.name.IndexOf("Monitor", System.StringComparison.OrdinalIgnoreCase) >= 0 || r.name.IndexOf("Display", System.StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var m in mats) if (m != null && m.name.IndexOf("Screen", System.StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
            if (!hit) continue;
            string path = r.name; for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            sb.Append(path).Append(" mesh=").Append(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name + " readable=" + mf.sharedMesh.isReadable + " sub=" + mf.sharedMesh.subMeshCount + " tris=" + mf.sharedMesh.triangles.Length / 3 : "-");
            sb.Append(" mats=");
            foreach (var m in mats) sb.Append(m != null ? m.name : "null").Append(",");
            if (mf != null && mf.sharedMesh != null)
            {
                var b = mf.sharedMesh.bounds;
                sb.Append(" localBounds c=").Append(b.center.ToString("F4")).Append(" s=").Append(b.size.ToString("F4"));
                sb.Append(" prefabLossyRot=").Append(r.transform.rotation.eulerAngles.ToString("F0")).Append(" lossyScale=").Append(r.transform.lossyScale.ToString("F3"));
                // triangle normal histogram in prefab space
                var v = mf.sharedMesh.vertices; var tr = mf.sharedMesh.triangles;
                int[] h = new int[6]; // +x -x +y -y +z -z
                for (int i = 0; i < tr.Length; i += 3)
                {
                    Vector3 a = r.transform.TransformPoint(v[tr[i]]), bb = r.transform.TransformPoint(v[tr[i + 1]]), c = r.transform.TransformPoint(v[tr[i + 2]]);
                    Vector3 n = Vector3.Cross(bb - a, c - a); if (n.sqrMagnitude < 1e-16f) continue; n.Normalize();
                    float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                    if (ax >= ay && ax >= az) h[n.x > 0 ? 0 : 1]++; else if (ay >= az) h[n.y > 0 ? 2 : 3]++; else h[n.z > 0 ? 4 : 5]++;
                }
                sb.Append(" normals(+x,-x,+y,-y,+z,-z)=").Append(string.Join(",", h));
                float[] ar = new float[6];
                for (int i = 0; i < tr.Length; i += 3)
                {
                    Vector3 a = r.transform.TransformPoint(v[tr[i]]), bb = r.transform.TransformPoint(v[tr[i + 1]]), c = r.transform.TransformPoint(v[tr[i + 2]]);
                    Vector3 n = Vector3.Cross(bb - a, c - a); float area = n.magnitude * 0.5f; if (area < 1e-12f) continue; n.Normalize();
                    float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                    if (ax >= ay && ax >= az) ar[n.x > 0 ? 0 : 1] += area; else if (ay >= az) ar[n.y > 0 ? 2 : 3] += area; else ar[n.z > 0 ? 4 : 5] += area;
                }
                sb.Append(" areaMm2=");
                for (int i = 0; i < 6; i++) sb.Append((ar[i] * 1e6f).ToString("F0")).Append(",");
                sb.Append(" worldPos=").Append(r.transform.position.ToString("F3")).Append(" parentRotY=").Append(r.transform.parent.rotation.eulerAngles.y.ToString("F0")).Append(" selfRot=").Append(r.transform.localRotation.eulerAngles.ToString("F0"));
            }
            sb.Append("\n");
        }
        return sb.ToString();
    }
}
