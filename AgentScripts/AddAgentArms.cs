using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;

/// <summary>
/// Gives each primitive agent little shoulder-pivoted arms with cream "mitten" hands, and wires
/// AgentView.movement/armL/armR so the new walk cycle (bounce + opposed arm swing) can play while
/// the agent strolls up. Idempotent: re-running only (re)wires references if the arms already exist.
/// Still placeholder art — real rigged models remain a later swap — but reads as a character walking.
/// </summary>
public static class AddAgentArms
{
    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder();

        var urp = Shader.Find("Universal Render Pipeline/Lit");
        var sleeve = new Material(urp) { name = "AgentArm" };
        sleeve.SetColor("_BaseColor", new Color(0.82f, 0.80f, 0.86f));
        var mitten = new Material(urp) { name = "AgentHand" };
        mitten.SetColor("_BaseColor", new Color(0.97f, 0.90f, 0.78f));

        foreach (var view in scene.GetRootGameObjects()
                     .SelectMany(r => r.GetComponentsInChildren<AgentView>(true)))
        {
            var bob = view.bob != null ? view.bob : view.transform;

            view.movement = view.GetComponent<AgentMovement>();

            var l = bob.Find("Arm_L");
            var r = bob.Find("Arm_R");
            if (l == null) l = MakeArm(bob, "Arm_L", -0.038f, sleeve, mitten);
            if (r == null) r = MakeArm(bob, "Arm_R", 0.038f, sleeve, mitten);
            view.armL = l;
            view.armR = r;

            EditorUtility.SetDirty(view);
            sb.AppendLine($"{view.agentId}: arms wired (movement={view.movement != null})");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved");
        return sb.ToString();
    }

    // Pivot sits at the shoulder; the arm mesh + hand hang below so an X-rotation swings it.
    static Transform MakeArm(Transform bob, string name, float x, Material sleeve, Material mitten)
    {
        var pivot = new GameObject(name).transform;
        pivot.SetParent(bob, false);
        pivot.localPosition = new Vector3(x, 0.088f, 0f);

        var arm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        arm.name = "ArmMesh";
        arm.transform.SetParent(pivot, false);
        arm.transform.localPosition = new Vector3(0f, -0.028f, 0f);
        arm.transform.localScale = new Vector3(0.012f, 0.026f, 0.012f);
        Strip(arm); Paint(arm, sleeve);

        var hand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hand.name = "Hand";
        hand.transform.SetParent(pivot, false);
        hand.transform.localPosition = new Vector3(0f, -0.055f, 0f);
        hand.transform.localScale = Vector3.one * 0.016f;
        Strip(hand); Paint(hand, mitten);

        return pivot;
    }

    static void Strip(GameObject go)
    {
        var c = go.GetComponent<Collider>(); if (c != null) Object.DestroyImmediate(c);
    }

    static void Paint(GameObject go, Material m)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend != null) rend.sharedMaterial = m;
    }
}
