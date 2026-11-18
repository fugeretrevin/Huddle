#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Edit-mode, read-only: the habitat art prefab's renderers in its own root space (top floor first), to find the
/// right-hand end wall of the Manager floor.</summary>
public static class HabitatArtWalls
{
    public static string Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Prefabs/WallHabitat/WallHabitat_Complete_3Tier.prefab");
        if (prefab == null) return "no prefab";
        var root = prefab.transform;
        var sb = new StringBuilder();
        Bounds all = new Bounds(); bool any = false;
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds bb = LocalBounds(mf.transform, mf.sharedMesh.bounds, root);
            if (!any) { all = bb; any = true; } else all.Encapsulate(bb);
        }
        sb.Append("ALL min=").Append(all.min.ToString("F3")).Append(" max=").Append(all.max.ToString("F3")).Append('\n');
        float topThird = all.min.y + all.size.y * 0.62f;
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds b = LocalBounds(mf.transform, mf.sharedMesh.bounds, root);
            if (b.max.y < topThird) continue;                             // top floor and roof only
            string path = mf.name;
            for (var t = mf.transform.parent; t != null && t != root; t = t.parent) path = t.name + "/" + path;
            sb.Append(path).Append(" min=").Append(b.min.ToString("F3")).Append(" max=").Append(b.max.ToString("F3"))
              .Append(" tris=").Append(mf.sharedMesh.triangles.Length / 3).Append('\n');
        }
        return sb.ToString();
    }

    static Bounds LocalBounds(Transform t, Bounds mb, Transform root)
    {
        Bounds o = new Bounds(); bool first = true;
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 l = root.InverseTransformPoint(t.TransformPoint(p));
            if (first) { o = new Bounds(l, Vector3.zero); first = false; } else o.Encapsulate(l);
        }
        return o;
    }
}
#endif
