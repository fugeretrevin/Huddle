using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>Read-only: every mesh on the task card template with its enabled flag and its extent in
/// card-local millimetres (front = most negative z), to place text ON the visible surface.</summary>
public static class CardSurfaceProbe
{
    public static string Run()
    {
        var card = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).First(t => t.name == "CardTemplate");
        var sb = new StringBuilder();
        foreach (var r in card.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.GetComponent<TMPro.TMP_Text>() != null) continue;
            var f = r.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null) continue;
            var b = f.sharedMesh.bounds;
            Vector3 a = Vector3.positiveInfinity, z = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var l = card.InverseTransformPoint(r.transform.TransformPoint(p));
                a = Vector3.Min(a, l); z = Vector3.Max(z, l);
            }
            bool selfChainActive = true; var t = r.transform;
            while (t != card) { if (!t.gameObject.activeSelf) selfChainActive = false; t = t.parent; }
            string path = r.transform.parent == card ? r.name : r.transform.parent.name + "/" + r.name;
            sb.AppendLine($"{path,-44} shown={(r.enabled && selfChainActive)} x[{a.x * 1000:F0}..{z.x * 1000:F0}] y[{a.y * 1000:F0}..{z.y * 1000:F0}] front z={a.z * 1000:F1} back z={z.z * 1000:F1} mat={(r.sharedMaterial != null ? r.sharedMaterial.name : "none")}");
        }
        return sb.ToString();
    }
}
