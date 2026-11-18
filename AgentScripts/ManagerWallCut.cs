#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Edit-mode, read-only: is the Manager floor's structure mesh readable, and how many triangles sit in its -x end slab.</summary>
public static class ManagerWallCut
{
    public static string Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Prefabs/WallHabitat/WallHabitat_Complete_3Tier.prefab");
        if (prefab == null) return "no prefab";
        MeshFilter mf = null;
        foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true)) if (f.name == "WallHabitat_ManagerLookout_Structure") mf = f;
        if (mf == null || mf.sharedMesh == null) return "no structure mesh";
        var m = mf.sharedMesh;
        var sb = new StringBuilder("mesh=" + m.name + " readable=" + m.isReadable + " path=" + AssetDatabase.GetAssetPath(m) + " subMeshes=" + m.subMeshCount + "\n");
        var v = m.vertices; var root = prefab.transform;
        float minX = float.MaxValue;
        var wv = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) { wv[i] = root.InverseTransformPoint(mf.transform.TransformPoint(v[i])); minX = Mathf.Min(minX, wv[i].x); }
        sb.Append("minX=" + minX.ToString("F3") + "\n");
        for (int s = 0; s < m.subMeshCount; s++)
        {
            var t = m.GetTriangles(s); int slab = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = wv[t[i]], b = wv[t[i + 1]], c = wv[t[i + 2]];
                if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < minX + 0.025f && Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > 0.148f && Mathf.Max(a.z, Mathf.Max(b.z, c.z)) > 0.03f) slab++;
            }
            sb.Append("submesh " + s + " tris=" + t.Length / 3 + " inEndSlab=" + slab + "\n");
        }
        return sb.ToString();
    }
}
#endif
