using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using MuseDesk.Agents;
using MuseDesk.Networking;
using MuseDesk.Workspace;

/// <summary>
/// "The characters respond to the person" pass. Adds and wires, in the OPEN scene
/// (Assets/MuseDesk/Scenes/MainMRScene.unity):
///
///   - an AgentPresence on each agent (next to its AgentView), with the rig bones it layers on
///     assigned by name, and a child "PokeVolume" (trigger capsule around head and body, on the
///     Ignore Raycast layer, a child of the agent root and not of a bone);
///   - WorkspaceRoot/AgentTalk with, per agent, "Bubble_<Role>" (AgentSpeechBubble: Paper plate,
///     Ink text) and "Chip_<Role>" (AgentStatusChip: dark plate, role strip, On-plate text). They
///     are saved hidden (renderers off) and are placed above the heads at run time; none of them
///     is under a rig bone;
///   - ONE AgentAttention (on the AgentRegistry's object, else the SessionController's) with the
///     session, the office root, the talk root and the three presences;
///   - two materials under Assets/MuseDesk/Art/Materials/UI (AgentTalk_Paper, AgentTalk_Plate:
///     URP Unlit, opaque, two-sided), created only if missing;
///   - switches OFF (does not remove) the older AgentTouchSummary components: the poke bubble
///     replaces the touch card, and both answering one touch would show two signs.
///
/// It moves nothing that exists, deletes nothing and does not touch any other pass's objects.
/// Idempotent: a second run finds everything by name and re-assigns the same references.
/// Saves the scene once. Does nothing in Play Mode.
///
///   BuildAgentInteractionV1.Apply
/// </summary>
public static class BuildAgentInteractionV1
{
    const string MatFolder = "Assets/MuseDesk/Art/Materials/UI";
    const string PaperMatPath = MatFolder + "/AgentTalk_Paper.mat";
    const string PlateMatPath = MatFolder + "/AgentTalk_Plate.mat";
    static readonly string[] FontPaths =
    {
        "Assets/MuseDesk/Art/Fonts/Inter-Medium SDF.asset",
        "Assets/MuseDesk/Art/Fonts/Inter SDF.asset",
    };

