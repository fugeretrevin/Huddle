using System.Collections.Generic;
using System.Text;

namespace MuseDesk.Voice
{
    /// <summary>What the run looks like right now, as plain values read from the reducers. Filled
    /// by VoiceOffice; nothing here is invented.</summary>
    public struct VoiceRunFacts
    {
        public bool Live;
        public bool RunInProgress;
        public bool HasStartedRun;
        public bool DecisionPending;
        public string DecisionAgentId;
        public string DecisionSummary;
        /// <summary>The agent whose latest state is a working state, or null.</summary>
        public string ActiveAgentId;
        public string ActiveActivity;
        public string ActiveProgress;
        /// <summary>From the run's artifact.created event; -1 when no count was reported.</summary>
        public int TestsPassed, TestsFailed;
        /// <summary>"delivered", "failed", "rejected" or null (nothing ended yet in this run).</summary>
        public string LastOutcome;
        public string LastSummary;
        public int TasksReady;
    }

    /// <summary>What the help card and the "try saying" line depend on.</summary>
    public struct VoiceHelpState
    {
        public VoiceContext Context;
        public bool Live;
        /// <summary>0 desk, 1 floor, 2 ceiling.</summary>
        public int Mode;
        public bool FloorEnabled, CeilingEnabled;
        public bool OfficeAway;
        public bool CanStart, CanRunAgain, HasResult, FocusOn, PaperTaken, CanResetLayout;
    }

    /// <summary>
    /// The words voice shows: an agent's answer, the status line, the phrases that fit the moment.
    /// Plain C# (no Unity types), tested in EditMode. Every sentence is assembled from values the
    /// reducers reported; in the scripted demo the caller adds the "(scripted demo)" label.
    /// </summary>
    public static class VoiceAnswers
    {
        public static string RoleName(string agentId)
        {
            switch (agentId)
            {
                case "researcher": return "Researcher";
                case "developer": return "Developer";
                case "tester": return "Tester";
                default: return string.IsNullOrEmpty(agentId) ? "Agent" : char.ToUpperInvariant(agentId[0]) + agentId.Substring(1);
            }
        }

        /// <summary>The honesty label every spoken answer carries.</summary>
        public static string Labelled(string text, bool live)
        {
            return live ? text : text + " (scripted demo)";
        }

        /// <summary>
        /// What one agent says when asked what it is doing.
        /// <paramref name="activity"/> is the shared activity word for its state ("Researching");
        /// <paramref name="progress"/> its own latest progress line; <paramref name="known"/> false
        /// when the reducer has not reported this agent at all.
        /// </summary>
        public static string AgentLine(string agentId, bool known, string activity, string progress, bool waitingOnUser,
                                       string taskLine, VoiceRunFacts run)
        {
            if (!known) return "No report from the " + RoleName(agentId) + " yet.";
            var sb = new StringBuilder(120);
            if (waitingOnUser)
            {
                sb.Append("I need your decision.");
                if (!string.IsNullOrEmpty(run.DecisionSummary)) sb.Append(' ').Append(Sentence(Clip(run.DecisionSummary, 70)));
                return sb.ToString();
            }
            sb.Append(Sentence(string.IsNullOrEmpty(activity) ? "Idle" : activity));
            if (!string.IsNullOrEmpty(progress)) sb.Append(' ').Append(Sentence(Clip(progress, 80)));
            else if (!string.IsNullOrEmpty(taskLine)) sb.Append(' ').Append(Sentence(Clip(taskLine, 70)));
            else if (!run.HasStartedRun) sb.Append(" No task has been started.");
            else if (!run.RunInProgress && run.LastOutcome != null) sb.Append(' ').Append(OutcomeWords(run));
            if (agentId == "tester" && run.TestsPassed >= 0)
                sb.Append(" Tests: ").Append(run.TestsPassed).Append(" passed, ").Append(run.TestsFailed < 0 ? 0 : run.TestsFailed).Append(" failed.");
            return sb.ToString();
        }

