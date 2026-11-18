using System.Text;
using UnityEngine;
using MuseDesk.Voice;

/// <summary>
/// rc22 Play Mode probes for the "less picky" voice pass (owner, worn rc21: "the mic needs to be less picky so it actually
/// reads better"). No microphone, no network. Every method takes no argument (scripts\drv.ps1 cannot pass spaces or '|'),
/// with a built-in sample sentence. Each one feeds a final sentence through the real VoiceCommandCenter path and reports
/// what it did; the Console also gets the new "[MuseDesk.Voice] heard ..." line for each.
///
/// Mic key path (InjectFinal):
///   FuzzyManager          "man ager, plan a login page"            -> ManagerRequest (when the Wall Manager is registered)
///   TheDeveloper          "the developer, what are you working on" -> AskAgent:developer
///   FillerStatus          "um, okay so what's the, uh, status please" -> Status (read-only; the grammar alone misses it)
///   FillerHelp            "so, what can I, um, say"                 -> Help (read-only; the grammar alone misses it)
///   FillerApprove         "uh, approve it please"                   -> Approve while a paper is open, else DecisionNotPending
///   PoliteApproveStays    "um, could you approve it for me"         -> must NOT be a decision (Not understood)
///   NoNameMicKey          "I'd like a page that lists my reading goals" -> ManagerRequest(unnamed) when the Manager can take it
///   SmallTalkMicKey       "thank you"                               -> Not understood (never sent to the Manager)
/// Meet by voice (owner: "'Developer, come down here' and then it triggers the Meet feature"; needs the Wall registered):
///   ComeDownHere          "Developer, come down here"               -> PortalRequest:Developer
///   ComeDownHereNameLast  "can you come down here, developer"       -> PortalRequest:Developer
///   ComeDownHereHandsFree "come down here, researcher" (hands-free) -> PortalRequest:Researcher (the name is the address)
///   ComeNoName            "can you come down here"                  -> not sent to the Manager; the fallback asks who
/// "Can't do that, but here's what I can do" (shown and spoken):
///   NotUnderstoodNamed    "Developer, purple monkey dishwasher"     -> the Developer's fallback line (msg), then Spoken
///   NotUnderstoodUnnamed  "banana"                                  -> the Manager's fallback line
///   Spoken                what AgentSpeech last said, and who
///   SelfCheck             (Edit Mode too) the pure rules on ~80 sample sentences; lists mismatches
/// Hands-free path (InjectAmbientFinal: the name rule applies):
///   FuzzyManagerHandsFree "managers, plan a login page"             -> ManagerRequest
///   HeyMuseHandsFree      "hey muse, what's the status"             -> Status
///   NoNameHandsFree       "I'd like a page that lists my reading goals" -> Unaddressed (privacy: dropped)
///   ChatterHandsFree      "the manager said we need budget cuts"   -> UnaddressedHint (privacy: nothing done, "start with a name" hint)
/// Through WitVoiceSource with fake service events (the stitcher runs):
///   StitchFiller          "okay so" (pause) "what's the status"     -> call State after ~2 s: one sentence, pieces=2, Status
///   State                 what the source and the centre hold now
///   Verdicts              the stitcher verdict for sample pieces ("okay so" / "Manager, um" must be Incomplete)
/// </summary>
public static class Rc22VoiceProbe
{
    static WitVoiceSource Src => Object.FindAnyObjectByType<WitVoiceSource>(FindObjectsInactive.Include);

    static string Feed(string sentence, bool handsFree)
    {
        if (!Application.isPlaying) return "not playing";
        var cc = VoiceCommandCenter.Instance;
        if (cc == null) return "no VoiceCommandCenter";
        int before = cc.HandledCount;
        if (handsFree) cc.InjectAmbientFinal(sentence); else cc.InjectFinal(sentence);
        return "said='" + sentence + "' path=" + (handsFree ? "hands-free" : "mic-key")
               + " handled=" + (cc.HandledCount - before)
               + " | read='" + cc.LastTranscript + "' intent=" + cc.LastIntent + " msg='" + cc.LastMessage + "'"
               + " | managerHandler=" + (VoiceCommandCenter.ManagerRequestHandler != null)
               + " managerReady=" + (VoiceCommandCenter.ManagerReady == null ? "unset" : VoiceCommandCenter.ManagerReady().ToString())
               + " companion=" + (VoiceCommandCenter.CompanionAgent != null ? VoiceCommandCenter.CompanionAgent().ToString() : "unset");
    }

