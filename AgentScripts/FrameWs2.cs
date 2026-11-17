using UnityEngine;
using UnityEditor;
public static class FrameWs2 {
  public static string Run(){
    var ws = GameObject.Find("WorkspaceRoot");
    var sv = SceneView.lastActiveSceneView;
    sv.pivot = ws.transform.position + new Vector3(0,0.12f,0);
    sv.rotation = Quaternion.Euler(14f, 0f, 0f);
    sv.size = 0.42f;
    sv.Repaint();
    return "framed front";
  }
}
