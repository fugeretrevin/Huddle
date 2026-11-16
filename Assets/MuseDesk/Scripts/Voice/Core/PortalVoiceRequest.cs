using System.Collections.Generic;

namespace MuseDesk.Voice
{
    /// <summary>
    /// "Researcher, come here" / "Tester, meet me" / "meet the developer": the user wants to see that agent step out full
    /// size beside them (the Wall habitat's large-agent portal). Pure, no Unity types. Exactly two sentence shapes, each
    /// naming exactly one agent:
    ///   1. Addressed: the sentence STARTS with the agent's name (the <see cref="WakeWord"/> rule) and the rest is a
    ///      come / meet phrase plus at most polite filler: "Tester, come here please", "Researcher, could you come over?",
    ///      "Developer, meet me".
    ///   2. Meet-first: the sentence starts with "meet" (after at most three lead-in words such as "hey", "let me",
    ///      "I want to", or after a generic address like "MuseDesk,") and the next words name the agent:
    ///      "meet the developer", "let me meet the tester", "MuseDesk, meet the researcher".
    /// Never on a negation ("don't come here", "I don't want to meet the tester"), never without an agent
    /// ("MuseDesk, come here"), never when the sentence carries anything else ("Researcher, come here and fix the build").
    /// What it opens is only a preview the user must confirm on the card.
    /// </summary>
    public static class PortalVoiceRequest
    {
        static readonly string[][] ComePhrases = Split(
            "come and meet me", "come meet me", "come over here", "come on over", "get over here", "come over", "come here",
            "come to me", "over here", "meet me", "join me", "step out", "come out");

        static readonly HashSet<string> Polite = new HashSet<string>
        {
            "please", "pls", "now", "can", "could", "would", "will", "you", "quickly", "quick", "a", "sec", "second", "moment",
            "for", "just", "right", "ok", "okay", "hey", "here", "then", "so",
        };

        static readonly HashSet<string> Negators = new HashSet<string>
        {
            "dont", "not", "never", "no", "cant", "cannot", "wont", "shouldnt", "stop", "nobody", "without", "didnt", "doesnt",
        };

        static readonly HashSet<string> LeadIn = new HashSet<string>
        {
            "hey", "hi", "ok", "okay", "please", "lets", "let", "me", "i", "want", "to", "id", "like", "we", "so", "now",
        };

        static readonly HashSet<string> Joiners = new HashSet<string> { "the", "our", "my", "with", "your" };

        static readonly HashSet<string> Trailing = new HashSet<string>
        {
            "please", "pls", "now", "here", "larger", "large", "big", "bigger", "full", "size", "sized", "up", "close", "in", "person",
            "beside", "next", "to", "me",
        };

        static readonly HashSet<string> GenericAddress = new HashSet<string> { "musedesk", "muse", "desk", "team", "everyone", "everybody", "agents", "office" };

        /// <summary>
        /// The routing rule VoiceCommandCenter applies to a final sentence: only while somebody registered for portal requests
        /// (the Wall, while it is active with the portal flag on), never in the middle of a conversation the user opened (a
        /// decision countdown or a task confirmation keeps its own words), and only for a sentence <see cref="TryParse"/> accepts.
        /// </summary>
        public static bool Routes(string heard, bool handlerRegistered, bool conversationOpen, out VoiceAgentTarget agent)
        {
            agent = VoiceAgentTarget.None;
            if (!handlerRegistered || conversationOpen) return false;
            return TryParse(heard, out agent);
        }

        /// <summary>
        /// Hands-free gate: exactly <see cref="WakeWord.Allows"/>, except that a portal request (which names its agent, e.g.
        /// "meet the developer") counts as addressed. Room chatter ("you should meet the developer") is not a request.
        /// </summary>
        public static bool AllowsHandsFree(bool addressed, bool conversationOpen, bool haltingWord, bool portalRequest)
        {
            return WakeWord.Allows(addressed || portalRequest, conversationOpen, haltingWord);
        }

        /// <summary>True when <paramref name="heard"/> asks for one agent to step out; <paramref name="agent"/> says which.</summary>
        public static bool TryParse(string heard, out VoiceAgentTarget agent)
        {
            agent = VoiceAgentTarget.None;
            if (string.IsNullOrEmpty(heard)) return false;
            bool q;
            List<string> all = VoiceIntentClassifier.Tokenize(heard, out q);
            if (all.Count == 0 || all.Count > 12) return false;
            for (int i = 0; i < all.Count; i++) if (Negators.Contains(all[i])) return false;

            VoiceAgentTarget named; string rest;
            if (WakeWord.Parse(heard, out named, out rest))
            {
                List<string> r = VoiceIntentClassifier.Tokenize(rest, out q);
                if (named != VoiceAgentTarget.None && IsComeRequest(r, named)) { agent = named; return true; }
                // "Researcher, meet the tester" / "MuseDesk, meet the developer": the agent after "meet" is the one asked for.
                return MeetFirst(r, out agent);
            }
            return MeetFirst(all, out agent);
        }

        /// <summary>The words after the name: one come / meet phrase and nothing but polite filler (or the name again).</summary>
        static bool IsComeRequest(List<string> t, VoiceAgentTarget named)
        {
            if (t.Count == 0) return false;
            var used = new bool[t.Count];
            bool matched = false;
            for (int p = 0; p < ComePhrases.Length && !matched; p++)
            {
                string[] w = ComePhrases[p];
                for (int i = 0; i + w.Length <= t.Count && !matched; i++)
                {
                    bool ok = true;
                    for (int k = 0; k < w.Length && ok; k++) ok = t[i + k] == w[k];
                    if (!ok) continue;
                    for (int k = 0; k < w.Length; k++) used[i + k] = true;
                    matched = true;
                }
            }
            if (!matched) return false;
            for (int i = 0; i < t.Count; i++)
            {
                if (used[i] || Polite.Contains(t[i])) continue;
                if (VoiceIntentClassifier.FromId(t[i]) == named) continue;
                return false;
            }
            return true;
        }

        /// <summary>"[lead-in] meet [the|with the] &lt;agent&gt; [trailing]".</summary>
        static bool MeetFirst(List<string> t, out VoiceAgentTarget agent)
        {
            agent = VoiceAgentTarget.None;
            int i = 0;
            while (i < t.Count && GenericAddress.Contains(t[i]) && i < 2) i++;      // "MuseDesk, meet ..." when not already stripped
            int lead = 0;
            while (i < t.Count && lead < 3 && t[i] != "meet" && LeadIn.Contains(t[i])) { i++; lead++; }
            if (i >= t.Count || t[i] != "meet") return false;
            i++;
            int join = 0;
            while (i < t.Count && join < 2 && Joiners.Contains(t[i])) { i++; join++; }
            if (i >= t.Count) return false;
            VoiceAgentTarget a = VoiceIntentClassifier.FromId(t[i]);
            if (a == VoiceAgentTarget.None) return false;
            i++;
            for (; i < t.Count; i++) if (!Trailing.Contains(t[i]) && !Polite.Contains(t[i])) return false;
            agent = a;
            return true;
        }

        static string[][] Split(params string[] phrases)
        {
            var r = new string[phrases.Length][];
            for (int i = 0; i < phrases.Length; i++) r[i] = phrases[i].Split(' ');
            return r;
        }
    }
}
