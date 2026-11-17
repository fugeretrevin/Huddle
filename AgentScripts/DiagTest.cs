using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Diagrams;
public static class DiagTest {
  public static string Run(){
    EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
    var diag = Object.FindAnyObjectByType<ArchitectureDiagramController>(FindObjectsInactive.Include);
    if(diag==null) return "no diagram controller";
    diag.gameObject.SetActive(true);
    diag.ShowExample();
    var sb=new StringBuilder();
    int nodes=0,conn=0,label=0;
    foreach(Transform c in diag.contentRoot){
      if(c.name.StartsWith("Node_")) nodes++;
      else if(c.name=="Connector") conn++;
      else if(c.name=="Label") label++;
    }
    sb.AppendLine($"content children: nodes={nodes} connectors={conn} labels={label}");
    var sel = diag.SelectNode(2);
    sb.AppendLine("SelectNode(2) details: " + (sel??"null"));
    // do NOT save; this was a runtime assembly check
    return sb.ToString();
  }
}
