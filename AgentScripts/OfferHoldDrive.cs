using System.Linq;
using UnityEditor;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Documents;
using MuseDesk.DebugTools;

/// <summary>
/// Play Mode check that the offering agent raises the decision paper ONCE and holds it until it is taken: no give /
/// take-back loop. Never grabs the paper. Sampling runs in an OfferHoldProbe component in the scene, because each
/// driver call compiles a fresh assembly and static state would not survive between calls.
///   scripts\drv.ps1 OfferHoldDrive Start     (starts the offline desk demo task and the probe)
///   scripts\drv.ps1 OfferHoldDrive Status    (until "done", about 20 s of offer plus the walk)
///   scripts\drv.ps1 OfferHoldDrive Report
///   scripts\drv.ps1 OfferHoldDrive Stop
/// </summary>
public static class OfferHoldDrive
{
    const string ProbeName = "__OfferHoldProbe";

    static OfferHoldProbe Probe()
    {
        var go = GameObject.Find(ProbeName);
        return go != null ? go.GetComponent<OfferHoldProbe>() : null;
    }

    public static string Start()
    {
        if (!EditorApplication.isPlaying) return "enter Play Mode first";
        var researcher = Object.FindObjectsByType<AgentView>(FindObjectsSortMode.None).FirstOrDefault(v => v.agentId == "researcher");
        if (researcher == null) return "no researcher AgentView";
        var bridge = researcher.GetComponent<AgentAnimatorBridge>();
        var move = researcher.GetComponent<AgentMovement>();
        var paper = Object.FindFirstObjectByType<HeldPaperController>(FindObjectsInactive.Include);
        if (bridge == null || move == null || paper == null) return "missing researcher bridge / movement / HeldPaperController";

        Stop();
        var go = new GameObject(ProbeName);
        go.SetActive(false);
        var probe = go.AddComponent<OfferHoldProbe>();
        probe.bridge = bridge; probe.move = move; probe.paper = paper;
        var wsGo = GameObject.Find("WorkspaceRoot"); probe.ws = wsGo != null ? wsGo.transform : null;
        go.SetActive(true);

        // Start the offline task the way PlayDrive.Start does (TaskDock.StartReadyTask), so a decision gets requested.
        var dock = GameObject.Find("TaskDock");
        var comp = dock != null ? dock.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "TaskDock") : null;
        var mi = comp != null ? comp.GetType().GetMethod("StartReadyTask", System.Type.EmptyTypes) : null;
        if (mi != null) mi.Invoke(comp, null);
        return mi != null ? "probe running; started the offline demo task, waiting for the paper offer" : "probe running; TaskDock.StartReadyTask not found, start the task by hand";
    }

    public static string Status() { var p = Probe(); return p == null ? "not started" : p.Status(); }

    public static string Report() { var p = Probe(); return p == null ? "not started" : p.Report(); }

    public static string Stop()
    {
        var go = GameObject.Find(ProbeName);
        if (go != null) Object.Destroy(go);
        return "stopped";
    }
}
