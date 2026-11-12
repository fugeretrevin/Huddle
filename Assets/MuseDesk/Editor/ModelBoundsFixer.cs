using UnityEditor;
using UnityEngine;

namespace MuseDesk.EditorTools
{
    /// <summary>
    /// The Antigravity static-mesh FBX exported with zero/empty mesh bounds. A MeshRenderer whose
    /// mesh has zero bounds is frustum-culled on device (it renders in the Scene view, which can
    /// force-draw, but vanishes on Quest), which made every workstation/decoration/document/diagram
    /// prop invisible on-head while the skinned agents/factory and the procedural board showed fine.
    /// The meshes import with Read/Write disabled, so a runtime RecalculateBounds is a no-op — but
    /// during model import the meshes are mutable, so we recompute bounds here and bake correct
    /// bounds into the imported asset. Scoped to the MuseDesk art library.
    /// </summary>
    public class ModelBoundsFixer : AssetPostprocessor
    {
        void OnPostprocessModel(GameObject root)
        {
            string p = assetPath.Replace("\\", "/");
            if (!p.Contains("/MuseDesk/Art/Models/")) return;

            int fixedCount = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) { mf.sharedMesh.RecalculateBounds(); fixedCount++; }
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null) { smr.sharedMesh.RecalculateBounds(); fixedCount++; }

            if (fixedCount > 0)
                Debug.Log($"[ModelBoundsFixer] recomputed bounds on {fixedCount} meshes in {p}");
        }
    }
}
