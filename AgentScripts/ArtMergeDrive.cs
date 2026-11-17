using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Art merge check (docs/Engineering/ART_INTEGRATION_RESULT.md). Play Mode: Start() hooks EditorApplication.update and records,
/// per desk agent, every distinct Base Layer state the Animator enters; Report() prints them and the controller's missing-state
/// check. Shot() writes an Editor render of the office (temporary hidden camera) to docs/review_screenshots/polish/.
/// Editor renders only: never headset evidence. Presentation only; reads state, writes nothing to the scene.
/// </summary>
public static class ArtMergeDrive
{
    static readonly Dictionary<string, HashSet<string>> seen = new Dictionary<string, HashSet<string>>();
    static readonly Dictionary<string, List<string>> order = new Dictionary<string, List<string>>();
    static readonly Dictionary<int, string> names = new Dictionary<int, string>();
    static readonly List<(string id, Animator a)> agents = new List<(string, Animator)>();
    static bool hooked;
    static int frames, layersMax;

    static Transform Root() =>
        EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "WorkspaceRoot");

    public static string Start()
    {
        if (!EditorApplication.isPlaying) return "enter Play Mode first";
        var root = Root();
        System.IO.File.WriteAllText(LogPath, ""); seen.Clear(); order.Clear(); names.Clear(); agents.Clear(); frames = 0;
        string info = "";
        foreach (var v in root.GetComponentsInChildren<MuseDesk.Agents.AgentView>(false))
        {
            var an = v.GetComponentInChildren<Animator>();
            if (an == null || agents.Any(x => x.id == v.agentId)) continue;
            agents.Add((v.agentId, an));
            seen[v.agentId] = new HashSet<string>(); order[v.agentId] = new List<string>();
            var ctrl = an.runtimeAnimatorController as AnimatorController;
            if (ctrl != null)
            {
                layersMax = ctrl.layers.Length;
                foreach (var l in ctrl.layers)
                    foreach (var s in l.stateMachine.states)
                        names[Animator.StringToHash(l.name + "." + s.state.name)] = l.name + "." + s.state.name;
                info += v.agentId + ": controller '" + ctrl.name + "' layers=" + ctrl.layers.Length + " params=" + ctrl.parameters.Length + " | ";
            }
        }
        if (!hooked) { EditorApplication.update += Tick; hooked = true; }
        return "sampling " + agents.Count + " agents. " + info;
    }

    const string LogPath = "Temp/artmerge_states.txt";
    static void Log(string id, string n) { System.IO.File.AppendAllText(LogPath, id + "|" + (UnityEngine.Time.time).ToString("F1") + "|" + n + "\n"); }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        frames++;
        foreach (var (id, a) in agents)
        {
            if (a == null || !a.isActiveAndEnabled) continue;
            int h = a.GetCurrentAnimatorStateInfo(0).fullPathHash;
            string n = names.TryGetValue(h, out var s) ? s : "?" + h;
            if (seen[id].Add(n)) { order[id].Add(n); Log(id, n); }
            if (a.IsInTransition(0))
            {
                int h2 = a.GetNextAnimatorStateInfo(0).fullPathHash;
                string n2 = names.TryGetValue(h2, out var s2) ? s2 : "?" + h2;
                if (seen[id].Add(n2)) { order[id].Add(n2); Log(id, n2); }
            }
        }
    }

    public static string Report()
    {
        var sb = new StringBuilder();
        var by = new Dictionary<string, List<string>>();
        foreach (var l in System.IO.File.ReadAllLines(LogPath)) { var p = l.Split('|'); if (p.Length < 3) continue; if (!by.ContainsKey(p[0])) by[p[0]] = new List<string>(); by[p[0]].Add(p[2] + "@" + p[1]); }
        foreach (var kv in by) sb.AppendLine(kv.Key + " [" + kv.Value.Count + "]: " + string.Join(", ", kv.Value));
        return sb.ToString();
    }

    public static string Stop()
    {
        if (hooked) { EditorApplication.update -= Tick; hooked = false; }
        return "stopped";
    }

    /// <summary>"tag;ex,ey,ez;tx,ty,tz;fov" root-local metres; saved as docs/review_screenshots/{path}.</summary>
    public static string Shot(string spec)
    {
        var root = Root();
        if (root == null) return "no WorkspaceRoot";
        var f = spec.Split(';');
        var c = System.Globalization.CultureInfo.InvariantCulture;
        Vector3 V(string s) { var p = s.Split(','); return new Vector3(float.Parse(p[0], c), float.Parse(p[1], c), float.Parse(p[2], c)); }
        string path = "docs/review_screenshots/" + f[0];
        var go = new GameObject("ArtMergeTempCam") { hideFlags = HideFlags.HideAndDontSave };
        RenderTexture rt = null; Texture2D tex = null;
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 6f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.20f, 0.22f, 0.26f);
            cam.fieldOfView = float.Parse(f[3], c);
            Vector3 eye = root.TransformPoint(V(f[1])), target = root.TransformPoint(V(f[2]));
            go.transform.position = eye;
            go.transform.rotation = Quaternion.LookRotation(target - eye, Vector3.up);
            rt = new RenderTexture(1400, 900, 24); cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(1400, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1400, 900), 0, 0); tex.Apply();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null; cam.targetTexture = null;
        }
        finally
        {
            if (rt != null) Object.DestroyImmediate(rt);
            if (tex != null) Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
        }
        return "saved " + path;
    }

    /// <summary>Heights and positions of the three desk agents (root-local), for the scale/floating check.</summary>
    public static string Heights()
    {
        var root = Root();
        var sb = new StringBuilder();
        foreach (var v in root.GetComponentsInChildren<MuseDesk.Agents.AgentView>(false))
        {
            var rs = v.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0) continue;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            Vector3 mn = root.InverseTransformPoint(b.min), mx = root.InverseTransformPoint(b.max);
            sb.AppendLine(v.agentId + " pos " + root.InverseTransformPoint(v.transform.position).ToString("F3") + " bottomY " + mn.y.ToString("F3") + " topY " + mx.y.ToString("F3") + " scale " + v.transform.lossyScale.x.ToString("F3"));
        }
        return sb.ToString();
    }
}

