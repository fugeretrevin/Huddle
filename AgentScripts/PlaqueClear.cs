using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The desk's mode plaque (Panel_Plaque/ModePlaque: the hint line + the SCRIPTED DEMO / LIVE word) sits behind the stamp
/// station; from the seated eye (WorkspaceRoot local (0, 0.30, 0.62)) the stamp domes hide its mode line. Report the sight line
/// from the eye to the mode text against the domes' tops, and (Apply) raise the panel's default pose just enough to clear them.
/// MovablePanel takes its default from the scene pose; a pose the user saved on the headset still wins until Reset layout.
///   PlaqueClear.Report | PlaqueClear.Apply
/// </summary>
public static class PlaqueClear
{
    static Transform Find(string name) => Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == name && t.gameObject.scene.IsValid());

    static float Needed(out string detail)
    {
        Transform ws = Find("WorkspaceRoot"), panel = Find("Panel_Plaque"), mode = Find("ModePlaque");
        TMP_Text modeText = mode != null ? mode.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Mode") : null;
        Vector3 eye = ws.TransformPoint(new Vector3(0f, 0.30f, 0.62f));
        float need = 0f; var sb = new StringBuilder();
        Vector3 textBottom = modeText.transform.position - modeText.transform.up * (modeText.rectTransform.rect.height * 0.5f * modeText.transform.lossyScale.y);
        foreach (string n in new[] { "StampApprove", "StampReject" })
        {
            Transform s = Find(n); if (s == null) continue;
            Bounds b = new Bounds(s.position, Vector3.zero); bool any = false;
            foreach (var r in s.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            // Where the eye -> text-bottom line passes the dome: its height there vs the dome top.
            Vector3 top = new Vector3(b.center.x, b.max.y, b.center.z);
            float t = Mathf.Clamp01(Vector3.Dot(top - eye, textBottom - eye) / (textBottom - eye).sqrMagnitude);
            float lineY = Vector3.Lerp(eye, textBottom, t).y;
            float gap = lineY - top.y;
            sb.Append(n + " top=" + ws.InverseTransformPoint(top).ToString("F3") + " lineY-top=" + gap.ToString("F3") + "  ");
            // Raising the text by d raises the line at that point by d*(t): solve gap + d*t >= 0.01.
            if (gap < 0.01f && t > 0.05f) need = Mathf.Max(need, (0.01f - gap) / t);
        }
        detail = sb + " text bottom wsLocal=" + ws.InverseTransformPoint(textBottom).ToString("F3") + " panel wsLocal=" + ws.InverseTransformPoint(panel.position).ToString("F3");
        return need;
    }

    public static string Report() { string d; float need = Needed(out d); return "raise needed=" + need.ToString("F3") + " m | " + d; }

    public static string Apply()
    {
        string d; float need = Needed(out d);
        if (need <= 0f) return "already clear | " + d;
        need = Mathf.Min(need + 0.004f, 0.06f);
        Transform panel = Find("Panel_Plaque");
        Undo.RecordObject(panel, "Raise plaque");
        panel.position += Vector3.up * need;
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        EditorSceneManager.SaveScene(panel.gameObject.scene);
        string after; float still = Needed(out after);
        return "raised by " + need.ToString("F3") + " m; still needed=" + still.ToString("F3") + " | " + after;
    }
}
