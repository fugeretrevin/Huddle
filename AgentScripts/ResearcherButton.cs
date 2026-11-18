#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Edit-mode, read-only: Researcher model parts near the chest (name, parent, renderer, material, bounds).</summary>
public static class ResearcherButton
{
    public static string Run()
    {
        var sb = new StringBuilder();
        foreach (string path in new[] { "Assets/MuseDesk/Art/Models/Characters/Agent_Researcher.fbx" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) { sb.Append("missing " + path + "\n"); continue; }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                string mats = "";
                foreach (var m in r.sharedMaterials) mats += (m != null ? m.name : "null") + ",";
                var mf = r.GetComponent<MeshFilter>();
                var smr = r as SkinnedMeshRenderer;
                Mesh mesh = mf != null ? mf.sharedMesh : smr != null ? smr.sharedMesh : null;
                sb.Append(r.name).Append(" parent=").Append(r.transform.parent != null ? r.transform.parent.name : "-")
                  .Append(" type=").Append(r.GetType().Name).Append(" mats=").Append(mats)
                  .Append(" subMeshes=").Append(mesh != null ? mesh.subMeshCount : 0)
                  .Append(" boundsC=").Append(r.bounds.center.ToString("F3")).Append(" size=").Append(r.bounds.size.ToString("F3")).Append('\n');
            }
        }
        return sb.ToString();
    }
}
#endif
