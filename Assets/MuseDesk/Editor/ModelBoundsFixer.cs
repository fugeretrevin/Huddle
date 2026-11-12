using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MuseDesk.EditorTools
{
    /// <summary>
    /// Fixes imported FBX models under Assets/MuseDesk/Art/Models/:
    /// 1. OnPreprocessModel enables ModelImporter.isReadable = true so meshes remain readable
    ///    and ensures animation loop flags on character/factory clips are preserved.
    /// 2. OnPostprocessModel bakes Blender's 100x cm-to-m scale and -90 deg X axis conversion
    ///    directly into static mesh vertices and child transforms so every static FBX imports with
    ///    root localScale=(1,1,1), localRotation=identity, child localPositions in real meters,
    ///    and real cm/meter serialized Mesh.bounds (preventing Quest frustum culling).
    /// </summary>
    public class ModelBoundsFixer : AssetPostprocessor
    {
        static readonly HashSet<string> LoopingClips = new HashSet<string>
        {
            "Idle_Breathing", "Idle_LookAround", "Idle_Blink", "Idle_HappySway", "Idle_CuriousTilt",
            "Idle_ShiftWeight", "Walk_Loop", "Sit_Loop", "Research_ReadBook_Loop", "Research_WriteNotes_Loop",
            "Research_InspectBlueprint", "Research_ThinkChinTap", "Dev_Type_Loop", "Dev_Type_Fast_Loop",
            "Dev_MonitorCheck", "Dev_DebugScratchHead", "Test_InspectArtifact_Loop", "Test_ClipboardCheck_Loop",
            "Test_Scan_Loop", "Carry_Walk_Loop", "WaitForUser_Loop", "Approval_Request_Loop",
            "Blocked_Frustrated_Loop", "Thinking_Loop", "Listening_To_User_Loop", "Grabbed_Held_Loop",
            "3_Artifact_Assemble", "5_Artifact_Transport"
        };

        void OnPreprocessModel()
        {
            string p = assetPath.Replace("\\", "/");
            if (!p.Contains("/MuseDesk/Art/Models/")) return;

            var importer = assetImporter as ModelImporter;
            if (importer == null) return;

            importer.isReadable = true;

            bool isAnimated = p.Contains("/Characters/") || p.Contains("/Factory/");
            if (isAnimated)
            {
                var clips = importer.clipAnimations;
                if (clips != null && clips.Length > 0)
                {
                    bool changed = false;
                    for (int i = 0; i < clips.Length; i++)
                    {
                        bool shouldLoop = LoopingClips.Contains(clips[i].name);
                        if (clips[i].loopTime != shouldLoop)
                        {
                            clips[i].loopTime = shouldLoop;
                            changed = true;
                        }
                    }
                    if (changed) importer.clipAnimations = clips;
                }
            }
        }

        Material OnAssignMaterialModel(Material material, Renderer renderer)
        {
            string p = assetPath.Replace("\\", "/");
            if (!p.Contains("/MuseDesk/Art/Models/") || material == null) return null;

            string rawName = material.name;
            int dot = rawName.IndexOf('.');
            if (dot > 0) rawName = rawName.Substring(0, dot);

            string matPath = $"Assets/MuseDesk/Art/Materials/{rawName}.mat";
            var urpMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            return urpMat;
        }

        void OnPostprocessModel(GameObject root)
        {
            string p = assetPath.Replace("\\", "/");
            if (!p.Contains("/MuseDesk/Art/Models/")) return;

            bool isSkinnedRig = p.Contains("/Characters/") || p.Contains("/Factory/") ||
                                root.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;

            int fixedCount = 0;

            if (!isSkinnedRig)
            {
                // Bake the FBX 100x scale and -90 X basis conversion into static meshes and transforms
                // so root and all children have localScale=(1,1,1) and real meter-scale mesh.bounds.
                Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
                var targetPos = new Dictionary<Transform, Vector3>(allTransforms.Length);
                var targetRot = new Dictionary<Transform, Quaternion>(allTransforms.Length);

                foreach (var t in allTransforms)
                {
                    if (t == root.transform)
                    {
                        targetPos[t] = Vector3.zero;
                        targetRot[t] = Quaternion.identity;
                    }
                    else
                    {
                        targetPos[t] = t.position;
                        bool isAnchor = t.GetComponentsInChildren<Renderer>(true).Length == 0;
                        targetRot[t] = isAnchor ? (t.rotation * Quaternion.Euler(90f, 0f, 0f)) : Quaternion.identity;
                    }
                }

                var processedMeshes = new HashSet<Mesh>();
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh;
                    if (m == null || processedMeshes.Contains(m)) continue;
                    processedMeshes.Add(m);

                    Vector3 pivot = mf.transform.position;
                    Vector3[] verts = m.vertices;
                    for (int i = 0; i < verts.Length; i++)
                        verts[i] = mf.transform.TransformPoint(verts[i]) - pivot;
                    m.vertices = verts;

                    Vector3[] norms = m.normals;
                    if (norms != null && norms.Length == verts.Length)
                    {
                        for (int i = 0; i < norms.Length; i++)
                            norms[i] = mf.transform.TransformDirection(norms[i]).normalized;
                        m.normals = norms;
                    }

                    m.RecalculateTangents();
                    m.RecalculateBounds();
                    fixedCount++;
                }

                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;

                foreach (var t in allTransforms)
                {
                    if (t == root.transform) continue;
                    t.localScale = Vector3.one;
                    t.position = targetPos[t];
                    t.rotation = targetRot[t];
                }
            }
            else
            {
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) { mf.sharedMesh.RecalculateBounds(); fixedCount++; }
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (smr.sharedMesh != null) { smr.sharedMesh.RecalculateBounds(); fixedCount++; }
            }

            if (fixedCount > 0)
                Debug.Log($"[ModelBoundsFixer] baked scale/orientation and recomputed bounds on {fixedCount} meshes in {p}");
        }
    }
}
