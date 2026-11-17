using UnityEngine;
using UnityEditor;
public static class FrameP6 {
  public static string Run(){
    var ws = GameObject.Find("WorkspaceRoot");
    var sv = SceneView.lastActiveSceneView;
    sv.pivot = ws.transform.position + new Vector3(0,0.15f,0.05f);
    sv.rotation = Quaternion.Euler(22f, 0f, 0f);
    sv.size = 0.62f;
    sv.Repaint();
    return "framed";
  }
}
