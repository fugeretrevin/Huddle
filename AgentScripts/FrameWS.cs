using UnityEngine;
using UnityEditor;
public static class FrameWs {
  public static string Run(){
    var ws = GameObject.Find("WorkspaceRoot");
    var sv = SceneView.lastActiveSceneView;
    if (sv==null) return "no sceneview";
    sv.pivot = ws.transform.position + new Vector3(0,0.1f,0);
    sv.rotation = Quaternion.Euler(18f, 180f, 0f);
    sv.size = 0.55f;
    sv.Repaint();
    return "framed";
  }
}
