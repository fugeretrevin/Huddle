using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
public static class PerRend {
  public static string Run(){
    var sb=new StringBuilder();
    var fbx=AssetDatabase.LoadMainAssetAtPath("Assets/MuseDesk/Art/Models/Workstations/Workstation_Developer.fbx") as GameObject;
    var inst=Object.Instantiate(fbx);
    var rs=inst.GetComponentsInChildren<Renderer>();
    int zero=0,nonzero=0;
    foreach(var r in rs){ if(r.bounds.size.sqrMagnitude<1e-9f) zero++; else nonzero++; }
    sb.AppendLine($"Workstation_Developer: {rs.Length} renderers, zeroBounds={zero} nonZeroBounds={nonzero}");
    foreach(var r in rs.Take(4)){
      var mf=r.GetComponent<MeshFilter>();
      sb.AppendLine($"  {r.name}: rendBounds={r.bounds.size:0.###} meshLocal={(mf&&mf.sharedMesh?mf.sharedMesh.bounds.size.ToString("0.###"):"n/a")} readable={(mf&&mf.sharedMesh?mf.sharedMesh.isReadable.ToString():"?")}");
    }
    Object.DestroyImmediate(inst);
    return sb.ToString();
  }
}