    // ── mic key ──
    public static string FuzzyManager() => Feed("man ager, plan a login page", false);
    public static string TheDeveloper() => Feed("the developer, what are you working on", false);
    public static string FillerStatus() => Feed("um, okay so what's the, uh, status please", false);
    public static string FillerHelp() => Feed("so, what can I, um, say", false);
    public static string FillerApprove() => Feed("uh, approve it please", false);
    public static string PoliteApproveStays() => Feed("um, could you approve it for me", false);
    public static string NoNameMicKey() => Feed("I'd like a page that lists my reading goals", false);
    public static string SmallTalkMicKey() => Feed("thank you", false);

    // ── Meet by voice (needs the Wall registered: PortalRequestHandler) ──
    public static string ComeDownHere() => Feed("Developer, come down here", false);
    public static string ComeDownHereNameLast() => Feed("can you come down here, developer", false);
    public static string ComeDownHereHandsFree() => Feed("come down here, researcher", true);
    public static string ComeNoName() => Feed("can you come down here", false);

    // ── "can't do that, but here's what I can do" (spoken through AgentSpeech when speech is on) ──
    public static string NotUnderstoodNamed() => Feed("Developer, purple monkey dishwasher", false);
    public static string NotUnderstoodUnnamed() => Feed("banana", false);
    public static string Spoken() => "lastSaid='" + AgentSpeech.LastSaid + "' speaker=" + AgentSpeech.LastSpeaker + " count=" + AgentSpeech.SaidCount;

    // ── hands-free ──
    public static string FuzzyManagerHandsFree() => Feed("managers, plan a login page", true);
    public static string HeyMuseHandsFree() => Feed("hey muse, what's the status", true);
    public static string NoNameHandsFree() => Feed("I'd like a page that lists my reading goals", true);
    public static string ChatterHandsFree() => Feed("the manager said we need budget cuts", true);

    // ── through the speech source and the stitcher ──
    public static string StitchFiller()
    {
        if (!Application.isPlaying) return "not playing";
        var s = Src; if (s == null) return "no WitVoiceSource";
        s.DriverOpen();
        s.DriverPiece("okay so");
        s.DriverMicReopened();
        s.DriverPartial("what's");
        s.DriverPiece("what's the status");
        return "queued 'okay so' + 'what's the status'; call State after ~2 s (expect pieces=2, intent Status). " + s.StitchState;
    }

    public static string State()
    {
        var s = Src;
        var cc = VoiceCommandCenter.Instance;
        return (s != null ? s.StitchState + " listening=" + s.IsListening : "no WitVoiceSource")
               + (cc != null ? " | centre: lastHeard='" + cc.LastTranscript + "' intent=" + cc.LastIntent + " msg='" + cc.LastMessage + "'" : " | no command centre");
    }

