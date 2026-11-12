using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using MuseDesk.Documents;

var sb = new StringBuilder();
static string Path(Transform t){var s=t.name;while(t.parent!=null){t=t.parent;s=t.name+"/"+s;}return s;}

// DecisionDocument hierarchy in the open MainMRScene
var ddc = Object.FindObjectsByType<DocumentDeliveryController>(FindObjectsInactive.Include).FirstOrDefault();
if (ddc != null)
{
    sb.AppendLine("DocumentDeliveryController path=" + Path(ddc.transform));
    sb.AppendLine("  paperRoot=" + (ddc.paperRoot? Path(ddc.paperRoot.transform):"null") + " panel=" + (ddc.panel?"set":"null") + " controller=" + (ddc.controller?"set":"null"));
    var root = ddc.paperRoot != null ? ddc.paperRoot.transform : ddc.transform;
    sb.AppendLine("HIERARCHY of paperRoot:");
    void W(Transform t,int d){ sb.AppendLine(new string(' ',d*2)+t.name+" ls="+t.localScale.ToString("0.00")); for(int i=0;i<t.childCount;i++) W(t.GetChild(i),d+1);}
    W(root,0);
    sb.AppendLine("paperRoot worldScale="+root.lossyScale.ToString("0.000")+" worldPos="+root.position.ToString("0.00"));
}
else sb.AppendLine("no DocumentDeliveryController");

// Prop FBX bounds
foreach (var f in new[]{"Doc_ApprovalStamp","Doc_ApprovalPaper","Doc_Clipboard"})
{
    string p = "Assets/MuseDesk/Art/Models/Documents/"+f+".fbx";
    var go = AssetDatabase.LoadMainAssetAtPath(p) as GameObject;
    if (go==null){ sb.AppendLine(f+": MISSING"); continue; }
    var rends = go.GetComponentsInChildren<Renderer>();
    var b = rends.Length>0? rends[0].bounds : new Bounds();
    foreach(var r in rends) b.Encapsulate(r.bounds);
    sb.AppendLine($"{f}: children=[{string.Join(",", go.GetComponentsInChildren<Transform>().Select(t=>t.name).Take(12))}] size={b.size.ToString("0.000")}");
}
return sb.ToString();
