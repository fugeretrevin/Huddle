using UnityEngine;
using UnityEditor;
public static class UserPov {
  public static string Run(){
    var ws = GameObject.Find("WorkspaceRoot");
    var sv = SceneView.lastActiveSceneView;
    // User is on +Z after FacingUserYaw (root.forward points at user). Look from +Z toward -Z.
    sv.pivot = ws.transform.position + new Vector3(0,0.1f,0.4f);
    sv.rotation = Quaternion.Euler(8f, 180f, 0f);
    sv.size = 0.5f;
    sv.Repaint();
    return "user pov";
  }
}
