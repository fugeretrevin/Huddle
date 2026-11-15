using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MuseDesk.Editor.ArtPipeline
{
    public static class ExperiencePolishCapture
    {
        private const string OutDir = "Screenshots/ExperiencePolish";

        [MenuItem("MuseDesk/Art/Capture Experience Polish Verification Shots")]
        public static void CaptureAll()
        {
            Directory.CreateDirectory(OutDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.76f, 0.80f, 0.86f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.60f, 0.64f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.35f, 0.33f, 0.30f, 1f);

            var keyGo = new GameObject("Studio_Key");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.25f;
            key.color = new Color(1.0f, 0.97f, 0.92f);
            keyGo.transform.rotation = Quaternion.Euler(38f, 145f, 0f);

            var fillGo = new GameObject("Studio_Fill");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.color = new Color(0.84f, 0.91f, 1.0f);
            fillGo.transform.rotation = Quaternion.Euler(26f, -45f, 0f);

            var floorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/MuseDesk/Art/Materials/Mat_FeltSlate.mat");
            var deskMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/MuseDesk/Art/Materials/Mat_WarmWood.mat");
            var wallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/MuseDesk/Art/Materials/Mat_WarmBeige.mat");

            MakeBox("Floor", new Vector3(0f, -0.025f, 0f), new Vector3(14f, 0.05f, 6f), floorMat);
            MakeBox("Wall", new Vector3(0f, 1.6f, -0.025f), new Vector3(14f, 3.4f, 0.05f), wallMat);

            var camGo = new GameObject("CaptureCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.20f, 0.23f, 0.28f, 1f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 25f;

            // Warmup frame
            RenderShot(cam, "_warmup.png", new Vector3(0f, 0.4f, 1.5f), Vector3.zero, 256, 256);
            string warmup = Path.Combine(OutDir, "_warmup.png");
            if (File.Exists(warmup)) File.Delete(warmup);

            int missingMats = 0;

            // 1. All 4 mascots close-up lineup (x = -4.0)
            string[] agentPaths =
            {
                "Assets/MuseDesk/Art/Prefabs/Characters/Agent_Researcher.prefab",
                "Assets/MuseDesk/Art/Prefabs/Characters/Agent_Developer.prefab",
                "Assets/MuseDesk/Art/Prefabs/Characters/Agent_Tester.prefab",
                "Assets/MuseDesk/Art/Prefabs/WallHabitat/Agent_Manager.prefab",
            };
            MakeBox("Plinth_Mascots", new Vector3(-4.0f, 0.37f, 0.6f), new Vector3(1.1f, 0.74f, 0.5f), deskMat);
            for (int i = 0; i < agentPaths.Length; i++)
            {
                var inst = SpawnPrefab(agentPaths[i], new Vector3(-4.27f + i * 0.18f, 0.74f, 0.6f), Quaternion.identity, ref missingMats);
                if (inst != null) inst.transform.localScale = Vector3.one;
            }
            RenderShot(cam, "01_Mascots_All4_EarPod_Lineup.png",
                new Vector3(-3.84f, 0.97f, 1.48f), new Vector3(-4.0f, 0.86f, 0.60f), 1280, 760);

            // 2. Stamp Juice & Ink marks on Expanded Paper (x = -1.6)
            MakeBox("Plinth_Stamps", new Vector3(-1.6f, 0.37f, 0.6f), new Vector3(0.8f, 0.74f, 0.5f), deskMat);
            SpawnPrefab("Assets/MuseDesk/Art/Prefabs/VisualPolish/DecisionPaper_Expanded_Polished.prefab",
                new Vector3(-1.60f, 0.742f, 0.60f), Quaternion.identity, ref missingMats);
            SpawnPrefab("Assets/MuseDesk/Art/Prefabs/Documents/Doc_StampInkMark_Approve.prefab",
                new Vector3(-1.68f, 0.752f, 0.61f), Quaternion.Euler(0f, -8f, 0f), ref missingMats);
            SpawnPrefab("Assets/MuseDesk/Art/Prefabs/Documents/Doc_StampInkMark_Reject.prefab",
                new Vector3(-1.52f, 0.752f, 0.61f), Quaternion.Euler(0f, 8f, 0f), ref missingMats);
            SpawnPrefab("Assets/MuseDesk/Art/Prefabs/VisualPolish/Doc_Stamp_Approve_Polished.prefab",
                new Vector3(-1.73f, 0.76f, 0.54f), Quaternion.identity, ref missingMats);
            SpawnPrefab("Assets/MuseDesk/Art/Prefabs/VisualPolish/Doc_Stamp_Reject_Polished.prefab",
                new Vector3(-1.47f, 0.76f, 0.54f), Quaternion.identity, ref missingMats);
            RenderShot(cam, "02_StampJuice_On_DecisionPaper.png",
                new Vector3(-1.60f, 1.06f, 0.94f), new Vector3(-1.60f, 0.75f, 0.59f), 1280, 800);

            // 3. Ceiling Pod + PortalCone Lite check (x = 1.6)
            SpawnPrefab("Assets/MuseDesk/Art/Prefabs/Spatial/Spatial_CeilingPod_Complete.prefab",
                new Vector3(1.60f, 2.35f, 0.75f), Quaternion.identity, ref missingMats);
            var portalConeLite = AssetDatabase.LoadAssetAtPath<Material>("Assets/MuseDesk/Art/Materials/Mat_Spatial_PortalCone_Lite.mat");
            RenderShot(cam, "03_CeilingPod_And_PortalCone_Check.png",
                new Vector3(2.35f, 1.85f, 1.95f), new Vector3(1.60f, 1.98f, 0.75f), 1280, 800);

            // 4. Wall Habitat Seated Eye-Level + DeskVisit + Elevator Floor-Stop (x = 0.0)
            MakeBox("Desk_EyeLevel", new Vector3(0.0f, 0.37f, 0.72f), new Vector3(1.05f, 0.74f, 0.52f), deskMat);
            SpawnPrefab("Assets/MuseDesk/Art/Prefabs/WallHabitat/WallHabitat_Complete_3Tier.prefab",
                new Vector3(0.0f, 1.48f, 0.0f), Quaternion.identity, ref missingMats);
            SpawnPrefab("Assets/MuseDesk/Art/Prefabs/WallHabitat/WallHabitat_DeskVisit_Complete.prefab",
                new Vector3(-0.22f, 0.74f, 0.72f), Quaternion.identity, ref missingMats);
            RenderShot(cam, "04_WallHabitat_SeatedEyeLevel_Check.png",
                new Vector3(-0.35f, 1.24f, 1.52f), new Vector3(0.02f, 1.32f, 0.10f), 1440, 960);

            Debug.Log($"[ExperiencePolishCapture] Captured 4 PNGs in {OutDir}. missingMats={missingMats}, hasPortalConeLite={portalConeLite != null}");
        }

        private static GameObject SpawnPrefab(string path, Vector3 pos, Quaternion rot, ref int missingMats)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (src == null)
            {
                Debug.LogWarning($"[ExperiencePolishCapture] Missing prefab: {path}");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.transform.SetPositionAndRotation(pos, rot);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                    if (m == null) missingMats++;
            }
            return go;
        }

        private static void MakeBox(string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static void RenderShot(Camera cam, string fileName, Vector3 pos, Vector3 lookAt, int width, int height)
        {
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            cam.targetTexture = null;
            RenderTexture.active = prev;
            UnityEngine.Object.DestroyImmediate(rt);

            File.WriteAllBytes(Path.Combine(OutDir, fileName), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
