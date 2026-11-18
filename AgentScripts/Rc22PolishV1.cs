using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Wall;

/// <summary>
/// rc22 polish builder (owner, after rc22 on head): idempotent, saves the scene. Never hand-edit the scene for these.
///
///  1 Monitors (owner: "fix the clipping on the monitors and make the Developer's workstation not have a double-sided
///    screen"). Each desk workstation screen (…MonitorScreen, …DisplayPanelScreen) is a thin box with the glowing screen
///    material on EVERY face, so the user (who sits behind the monitors, the agents face them) saw code on the back, and the
///    box poked through the housing. The screen mesh is split: the faces toward the agent's seat keep the screen material
///    (nudged 0.4 mm toward the seat, off the bezel); the back and the edges become a child "BackCover" in the station's
///    charcoal housing material. Meshes are saved under Assets/MuseDesk/Art/Meshes/Rc22/.
///  2 Clutter (owner: "remove extraneous props and clutter"): the loose desk decor (mini plant, desk lamp, book stack, mini
///    clock: never used by any script) is switched off in the scene, as the coffee mug and the rest already are; the tiny
///    plant pot on the Developer's desk stops rendering.
///  3 Arms (owner: "the agents' arms clip through themselves"): AgentArmClearance on the desk agents in the scene (the
///    portal / perch clones add it themselves).
///
///   powershell scripts\run_guarded.ps1 -File AgentScripts\Rc22PolishV1.cs -Entry Rc22PolishV1.Apply
///   Report (read-only): Rc22PolishV1.Report
/// </summary>
public static class Rc22PolishV1
{
    const string MeshDir = "Assets/MuseDesk/Art/Meshes/Rc22";
    static readonly string[] Stations = { "ResearchStation", "DevStation", "QAStation" };
    static readonly string[] DeskDecor = { "Phase6/Decor_MiniPlant", "Phase6/Decor_DeskLamp", "Phase6/Decor_BookStack", "Phase6/Decor_MiniClock" };
    static readonly string[] HideRenderers = { "DevStation/Workstation/Developer_Static_PlantGreen", "DevStation/Workstation/Developer_Static_Terracotta" };

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Rc22PolishV1: stop Play Mode first.";
        var scene = EditorSceneManager.GetActiveScene();
        var rootGo = GameObject.Find("WorkspaceRoot");
        if (rootGo == null) return "Rc22PolishV1: no WorkspaceRoot in the open scene (" + scene.path + ").";
        Transform root = rootGo.transform;
        var sb = new StringBuilder("Rc22PolishV1 on " + scene.path + "\n");

