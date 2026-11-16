using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Documents;

/// <summary>
/// Play Mode check that the offering agent raises the decision paper ONCE and holds it until it is taken: no give /
/// take-back loop. Never grabs the paper.
///   scripts\drv.ps1 OfferHoldDrive Start        (Play Mode: starts the offline desk demo task and begins sampling)
///   ... wait until Status says "done" (about 20 s of offer, plus the walk) ...
///   scripts\drv.ps1 OfferHoldDrive Report
///   scripts\drv.ps1 OfferHoldDrive Stop
/// Samples every frame: the researcher's AgentAnimatorBridge (IsOffering, OfferPoseHeld, ReactionPlaying), its walk state
/// and the DecisionDocument (active, parent, workspace-local position). "retracts" counts frames where the offer ended
/// while the paper was still in the agent's hand, undecided and untaken, and the agent was standing at the hand-over spot.
/// </summary>
public static class OfferHoldDrive
{
    const float SampleSeconds = 20f;

    static AgentAnimatorBridge bridge; static AgentMovement move; static HeldPaperController paper; static Transform ws;
    static bool hooked, started, offeredOnce, done;
    static float offerSince = -1f, startedAt, firstOfferAt = -1f;
    static int frames, offeringFrames, heldFrames, retractEvents, walkFramesWhileOffering;
    static bool prevOffering;
    static Vector3 minP = Vector3.positiveInfinity, maxP = Vector3.negativeInfinity;
    static string endReason = "";

    static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);

    public static string Start()
    {
        if (!EditorApplication.isPlaying) return "enter Play Mode first";
        var researcher = Object.FindObjectsByType<AgentView>(FindObjectsSortMode.None).FirstOrDefault(v => v.agentId == "researcher");
        if (researcher == null) return "no researcher AgentView";
        bridge = researcher.GetComponent<AgentAnimatorBridge>(); move = researcher.GetComponent<AgentMovement>();
        paper = Find<HeldPaperController>();
        var wsGo = GameObject.Find("WorkspaceRoot"); ws = wsGo != null ? wsGo.transform : null;
        if (bridge == null || move == null || paper == null) return "missing researcher bridge / movement / HeldPaperController";

        frames = offeringFrames = heldFrames = retractEvents = walkFramesWhileOffering = 0;
        offeredOnce = done = false; started = true; prevOffering = false;
        offerSince = firstOfferAt = -1f; startedAt = Time.time; endReason = "";
        minP = Vector3.positiveInfinity; maxP = Vector3.negativeInfinity;
        if (!hooked) { EditorApplication.update += Tick; hooked = true; }

        // Start the offline task the way PlayDrive.Start does (TaskDock.StartReadyTask), so a decision gets requested.
        var dock = GameObject.Find("TaskDock");
        var comp = dock != null ? dock.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "TaskDock") : null;
        var mi = comp != null ? comp.GetType().GetMethod("StartReadyTask", System.Type.EmptyTypes) : null;
        if (mi != null) mi.Invoke(comp, null);
        return mi != null ? "sampling; started the demo task (offline), waiting for the paper offer" : "sampling; TaskDock.StartReadyTask not found, start the task by hand";
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || !started || done || bridge == null) return;
        frames++;
        bool inHand = paper != null && paper.gameObject.activeInHierarchy && !paper.IsTaken;
        bool offering = bridge.IsOffering;
        bool standing = !move.IsMoving && !move.AtHome;

        if (offering && inHand && standing)
        {
            if (!offeredOnce) { offeredOnce = true; firstOfferAt = Time.time; }
            offerSince = offerSince < 0f ? Time.time : offerSince;
        }

        if (offeredOnce)
        {
            if (inHand && standing)
            {
                if (offering) offeringFrames++;
                if (bridge.OfferPoseHeld) heldFrames++;
                if (prevOffering && !offering) retractEvents++;
                if (ws != null && paper != null)
                {
                    Vector3 p = ws.InverseTransformPoint(paper.transform.position);
                    minP = Vector3.Min(minP, p); maxP = Vector3.Max(maxP, p);
                }
            }
            else if (offering && move.IsMoving) walkFramesWhileOffering++;
            prevOffering = offering && inHand && standing;
            if (paper != null && paper.IsTaken) { done = true; endReason = "paper taken (not by this driver)"; }
            else if (Time.time - firstOfferAt >= SampleSeconds) { done = true; endReason = $"{SampleSeconds:F0} s elapsed without a grab"; }
        }
        else if (Time.time - startedAt > 90f) { done = true; endReason = "no paper offer within 90 s"; }
    }

    public static string Status() => !started ? "not started" : done ? "done: " + endReason : (offeredOnce ? $"sampling, {Time.time - firstOfferAt:F1} s into the offer" : "waiting for the offer");

    public static string Report()
    {
        if (!started) return "not started";
        var sb = new StringBuilder();
        sb.AppendLine($"status: {Status()}");
        sb.AppendLine($"offer seen: {offeredOnce}{(offeredOnce ? $" at t+{firstOfferAt - startedAt:F1} s" : "")}");
        if (offeredOnce)
        {
            float span = Time.time - firstOfferAt;
            sb.AppendLine($"frames standing at the hand-over spot with the paper in hand: IsOffering true in {offeringFrames}, pose frozen (held) in {heldFrames}");
            sb.AppendLine($"RETRACTS (offer ended while the paper was still in hand and untaken): {retractEvents}   <- must be 0");
            sb.AppendLine($"frames offering while walking (arrival): {walkFramesWhileOffering}");
            if (!float.IsInfinity(minP.x))
                sb.AppendLine($"paper workspace-local position range while held: x {minP.x:F3}..{maxP.x:F3}, y {minP.y:F3}..{maxP.y:F3}, z {minP.z:F3}..{maxP.z:F3} (a held pose moves only with the hand; a retract shows as a large y / z swing)");
            sb.AppendLine($"sampled over {span:F1} s (total frames {frames})");
        }
        sb.AppendLine($"PASS: {(offeredOnce && retractEvents == 0 && heldFrames > 0 ? "yes" : "NO")} (needs an offer, zero retracts and a frozen hold; paper was NOT grabbed by this driver)");
        return sb.ToString();
    }

    public static string Stop()
    {
        if (hooked) { EditorApplication.update -= Tick; hooked = false; }
        started = false;
        return "stopped";
    }
}
