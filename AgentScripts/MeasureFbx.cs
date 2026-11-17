using System.Text;
using UnityEngine;
using UnityEditor;

public static class MeasureFbx
{
    static string[] paths = new[]{
        "Assets/MuseDesk/Art/Models/Workstations/Workstation_Research.fbx",
        "Assets/MuseDesk/Art/Models/Workstations/Workstation_Developer.fbx",
        "Assets/MuseDesk/Art/Models/Workstations/Workstation_Testing.fbx",
        "Assets/MuseDesk/Art/Models/Characters/Agent_Researcher.fbx",
        "Assets/MuseDesk/Art/Models/Documents/Doc_InboxTray.fbx",
        "Assets/MuseDesk/Art/Models/Decorations/Decor_ProjectDisplayShelf.fbx",
        "Assets/MuseDesk/Art/Models/Decorations/Decor_MiniPlant.fbx",
        "Assets/MuseDesk/Art/Models/Diagrams/Diagram_FrontendNode.fbx",
        "Assets/MuseDesk/Art/Models/Diagrams/Diagram_ConnectorArrow.fbx",
    };
    public static string Run()
    {
        var sb = new StringBuilder();
        foreach (var p in paths)
        {
            var fbx = AssetDatabase.LoadMainAssetAtPath(p) as GameObject;
            if (fbx == null) { sb.AppendLine(p+" : NULL"); continue; }
            var inst = Object.Instantiate(fbx);
            var rs = inst.GetComponentsInChildren<Renderer>();
            if (rs.Length==0) { sb.AppendLine(System.IO.Path.GetFileName(p)+" : no renderers"); Object.DestroyImmediate(inst); continue; }
            var b = rs[0].bounds;
            for (int i=1;i<rs.Length;i++) b.Encapsulate(rs[i].bounds);
            sb.AppendLine($"{System.IO.Path.GetFileName(p),-34} size=({b.size.x:0.###},{b.size.y:0.###},{b.size.z:0.###}) center=({b.center.x:0.###},{b.center.y:0.###},{b.center.z:0.###})");
            Object.DestroyImmediate(inst);
        }
        return sb.ToString();
    }
}