        /// <summary>The answer to "what's the status" / "how is it going".</summary>
        public static string Status(VoiceRunFacts run)
        {
            var sb = new StringBuilder(140);
            if (run.DecisionPending)
            {
                sb.Append(RoleName(string.IsNullOrEmpty(run.DecisionAgentId) ? "researcher" : run.DecisionAgentId)).Append(" needs your decision.");
                if (!string.IsNullOrEmpty(run.DecisionSummary)) sb.Append(' ').Append(Sentence(Clip(run.DecisionSummary, 70)));
                return sb.ToString();
            }
            if (run.RunInProgress)
            {
                if (!string.IsNullOrEmpty(run.ActiveAgentId))
                {
                    sb.Append(RoleName(run.ActiveAgentId)).Append(": ").Append(Sentence(string.IsNullOrEmpty(run.ActiveActivity) ? "working" : run.ActiveActivity));
                    if (!string.IsNullOrEmpty(run.ActiveProgress)) sb.Append(' ').Append(Sentence(Clip(run.ActiveProgress, 80)));
                }
                else sb.Append("A run is in progress.");
                return sb.ToString();
            }
            if (run.LastOutcome != null) return OutcomeWords(run);
            if (run.TasksReady > 0) return "Nothing is running. A task is ready: say start.";
            return "Nothing is running.";
        }

        /// <summary>The answer to "who needs me?".</summary>
        public static string WhoNeedsMe(VoiceRunFacts run)
        {
            if (run.DecisionPending)
                return RoleName(string.IsNullOrEmpty(run.DecisionAgentId) ? "researcher" : run.DecisionAgentId)
                       + " needs your decision on the paper.";
            if (run.RunInProgress) return "Nobody needs you. " + Status(run);
            if (run.LastOutcome == "failed") return "Nobody is waiting. The last run failed.";
            return "Nobody needs you right now.";
        }

        static string OutcomeWords(VoiceRunFacts run)
        {
            var sb = new StringBuilder(120);
            switch (run.LastOutcome)
            {
                case "delivered":
                    sb.Append("Delivered.");
                    if (!string.IsNullOrEmpty(run.LastSummary)) sb.Append(' ').Append(Sentence(Clip(run.LastSummary, 70)));
                    if (run.TestsPassed >= 0)
                        sb.Append(" Tests: ").Append(run.TestsPassed).Append(" passed, ").Append(run.TestsFailed < 0 ? 0 : run.TestsFailed).Append(" failed.");
                    break;
                case "failed":
                    sb.Append("The last run failed.");
                    if (!string.IsNullOrEmpty(run.LastSummary)) sb.Append(' ').Append(Sentence(Clip(run.LastSummary, 70)));
                    break;
                case "rejected":
                    sb.Append("Stopped. You rejected the proposal.");
                    break;
                default:
                    sb.Append("Nothing is running.");
                    break;
            }
            return sb.ToString();
        }

        // ───────────────────────── phrases that fit the moment ─────────────────────────

        /// <summary>Up to <paramref name="max"/> phrases the user can say right now, most relevant first.</summary>
        public static List<string> Phrases(VoiceHelpState s, int max)
        {
            var list = new List<string>(12);
            VoiceContext c = s.Context;
            if ((c & VoiceContext.Moving) != 0) list.Add("stop");
            if ((c & VoiceContext.UndoWindow) != 0) list.Add("cancel");
            if ((c & VoiceContext.ConfirmationPending) != 0) { list.Add("confirm"); list.Add("cancel"); }
            if ((c & VoiceContext.Previewing) != 0) { list.Add("next spot"); list.Add("confirm"); list.Add("cancel"); }
            if ((c & VoiceContext.DecisionPending) != 0)
            {
                if (!s.PaperTaken) list.Add("show me the paper");
                list.Add("approve"); list.Add("reject"); list.Add("show details"); list.Add("why?");
            }
            if ((c & VoiceContext.ViewerOpen) != 0) { list.Add("next"); list.Add("close"); }
            if (s.CanStart) list.Add("start");
            if (s.HasResult && (c & VoiceContext.ViewerOpen) == 0) list.Add("view result");
            if (s.CanRunAgain) list.Add("run again");
            list.Add("what's the status?");
            list.Add("Developer, what are you working on?");
            list.Add((c & VoiceContext.BoardOpen) != 0 ? "close the board" : "open the board");
            if (s.Mode != 0)
            {
                list.Add(s.OfficeAway ? "bring it here" : "send it back");
                if (s.Mode == 2) list.Add(s.OfficeAway ? "lower the pod" : "raise the pod");
                list.Add("desk mode");
            }
            else
            {
                if (s.FloorEnabled) list.Add("floor mode");
                if (s.CeilingEnabled) list.Add("ceiling mode");
            }
            if (s.Live) list.Add("new task: add a dark mode toggle");
            list.Add(s.FocusOn ? "focus off" : "focus on");
            if (s.CanResetLayout) list.Add("reset layout");
            list.Add("who needs me?");

            // No phrase twice ("confirm" / "cancel" can come from two contexts).
            var seen = new HashSet<string>();
            var result = new List<string>(max);
            for (int i = 0; i < list.Count && result.Count < max; i++)
                if (seen.Add(list[i])) result.Add(list[i]);
            return result;
        }