    static string PathOf(Transform t)
    {
        if (t == null) return "MISSING";
        var sb = new StringBuilder(t.name);
        for (Transform p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    static string N(Object o) => o != null ? o.name : "MISSING";

    static Transform FindInScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
        return null;
    }

    static Material Mat(string path, Color color, StringBuilder sb)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) { sb.AppendLine("  WARNING: URP Unlit shader not found; no material at " + path); return null; }
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
            sb.AppendLine("  created " + path);
        }
        m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);          // two-sided: a sign is a thin sheet
        m.doubleSidedGI = false;
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Dirty(Object o)
    {
        if (o == null) return;
        EditorUtility.SetDirty(o);
        if (PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o);
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildAgentInteractionV1: stop Play Mode first.\n";
        Scene scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildAgentInteractionV1: no open scene.\n";

        var sb = new StringBuilder();
        sb.AppendLine("== BuildAgentInteractionV1 ==");

        Transform workspace = FindInScene(scene, "WorkspaceRoot");
        var session = Object.FindAnyObjectByType<SessionController>(FindObjectsInactive.Include);
        var registry = Object.FindAnyObjectByType<AgentRegistry>(FindObjectsInactive.Include);
        if (workspace == null) { sb.AppendLine("SKIP: no WorkspaceRoot in the open scene; nothing changed."); return sb.ToString(); }
        if (session == null) sb.AppendLine("WARNING: no SessionController; status sentences will fall back to the agents' own state only.");

        // ── 1. the agents, in pipeline order ──
        var views = Object.FindObjectsByType<AgentView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var ordered = new List<AgentView>();
        for (int step = 1; step <= 3; step++)
            foreach (AgentView v in views)
                if (v != null && AgentStatusSentence.StepOf(v.agentId) == step && !ordered.Contains(v)) ordered.Add(v);
        if (ordered.Count == 0) { sb.AppendLine("SKIP: no AgentView with agentId researcher / developer / tester; nothing changed."); return sb.ToString(); }
        if (ordered.Count != 3) sb.AppendLine($"WARNING: expected 3 agents, found {ordered.Count}.");

        // ── 2. assets ──
        sb.AppendLine("-- assets --");
        if (!AssetDatabase.IsValidFolder(MatFolder)) sb.AppendLine("  WARNING: " + MatFolder + " does not exist; materials cannot be created.");
        Material paper = AssetDatabase.IsValidFolder(MatFolder) ? Mat(PaperMatPath, OfficeTokens.Paper, sb) : null;
        Material plate = AssetDatabase.IsValidFolder(MatFolder) ? Mat(PlateMatPath, OfficeTokens.Plate, sb) : null;
        TMP_FontAsset font = null;
        foreach (string p in FontPaths)
        {
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(p);
            if (font != null) break;
        }
        sb.AppendLine($"  paper={N(paper)} plate={N(plate)} font={(font != null ? font.name : "none found (the coordinator's font pass sets it)")}");

        // ── 3. per agent: presence, poke volume, bubble, chip ──
        sb.AppendLine("-- agents --");
        Transform talkRoot = AgentTalkRig.EnsureRoot(workspace);
        var presences = new List<AgentPresence>();
        foreach (AgentView view in ordered)
        {
            GameObject go = view.gameObject;
            string role = AgentStatusSentence.NormalizeRole(view.agentId);

            AgentPresence presence = go.GetComponent<AgentPresence>();
            bool added = presence == null;
            if (added) presence = Undo.AddComponent<AgentPresence>(go);
            presence.view = view;
            presence.movement = go.GetComponent<AgentMovement>();
            presence.bridge = go.GetComponent<MuseDesk.Agents.AgentAnimatorBridge>();
            presence.gaze = go.GetComponent<AgentGaze>();
            presence.idle = go.GetComponent<AgentIdleLife>();
            presence.personality = role == AgentStatusSentence.Developer ? AgentPresence.Personality.Developer
                                 : role == AgentStatusSentence.Tester ? AgentPresence.Personality.Tester
                                 : AgentPresence.Personality.Researcher;
            presence.ResolveRig();

            CapsuleCollider vol = AgentTalkRig.EnsurePokeVolume(presence);
            presence.bubble = AgentTalkRig.EnsureBubble(talkRoot, role, paper, font);
            presence.chip = AgentTalkRig.EnsureChip(talkRoot, role, plate, font);
            Dirty(presence);
            if (vol != null) { Dirty(vol); Dirty(vol.gameObject); Dirty(vol.transform); }
            Dirty(presence.bubble); Dirty(presence.chip);
            presences.Add(presence);

            // The older touch card answers the same touch: switch it off (kept, not removed).
            var touch = go.GetComponent<AgentTouchSummary>();
            string touchNote = "none";
            if (touch != null)
            {
                touchNote = touch.enabled ? "switched off" : "already off";
                if (touch.enabled) { Undo.RecordObject(touch, "switch off AgentTouchSummary"); touch.enabled = false; Dirty(touch); }
            }

            float height = presence.TopPoint.y - go.transform.position.y;
            Animator anim = presence.bridge != null ? presence.bridge.animator : go.GetComponentInChildren<Animator>(true);
            var nudges = go.GetComponentsInChildren<AgentArmNudge>(true);
            sb.AppendLine($"  {go.name} ({role}): presence {(added ? "added" : "found")}, root scale {go.transform.lossyScale.x:F3}, height {height * 100f:F1} cm");
            sb.AppendLine($"    bones: armature={N(presence.armature)} head={N(presence.head)} headTop={N(presence.headTop)} chest={N(presence.chest)} spine={N(presence.spine)}"
                        + $" armL={N(presence.upperArmL)}/{N(presence.forearmL)}/{N(presence.handL)} armR={N(presence.upperArmR)}/{N(presence.forearmR)}/{N(presence.handR)}");
            if (presence.armature == null) sb.AppendLine("    WARNING: no Armature: no squash and stretch for this agent.");
            if (presence.head == null) sb.AppendLine("    WARNING: no Head bone: signs sit above the root, gaze reactions are off.");
            if (presence.upperArmR == null || presence.forearmR == null || presence.handR == null) sb.AppendLine("    WARNING: right arm bones missing: no wave.");
            if (presence.gaze == null) sb.AppendLine("    WARNING: no AgentGaze: no glance, no head expression.");
            if (presence.idle == null) sb.AppendLine("    NOTE: no AgentIdleLife: the torso turn is written by AgentPresence's own layers.");
            if (anim != null && anim.updateMode != AnimatorUpdateMode.Normal) sb.AppendLine("    WARNING: Animator update mode is not Normal; bone layers need Normal.");
            if (vol != null)
            {
                Vector3 s = vol.transform.lossyScale;
                sb.AppendLine($"    poke volume: height {vol.height * s.y * 100f:F1} cm, radius {vol.radius * Mathf.Max(s.x, s.z) * 100f:F1} cm, layer {LayerMask.LayerToName(vol.gameObject.layer)}, trigger={vol.isTrigger}");
            }
            sb.AppendLine($"    arm nudges on this agent={nudges.Length} (they keep working; the wave layers after them), touch summary: {touchNote}, rigidbody on root={(go.GetComponent<Rigidbody>() != null)}");
            sb.AppendLine($"    signs: {PathOf(presence.bubble.transform)}, {PathOf(presence.chip.transform)}");
        }
        Dirty(talkRoot.gameObject);

        // ── 4. the one AgentAttention ──
        sb.AppendLine("-- attention --");
        var all = Object.FindObjectsByType<AgentAttention>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        AgentAttention attention = all.Length > 0 ? all[0] : null;
        if (all.Length > 1) sb.AppendLine($"  WARNING: {all.Length} AgentAttention components in the scene; using the one on {all[0].name}. Remove the others by hand.");
        if (attention == null)
        {
            GameObject host = registry != null ? registry.gameObject : (session != null ? session.gameObject : workspace.gameObject);
            attention = Undo.AddComponent<AgentAttention>(host);
            sb.AppendLine("  added AgentAttention to " + PathOf(host.transform));
        }
        else sb.AppendLine("  found AgentAttention on " + PathOf(attention.transform) + " (re-wired, tuning left alone)");
        attention.controller = session;
        attention.workspaceRoot = workspace;
        attention.talkRoot = talkRoot;
        attention.agents = presences.ToArray();
        Dirty(attention);
        sb.AppendLine($"  controller={N(session)} workspaceRoot={N(workspace)} talkRoot={PathOf(talkRoot)} agents={presences.Count}");
        sb.AppendLine($"  completionSequence in scene={(Object.FindAnyObjectByType<CompletionSequence>(FindObjectsInactive.Include) != null)} (reactions stand down while it plays)");
        var beacon = Object.FindAnyObjectByType<MuseDesk.Spatial.AttentionBeacon>(FindObjectsInactive.Include);
        sb.AppendLine($"  attention beacon={(beacon != null ? "found, office cue " + N(beacon.officeCue) : "none")} (signs are lifted clear of it and of each agent's '!' mark)");

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }
}
