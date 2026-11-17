using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Owner (worn rc15): "the desks are pointed the wrong way: their chairs and computers face away from them".
/// In the Desk office each Workstation was turned 180 degrees: its chair stood on the USER's side of the desk and its
/// monitors faced the user, while the agent stands behind the desk facing the user. This turns each Workstation to face
/// the same way as its agent (station yaw), and slides it so the chair is just behind the agent and the monitors face
/// the agent across the desk (the user sees the monitor backs and the agent's face over them).
///   unity command script run --file AgentScripts/DeskFacingV1.cs --entry DeskFacingV1.Apply --args '[false]'   (dry run)
///   unity command script run --file AgentScripts/DeskFacingV1.cs --entry DeskFacingV1.Apply --args '[true]'    (save)
/// </summary>
public static class DeskFacingV1
{
    static readonly string[] Stations = { "ResearchStation", "DevStation", "QAStation" };
    const float WorkPointAhead = 0.065f;   // the desk's work point (front edge of the desk, where a sitter's knees go) this far in front of the agent's feet: the agent's body reaches 0.044 m forward

    /// <summary>The desk nameplates (WorkspaceRoot/OfficeV2/Nameplate_*) stood on the OLD desk front edges with hand-set angles.
    /// Put each on its turned desk's front edge (the user's side), centred on the agent, resting square to the seated user's eye
    /// (WorkspaceRoot local (0, 0.30, 0.62)); AgentNameplate then turns it toward the head at run time.</summary>
    public static string Plates(bool save)
    {
        var sb = new StringBuilder();
        Transform ws = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "WorkspaceRoot" && t.gameObject.scene.IsValid());
        if (ws == null) return "no WorkspaceRoot";
        var map = new[] { new[] { "ResearchStation", "Nameplate_Researcher" }, new[] { "DevStation", "Nameplate_Developer" }, new[] { "QAStation", "Nameplate_Tester" } };
        Vector3 eye = ws.TransformPoint(new Vector3(0f, 0.30f, 0.62f));
        foreach (var m in map)
        {
            Transform st = ws.Find(m[0]), wk = st != null ? st.Find("Workstation") : null, plate = ws.Find("OfficeV2/" + m[1]);
            if (wk == null || plate == null) { sb.AppendLine(m[1] + ": missing"); continue; }
            Undo.RecordObject(plate, "Plate");
            Vector3 before = ws.InverseTransformPoint(plate.position);
            // Front edge of the desk top (desk spans work-point local z 0..0.114, top at y 0.093), a little proud of it.
            Vector3 edge = wk.TransformPoint(new Vector3(0f, 0f, 0.128f));
            plate.position = new Vector3(edge.x, plate.position.y, edge.z);
            Vector3 away = plate.position - eye; away.y = 0f;
            if (away.sqrMagnitude > 1e-6f) plate.rotation = Quaternion.LookRotation(away.normalized, ws.up);
            sb.AppendLine(m[1] + ": " + before.ToString("F3") + " -> " + ws.InverseTransformPoint(plate.position).ToString("F3") + " yaw " + plate.localEulerAngles.y.ToString("F0"));
        }
        if (save)
        {
            EditorSceneManager.MarkSceneDirty(ws.gameObject.scene);
            EditorSceneManager.SaveScene(ws.gameObject.scene);
            sb.AppendLine("saved");
        }
        return sb.ToString();
    }

    /// <summary>Turning a desk 180 degrees also swapped its sides (bookshelf, PC tower), which put side pieces next to the
    /// walk lanes (DeskClearanceDrive: 6 of 7 routes under 0.015 m). Mirroring each Workstation in X restores the side
    /// layout the lanes were designed around, keeping the new facing. Unity flips the winding for a negative scale.</summary>
    public static string Mirror(bool on)
    {
        var sb = new StringBuilder();
        Transform ws = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "WorkspaceRoot" && t.gameObject.scene.IsValid());
        foreach (string sName in Stations)
        {
            Transform wk = ws.Find(sName + "/Workstation");
            if (wk == null) continue;
            Undo.RecordObject(wk, "Mirror desk");
            Vector3 s = wk.localScale;
            s.x = (on ? -1f : 1f) * Mathf.Abs(s.x);
            wk.localScale = s;
            sb.AppendLine(sName + " scale " + wk.localScale.ToString("F2"));
        }
        EditorSceneManager.MarkSceneDirty(ws.gameObject.scene);
        if (!Application.isPlaying) EditorSceneManager.SaveScene(ws.gameObject.scene);
        return sb.ToString();
    }

    public static string Apply(bool save)
    {
        var sb = new StringBuilder();
        Transform ws = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "WorkspaceRoot" && t.gameObject.scene.IsValid());
        if (ws == null) return "no WorkspaceRoot";
        foreach (string sName in Stations)
        {
            Transform st = ws.Find(sName);
            Transform wk = st != null ? st.Find("Workstation") : null;
            Transform agent = st != null ? st.Cast<Transform>().FirstOrDefault(c => c.name.StartsWith("Agent_")) : null;
            Transform seat = wk != null ? wk.Find("AgentWorkPosition") : null;
            if (wk == null || agent == null || seat == null) { sb.AppendLine(sName + ": missing parts"); continue; }

            float yawBefore = wk.localEulerAngles.y;
            Undo.RecordObject(wk, "Desk facing");
            // Face the same way as the agent (station local +Z = toward the user).
            Vector3 agentLocal = st.InverseTransformPoint(agent.position);
            wk.localRotation = Quaternion.Euler(0f, agent.localEulerAngles.y, 0f);
            // Put the work point (the sitter's spot at the desk front) just in front of the agent, centred on it.
            Vector3 seatLocal = st.InverseTransformPoint(seat.position);
            Vector3 want = new Vector3(agentLocal.x, seatLocal.y, agentLocal.z + WorkPointAhead);
            wk.localPosition += want - seatLocal;

            // Clearance: the desk's (non-chair) renderers vs the agent body, along station Z.
            float deskNear = float.MaxValue, chairFar = float.MinValue;
            foreach (var r in wk.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.bounds;
                float zMin = st.InverseTransformPoint(b.center - st.forward * b.extents.z).z;
                float zMax = st.InverseTransformPoint(b.center + st.forward * b.extents.z).z;
                if (r.name.ToLowerInvariant().Contains("chair")) chairFar = Mathf.Max(chairFar, zMax);
                else deskNear = Mathf.Min(deskNear, Mathf.Min(zMin, zMax));
            }
            float agentFront = float.MinValue;
            foreach (var r in agent.GetComponentsInChildren<Renderer>(true))
            {
                Bounds b = r.bounds;
                agentFront = Mathf.Max(agentFront, st.InverseTransformPoint(b.center + st.forward * b.extents.z).z);
            }
            sb.AppendLine(sName + ": yaw " + yawBefore.ToString("F0") + " -> " + wk.localEulerAngles.y.ToString("F0")
                          + " pos=" + wk.localPosition.ToString("F3") + " agentZ=" + agentLocal.z.ToString("F3")
                          + " agentFront=" + agentFront.ToString("F3") + " deskNear=" + deskNear.ToString("F3")
                          + " gap=" + (deskNear - agentFront).ToString("F3") + " chairFar=" + chairFar.ToString("F3"));
            if (!save) { Undo.PerformUndo(); }
        }
        if (save)
        {
            EditorSceneManager.MarkSceneDirty(ws.gameObject.scene);
            EditorSceneManager.SaveScene(ws.gameObject.scene);
            sb.AppendLine("saved " + ws.gameObject.scene.path);
        }
        return sb.ToString();
    }
}
