using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// rc17 worn: "there are chairs at the desks but the agents are standing". The optimized workstation FBX merges parts by
/// material, so the chair is not its own object. This splits each merged mesh into connected islands (in Workstation
/// local space) and can drop the islands that make up the chair (behind the desk's back edge, centred on the seat).
///   Analyze("ResearchStation")         list islands
///   Strip(false|true)                   dry run / write stripped mesh assets + assign them in the scene and save
/// </summary>
public static class DeskIslands
{
    static readonly string[] Stations = { "ResearchStation", "DevStation", "QAStation" };
    const string OutDir = "Assets/MuseDesk/Art/Models/Optimized/NoChair";

    struct Island { public List<int> tris; public Bounds b; }

    static Transform Ws() => Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "WorkspaceRoot" && t.gameObject.scene.IsValid());

    static List<Island> Islands(Mesh m, Transform mf, Transform wk)
    {
        var v = m.vertices; var tri = m.triangles;
        // Weld by position so seams split by normals/UVs still join.
        var key = new Dictionary<Vector3Int, int>(); var rep = new int[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 1e5f), Mathf.RoundToInt(v[i].y * 1e5f), Mathf.RoundToInt(v[i].z * 1e5f));
            if (!key.TryGetValue(k, out int r)) { r = i; key[k] = i; }
            rep[i] = r;
        }
        var parent = Enumerable.Range(0, v.Length).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
        for (int t = 0; t < tri.Length; t += 3) { Union(rep[tri[t]], rep[tri[t + 1]]); Union(rep[tri[t]], rep[tri[t + 2]]); }
        var groups = new Dictionary<int, Island>();
        for (int t = 0; t < tri.Length; t += 3)
        {
            int g = Find(rep[tri[t]]);
            if (!groups.TryGetValue(g, out var isl)) { isl = new Island { tris = new List<int>(), b = new Bounds(wk.InverseTransformPoint(mf.TransformPoint(v[tri[t]])), Vector3.zero) }; }
            isl.tris.Add(t);
            for (int k = 0; k < 3; k++) isl.b.Encapsulate(wk.InverseTransformPoint(mf.TransformPoint(v[tri[t + k]])));
            groups[g] = isl;
        }
        return groups.Values.ToList();
    }

    /// <summary>Chair = behind the desk top (centre z below 0.005 in Workstation space, where the desk spans z 0..0.114)
    /// and within the seat's width (|x| under 0.07), lower than the desk top's underside plus a back-rest allowance.</summary>
    static bool IsChair(Bounds b) => b.center.z < 0.005f && Mathf.Abs(b.center.x) < 0.07f && b.min.y < 0.12f && b.max.z < 0.03f;

    public static string Analyze(string station)
    {
        var sb = new StringBuilder();
        Transform wk = Ws().Find(station + "/Workstation");
        foreach (var mf in wk.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var isl = Islands(mf.sharedMesh, mf.transform, wk);
            sb.AppendLine(mf.name + " islands=" + isl.Count);
            foreach (var i in isl.OrderBy(x => x.b.center.z))
                sb.AppendLine("   c=" + i.b.center.ToString("F3") + " size=" + i.b.size.ToString("F3") + " tris=" + i.tris.Count + (IsChair(i.b) ? "  CHAIR" : ""));
        }
        return sb.ToString();
    }

    public static string Strip(bool save)
    {
        var sb = new StringBuilder();
        Transform ws = Ws();
        if (save && !AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/MuseDesk/Art/Models/Optimized", "NoChair");
        foreach (string s in Stations)
        {
            Transform wk = ws.Find(s + "/Workstation");
            foreach (var mf in wk.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh; if (m == null || m.name.EndsWith("_NoChair")) continue;
                if (m.subMeshCount != 1) { sb.AppendLine("  skip multi-submesh " + m.name); continue; }
                var isl = Islands(m, mf.transform, wk);
                var drop = isl.Where(i => IsChair(i.b)).ToList();
                if (drop.Count == 0) continue;
                var dropTris = new HashSet<int>(drop.SelectMany(i => i.tris));
                var tri = m.triangles; var keep = new List<int>();
                for (int t = 0; t < tri.Length; t += 3) if (!dropTris.Contains(t)) { keep.Add(tri[t]); keep.Add(tri[t + 1]); keep.Add(tri[t + 2]); }
                sb.AppendLine(s + "/" + mf.name + ": drop " + drop.Count + " islands (" + dropTris.Count + " tris) of " + isl.Count + (keep.Count == 0 ? " -> renderer off" : ""));
                if (!save) continue;
                Undo.RecordObject(mf, "No chair");
                if (keep.Count == 0)
                {
                    var r = mf.GetComponent<MeshRenderer>(); Undo.RecordObject(r, "No chair"); r.enabled = false; continue;
                }
                var nm = Object.Instantiate(m); nm.name = m.name + "_NoChair";
                nm.triangles = keep.ToArray(); nm.RecalculateBounds();
                string path = OutDir + "/" + s + "_" + m.name + "_NoChair.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(nm, path);
                mf.sharedMesh = nm;
                EditorUtility.SetDirty(mf);
            }
        }
        if (save)
        {
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(ws.gameObject.scene);
            EditorSceneManager.SaveScene(ws.gameObject.scene);
            sb.AppendLine("saved");
        }
        return sb.ToString();
    }
}
