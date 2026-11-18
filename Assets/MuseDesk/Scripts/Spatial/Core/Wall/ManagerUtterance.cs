using System;
using System.Collections.Generic;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>
    /// rc22 (owner, worn rc21: "every question means the manager starts building a plan, so allow it to just answer normal
    /// questions too"). Tells a QUESTION for the Manager ("what is the developer doing?", "how long will this take?",
    /// "is anything blocked?") from a GOAL to plan ("build a program that prints stars", "can you make me an expense tracker").
    /// A question is answered (read-only side conversation); only a goal drafts a plan. When unsure it is a goal, as before.
    /// Pure logic, EditMode-tested.
    /// </summary>
    public static class ManagerUtterance
    {
        static readonly HashSet<string> GoalVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "build", "make", "create", "write", "add", "implement", "fix", "set", "setup", "plan", "refactor", "design",
            "develop", "code", "program", "port", "migrate", "rewrite", "update", "change", "remove", "delete", "start",
            "prepare", "draft", "generate", "test", "deploy", "research", "investigate", "find", "improve", "optimize",
        };

        static readonly HashSet<string> QuestionStarts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "what", "what's", "whats", "why", "how", "how's", "hows", "when", "where", "who", "who's", "whos", "which",
            "is", "are", "was", "were", "do", "does", "did", "has", "have", "had", "should", "will", "would", "can", "could",
            "any", "anything", "status", "explain", "tell",
        };

        static readonly HashSet<string> Filler = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hey", "hi", "ok", "okay", "so", "um", "uh", "erm", "well", "please", "and", "also", "quick", "question", "manager",
        };

        static readonly HashSet<string> Polite = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "can", "could", "would", "will", "please",
        };

        /// <summary>True when the words ask the Manager something rather than give it a goal to plan.</summary>
        public static bool IsQuestion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            List<string> w = Words(t);
            int i = 0;
            while (i < w.Count && Filler.Contains(w[i])) i++;
            if (i >= w.Count) return false;

            // "can you build ...", "could you please make ...", "will you add ...": a polite goal.
            if (Polite.Contains(w[i]))
            {
                int j = i + 1;
                while (j < w.Count && (w[j] == "you" || w[j] == "u" || w[j] == "please" || w[j] == "just" || w[j] == "go" || w[j] == "ahead" || w[j] == "and")) j++;
                if (j < w.Count && GoalVerbs.Contains(w[j]) && !TellLike(w, j)) return false;
                return true;                                  // "can you tell me ...", "could you explain ...", "will it be done?"
            }
            // "tell me ...", "explain ...": a question in imperative form.
            if (TellLike(w, i)) return true;
            // An imperative goal verb first: a goal ("build a login page", "test the parser").
            if (GoalVerbs.Contains(w[i])) return false;
            if (QuestionStarts.Contains(w[i])) return true;
            // "I want / I need / we need / let's ...": a goal.
            if (w[i] == "i" || w[i] == "we" || w[i] == "let's" || w[i] == "lets") return false;
            // Nothing obvious: a question mark decides; else a goal (the old behaviour).
            return t.EndsWith("?", StringComparison.Ordinal);
        }

        static bool TellLike(List<string> w, int i)
        {
            return w[i] == "tell" || w[i] == "explain" || w[i] == "describe" || w[i] == "summarize" || w[i] == "summarise"
                || w[i] == "remind" || w[i] == "show";
        }

        static List<string> Words(string s)
        {
            var list = new List<string>();
            var cur = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '\'') cur.Append(char.ToLowerInvariant(c));
                else if (cur.Length > 0) { list.Add(cur.ToString()); cur.Length = 0; }
            }
            if (cur.Length > 0) list.Add(cur.ToString());
            return list;
        }
    }
}