    /// <summary>Edit Mode or Play Mode: the pure rules (WakeWord / VoiceFiller / classifier) on the owner's kinds of sentences,
    /// as a quick check outside the test runner. Lists every mismatch.</summary>
    public static string SelfCheck()
    {
        var bad = new StringBuilder();
        int n = 0;
        void Canon(string s, string want) { n++; string got = WakeWord.Canonical(s); if (got != want) bad.Append("\n canonical '" + s + "' -> '" + got + "' want '" + want + "'"); }
        void Addr(string s, bool want) { n++; bool got = WakeWord.IsAddressed(s); if (got != want) bad.Append("\n addressed '" + s + "' -> " + got); }
        void Clean(string s, string want) { n++; string got = VoiceFiller.Clean(s); if (got != want) bad.Append("\n clean '" + s + "' -> '" + got + "' want '" + want + "'"); }

        Canon("man ager plan a login page", "Manager, plan a login page");
        Canon("man age her, plan a login page", "Manager, plan a login page");
        Canon("Managers, plan a login page", "Manager, plan a login page");
        Canon("The developer, why did you pick this?", "Developer, why did you pick this?");
        Canon("Testa, status", "Tester, status");
        Canon("Research your why", "Researcher, why");
        Canon("Excuse me Manager, plan a login page", "Manager, plan a login page");
        Canon("HEY MANAGE HER: plan a login page!", "Manager, plan a login page!");
        Canon("Researcher, why this source?", "Researcher, why this source?");
        Addr("hey muse, pause", true); Addr("use desk, pause", true); Addr("Muse Desk resume", true);
        Addr("um okay manager, plan a login page", true);
        Addr("the manager said we need budget cuts", false); Addr("my manager is annoying", false);
        Addr("the testers are slow today", false); Addr("research a new charting library", false);
        Addr("test a login page", false); Addr("hey hey hey researcher", false); Addr("so I said to my developer friend", false);
        Addr("tell the researcher to approve", false); Addr("research shows the login works", false);
        Clean("um, okay so can you open the, uh, board for me please", "open the board");
        Clean("Could you please switch to floor mode?", "switch to floor mode?");
        Clean("Okay so um, what's the status, please?", "what's the status?");
        Clean("um, please don't open the board", "don't open the board");
        Clean("not now", "not now"); Clean("please", "please"); Clean("um", "um");
        n++; var c = VoiceIntentClassifier.Classify("man ager, plan a login page");
        if (c.Kind != VoiceAction.ManagerRequest || c.Question != "plan a login page") bad.Append("\n classify split manager -> " + c + " q='" + c.Question + "'");
        n++; if (VoiceFiller.ContentWords("thank you") != 0 || VoiceFiller.ContentWords("plan a login page") != 3) bad.Append("\n content words");
        n++; if (!VoiceFiller.IsOnlyFiller("okay so") || VoiceFiller.IsOnlyFiller("yes")) bad.Append("\n only filler");
        void Meet(string s, VoiceAgentTarget want) { n++; VoiceAgentTarget a; bool ok = PortalVoiceRequest.TryParse(s, out a); if ((ok ? a : VoiceAgentTarget.None) != want) bad.Append("\n meet '" + s + "' -> " + a); }
        Meet("Developer, come down here", VoiceAgentTarget.Developer); Meet("come down here, developer", VoiceAgentTarget.Developer);
        Meet("get over here, researcher", VoiceAgentTarget.Researcher); Meet("Researcher, come see me", VoiceAgentTarget.Researcher);
        Meet("come meet me, tester", VoiceAgentTarget.Tester); Meet("can you come here, developer?", VoiceAgentTarget.Developer);
        Meet("um, okay, developer, could you come down here please", VoiceAgentTarget.Developer);
        Meet("I was wondering if you could come down here, developer, for me please", VoiceAgentTarget.Developer);
        Meet("come down here manage her", VoiceAgentTarget.Manager); Meet("meet the developer", VoiceAgentTarget.Developer);
        Meet("come down here", VoiceAgentTarget.None); Meet("Developer, don't come down here", VoiceAgentTarget.None);
        Meet("come down here and fix the build, developer", VoiceAgentTarget.None); Meet("Tester, come back", VoiceAgentTarget.None);
        Meet("the developer should come down here", VoiceAgentTarget.None); Meet("you should come see the developer", VoiceAgentTarget.None);
        Meet("Researcher, come here, developer", VoiceAgentTarget.None);
        n++; if (!PortalVoiceRequest.IsUnnamedComeRequest("can you come down here") || PortalVoiceRequest.IsUnnamedComeRequest("come here and fix it")) bad.Append("\n unnamed come");
        n++; string fl = VoiceFallbackLine.Line(new FallbackFacts { Speaker = VoiceAgentTarget.Developer, Companion = VoiceAgentTarget.Developer, ManagerCanPlan = true, PortalAvailable = true, BoardAvailable = true });
        if (fl != "I can't do that, but I can: answer a question about my work, tell you what I'm working on, or ask the Manager for a plan.") bad.Append("\n fallback '" + fl + "'");
        for (int bits = 0; bits < 256; bits++)
        {
            var f = new FallbackFacts { Speaker = (VoiceAgentTarget)(bits % 5), Companion = (bits & 8) != 0 ? VoiceAgentTarget.Developer : VoiceAgentTarget.None, Mode = (bits >> 4) & 3, Moving = (bits & 64) != 0, DecisionWaiting = (bits & 128) != 0, ManagerCanPlan = (bits & 1) != 0, PortalAvailable = (bits & 2) != 0, BoardAvailable = true };
            int c2 = VoiceFallbackLine.Options(f, VoiceFallbackLine.MaxOptions).Count;
            if (c2 < 2 || c2 > 3 || VoiceFallbackLine.Line(f).Length >= 170) { bad.Append("\n fallback bits " + bits + " count " + c2); break; }
        }
        n++;
        return n + " checks, " + (bad.Length == 0 ? "all pass" : "MISMATCHES:" + bad);
    }

    public static string Verdicts()
    {
        var cc = VoiceCommandCenter.Instance; if (cc == null) return "no command centre";
        var sb = new StringBuilder();
        foreach (string t in new[] { "okay so", "hey", "um", "Manager", "Manager, um", "man ager", "the developer,", "open the board please",
                                     "uh, open the board", "stop", "Manager, build a program" })
            sb.Append("'").Append(t).Append("' -> ").Append(cc.SentenceVerdict(t)).Append("; ");
        return sb.ToString();
    }
}
