using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>Read-only: for the card template, how far each renderer extends toward the viewer
/// (card-local -Z), to find what covers the text.</summary>
public static class CardDepthProbe
{
    public static string Run()
    {
        var card = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).First(t => t.name == "CardTemplate");
        bool was = card.gameObject.activeSelf;
        card.gameObject.SetActive(true);
        var sb = new StringBuilder();
        foreach (var r in card.GetComponentsInChildren<Renderer>(true))
        {
            var mfp = r.GetComponent<MeshFilter>(); if (mfp == null || mfp.sharedMesh == null) continue; var b = mfp.sharedMesh.bounds;
            float minZ = float.MaxValue, maxZ = float.MinValue, minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var l = card.InverseTransformPoint(r.transform.TransformPoint(c));
                minZ = Mathf.Min(minZ, l.z); maxZ = Mathf.Max(maxZ, l.z);
                minX = Mathf.Min(minX, l.x); maxX = Mathf.Max(maxX, l.x);
                minY = Mathf.Min(minY, l.y); maxY = Mathf.Max(maxY, l.y);
            }
            string mat = r.sharedMaterial != null ? r.sharedMaterial.name + " q=" + r.sharedMaterial.renderQueue : "none";
            sb.AppendLine($"{r.name,-22} front z={minZ * 1000:F1}mm back z={maxZ * 1000:F1}mm  x[{minX * 1000:F0}..{maxX * 1000:F0}] y[{minY * 1000:F0}..{maxY * 1000:F0}]  {mat} enabled={r.enabled} active={r.gameObject.activeInHierarchy}");
        }
        card.gameObject.SetActive(was);
        return sb.ToString();
    }
}