        // 1 monitors
        EnsureDir(MeshDir);
        foreach (string st in Stations)
        {
            Transform ws = root.Find(st + "/Workstation");
            if (ws == null) { sb.Append("  ").Append(st).Append(": no Workstation\n"); continue; }
            Transform seat = ws.Find("AgentSeatPosition");
            Material housing = HousingMaterial(ws);
            foreach (var mf in ws.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.transform.parent != ws) continue;               // the station's own parts, not the legacy backup
                string n = mf.name;
                if (n.IndexOf("MonitorScreen", System.StringComparison.Ordinal) < 0 && n.IndexOf("DisplayPanelScreen", System.StringComparison.Ordinal) < 0) continue;
                sb.Append("  ").Append(st).Append("/").Append(n).Append(": ").Append(SplitScreen(st, mf, seat, housing)).Append("\n");
            }
        }

        // 2 clutter
        foreach (string p in DeskDecor)
        {
            Transform t = root.Find(p);
            if (t == null) { sb.Append("  decor ").Append(p).Append(": not found\n"); continue; }
            if (t.gameObject.activeSelf) { Undo.RecordObject(t.gameObject, "Rc22 clutter"); t.gameObject.SetActive(false); sb.Append("  decor ").Append(p).Append(": switched off\n"); }
            else sb.Append("  decor ").Append(p).Append(": already off\n");
        }
        foreach (string p in HideRenderers)
        {
            Transform t = root.Find(p);
            var r = t != null ? t.GetComponent<Renderer>() : null;
            if (r == null) { sb.Append("  prop ").Append(p).Append(": not found\n"); continue; }
            if (r.enabled) { Undo.RecordObject(r, "Rc22 clutter"); r.enabled = false; sb.Append("  prop ").Append(p).Append(": renderer off\n"); }
            else sb.Append("  prop ").Append(p).Append(": already off\n");
        }

        // 3 arms
        var c = Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);
        var agents = new List<GameObject>();
        if (c != null && c.agentSources != null) agents.AddRange(c.agentSources);
        if (c != null && c.managerSource != null) agents.Add(c.managerSource);
        foreach (var go in agents)
        {
            if (go == null || !go.scene.IsValid()) continue;            // a prefab asset: never modified here
            if (go.GetComponent<AgentArmClearance>() != null) { sb.Append("  arms ").Append(go.name).Append(": already\n"); continue; }
            Undo.AddComponent<AgentArmClearance>(go);
            sb.Append("  arms ").Append(go.name).Append(": AgentArmClearance added\n");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.Append("scene saved=").Append(ok);
        return sb.ToString();
    }

    public static string Report()
    {
        var rootGo = GameObject.Find("WorkspaceRoot");
        if (rootGo == null) return "no WorkspaceRoot";
        var sb = new StringBuilder();
        foreach (string st in Stations)
        {
            Transform ws = rootGo.transform.Find(st + "/Workstation");
            if (ws == null) continue;
            foreach (var mf in ws.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.Contains("Screen") || mf.name == "BackCover")
                    sb.Append(st).Append("/").Append(mf.transform.parent != null ? mf.transform.parent.name + "/" : "").Append(mf.name).Append(" mesh=")
                      .Append(mf.sharedMesh != null ? mf.sharedMesh.name : "-").Append("\n");
        }
        foreach (string p in DeskDecor) { var t = rootGo.transform.Find(p); sb.Append(p).Append(" active=").Append(t != null && t.gameObject.activeSelf).Append("\n"); }
        return sb.ToString();
    }

    static Material HousingMaterial(Transform ws)
    {
        foreach (var r in ws.GetComponentsInChildren<MeshRenderer>(true))
            if (r.transform.parent == ws && r.name.Contains("CharcoalVisor") && r.sharedMaterial != null) return r.sharedMaterial;
        foreach (var r in ws.GetComponentsInChildren<MeshRenderer>(true))
            if (r.name.Contains("CharcoalVisor") && r.sharedMaterial != null) return r.sharedMaterial;
        return null;
    }

    static string SplitScreen(string station, MeshFilter mf, Transform seat, Material housing)
    {
        Mesh src = mf.sharedMesh;
        if (src == null) return "no mesh";
        if (src.name.EndsWith("_FrontOnly")) return "already split (" + src.name + ")";
        if (!src.isReadable) return "mesh not readable: left as is";
        if (housing == null) return "no housing material found: left as is";
        Transform t = mf.transform;
        Vector3 centre = t.TransformPoint(src.bounds.center);
        Vector3 toSeat = seat != null ? seat.position - centre : -t.parent.forward;
        toSeat.y = 0f;
        if (toSeat.sqrMagnitude < 1e-10f) return "no seat direction: left as is";
        toSeat.Normalize();

        Vector3[] v = src.vertices; Vector3[] nrm = src.normals; Vector2[] uv = src.uv; int[] tri = src.triangles;
        var front = new List<int>(tri.Length); var rest = new List<int>(tri.Length);
        for (int i = 0; i < tri.Length; i += 3)
        {
            Vector3 a = t.TransformPoint(v[tri[i]]), b = t.TransformPoint(v[tri[i + 1]]), c = t.TransformPoint(v[tri[i + 2]]);
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-16f) { rest.Add(tri[i]); rest.Add(tri[i + 1]); rest.Add(tri[i + 2]); continue; }
            var dst = Vector3.Dot(n.normalized, toSeat) > 0.5f ? front : rest;
            dst.Add(tri[i]); dst.Add(tri[i + 1]); dst.Add(tri[i + 2]);
        }
        if (front.Count == 0) return "no face toward the seat: left as is";

        // The screen face, nudged 0.4 mm (world) toward the seat so it sits clear of the bezel.
        Vector3 nudge = t.InverseTransformVector(toSeat * 0.0004f);
        var fv = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) fv[i] = v[i] + nudge;
        Mesh fm = Build(station + "_" + mf.name + "_FrontOnly", fv, nrm, uv, front);
        Mesh bm = rest.Count > 0 ? Build(station + "_" + mf.name + "_BackCover", v, nrm, uv, rest) : null;
        fm = Save(fm); if (bm != null) bm = Save(bm);

        Undo.RecordObject(mf, "Rc22 screen");
        mf.sharedMesh = fm;
        string back = "no back faces";
        if (bm != null)
        {
            Transform cover = t.Find("BackCover");
            if (cover == null)
            {
                var go = new GameObject("BackCover");
                Undo.RegisterCreatedObjectUndo(go, "Rc22 screen back");
                go.transform.SetParent(t, false);
                cover = go.transform;
            }
            cover.localPosition = Vector3.zero; cover.localRotation = Quaternion.identity; cover.localScale = Vector3.one;
            var cmf = cover.GetComponent<MeshFilter>(); if (cmf == null) cmf = Undo.AddComponent<MeshFilter>(cover.gameObject);
            var cmr = cover.GetComponent<MeshRenderer>(); if (cmr == null) cmr = Undo.AddComponent<MeshRenderer>(cover.gameObject);
            cmf.sharedMesh = bm;
            cmr.sharedMaterial = housing;
            var srcR = mf.GetComponent<MeshRenderer>();
            if (srcR != null) { cmr.shadowCastingMode = srcR.shadowCastingMode; cmr.receiveShadows = srcR.receiveShadows; }
            back = "back and edges (" + rest.Count / 3 + " tris) in " + housing.name;
        }
        return "screen face " + front.Count / 3 + " tris toward the seat; " + back;
    }

    static Mesh Build(string name, Vector3[] v, Vector3[] n, Vector2[] uv, List<int> tris)
    {
        var m = new Mesh { name = name };
        m.vertices = v;
        if (n != null && n.Length == v.Length) m.normals = n;
        if (uv != null && uv.Length == v.Length) m.uv = uv;
        m.SetTriangles(tris, 0);
        if (n == null || n.Length != v.Length) m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static Mesh Save(Mesh m)
    {
        string path = MeshDir + "/" + m.name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(m, existing);
            existing.name = m.name;
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void EnsureDir(string dir)
    {
        if (AssetDatabase.IsValidFolder(dir)) return;
        string parent = System.IO.Path.GetDirectoryName(dir).Replace('\\', '/');
        EnsureDir(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(dir));
    }
}
