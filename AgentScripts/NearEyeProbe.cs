using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class NearEyeProbe
{
    public static string Run()
    {
        var root = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).First(t => t.name == "WorkspaceRoot");
        Vector3 eye = root.TransformPoint(new Vector3(0, 0.30f, 0.62f));
        var sb = new StringBuilder();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (!r.enabled) continue;
            var b = r.bounds;
            if (r is ParticleSystemRenderer) continue; var mid = root.TransformPoint(new Vector3(0, 0.22f, 0.40f)); if (b.SqrDistance(eye) > 0.3f*0.3f && !b.Contains(mid)) continue; if (b.size.magnitude < 0.25f) continue;
            string path = r.name; var t = r.transform.parent; int n = 0;
            while (t != null && n++ < 6) { path = t.name + "/" + path; t = t.parent; }
            sb.AppendLine($"{path} size={b.size:F2} lossy={r.transform.lossyScale:F3} type={r.GetType().Name}");
        }
        return sb.Length == 0 ? "nothing near eye" : sb.ToString();
    }
}

