using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

const string path = "Assets/MuseDesk/Art/Models/Characters/Agent_Researcher.fbx";
var sb = new StringBuilder();

var importer = (ModelImporter)AssetImporter.GetAtPath(path);
sb.AppendLine("animationType=" + importer.animationType);
var takes = importer.importedTakeInfos;
sb.AppendLine("importedTakeInfos.count=" + (takes == null ? 0 : takes.Length));
if (takes != null)
    foreach (var t in takes)
        sb.AppendLine($"  TAKE {t.name} [{t.startTime:0.###}..{t.stopTime:0.###}] sr={t.sampleRate}");

var clips = importer.clipAnimations;
sb.AppendLine("clipAnimations.count=" + (clips == null ? 0 : clips.Length));

// Sub-assets (mesh, avatar, clips)
var subs = AssetDatabase.LoadAllAssetsAtPath(path);
sb.AppendLine("subAssets:");
foreach (var s in subs.Where(x => x != null))
    sb.AppendLine($"  {s.GetType().Name}: {s.name}");

// Hierarchy of the imported prefab (bones + renderers + attach points)
var go = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
if (go != null)
{
    sb.AppendLine("HIERARCHY:");
    void Walk(Transform t, int d)
    {
        string tag = "";
        if (t.GetComponent<SkinnedMeshRenderer>() != null) tag += " [SMR]";
        if (t.GetComponent<MeshRenderer>() != null) tag += " [MR]";
        sb.AppendLine(new string(' ', d * 2) + t.name + tag);
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), d + 1);
    }
    Walk(go.transform, 0);
    var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
    if (smr != null)
        sb.AppendLine("SMR bones=" + smr.bones.Length + " mesh=" + (smr.sharedMesh ? smr.sharedMesh.name : "null") + " tris=" + (smr.sharedMesh ? smr.sharedMesh.triangles.Length / 3 : 0));
}
return sb.ToString();