        /// <summary>The line under "not understood": two phrases that would work now.</summary>
        public static string TrySaying(VoiceHelpState s)
        {
            List<string> p = Phrases(s, 2);
            if (p.Count == 0) return "Say help.";
            if (p.Count == 1) return "Try: \"" + p[0] + "\"";
            return "Try: \"" + p[0] + "\" or \"" + p[1] + "\"";
        }

        /// <summary>The help card: one phrase per line.</summary>
        public static string HelpCard(VoiceHelpState s, int max)
        {
            List<string> p = Phrases(s, max);
            var sb = new StringBuilder(200);
            for (int i = 0; i < p.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(p[i]);
            }
            return sb.ToString();
        }

        /// <summary>Reflection-friendly form of <see cref="Phrases"/> for the EditMode tests:
        /// phrases joined by " | ". <paramref name="flags"/>: 1 live, 2 floor enabled, 4 ceiling
        /// enabled, 8 office away, 16 can start, 32 can run again, 64 has result, 128 focus on,
        /// 256 paper taken, 512 can reset layout.</summary>
        public static string DescribePhrases(int context, int mode, int flags, int max)
        {
            var s = new VoiceHelpState
            {
                Context = (VoiceContext)context, Mode = mode,
                Live = (flags & 1) != 0, FloorEnabled = (flags & 2) != 0, CeilingEnabled = (flags & 4) != 0,
                OfficeAway = (flags & 8) != 0, CanStart = (flags & 16) != 0, CanRunAgain = (flags & 32) != 0,
                HasResult = (flags & 64) != 0, FocusOn = (flags & 128) != 0, PaperTaken = (flags & 256) != 0,
                CanResetLayout = (flags & 512) != 0,
            };
            return string.Join(" | ", Phrases(s, max).ToArray());
        }

        /// <summary>Reflection-friendly form of <see cref="AgentLine"/> for the EditMode tests.</summary>
        public static string DescribeAgentLine(string agentId, bool known, string activity, string progress, bool waitingOnUser,
                                               string decisionSummary, int testsPassed, int testsFailed, bool runInProgress,
                                               bool hasStartedRun, string lastOutcome, bool live)
        {
            var run = new VoiceRunFacts
            {
                Live = live, RunInProgress = runInProgress, HasStartedRun = hasStartedRun, DecisionPending = waitingOnUser,
                DecisionAgentId = waitingOnUser ? agentId : null, DecisionSummary = decisionSummary,
                TestsPassed = testsPassed, TestsFailed = testsFailed, LastOutcome = lastOutcome,
            };
            return Labelled(AgentLine(agentId, known, activity, progress, waitingOnUser, null, run), live);
        }

        // ───────────────────────── text helpers ─────────────────────────

        /// <summary>Shorten to <paramref name="max"/> characters on a word boundary, with "...".</summary>
        public static string Clip(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = Ascii(text.Trim());
            if (text.Length <= max) return text;
            int cut = text.LastIndexOf(' ', max - 3);
            if (cut < max / 2) cut = max - 3;
            return text.Substring(0, cut).TrimEnd(' ', ',', ';', ':', '.') + "...";
        }

        /// <summary>End with a full stop unless it already ends with punctuation.</summary>
        public static string Sentence(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            char last = text[text.Length - 1];
            return last == '.' || last == '!' || last == '?' ? text : text + ".";
        }

        /// <summary>The interface font has ASCII letters, digits and basic punctuation only.</summary>
        public static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            StringBuilder sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                string rep = null;
                if (ch == '…') rep = "...";
                else if (ch == '“' || ch == '”') rep = "\"";
                else if (ch == '‘' || ch == '’') rep = "'";
                else if (ch == '–' || ch == '—') rep = "-";
                else if (ch == '<' || ch == '>') rep = " ";          // never let a transcript carry rich-text tags
                else if (ch > 126 || (ch < 32 && ch != '\n')) rep = "?";
                if (rep == null) { if (sb != null) sb.Append(ch); continue; }
                if (sb == null) sb = new StringBuilder(s.Length + 8).Append(s, 0, i);
                sb.Append(rep);
            }
            return sb == null ? s : sb.ToString();
        }
    }
}
