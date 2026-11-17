using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;
using TMPro;

public static class DeskProbe
{
    /// <summary>Height profile (max vertex y) along z, in a 9 cm wide strip at each station's centre x.</summary>
    public static string Run()
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var ws = all.First(t => t.name == "WorkspaceRoot");
        var sb = new StringBuilder();
        var rends = ws.GetComponentsInChildren<MeshRenderer>(false)
            .Where(r => r.GetComponent<TMP_Text>() == null && r.GetComponentInParent<Animator>() == null && !r.transform.IsChildOf(all.First(t => t.name == "OfficeV2")))
            .Select(r => (r, f: r.GetComponent<MeshFilter>())).Where(x => x.f != null && x.f.sharedMesh != null && x.f.sharedMesh.isReadable).ToList();
        foreach (float cx in new[] { -0.30f, 0f, 0.30f })
        {
            sb.Append($"x={cx:F2}: ");
            float[] maxy = new float[24];
            foreach (var (r, f) in rends)
            {
                var m = r.transform.localToWorldMatrix; var inv = ws.worldToLocalMatrix;
                foreach (var v in f.sharedMesh.vertices)
                {
                    var l = inv.MultiplyPoint3x4(m.MultiplyPoint3x4(v));
                    if (Mathf.Abs(l.x - cx) > 0.045f) continue;
                    int bin = Mathf.FloorToInt((l.z - 0.0f) / 0.015f);
                    if (bin < 0 || bin >= maxy.Length) continue;
                    if (l.y > maxy[bin]) maxy[bin] = l.y;
                }
            }
            for (int i = 0; i < maxy.Length; i++) sb.Append($"z{i * 0.015f:F3}={maxy[i] * 100f:F1} ");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
