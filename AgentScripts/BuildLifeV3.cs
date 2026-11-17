using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Agents;
using MuseDesk.Feedback;
using MuseDesk.Networking;
using MuseDesk.Workspace;

/// <summary>
/// Final refinement pass, "Life": adds and wires the one new component of this pass,
/// MuseDesk.Agents.CompletionSequence (the delivery payoff and the failure beat), in the OPEN scene.
/// Everything else in the pass is code in components that are already in the scene and needs no
/// scene change.
///
/// What it does:
///   - finds or adds ONE CompletionSequence, on the AgentRegistry's object (else the
///     SessionController's), and assigns its references: SessionController, AgentRegistry,
///     TestingStationController, FactoryEventController, ProjectShelfController,
///     AgentHandoffController. Tuning values already on the component are left alone;
///   - reports, without changing anything, what the animation audit could not read from code:
///     per agent the Animator settings, the serialized blend times, the walk bob, the gaze limits,
///     which bone AgentTilt leans and whether AgentIdleLife owns it, and whether the legacy
///     AgentView.bob / arm fields point at anything.
///
/// It moves nothing, creates no visible object and does not touch the nameplates, the plaque or
/// the guide line. Idempotent: a second run finds the component and re-assigns the same references.
/// Saves the scene once. Does nothing in Play Mode.
///
///   BuildLifeV3.Apply
/// </summary>
public static class BuildLifeV3
{
    static string Name(Object o) => o != null ? o.name : "MISSING";

