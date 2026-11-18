using System.Collections.Generic;
using System.Text;

namespace MuseDesk.Voice
{
    /// <summary>What is true right now, for <see cref="VoiceFallbackLine"/>. Filled by VoiceCommandCenter from the office.</summary>
    public struct FallbackFacts
    {
        /// <summary>Who answers: the agent the sentence named, else the agent with the user, else the Manager.</summary>
        public VoiceAgentTarget Speaker;
        /// <summary>The agent with the user (perched or out of the portal), or None.</summary>
        public VoiceAgentTarget Companion;
        /// <summary>0 Desk, 1 Floor, 2 Ceiling, 3 Wall (WorkspaceMode's values).</summary>
        public int Mode;
        public bool Live;
        public bool Moving, Previewing;
        public bool DecisionWaiting, PaperOpen;
        public bool ManagerPlanOpen;
        /// <summary>The Manager can take a goal now (its handler is registered and it is available).</summary>
        public bool ManagerCanPlan;
        /// <summary>"Developer, come here" opens the portal (the Wall registered for it).</summary>
        public bool PortalAvailable;
        public bool BoardAvailable;
        public bool FloorEnabled, CeilingEnabled;
        public bool HasResult;
        /// <summary>The sentence asked someone to come over without saying who.</summary>
        public bool AskedToComeWithoutName;
    }

    /// <summary>
    /// rc22 (owner: "if something's not understood by the agents then have them be like 'can't do that, but here's what I
    /// can do'"). Instead of a bare "Not understood", the agent the sentence was for (or the companion, or the Manager) says
    /// one short sentence and 2-4 things that WOULD work right now, picked from the moment (moving, a placement preview, a
    /// decision paper, a Manager plan, an agent beside the user, the mode, live or scripted demo). Every option is something
    /// this build's voice path really does; options that depend on a feature are offered only when the caller says it is
    /// there. A template, not a model's answer. Pure: no Unity types, tested in EditMode.
    /// </summary>
    public static class VoiceFallbackLine
    {
        public const int MaxOptions = 3;

        /// <summary>The spoken / shown line: "I can't do that, but I can: a, b, or c."</summary>
        public static string Line(FallbackFacts f)
        {
            List<string> o = Options(f, MaxOptions);
            string lead = f.AskedToComeWithoutName ? "I'm not sure who you're asking" : "I can't do that";
            if (o.Count == 0) return lead + ".";
            var sb = new StringBuilder(160);
            sb.Append(lead).Append(", but I can: ");
            for (int i = 0; i < o.Count; i++)
            {
                if (i > 0) sb.Append(i == o.Count - 1 ? (o.Count > 2 ? ", or " : " or ") : ", ");
                sb.Append(o[i]);
            }
            sb.Append('.');
            return sb.ToString();
        }

        /// <summary>The agent id that speaks the line ("manager" when nobody else is involved).</summary>
        public static string SpeakerId(FallbackFacts f)
        {
            return VoiceIntentClassifier.AgentId(f.Speaker) ?? "manager";
        }

        /// <summary>Up to <paramref name="max"/> things that would work now, most relevant first (at least two whenever two exist).</summary>
        public static List<string> Options(FallbackFacts f, int max)
        {
            var list = new List<string>(10);
            bool manager = f.Speaker == VoiceAgentTarget.Manager || f.Speaker == VoiceAgentTarget.None;
            bool worker = !manager;

            if (f.Moving) list.Add("stop the office if you say stop");
            if (f.Previewing) { list.Add("show the next spot"); list.Add("confirm this spot"); }
            if (f.AskedToComeWithoutName && f.PortalAvailable) list.Add(worker ? "come over if you say my name first" : "bring an agent over if you say its name first");
            if (f.DecisionWaiting)
            {
                if (!f.PaperOpen) list.Add("hand you the decision paper");
                else { list.Add("explain why we recommend it"); list.Add("show you the details"); }
            }
            if (f.ManagerPlanOpen && manager) list.Add("start the plan once you approve it on the card");

            if (worker)
            {
                if (f.Companion == f.Speaker) list.Add("answer a question about my work");
                list.Add("tell you what I'm working on");
                if (f.ManagerCanPlan) list.Add("ask the Manager for a plan");
                if (f.PortalAvailable && f.Companion != f.Speaker && !f.AskedToComeWithoutName) list.Add("come over to you");
            }
            else
            {
                if (f.ManagerCanPlan && !f.ManagerPlanOpen) list.Add("draft a plan for a goal you give me");
                if (f.Companion != VoiceAgentTarget.None) list.Add("let the agent beside you answer your question");
                list.Add("tell you how the team is doing");
            }

            if (f.HasResult) list.Add("show you the result");
            if (f.BoardAvailable) list.Add("open the task board");
            if (f.Mode == 0)
            {
                if (f.FloorEnabled) list.Add("move the office to the floor");
                else if (f.CeilingEnabled) list.Add("move the office to the ceiling");
            }
            else list.Add("put the office back on the desk");
            if (worker) list.Add("give you a status update");

            var seen = new HashSet<string>();
            var result = new List<string>(max);
            for (int i = 0; i < list.Count && result.Count < max; i++)
                if (seen.Add(list[i])) result.Add(list[i]);
            return result;
        }
    }
}
