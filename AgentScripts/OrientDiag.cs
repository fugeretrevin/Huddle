using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;
using MuseDesk.Workspace;
public static class OrientDiag {
  public static string Run(){
    EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
    var sb=new StringBuilder();
    var ws=GameObject.Find("WorkspaceRoot").transform;

    // Board
    var board=ws.Find("TaskBoard");
    sb.AppendLine($"TaskBoard: localRot={board.localEulerAngles} hasFaceUser={board.GetComponent<FaceUser>()!=null}");
    var title=board.Find("Title");
    if(title!=null) sb.AppendLine($"  Title fwd(world)={board.Find("Title").forward} (points +Z toward user if z>0)");

    // Desk front: MonitorScreen anchors + a desk child forward
    var desk=ws.Find("DevStation/Workstation");
    var mon=FindDeep(desk,"MonitorScreen_Left"); 
    if(mon!=null) sb.AppendLine($"Dev MonitorScreen_Left localPos={mon.localPosition} worldFwd={mon.forward}");
    sb.AppendLine($"Dev desk worldForward={desk.forward} (its +Z)");

    // Agent colliders (hitboxes)
    foreach(var aName in new[]{"ResearchStation/Agent_Researcher"}){
      var a=ws.Find(aName);
      if(a==null){ sb.AppendLine(aName+" missing"); continue; }
      sb.AppendLine($"== {aName} colliders/physics ==");
      foreach(var c in a.GetComponents<Collider>()) sb.AppendLine($"   Collider {c.GetType().Name} isTrigger={c.isTrigger} enabled={c.enabled} bounds={c.bounds.size}");
      var rb=a.GetComponent<Rigidbody>(); sb.AppendLine("   Rigidbody="+(rb!=null? $"kinematic={rb.isKinematic}":"NONE"));
      foreach(var comp in a.GetComponents<MonoBehaviour>()) sb.AppendLine("   MB "+comp.GetType().Name);
    }
    return sb.ToString();
  }
  static Transform FindDeep(Transform r,string n){foreach(var t in r.GetComponentsInChildren<Transform>(true))if(t.name==n)return t;return null;}
}
