using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
public static class MeshBoundsTruth {
  public static string Run(){
    var sb=new StringBuilder();
    foreach(var p in new[]{
      "Assets/MuseDesk/Art/Models/Workstations/Workstation_Developer.fbx",
      "Assets/MuseDesk/Art/Models/Decorations/Decor_MiniPlant.fbx"}){
      var subs=AssetDatabase.LoadAllAssetsAtPath(p).OfType<Mesh>().ToArray();
      sb.AppendLine($"{System.IO.Path.GetFileName(p)}: {subs.Length} mesh sub-assets");
      foreach(var m in subs.Take(5))
        sb.AppendLine($"   '{m.name}' readable={m.isReadable} verts={m.vertexCount} serializedBounds={m.bounds.size:0.####}");
      var imp=AssetImporter.GetAtPath(p) as ModelImporter;
      sb.AppendLine($"   importer.isReadable={imp.isReadable} meshCompression={imp.meshCompression} optimizeMesh={imp.optimizeMeshPolygons}");
    }
    return sb.ToString();
  }
}
