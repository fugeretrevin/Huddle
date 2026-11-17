using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;
public static class HandDiag {
  public static string Run(){
    EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
    var sb=new StringBuilder();
    // find hand physics push objects
    var all=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    var pushers=all.Where(t=>t.name.Contains("HandPhysicsPush")||t.name.Contains("PhysicsCapsule")||t.name.Contains("HandPhysics")).Take(6).ToArray();
    sb.AppendLine("push-related objects: "+pushers.Length);
    foreach(var t in pushers){
      var cols=t.GetComponentsInChildren<Collider>(true);
      sb.AppendLine($"  {t.name} layer={LayerMask.LayerToName(t.gameObject.layer)}({t.gameObject.layer}) childColliders={cols.Length}");
      foreach(var c in cols.Take(3)) sb.AppendLine($"      col {c.GetType().Name} isTrigger={c.isTrigger} layer={LayerMask.LayerToName(c.gameObject.layer)}({c.gameObject.layer}) rb={(c.attachedRigidbody!=null? (c.attachedRigidbody.isKinematic?"kinematic":"dynamic"):"none")}");
    }
    // agent collider layer
    var ag=GameObject.Find("WorkspaceRoot").transform.Find("ResearchStation/Agent_Researcher");
    var ac=ag.GetComponent<Collider>();
    sb.AppendLine($"AGENT collider: isTrigger={ac.isTrigger} layer={LayerMask.LayerToName(ag.gameObject.layer)}({ag.gameObject.layer}) height~0.21 radius0.055");
    // Physics matrix: does agent layer collide with pusher layer?
    int agl=ag.gameObject.layer;
    for(int i=0;i<32;i++){ if(!Physics.GetIgnoreLayerCollision(agl,i)){ var n=LayerMask.LayerToName(i); if(!string.IsNullOrEmpty(n)) sb.Append(n+" "); } }
    sb.Insert(sb.Length, "\n^layers that COLLIDE with agent layer");
    return sb.ToString();
  }
}
