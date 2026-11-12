using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ReimportModels
{
    public static string Run()
    {
        var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/MuseDesk/Art/Models" });
        int n = 0;
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp != null)
            {
                imp.isReadable = true;
                imp.SaveAndReimport();
                n++;
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var sb = new StringBuilder();
        sb.AppendLine($"reimported {n} models");

        foreach (var p in new[]
        {
            "Assets/MuseDesk/Art/Models/Workstations/Workstation_Developer.fbx",
            "Assets/MuseDesk/Art/Models/Decorations/Decor_MiniPlant.fbx",
            "Assets/MuseDesk/Art/Models/Documents/Doc_InboxTray.fbx",
            "Assets/MuseDesk/Art/Models/TaskBoard/TaskBoard_KanbanFrame.fbx"
        })
        {
            var subs = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Mesh>().ToArray();
            var imp = AssetImporter.GetAtPath(p) as ModelImporter;
            sb.AppendLine($"{System.IO.Path.GetFileName(p)}: {subs.Length} meshes, importer.isReadable={imp.isReadable}");
            foreach (var m in subs.Take(3))
                sb.AppendLine($"   '{m.name}' readable={m.isReadable} verts={m.vertexCount} serializedBounds={m.bounds.size:0.####}");
        }
        return sb.ToString();
    }
}
