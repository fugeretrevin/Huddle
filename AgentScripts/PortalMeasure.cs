using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Measures the large portal prefab (arch size) and the agent prefabs (height, width incl. headphones).</summary>
public static class PortalMeasure
{
    public static string Run()
    {
        var sb = new StringBuilder();
        var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Prefabs/WallHabitat/WallHabitat_LargeAgentPortal.prefab");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(p);
        try
        {
            go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                sb.Append(r.name).Append(" ").Append(r.GetType().Name).Append(" b=").Append(r.bounds.min.ToString("F3")).Append("..").Append(r.bounds.max.ToString("F3")).Append('\n');
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Bone_") || t.name.EndsWith("Pose")) sb.Append(t.name).Append(" at ").Append(t.position.ToString("F3")).Append('\n');
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null && smr.sharedMesh != null)
            {
                var m = smr.sharedMesh; var v = m.vertices;
                // the inner opening: vertices above 0.3 of height near x=0 give the crown's underside
                float top = m.bounds.max.y, crownLow = float.MaxValue, halfInner = float.MaxValue;
                foreach (var q in v) { if (Mathf.Abs(q.x) < 0.05f && q.y > 0.3f * top) crownLow = Mathf.Min(crownLow, q.y); }
                foreach (var q in v) { if (q.y > 0.3f * top && q.y < 0.6f * top) halfInner = Mathf.Min(halfInner, Mathf.Abs(q.x)); }
                sb.Append("mesh bounds ").Append(m.bounds.min.ToString("F3")).Append("..").Append(m.bounds.max.ToString("F3"))
                  .Append(" crownUndersideY(mesh)=").Append(crownLow.ToString("F3")).Append(" innerHalfWidth(mesh, mid height)=").Append(halfInner.ToString("F3"))
                  .Append(" smrScale=").Append(smr.transform.lossyScale.ToString("F3")).Append('\n');
            }
        }
        finally { Object.DestroyImmediate(go); }
        foreach (var name in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester", "Agent_Manager" })
        {
            var guids = AssetDatabase.FindAssets(name + " t:Prefab");
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var a = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(a);
                try
                {
                    inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity;
                    Bounds b = new Bounds(); bool any = false;
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                    sb.Append(path).Append(" size=").Append(b.size.ToString("F3")).Append(" scale=").Append(inst.transform.localScale.ToString("F3")).Append('\n');
                }
                finally { Object.DestroyImmediate(inst); }
                break;
            }
        }
        return sb.ToString();
    }
}
