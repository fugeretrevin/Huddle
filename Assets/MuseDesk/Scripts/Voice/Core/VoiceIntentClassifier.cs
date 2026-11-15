using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MuseDesk.Voice
{
    /// <summary>Which character a sentence is directed at.</summary>
    public enum VoiceAgentTarget
    {
        None = 0,
        Researcher,
        Developer,
        Tester,
        Manager,
    }

    /// <summary>What a spoken sentence asks for (agent-directed layer; see VoiceIntentParser for the
    /// office-wide command grammar).</summary>
    public enum VoiceAction
    {
        Unknown = 0,
        AskAgent,
        Summon,
        StartWork,
        Approve,
        Reject,
        Pause,
        Resume,
        ManagerRequest,
        Cancel,
    }

    /// <summary>The result of classifying one transcript.</summary>
    public struct VoiceClassification
    {
        public VoiceAction Kind;
        /// <summary>The agent the sentence is for, or None.</summary>
        public VoiceAgentTarget Target;
        /// <summary>0..1. Zero for Unknown.</summary>
        public float Confidence;
        /// <summary>Always true for Approve and Reject: a spoken decision never executes by itself.</summary>
        public bool RequiresConfirmation;
        /// <summary>AskAgent / ManagerRequest: what was said after the agent's name (may be empty).</summary>
        public string Question;
        /// <summary>A command word was present but negated ("don't approve"); Kind is Unknown.</summary>
        public bool Negated;
        /// <summary>A command word was present but hedged ("maybe approve?"); Kind is Unknown.</summary>
        public bool Hedged;
        public string Raw;

        public bool IsUnknown { get { return Kind == VoiceAction.Unknown; } }

        public override string ToString()
        {
            return Kind + (Target != VoiceAgentTarget.None ? ":" + Target : "")
                + (RequiresConfirmation ? " (confirm)" : "");
        }
    }

    /// <summary>
    /// Pure transcript to intent + target classifier. No Unity types. Rules:
    ///  - Approve / Reject need a SHORT sentence made only of the decision word and filler, never
    ///    negated or hedged, and always set RequiresConfirmation (voice never executes a
    ///    consequential action by itself).
    ///  - Negation ("don't", "do not", "never") or hedging ("maybe", "should I", a question) on a
    ///    command word gives Unknown with Negated / Hedged set. "never mind" is Cancel.
    ///  - A name or role word ("developer", "dev", "tester", "qa", "manager", ...) sets Target;
    ///    "you" resolves to the focused agent when the caller supplies one.
    ///  - Nothing is invented: a sentence that matches nothing is Unknown with confidence 0.
    /// </summary>
    public static class VoiceIntentClassifier
    {
        static readonly HashSet<string> Negators = new HashSet<string>
        {
            "dont", "not", "never", "no", "cant", "cannot", "wont", "shouldnt", "wouldnt", "couldnt",
            "without", "nothing", "doesnt", "didnt", "isnt", "arent", "neither", "nor",
        };

        static readonly HashSet<string> Hedges = new HashSet<string>
        {
            "maybe", "perhaps", "might", "possibly", "should", "could", "would", "can", "will", "shall",
            "if", "whether", "probably", "guess", "think", "wonder", "wondering", "unsure", "or",
            "somehow", "kinda", "sorta", "hmm", "um", "uh",
        };

        static readonly HashSet<string> QuestionStarters = new HashSet<string>
        {
            "do", "does", "did", "is", "are", "was", "were", "who", "what", "why", "how", "when",
            "where", "which", "tell", "explain", "show", "give", "describe",
        };

        static readonly HashSet<string> Filler = new HashSet<string>
        {
            "please", "it", "this", "that", "the", "paper", "report", "decision", "now", "yes", "yeah",
            "okay", "ok", "hey", "and", "i", "we", "want", "to", "lets", "let", "us", "is", "a", "all",
            "right", "thanks", "thank", "you", "them", "work", "result", "results", "plan", "for", "me",
            "go", "ahead", "everyone", "agents", "team", "again", "up", "back", "over", "here", "on",
            "off", "out", "your", "youre", "my", "our", "just", "then", "well", "so", "get", "working",
            "do", "does", "of", "hold", "keep", "going", "carry", "come", "step",
        };

        static readonly string[] ApprovePhrases = { "approve", "accept", "lgtm", "ship it", "sign off", "looks good" };
        static readonly string[] RejectPhrases = { "reject", "decline", "deny", "turn down" };
        static readonly string[] PausePhrases = { "pause", "hold on", "hold off", "hold it", "wait", "freeze" };
        static readonly string[] ResumePhrases = { "resume", "continue", "unpause", "carry on", "keep going", "go on" };
        static readonly string[] StartPhrases =
            { "start", "begin", "kick off", "get started", "get to work", "get going", "lets go", "run it", "launch" };
        static readonly string[] SummonPhrases =
            { "summon", "come here", "come over", "come to me", "over here", "get over here", "come on over", "come back", "come" };
        static readonly string[] CancelPhrases = { "cancel", "stop", "abort", "forget it", "scratch that", "nevermind" };

        static readonly Regex LeadingName = new Regex(
            @"^\s*(?:(?:hey|ok|okay|hi|yo)\W+)?(?:the\s+)?(?:researcher|developer|tester|manager|dev|coder|programmer|engineer|qa)\b[\s,:;?!.\-]*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // ───────────────────────── public API ─────────────────────────

        /// <param name="transcript">What was heard.</param>
        /// <param name="focused">The agent the user is currently focused on (gaze / selection), for "you". None if unknown.</param>
        public static VoiceClassification Classify(string transcript, VoiceAgentTarget focused = VoiceAgentTarget.None)
        {
            var r = new VoiceClassification { Kind = VoiceAction.Unknown, Raw = transcript ?? "", Question = "" };
            bool hadQuestionMark;
            List<string> t = Tokenize(transcript, out hadQuestionMark);
            if (t.Count == 0) return r;

            VoiceAgentTarget named = ResolveTarget(t);
            bool you = t.Contains("you") || t.Contains("your") || t.Contains("youre");
            VoiceAgentTarget target = named != VoiceAgentTarget.None ? named : (you ? focused : VoiceAgentTarget.None);
            bool viaFocus = named == VoiceAgentTarget.None && target != VoiceAgentTarget.None;
            r.Target = target;

            // "never mind" is the one negator that is itself a command.
            if (HasPhrase(t, "never mind") || HasPhrase(t, "nevermind"))
                return Done(r, VoiceAction.Cancel, 0.95f);

            bool negated = false, hedgeWord = false;
            for (int i = 0; i < t.Count; i++)
            {
                if (Negators.Contains(t[i])) negated = true;
                if (Hedges.Contains(t[i])) hedgeWord = true;
            }
            bool questionShaped = hadQuestionMark || QuestionStarters.Contains(t[0]) || (t.Count > 1 && t[0] == "i" && Hedges.Contains(t[1]));
            bool hedgedDecision = hedgeWord || questionShaped;

            // ── decisions: strict, never negated or hedged, always need confirmation ──
            bool[] used = new bool[t.Count];
            bool approve = Match(t, ApprovePhrases, used);
            bool reject = Match(t, RejectPhrases, used);
            if (approve || reject)
            {
                int extras = Extras(t, used, named);
                if (extras == 0)
                {
                    if (approve && reject) { r.Hedged = true; return r; }       // "approve or reject": ambiguous
                    if (negated) { r.Negated = true; return r; }
                    if (hedgedDecision) { r.Hedged = true; return r; }
                    r.Kind = approve ? VoiceAction.Approve : VoiceAction.Reject;
                    r.RequiresConfirmation = true;
                    r.Confidence = viaFocus ? 0.8f : 0.9f;
                    return r;
                }
                // A decision word inside a longer sentence is not a decision. Remember why, then see
                // whether it is a question for a named agent.
                r.Negated = negated;
                r.Hedged = hedgedDecision;
                return Ask(r, t, named, target, viaFocus, hadQuestionMark);
            }

            // ── plain commands ──
            VoiceAction act = VoiceAction.Unknown;
            used = new bool[t.Count];
            if (Match(t, PausePhrases, used)) act = VoiceAction.Pause;
            else if (Match(t, ResumePhrases, used)) act = VoiceAction.Resume;
            else if (Match(t, StartPhrases, used)) act = VoiceAction.StartWork;
            else if (Match(t, SummonPhrases, used)) act = VoiceAction.Summon;
            else if (Match(t, CancelPhrases, used)) act = VoiceAction.Cancel;

            if (act != VoiceAction.Unknown)
            {
                int extras = Extras(t, used, named);
                if (extras <= 2 && !(act == VoiceAction.Pause && questionShaped && extras > 0))
                {
                    if (negated) { r.Negated = true; return r; }
                    float c = 0.92f - 0.07f * extras;
                    if (hedgeWord && !PoliteOnly(t)) c *= 0.7f;
                    return Done(r, act, c);
                }
            }

            // ── manager / agent questions ──
            if (named == VoiceAgentTarget.Manager)
            {
                r.Question = StripName(transcript);
                return Done(r, VoiceAction.ManagerRequest, 0.8f);
            }
            return Ask(r, t, named, target, viaFocus, hadQuestionMark);
        }

        /// <summary>The first agent a token list names (role words included), or None.</summary>
        public static VoiceAgentTarget ResolveTarget(IList<string> tokens)
        {
            if (tokens == null) return VoiceAgentTarget.None;
            for (int i = 0; i < tokens.Count; i++)
            {
                VoiceAgentTarget a = TargetOf(tokens[i]);
                if (a != VoiceAgentTarget.None) return a;
            }
            return VoiceAgentTarget.None;
        }

        /// <summary>The first agent a transcript names, or None. Used to track who is being addressed.</summary>
        public static VoiceAgentTarget ResolveTarget(string transcript)
        {
            bool q;
            return ResolveTarget(Tokenize(transcript, out q));
        }

        /// <summary>The agent id string the rest of the app uses ("researcher"...), or null.</summary>
        public static string AgentId(VoiceAgentTarget t)
        {
            switch (t)
            {
                case VoiceAgentTarget.Researcher: return "researcher";
                case VoiceAgentTarget.Developer: return "developer";
                case VoiceAgentTarget.Tester: return "tester";
                case VoiceAgentTarget.Manager: return "manager";
                default: return null;
            }
        }

        /// <summary>The target for an agent id / role string (case-insensitive), or None.</summary>
        public static VoiceAgentTarget FromId(string id)
        {
            if (string.IsNullOrEmpty(id)) return VoiceAgentTarget.None;
            return TargetOf(id.Trim().ToLowerInvariant());
        }

        // ───────────────────────── internals ─────────────────────────

        static VoiceAgentTarget TargetOf(string w)
        {
            switch (w)
            {
                case "researcher": case "researchers": return VoiceAgentTarget.Researcher;
                case "developer": case "dev": case "coder": case "programmer": case "engineer": return VoiceAgentTarget.Developer;
                case "tester": case "qa": return VoiceAgentTarget.Tester;
                case "manager": return VoiceAgentTarget.Manager;
                default: return VoiceAgentTarget.None;
            }
        }

        static VoiceClassification Done(VoiceClassification r, VoiceAction kind, float confidence)
        {
            r.Kind = kind;
            r.Confidence = confidence < 0.05f ? 0.05f : (confidence > 1f ? 1f : confidence);
            return r;
        }

        static VoiceClassification Ask(VoiceClassification r, List<string> t, VoiceAgentTarget named,
            VoiceAgentTarget target, bool viaFocus, bool hadQuestionMark)
        {
            if (target == VoiceAgentTarget.None) return r;
            // "you" alone is not an address: it must at least be shaped like a question.
            if (viaFocus && !(hadQuestionMark || QuestionStarters.Contains(t[0]))) return r;
            string q = StripName(r.Raw);
            r.Question = q;
            r.Negated = false;
            r.Hedged = false;
            r.Kind = target == VoiceAgentTarget.Manager ? VoiceAction.ManagerRequest : VoiceAction.AskAgent;
            r.Confidence = viaFocus ? 0.7f : (q.Length == 0 ? 0.6f : 0.85f);
            return r;
        }

        static string StripName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            return LeadingName.Replace(raw, "").Trim();
        }

        static bool PoliteOnly(List<string> t)
        {
            // "could you start", "can you come here": a polite request is not a hedge.
            return (t[0] == "can" || t[0] == "could" || t[0] == "would" || t[0] == "will") && t.Contains("you");
        }

        /// <summary>Tokens not explained by a matched phrase, filler, negator, hedge or the agent's name.</summary>
        static int Extras(List<string> t, bool[] used, VoiceAgentTarget named)
        {
            int n = 0;
            for (int i = 0; i < t.Count; i++)
            {
                if (used[i]) continue;
                string w = t[i];
                if (Filler.Contains(w) || Negators.Contains(w) || Hedges.Contains(w)) continue;
                if (named != VoiceAgentTarget.None && TargetOf(w) != VoiceAgentTarget.None) continue;
                n++;
            }
            return n;
        }

        static bool Match(List<string> t, string[] phrases, bool[] used)
        {
            for (int p = 0; p < phrases.Length; p++)
            {
                string[] w = phrases[p].Split(' ');
                int at = Find(t, w);
                if (at < 0) continue;
                for (int k = 0; k < w.Length; k++) used[at + k] = true;
                return true;
            }
            return false;
        }

        static bool HasPhrase(List<string> t, string phrase)
        {
            return Find(t, phrase.Split(' ')) >= 0;
        }

        static int Find(List<string> t, string[] w)
        {
            for (int i = 0; i + w.Length <= t.Count; i++)
            {
                bool ok = true;
                for (int k = 0; k < w.Length && ok; k++) ok = t[i + k] == w[k];
                if (ok) return i;
            }
            return -1;
        }

        /// <summary>Lower-case words; apostrophes are dropped ("don't" becomes "dont"); everything else
        /// that is not a letter or digit separates words.</summary>
        public static List<string> Tokenize(string s, out bool hadQuestionMark)
        {
            hadQuestionMark = false;
            var list = new List<string>(8);
            if (string.IsNullOrEmpty(s)) return list;
            var sb = new StringBuilder(16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\'' || c == '’' || c == '‘') continue;
                if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); continue; }
                if (c == '?') hadQuestionMark = true;
                if (sb.Length > 0) { list.Add(sb.ToString()); sb.Length = 0; }
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
        }
    }
}
