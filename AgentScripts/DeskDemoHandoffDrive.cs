using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Networking;

/// <summary>
/// Play Mode trace for the scripted desk demo: logs (game time) artifact.created, work delivered, every handoff stage change
/// (Settling/Approaching/Exchanging/Returning) and every CompletionSequence phase change. Read-only.
///   scripts\drv.ps1 DeskDemoHandoffDrive Start   then PlayDrive Start / Approve ... then DeskDemoHandoffDrive Report
/// </summary>
public static class DeskDemoHandoffDrive
{
    const string LogPath = "Temp/deskdemo_handoff.txt";
    static AgentHandoffController h; static CompletionSequence cs; static SessionController sc;
    static FieldInfo stageF, phaseF, queueF;
    static string lastStage = "", lastPhase = ""; static bool hooked;

    static void Log(string s) => System.IO.File.AppendAllText(LogPath, Time.time.ToString("F2") + " " + s + "\n");

    public static string Start()
    {
        if (!EditorApplication.isPlaying) return "enter Play Mode first";
        System.IO.File.WriteAllText(LogPath, "");
        h = Object.FindFirstObjectByType<AgentHandoffController>();
        cs = Object.FindFirstObjectByType<CompletionSequence>();
        sc = Object.FindFirstObjectByType<SessionController>();
        if (h == null || cs == null || sc == null) return "missing handoff/completion/session";
        var bf = BindingFlags.Instance | BindingFlags.NonPublic;
        stageF = typeof(AgentHandoffController).GetField("_stage", bf);
        phaseF = typeof(CompletionSequence).GetField("_phase", bf);
        sc.Reducer.ArtifactCreated += e => Log("EVENT artifact.created");
        sc.Reducer.WorkDelivered += e => Log("EVENT work delivered (task.completed)");
        sc.Reducer.DecisionResolved += (i, s) => Log("EVENT decision.resolved " + s);
        lastStage = lastPhase = "";
        if (!hooked) { EditorApplication.update += Tick; hooked = true; }
        return "tracing; chainWaitMax=" + cs.handoffChainWaitMax;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || h == null || cs == null) return;
        string st = stageF.GetValue(h).ToString() + (AgentHandoffController.ExchangePending ? "+pending" : "");
        if (st != lastStage) { lastStage = st; Log("handoff " + st); }
        string ph = phaseF.GetValue(cs).ToString();
        if (ph != lastPhase) { lastPhase = ph; Log("completion " + ph); }
    }

    public static string Report() => System.IO.File.ReadAllText(LogPath).Replace("\n", " | ");

    public static string Stop()
    {
        if (hooked) { EditorApplication.update -= Tick; hooked = false; }
        return "stopped";
    }
}
