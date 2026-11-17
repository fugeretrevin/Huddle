using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>Read-only: for each poke button, which way its press surface and its label face,
/// relative to WorkspaceRoot (+Z = toward the user) and how its label text is laid out.</summary>
public static class ButtonProbe
{
    public static string Run()
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var ws = all.First(t => t.name == "WorkspaceRoot");
        var eye = ws.TransformPoint(new Vector3(0f, 0.30f, 0.62f));
        var sb = new StringBuilder();
        foreach (var t in all)
        {
            if (!t.GetComponents<MonoBehaviour>().Any(m => m != null && m.GetType().Name == "PokeInteractable")) continue;
            Vector3 toEye = (eye - t.position).normalized;
            sb.AppendLine($"== {t.parent?.name}/{t.name} active={t.gameObject.activeInHierarchy} localEuler={t.localEulerAngles} dot(-fwd,toEye)={Vector3.Dot(-t.forward, toEye):F2}");
            foreach (var c in t.GetComponentsInChildren<Transform>(true))
            {
                if (c == t) continue;
                var comps = string.Join(",", c.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name));
                var tmp = c.GetComponent<TMP_Text>();
                string extra = "";
                if (tmp != null)
                {
                    var rt = c as RectTransform;
                    extra = $" TEXT=\"{tmp.text.Replace("\n", "|")}\" size={tmp.fontSize} wrap={tmp.textWrappingMode} overflow={tmp.overflowMode} rect={(rt != null ? rt.sizeDelta.ToString() : "n/a")} labelFacesEye={Vector3.Dot(-c.forward, (eye - c.position).normalized):F2}";
                }
                sb.AppendLine($"   {c.name} lp={c.localPosition} le={c.localEulerAngles} ls={c.localScale} [{comps}]{extra}");
            }
        }
        return sb.ToString();
    }
}
