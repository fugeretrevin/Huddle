using System.Text;
using UnityEngine;
using UnityEditor;
public static class VertCheck {
  public static string Run(){
    var sb=new StringBuilder();
    foreach(var p in new[]{
      "Assets/MuseDesk/Art/Models/Workstations/Workstation_Developer.fbx",
      "Assets/MuseDesk/Art/Models/Decorations/Decor_MiniPlant.fbx",
      "Assets/MuseDesk/Art/Models/Documents/Doc_InboxTray.fbx",
      "Assets/MuseDesk/Art/Models/Diagrams/Diagram_FrontendNode.fbx"}){
      var fbx=AssetDatabase.LoadMainAssetAtPath(p) as GameObject;
      var inst=Object.Instantiate(fbx);
      var mfs=inst.GetComponentsInChildren<MeshFilter>();
      int tot=0; Bounds enc=new Bounds();
      bool first=true;
      foreach(var mf in mfs){ if(mf.sharedMesh==null)continue; tot+=mf.sharedMesh.vertexCount; var b=mf.sharedMesh.bounds; if(first){enc=b;first=false;} else enc.Encapsulate(b); }
      sb.AppendLine($"{System.IO.Path.GetFileName(p)}: meshes={mfs.Length} totalVerts={tot} meshBoundsEncLocal={enc.size:0.###}");
      // test recalc on first mesh
      var m0=mfs.Length>0?mfs[0].sharedMesh:null;
      if(m0!=null){ var before=m0.bounds.size; m0.RecalculateBounds(); sb.AppendLine($"   mesh0 '{m0.name}' isReadable={m0.isReadable} boundsBefore={before:0.###} afterRecalc={m0.bounds.size:0.###}"); }
      Object.DestroyImmediate(inst);
    }
    return sb.ToString();
  }
}
