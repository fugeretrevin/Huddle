using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;
public static class DeepDiag {
  public static string Run(){
    var sb=new StringBuilder();
    foreach(var s in EditorBuildSettings.scenes) sb.AppendLine($"  buildscene enabled={s.enabled} {s.path}");
    EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
    var ws=GameObject.Find("WorkspaceRoot");
    sb.AppendLine($"WorkspaceRoot activeSelf={ws.activeSelf} scale={ws.transform.localScale} childCount={ws.transform.childCount}");
    foreach(var sName in new[]{"ResearchStation/Workstation","DevStation/Workstation","QAStation/Workstation","Phase6/InboxTray","Phase6/ProjectShelf","Phase6/Decor_MiniPlant","Phase6/FocusToggle"}){
      var t=ws.transform.Find(sName);
      if(t==null){ sb.AppendLine($"{sName}: NOT FOUND"); continue; }
      bool activeInH=t.gameObject.activeInHierarchy;
      var rends=t.GetComponentsInChildren<Renderer>(true);
      sb.AppendLine($"{sName}: activeInHierarchy={activeInH} worldPos=({t.position.x:0.##},{t.position.y:0.##},{t.position.z:0.##}) rends={rends.Length}");
      foreach(var r in rends.Take(3)){
        string mesh="?";
        var mf=r.GetComponent<MeshFilter>(); if(mf!=null&&mf.sharedMesh!=null) mesh=mf.sharedMesh.name;
        var smr=r as SkinnedMeshRenderer; if(smr!=null&&smr.sharedMesh!=null) mesh="skinned:"+smr.sharedMesh.name;
        sb.AppendLine($"     [{r.GetType().Name}] enabled={r.enabled} mesh={mesh} boundsSize=({r.bounds.size.x:0.##},{r.bounds.size.y:0.##},{r.bounds.size.z:0.##})");
      }
    }
    return sb.ToString();
  }
}
