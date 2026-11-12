using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wires the "squishy interactive agent" feel onto all three agents, idempotently:
///   - Body lean on push: AgentTilt on each agent root (complements the existing RubberBandReturn
///     displace+springback; shares the root trigger sphere).
///   - Nudgeable arms: AgentArmNudge on each arm pivot (Arm_L / Arm_R), each with its own trigger
///     collider + kinematic Rigidbody so the arm receives its own trigger callbacks.
/// Everything is procedural/kinematic; nothing here can make an agent fall, drift, or leave its spot.
///
/// Mirrors the reflection style of FixPass3 / FixPass6: MuseDesk runtime types are resolved by name
/// from the loaded assemblies (this builder lives outside Assets and cannot reference them directly).
/// Per the project gotcha, we NEVER use `??` with GetComponent/AddComponent (it bypasses Unity's
/// fake-null) — always explicit `if (x == null) x = AddComponent(...)`.
/// </summary>
public static class BuildInteractiveAgents
{
    static GameObject[] _all;

    static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
        .FirstOrDefault(t => t.Name == n);

    static GameObject Find(string name) => _all.FirstOrDefault(g => g.name == name);

    static readonly string[] Agents = { "Agent_Researcher", "Agent_Developer", "Agent_Tester" };
    static readonly string[] ArmNames = { "Arm_L", "Arm_R" };

    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        _all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToArray();
        var sb = new StringBuilder();

        var tiltType  = T("AgentTilt");
        var nudgeType = T("AgentArmNudge");
        var rbrType   = T("RubberBandReturn");
        var amType    = T("AgentMovement");

        if (tiltType == null)  sb.AppendLine("WARN: AgentTilt type not found (did Assets recompile?)");
        if (nudgeType == null) sb.AppendLine("WARN: AgentArmNudge type not found (did Assets recompile?)");
        if (amType == null)    sb.AppendLine("WARN: AgentMovement type not found");

        int agentsDone = 0, armsDone = 0;

        foreach (var agentName in Agents)
        {
            var root = Find(agentName);
            if (root == null) { sb.AppendLine($"{agentName}: NOT FOUND, skipped"); continue; }

            // AgentMovement on the root (used to yield while walking).
            Component movement = amType != null ? root.GetComponent(amType) : null;

            // --- Root body: ensure kinematic Rigidbody + trigger sphere so hand capsules fire triggers. ---
            var rb = root.GetComponent<Rigidbody>();
            if (rb == null) rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var sphere = root.GetComponents<SphereCollider>().FirstOrDefault(c => c.isTrigger);
            if (sphere == null)
            {
                sphere = root.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = 0.09f;
                sphere.center = new Vector3(0f, 0.08f, 0f);
            }

            // --- Ensure RubberBandReturn (existing push+springback) is present + wired. ---
            if (rbrType != null)
            {
                var rbr = root.GetComponent(rbrType);
                if (rbr == null) rbr = root.AddComponent(rbrType);
                SetField(rbrType, rbr, "movement", movement);
            }

            // --- Body lean on push: AgentTilt. ---
            Transform bob = FindChild(root.transform, "Bob");
            if (bob == null) bob = root.transform;   // degrade gracefully

            if (tiltType != null)
            {
                var tilt = root.GetComponent(tiltType);
                if (tilt == null) tilt = root.AddComponent(tiltType);
                SetField(tiltType, tilt, "movement", movement);
                SetField(tiltType, tilt, "bob", bob);
            }

            // --- Nudgeable arms: one AgentArmNudge per arm pivot, each with its own trigger + RB. ---
            foreach (var armName in ArmNames)
            {
                var arm = FindChild(bob, armName);
                if (arm == null) { sb.AppendLine($"{agentName}: {armName} not found, skipped"); continue; }

                // Own kinematic Rigidbody so trigger callbacks route to THIS arm (not the root RB).
                var arb = arm.GetComponent<Rigidbody>();
                if (arb == null) arb = arm.gameObject.AddComponent<Rigidbody>();
                arb.isKinematic = true;
                arb.useGravity = false;

                // Trigger collider hugging the arm (~0.015 radius), reuse an existing trigger if present.
                var trig = arm.GetComponents<Collider>().FirstOrDefault(c => c.isTrigger) as CapsuleCollider;
                if (trig == null)
                {
                    // don't convert a non-trigger collider; add a dedicated trigger capsule.
                    trig = arm.gameObject.AddComponent<CapsuleCollider>();
                }
                trig.isTrigger = true;
                trig.radius = 0.015f;
                trig.height = 0.075f;
                trig.direction = 1;                       // Y axis (arm hangs along local Y)
                trig.center = new Vector3(0f, -0.04f, 0f);

                if (nudgeType != null)
                {
                    var nudge = arm.GetComponent(nudgeType);
                    if (nudge == null) nudge = arm.gameObject.AddComponent(nudgeType);
                    SetField(nudgeType, nudge, "movement", movement);
                }
                armsDone++;
            }

            EditorUtility.SetDirty(root);
            agentsDone++;
            sb.AppendLine($"{agentName}: tilt + arm-nudge wired (movement={(movement != null)}, bob={(bob != root.transform)})");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine($"SAVED. agents={agentsDone}, arms={armsDone}");
        return sb.ToString();
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root == null) return null;
        return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    }

    static void SetField(Type ty, object o, string field, object val)
    {
        if (ty == null || o == null) return;
        var f = ty.GetField(field, BindingFlags.Instance | BindingFlags.Public);
        if (f != null && (val == null || f.FieldType.IsInstanceOfType(val)))
            f.SetValue(o, val);
    }
}