    static string PathOf(Transform t)
    {
        if (t == null) return "MISSING";
        var sb = new StringBuilder(t.name);
        for (Transform p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildLifeV3: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildLifeV3: no open scene.\n";

        var sb = new StringBuilder();
        sb.AppendLine("== BuildLifeV3 ==");

        var session = Object.FindAnyObjectByType<SessionController>(FindObjectsInactive.Include);
        var registry = Object.FindAnyObjectByType<AgentRegistry>(FindObjectsInactive.Include);
        var handoffs = Object.FindAnyObjectByType<AgentHandoffController>(FindObjectsInactive.Include);
        var station = Object.FindAnyObjectByType<MuseDesk.Art.TestingStationController>(FindObjectsInactive.Include);
        var factory = Object.FindAnyObjectByType<MuseDesk.Art.FactoryEventController>(FindObjectsInactive.Include);
        var shelf = Object.FindAnyObjectByType<ProjectShelfController>(FindObjectsInactive.Include);
        var feedback = Object.FindAnyObjectByType<FeedbackController>(FindObjectsInactive.Include);

        if (session == null)
        {
            sb.AppendLine("SKIP: no SessionController in the scene; nothing changed.");
            return sb.ToString();
        }

        // ── 1. the completion sequence ──
        sb.AppendLine("-- 1. completion sequence --");
        var all = Object.FindObjectsByType<CompletionSequence>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        CompletionSequence seq = all.Length > 0 ? all[0] : null;
        if (all.Length > 1)
            sb.AppendLine($"  WARNING: {all.Length} CompletionSequence components in the scene; using the one on {all[0].name}. Remove the others by hand.");
        if (seq == null)
        {
            GameObject host = registry != null ? registry.gameObject : session.gameObject;
            seq = Undo.AddComponent<CompletionSequence>(host);
            sb.AppendLine($"  added CompletionSequence to {PathOf(host.transform)}");
        }
        else
        {
            Undo.RecordObject(seq, "wire CompletionSequence");
            sb.AppendLine($"  found CompletionSequence on {PathOf(seq.transform)} (re-wired, tuning left alone)");
        }
        seq.controller = session;
        seq.registry = registry;
        seq.testingStation = station;
        seq.factoryEvents = factory;
        seq.shelf = shelf;
        seq.handoffs = handoffs;
        EditorUtility.SetDirty(seq);

        sb.AppendLine($"  controller={Name(session)} registry={Name(registry)} testingStation={Name(station)} factoryEvents={Name(factory)} shelf={Name(shelf)} handoffs={Name(handoffs)}");
        if (registry == null) sb.AppendLine("  WARNING: no AgentRegistry; agents are looked up through AgentGaze at run time instead.");
        if (station == null) sb.AppendLine("  NOTE: no TestingStationController; the sequence skips the lamps.");
        else if (station.testingStation == null) sb.AppendLine("  NOTE: TestingStationController.testingStation is empty; the lamps will not change.");
        if (factory == null) sb.AppendLine("  NOTE: no FactoryEventController; the sequence skips the factory clips.");
        else if (factory.factory == null) sb.AppendLine("  NOTE: FactoryEventController.factory is empty; the factory clips will not play.");
        if (shelf == null) sb.AppendLine("  NOTE: no ProjectShelfController; no shelf token.");
        else
        {
            int slots = shelf.slots != null ? shelf.slots.Count : 0;
            sb.AppendLine($"  shelf: active={shelf.gameObject.activeInHierarchy} enabled={shelf.enabled} slots={slots} tokenPrefab={(shelf.completedTokenPrefab != null ? shelf.completedTokenPrefab.name : "MISSING")} tokenScale={shelf.tokenScale:F3}");
            if (!shelf.gameObject.activeInHierarchy) sb.AppendLine("  NOTE: the shelf is inactive, so it places no token and the sequence has none to reveal.");
        }
        if (feedback == null) sb.AppendLine("  NOTE: no FeedbackController; no delivered cue.");
        else sb.AppendLine($"  feedback: on {feedback.name}, controller={(feedback.controller != null)} (the sequence plays FeedbackCue.Delivered at its token beat; the controller stands down for that cue only)");
        if (handoffs == null) sb.AppendLine("  NOTE: no AgentHandoffController; nothing to retire on delivery.");
        sb.AppendLine($"  timing: handoff wait max {seq.handoffWaitMax:F2}s, acknowledge {seq.acknowledgeDelay:F2}s, factory delivery {seq.factoryDeliveryDelay:F2}s, token + cue {seq.tokenDelay:F2}s, glance {seq.userGlanceDelay:F2}s, total {seq.totalSeconds:F2}s; failure {seq.failureSeconds:F2}s; delivery supersedes handoff={seq.deliverySupersedesHandoff}");

        // ── 2. read-only audit of what the code could not tell ──
        sb.AppendLine("-- 2. agent audit (read-only) --");
        var views = Object.FindObjectsByType<AgentView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var view in views)
        {
            if (view == null) continue;
            GameObject go = view.gameObject;
            var bridge = go.GetComponent<MuseDesk.Agents.AgentAnimatorBridge>();
            var move = go.GetComponent<AgentMovement>();
            var gaze = go.GetComponent<AgentGaze>();
            var life = go.GetComponent<AgentIdleLife>();
            var tilt = go.GetComponent<AgentTilt>();
            var nudges = go.GetComponentsInChildren<AgentArmNudge>(true);
            Animator anim = bridge != null ? bridge.animator : go.GetComponentInChildren<Animator>(true);

            sb.AppendLine($"  {go.name} (agentId '{view.agentId}')");
            if (anim != null)
                sb.AppendLine($"    animator: on {anim.name}, controller={(anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "MISSING")}, updateMode={anim.updateMode}, culling={anim.cullingMode}, applyRootMotion={anim.applyRootMotion}, speed={anim.speed:F2}"
                    + (anim.updateMode != AnimatorUpdateMode.Normal ? "  WARNING: bone layers need update mode Normal" : "")
                    + (anim.applyRootMotion ? "  WARNING: root motion is on; AgentMovement moves the root" : ""));
            else sb.AppendLine("    animator: MISSING");
            if (bridge != null)
                sb.AppendLine($"    bridge blends (serialized): base {bridge.baseBlend:F2}, walk in {bridge.walkInBlend:F2}, walk out {bridge.walkOutBlend:F2}, reaction {bridge.reactionBlend:F2} (code floor 0.20); walk match={bridge.matchWalkToSpeed}, cycle {bridge.walkCycleMeters:F3} m, rate {bridge.walkRateMin:F2}..{bridge.walkRateMax:F2}");
            else sb.AppendLine("    bridge: MISSING (no clip animation for this agent)");
            if (move != null)
                sb.AppendLine($"    movement: travel {move.travelSeconds:F2}s, walk bob {move.walkBobHeight * 1000f:F1} mm at {move.walkBobSpeed:F0} half-cycles per walk"
                    + (bridge != null && move.walkBobHeight > 0.0005f ? "  NOTE: this procedural bob is added on top of the Walk_Loop clip's own hip bounce" : ""));
            if (gaze != null)
                sb.AppendLine($"    gaze: head={Name(gaze.head)} neck={Name(gaze.neck)} maxYaw={gaze.maxYaw:F0} maxPitch={gaze.maxPitch:F0} (hard caps 60 / 30), smooth {gaze.smoothTime:F2}s, workTarget={Name(gaze.workTarget)}, paper={Name(gaze.paper)}");
            else sb.AppendLine("    gaze: MISSING (the sequence cannot make this agent look)");
            if (life != null)
                sb.AppendLine($"    idle life: spine={Name(life.spine)} chest={Name(life.chest)} focusAmount={life.focusAmount:F2}");
            if (tilt != null)
            {
                bool shared = life != null && tilt.bob != null && (tilt.bob == life.spine || tilt.bob == life.chest);
                sb.AppendLine($"    tilt: bone={(tilt.bob != null ? tilt.bob.name : "(found at run time: Chest, Bob, then Spine)")}"
                    + (shared ? " (shared with AgentIdleLife: the lean now goes through it)"
                              : (tilt.bob == null && life != null ? " (if that is the Chest or Spine, the lean goes through AgentIdleLife)" : "")));
            }
            sb.AppendLine($"    arm nudges={nudges.Length}, faceUserWhileDelivering={(go.GetComponent<FaceUserWhileDelivering>() != null)}, rubberBand={(go.GetComponent<RubberBandReturn>() != null)}, touchSummary={(go.GetComponent<AgentTouchSummary>() != null)}");
            if (view.bob != null || view.armL != null || view.armR != null)
                sb.AppendLine($"    NOTE: legacy AgentView fields are assigned (bob={Name(view.bob)}, armL={Name(view.armL)}, armR={Name(view.armR)}); AgentView.Update writes them every frame before the Animator. Harmless only if the clips key those transforms.");
        }
        if (views.Length == 0) sb.AppendLine("  no AgentView found");

        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }
}
