using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Documents;

/// <summary>
/// Art handoff P0 (RC8_ART_INTEGRATION_HANDOFF.md): HeldPaperController.agentHoldPoint ("PaperHoldPoint" under the
/// Researcher) sits 5.5 cm to one side, so the offered paper floats beside the outstretched hands (off-centre for
/// the user, 1.6 mm from the Developer's PC tower). Centre it on the agent's midline (local x = 0). Height and reach
/// (y, z) are kept: they were validated with the held offer pose (OfferHoldDrive).
/// Idempotent. Revert state is written once to Library/PaperHoldCenterV1.json.
///   scripts\run_guarded.ps1 -File AgentScripts\PaperHoldCenterV1.cs -Entry PaperHoldCenterV1.Apply
/// </summary>
public static class PaperHoldCenterV1
{
    const string StateFile = "Library/PaperHoldCenterV1.json";

    [System.Serializable] class State { public float x, y, z; }

    static Transform HoldPoint(out string why)
    {
        why = null;
        var paper = Object.FindFirstObjectByType<HeldPaperController>(FindObjectsInactive.Include);
        if (paper == null) { why = "no HeldPaperController in the scene"; return null; }
        var t = paper.agentHoldPoint;
        if (t == null) why = "HeldPaperController.agentHoldPoint is empty";
        return t;
    }

    public static string Apply()
    {
        var sb = new StringBuilder("PaperHoldCenterV1.Apply\n");
        string why;
        Transform t = HoldPoint(out why);
        if (t == null) return sb.Append("  ").Append(why).ToString();
        Vector3 p = t.localPosition;
        sb.AppendLine($"  {t.name} under {(t.parent != null ? t.parent.name : "(root)")}: local {p.x:F3}, {p.y:F3}, {p.z:F3}");
        if (!File.Exists(StateFile)) File.WriteAllText(StateFile, JsonUtility.ToJson(new State { x = p.x, y = p.y, z = p.z }));
        if (Mathf.Abs(p.x) < 1e-4f) return sb.AppendLine("  already centred; nothing to do").ToString();
        Undo.RecordObject(t, "Centre PaperHoldPoint");
        t.localPosition = new Vector3(0f, p.y, p.z);
        sb.AppendLine($"  -> local 0.000, {p.y:F3}, {p.z:F3}");
        var scene = t.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("  scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }

    public static string Revert()
    {
        if (!File.Exists(StateFile)) return "PaperHoldCenterV1.Revert: no saved state; nothing to do";
        var s = JsonUtility.FromJson<State>(File.ReadAllText(StateFile));
        string why;
        Transform t = HoldPoint(out why);
        if (t == null) return "PaperHoldCenterV1.Revert: " + why;
        Undo.RecordObject(t, "Restore PaperHoldPoint");
        t.localPosition = new Vector3(s.x, s.y, s.z);
        EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        return $"PaperHoldCenterV1.Revert: restored {s.x:F3}, {s.y:F3}, {s.z:F3}; saved=" + EditorSceneManager.SaveScene(t.gameObject.scene);
    }
}
