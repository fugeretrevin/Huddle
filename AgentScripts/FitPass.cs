using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// (1) Fit each agent's push trigger to the actual body+head (a vertical capsule that hugs the model
/// instead of a mis-sized sphere). (2) Remove the blank held-prop square on the agents. (3) Auto-size
/// the task board text so it stops overflowing.
/// </summary>
public static class FitPass
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();

        // ---- Board text: auto-size so nothing overflows (do this BEFORE any destroys) ----
        var board = all.FirstOrDefault(t => t.name == "TaskBoard");
        if (board != null)
        {
            int bn = 0;
            foreach (var tmp in board.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name.StartsWith("TextMeshPro")))
            {
                var ty = tmp.GetType();
                var auto = ty.GetProperty("enableAutoSizing"); if (auto != null) auto.SetValue(tmp, true);
                var fmin = ty.GetProperty("fontSizeMin"); if (fmin != null) fmin.SetValue(tmp, 0.3f);
                var fmax = ty.GetProperty("fontSizeMax"); if (fmax != null) fmax.SetValue(tmp, 3.0f);
                var wrap = ty.GetProperty("enableWordWrapping"); if (wrap != null) wrap.SetValue(tmp, true);
                var ov = ty.GetProperty("overflowMode"); if (ov != null) { try { ov.SetValue(tmp, System.Enum.ToObject(ov.PropertyType, 0)); } catch { } }
                EditorUtility.SetDirty(tmp); bn++;
            }
            sb.AppendLine($"board: auto-sized {bn} text elements");
        }

        // ---- Agents: fit trigger + remove blank prop ----
        // Resolve all agent refs BEFORE any destroy (so we never re-scan the stale `all` list).
        var agents = new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" }
            .Select(n => all.FirstOrDefault(t => t.name == n)).Where(t => t != null).ToList();
        foreach (var a in agents)
        {
            var agent = a.name;

            // Bounds of Body + Head (world), ignore arms so the capsule hugs the torso/head.
            var rends = a.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.transform.name == "Body" || r.transform.name == "Head").ToList();
            if (rends.Count > 0)
            {
                Bounds b = rends[0].bounds;
                foreach (var r in rends.Skip(1)) b.Encapsulate(r.bounds);
                // Convert world bounds -> agent-local for the capsule on the root.
                Vector3 localCenter = a.InverseTransformPoint(b.center);
                float ls = Mathf.Max(0.0001f, Mathf.Max(a.lossyScale.x, a.lossyScale.y, a.lossyScale.z));
                float height = b.size.y / ls;
                float radius = Mathf.Max(b.size.x, b.size.z) * 0.5f / ls;

                // Replace the trigger sphere with a fitted trigger capsule.
                foreach (var sphere in a.GetComponents<SphereCollider>().Where(c => c.isTrigger).ToList())
                    Object.DestroyImmediate(sphere);
                var cap = a.GetComponents<CapsuleCollider>().FirstOrDefault(c => c.isTrigger);
                if (cap == null) cap = a.gameObject.AddComponent<CapsuleCollider>();
                cap.isTrigger = true; cap.direction = 1; // Y
                cap.center = localCenter; cap.height = height + radius; cap.radius = radius;
                sb.AppendLine($"{agent}: fitted capsule center={localCenter} h={cap.height:F3} r={cap.radius:F3}");
            }

            // Remove the blank held-prop square.
            foreach (var prop in a.GetComponentsInChildren<Transform>(true).Where(t => t.name == "HeldProp").ToList())
            { sb.AppendLine($"removed {prop.name} on {agent}"); Object.DestroyImmediate(prop.gameObject); }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("SAVED.");
        return sb.ToString();
    }
}
