using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;
public static class CheckLayout {
  public static string Run(){
    EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
    var sb=new StringBuilder();
    var ws=GameObject.Find("WorkspaceRoot").transform;
    foreach(var n in new[]{"TaskBoard","SoftwareFactory","Phase6/InboxTray","Phase6/OutboxTray","Phase6/ProjectShelf","Phase6/ArchitectureDiagram","Phase6/FocusToggle","Phase6/ExplainToggle","Phase6/Decor_MiniPlant"}){
      var t=ws.Find(n);
      if(t==null){ sb.AppendLine(n+": MISSING"); continue; }
      var rs=t.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
      string size="(no rend)";
      if(rs.Length>0){ var b=rs[0].bounds; foreach(var r in rs) b.Encapsulate(r.bounds); size=$"size({b.size.x:0.##},{b.size.y:0.##},{b.size.z:0.##})"; }
      sb.AppendLine($"{n}: localPos({t.localPosition.x:0.##},{t.localPosition.y:0.##},{t.localPosition.z:0.##}) scale{t.localScale.x:0.##} world({t.position.x:0.##},{t.position.y:0.##},{t.position.z:0.##}) {size}");
    }
    return sb.ToString();
  }
}
