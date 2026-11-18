#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Edit-mode, read-only: the Researcher's brass parts by height (mesh Z = up, -Y = front) vs the shirt's front.</summary>
public static class ResearcherButton2
{
    public static string Run()
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MuseDesk/Art/Models/Characters/Agent_Researcher.fbx");
        var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
        var m = smr.sharedMesh;
        var v = m.vertices;
        var mats = smr.sharedMaterials;
        var sb = new StringBuilder();
        int brass = -1, shirt = -1;
        for (int i = 0; i < mats.Length; i++) { if (mats[i].name == "Mat_Brass") brass = i; if (mats[i].name == "Mat_RoleTrim_Researcher") shirt = i; }
        var bv = new HashSet<int>(m.GetTriangles(brass));
        var hist = new SortedDictionary<int, List<int>>();
        foreach (int i in bv) { int k = Mathf.RoundToInt(v[i].z * 10000f); if (!hist.ContainsKey(k)) hist[k] = new List<int>(); hist[k].Add(i); }
        foreach (var kv in hist)
        {
            Vector3 mn = Vector3.one * 9f, mx = -Vector3.one * 9f;
            foreach (int i in kv.Value) { mn = Vector3.Min(mn, v[i]); mx = Vector3.Max(mx, v[i]); }
            sb.Append("brass z~" + (kv.Key / 10000f).ToString("F4") + " n=" + kv.Value.Count + " x " + mn.x.ToString("F5") + ".." + mx.x.ToString("F5") + " y(front-) " + mn.y.ToString("F5") + ".." + mx.y.ToString("F5") + "\n");
        }
        // Shirt's front surface near the chest button (|x| < 0.0001, z 0.0009..0.0013)
        var sv = new HashSet<int>(m.GetTriangles(shirt));
        var rows = new SortedDictionary<int, float>();
        foreach (int i in sv)
        {
            if (Mathf.Abs(v[i].x) > 0.00012f) continue;
            int k = Mathf.RoundToInt(v[i].z * 10000f);
            if (!rows.ContainsKey(k) || v[i].y < rows[k]) rows[k] = v[i].y;
        }
        foreach (var kv in rows) sb.Append("shirt front z~" + (kv.Key / 10000f).ToString("F4") + " minY=" + kv.Value.ToString("F5") + "\n");
        return sb.ToString();
    }
}
#endif
