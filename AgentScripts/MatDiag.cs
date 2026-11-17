using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;
public static class MatDiag {
  public static string Run(){
    EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
    var sb=new StringBuilder();
    var ws=GameObject.Find("WorkspaceRoot").transform;
    Report(ws.Find("ResearchStation/Agent_Researcher"), "AGENT", sb);
    Report(ws.Find("DevStation/Workstation"), "DESK", sb);
    Report(ws.Find("Phase6/Decor_MiniPlant"), "DECOR", sb);
    Report(ws.Find("TaskBoard"), "BOARD", sb);
    Report(ws.Find("SoftwareFactory"), "FACTORY", sb);
    return sb.ToString();
  }
  static void Report(Transform t, string tag, StringBuilder sb){
    if(t==null){ sb.AppendLine(tag+": NULL"); return; }
    var r = t.GetComponentsInChildren<Renderer>(true).FirstOrDefault();
    if(r==null){ sb.AppendLine(tag+": no renderer"); return; }
    var m = r.sharedMaterial;
    string meshBounds="n/a"; 
    var mf=r.GetComponent<MeshFilter>(); if(mf&&mf.sharedMesh) meshBounds=mf.sharedMesh.bounds.size.ToString("0.###");
    var smr=r as SkinnedMeshRenderer; if(smr&&smr.sharedMesh) meshBounds="skinned "+smr.sharedMesh.bounds.size.ToString("0.###");
    sb.AppendLine($"{tag}: layer={LayerMask.LayerToName(t.gameObject.layer)}({t.gameObject.layer}) rend={r.GetType().Name} shader='{(m!=null?m.shader.name:"NULL MAT")}' matName={(m!=null?m.name:"-")} meshLocalBounds={meshBounds}");
  }
}
