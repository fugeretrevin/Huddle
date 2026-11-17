using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>rc17 worn-feedback probe: station hierarchy (chairs, monitors), nameplates, beacons, props.</summary>
public static class FeedbackProbe
{
    static string Path(Transform t, Transform root) { var s = t.name; while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; } return s; }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("scene " + SceneManager.GetActiveScene().path + " playing=" + Application.isPlaying);
        Transform ws = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "WorkspaceRoot" && t.gameObject.scene.IsValid());
        if (ws == null) return sb.Append("no WorkspaceRoot").ToString();
        foreach (Transform st in ws)
        {
            sb.AppendLine("ROOT CHILD " + st.name + " active=" + st.gameObject.activeSelf);
            if (!st.name.EndsWith("Station")) continue;
            foreach (var t in st.GetComponentsInChildren<Transform>(true))
            {
                if (t == st) continue;
                int depth = 0; var p = t; while (p.parent != st) { p = p.parent; depth++; }
                if (depth > 3) continue;
                if (t.GetComponentInParent<Animator>(true) != null && t.GetComponent<Animator>() == null && depth > 1) continue;
                var mf = t.GetComponent<MeshFilter>();
                var comps = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
                sb.AppendLine(new string(' ', 2 + depth * 2) + t.name + " lp=" + t.localPosition.ToString("F2") + " le=" + t.localEulerAngles.ToString("F0")
                              + (mf && mf.sharedMesh ? " mesh=" + mf.sharedMesh.name : "") + " [" + comps + "]" + (t.gameObject.activeSelf ? "" : " INACTIVE"));
            }
        }
        return sb.ToString();
    }
}
