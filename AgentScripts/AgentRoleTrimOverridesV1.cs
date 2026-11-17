using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// The three in-scene desk agents carry per-instance m_Materials overrides that point at legacy materials, so the
/// new matte role trims (Mat_RoleTrim_*) in the re-exported FBX/prefab never show. Apply() reverts ONLY the overridden
/// slots whose source-prefab slot now holds a Mat_RoleTrim_* material; every other override (status, glow, body) is left
/// alone and listed in the report. The exact previous overrides are written to docs/Polish/AgentRoleTrimOverrides_record.txt
/// and Revert() restores them. Idempotent. Saves the scene.
///   scripts\run_guarded.ps1 -File AgentScripts\AgentRoleTrimOverridesV1.cs -Entry AgentRoleTrimOverridesV1.Apply
///   ... -Entry AgentRoleTrimOverridesV1.Report   (read only)   |   ... .Revert
/// </summary>
public static class AgentRoleTrimOverridesV1
{
    static readonly string[] Names = { "Agent_Researcher", "Agent_Developer", "Agent_Tester" };
    const string Record = "docs/Polish/AgentRoleTrimOverrides_record.txt";

    static IEnumerable<Transform> AllTransforms()
    {
        var sc = SceneManager.GetActiveScene();
        foreach (var r in sc.GetRootGameObjects())
            foreach (var t in r.GetComponentsInChildren<Transform>(true)) yield return t;
    }

    static string Path(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    class Slot { public Renderer r; public int i; public Material cur; public Material src; public SerializedProperty p; }

    static List<Slot> Overridden(Transform agent)
    {
        var res = new List<Slot>();
        foreach (var r in agent.GetComponentsInChildren<Renderer>(true))
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(r)) continue;
            var srcR = PrefabUtility.GetCorrespondingObjectFromSource(r) as Renderer;
            if (srcR == null) continue;
            var so = new SerializedObject(r);
            var arr = so.FindProperty("m_Materials");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var p = arr.GetArrayElementAtIndex(i);
                if (!p.prefabOverride) continue;
                var sm = srcR.sharedMaterials;
                res.Add(new Slot { r = r, i = i, cur = r.sharedMaterials[i], src = i < sm.Length ? sm[i] : null, p = p });
            }
        }
        return res;
    }

    static string N(Object o) => o == null ? "null" : o.name + " [" + AssetDatabase.GetAssetPath(o) + "]";

    public static string Report() => Run(false);
    public static string Apply() => Run(true);

    static string Run(bool apply)
    {
        var sb = new StringBuilder();
        var rec = new StringBuilder();
        int reverted = 0, kept = 0;
        foreach (var n in Names)
        {
            var t = AllTransforms().FirstOrDefault(x => x.name == n);
            if (t == null) { sb.AppendLine(n + ": NOT FOUND"); continue; }
            foreach (var s in Overridden(t))
            {
                bool trim = s.src != null && s.src.name.StartsWith("Mat_RoleTrim_");
                string line = n + " | " + Path(s.r.transform).Replace(Path(t.parent ?? t) + "/", "") + " slot " + s.i
                    + " | override=" + N(s.cur) + " | prefab=" + N(s.src) + " | " + (trim ? "REVERT" : "KEEP");
                sb.AppendLine(line);
                if (trim)
                {
                    rec.AppendLine(Path(s.r.transform) + "\t" + s.i + "\t" + AssetDatabase.GetAssetPath(s.cur) + "\t" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(s.cur)));
                    if (apply) { PrefabUtility.RevertPropertyOverride(s.p, InteractionMode.AutomatedAction); reverted++; }
                }
                else kept++;
            }
        }
        if (apply)
        {
            if (rec.Length > 0)
            {
                string old = File.Exists(Record) ? File.ReadAllText(Record) : "";
                Directory.CreateDirectory("docs/Polish");
                File.WriteAllText(Record, old + rec.ToString());
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }
        sb.AppendLine((apply ? "APPLIED" : "REPORT") + " reverted=" + reverted + " kept=" + kept);
        return sb.ToString();
    }

    public static string Revert()
    {
        if (!File.Exists(Record)) return "no record";
        int n = 0;
        var done = new List<string>();
        foreach (var line in File.ReadAllLines(Record))
        {
            var f = line.Split('\t');
            if (f.Length < 3) continue;
            var go = AllTransforms().FirstOrDefault(x => Path(x) == f[0]);
            if (go == null) continue;
            var r = go.GetComponent<Renderer>();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(f[2]);
            if (r == null || mat == null) continue;
            var so = new SerializedObject(r);
            so.FindProperty("m_Materials").GetArrayElementAtIndex(int.Parse(f[1])).objectReferenceValue = mat;
            so.ApplyModifiedProperties();
            n++;
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        return "restored " + n;
    }
}
