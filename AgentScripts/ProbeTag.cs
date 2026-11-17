using System.Linq;
using System.Text;
using UnityEngine;

var cam = Camera.main;
var sb = new StringBuilder();
sb.AppendLine("Camera.main=" + (cam ? cam.name + " pos=" + cam.transform.position : "NULL"));
var tags = Object.FindObjectsByType<TMPro.TextMeshPro>(FindObjectsInactive.Include)
    .Where(t => t.gameObject.name == "NameTag").ToList();
foreach (var t in tags)
{
    var face = t.GetComponent<MuseDesk.Workspace.FaceUser>();
    Vector3 toCam = (cam.transform.position - t.transform.position).normalized;
    float dotFwd = Vector3.Dot(t.transform.forward, toCam);   // +1 => +Z faces camera
    // readable face is -Z; we want -Z toward camera => dot(forward,toCam) ~ -1
    sb.AppendLine($"{t.text.Split('\n')[0]}: faceUser.forward={(face? face.forward.ToString():"no comp")} dot(+Z,toCam)={dotFwd:0.00} => readable {(dotFwd<-0.3f?"TOWARD cam (good)":dotFwd>0.3f?"AWAY (mirrored)":"side")}");
}
return sb.ToString();
