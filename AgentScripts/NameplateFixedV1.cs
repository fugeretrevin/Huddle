using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Owner (worn rc17): "the name tags clip through things when they turn so maybe they can be stationary just facing forward
/// on the desk not at weird tilts". Each desk nameplate stops turning (AgentNameplate.faceViewer = false) and rests square to
/// its desk's front edge (readable side toward the user, +Z), leaning back a fixed LeanDeg like a real desk nameplate, and a
/// little proud of the desk front so nothing on the desk (the Researcher's whiteboard) reaches it.
///   unity command script run --file AgentScripts/NameplateFixedV1.cs --entry NameplateFixedV1.Apply --args '[true]'
/// </summary>
public static class NameplateFixedV1
{
    const float LeanDeg = 12f;
    const float EdgeZ = 0.136f;   // Workstation-local z of the plate (desk top spans z 0..0.114; was 0.128)

    public static string Apply(bool save)
    {
        var sb = new StringBuilder();
        Transform ws = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "WorkspaceRoot" && t.gameObject.scene.IsValid());
        if (ws == null) return "no WorkspaceRoot";
        var map = new[] { new[] { "ResearchStation", "Nameplate_Researcher" }, new[] { "DevStation", "Nameplate_Developer" }, new[] { "QAStation", "Nameplate_Tester" } };
        foreach (var m in map)
        {
            Transform wk = ws.Find(m[0] + "/Workstation"), plate = ws.Find("OfficeV2/" + m[1]);
            if (wk == null || plate == null) { sb.AppendLine(m[1] + ": missing"); continue; }
            var np = plate.GetComponent<MuseDesk.Workspace.AgentNameplate>();
            Undo.RecordObject(plate, "Plate"); if (np != null) Undo.RecordObject(np, "Plate");
            Vector3 edge = wk.TransformPoint(new Vector3(0f, 0f, EdgeZ));
            plate.position = new Vector3(edge.x, plate.position.y, edge.z);
            // Readable face = the side opposite the plate's forward: forward points away from the user (-Z of the office).
            Vector3 away = -wk.forward; away.y = 0f;
            plate.rotation = Quaternion.LookRotation(away.normalized, ws.up) * Quaternion.Euler(LeanDeg, 0f, 0f);
            if (np != null) { np.faceViewer = false; EditorUtility.SetDirty(np); }
            sb.AppendLine(m[1] + ": pos " + ws.InverseTransformPoint(plate.position).ToString("F3") + " euler " + plate.localEulerAngles.ToString("F0") + " faceViewer=" + (np != null ? np.faceViewer.ToString() : "-"));
        }
        if (save)
        {
            EditorSceneManager.MarkSceneDirty(ws.gameObject.scene);
            EditorSceneManager.SaveScene(ws.gameObject.scene);
            sb.AppendLine("saved");
        }
        return sb.ToString();
    }
}
