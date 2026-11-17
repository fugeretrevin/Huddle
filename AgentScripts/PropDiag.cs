using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;
public static class PropDiag {
  public static string Run(){
    EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
    var sb=new StringBuilder();
    var prop=GameObject.Find("WorkspaceRoot").transform.Find("PhysicsTray/Prop_Block");
    if(prop==null){ return "no Prop_Block"; }
    Dump(prop, sb, 0);
    return sb.ToString();
  }
  static void Dump(Transform t, StringBuilder sb, int d){
    var comps=string.Join(",", t.GetComponents<Component>().Where(c=>c!=null).Select(c=>c.GetType().Name));
    string extra="";
    var col=t.GetComponent<Collider>(); if(col!=null) extra+=$" [col {col.GetType().Name} trig={col.isTrigger}]";
    var rb=t.GetComponent<Rigidbody>(); if(rb!=null) extra+=$" [rb kin={rb.isKinematic} g={rb.useGravity}]";
    sb.AppendLine(new string(' ',d*2)+$"{t.name} L={LayerMask.LayerToName(t.gameObject.layer)}({t.gameObject.layer}) <{comps}>{extra}");
    foreach(Transform c in t) Dump(c, sb, d+1);
  }
}
