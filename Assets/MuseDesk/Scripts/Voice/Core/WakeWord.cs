using System.Collections.Generic;

namespace MuseDesk.Voice
{
    /// <summary>
    /// Hands-free addressing rule (owner decision, rc8 round 1): with the mic left on, a sentence only counts when it
    /// starts by naming who it is for, e.g. "Researcher, why this source?", "Manager, plan a login page",
    /// "Hey MuseDesk, pause". Room chatter ("I'd approve that", "stop it") is ignored, so nothing is approved,
    /// rejected or sent because somebody nearby said a command word.
    /// Exceptions (see <see cref="Allows"/>): halting words (stop / cancel) are always heard, because they only ever
    /// make things safer; and the follow-up of a conversation the user already opened by name (a decision countdown,
    /// a task confirmation) does not need the name again.
    /// Pure: no Unity types, fully testable.
    /// </summary>
    public static class WakeWord
    {
        static readonly HashSet<string> Greetings = new HashSet<string> { "hey", "hi", "hello", "ok", "okay", "yo", "um", "uh", "so" };
        static readonly HashSet<string> Generic = new HashSet<string> { "musedesk", "desk", "team", "everyone", "everybody", "agents", "office" };

        /// <summary>The words in the hint, so the user learns the rule.</summary>
        public const string Hint = "Start with a name: \"Researcher, why?\"";

        struct Tok { public string w; public int end; }

        /// <summary>
        /// True when the sentence starts with an address (after at most two filler words like "hey"/"okay").
        /// <paramref name="agent"/> is the named agent (None for "MuseDesk", "team", "everyone").
        /// <paramref name="rest"/> is the sentence after the address, trimmed of commas and spaces.
        /// </summary>
        public static bool Parse(string heard, out VoiceAgentTarget agent, out string rest)
        {
            agent = VoiceAgentTarget.None;
            rest = "";
            if (string.IsNullOrEmpty(heard)) return false;
            List<Tok> t = Tokens(heard, 5);
            int i = 0;
            while (i < t.Count && i < 2 && Greetings.Contains(t[i].w)) i++;
            if (i >= t.Count) return false;

            int end;
            string w = t[i].w;
            if (w == "muse" && i + 1 < t.Count && t[i + 1].w == "desk") end = t[i + 1].end;
            else if (Generic.Contains(w)) end = t[i].end;
            else
            {
                VoiceAgentTarget a = VoiceIntentClassifier.FromId(w);
                if (a == VoiceAgentTarget.None) return false;
                agent = a;
                end = t[i].end;
            }
            rest = heard.Substring(end).TrimStart(' ', ',', '.', ':', ';', '!', '-').Trim();
            return true;
        }

        public static bool IsAddressed(string heard)
        {
            VoiceAgentTarget a; string r;
            return Parse(heard, out a, out r);
        }

        /// <summary>
        /// Whether a hands-free sentence may act. Manual (key-pressed) sentences never go through this.
        /// </summary>
        /// <param name="addressed">The sentence started with a name (<see cref="Parse"/>).</param>
        /// <param name="conversationOpen">A decision countdown or task confirmation the user opened is waiting.</param>
        /// <param name="haltingWord">The sentence is stop / cancel.</param>
        public static bool Allows(bool addressed, bool conversationOpen, bool haltingWord)
        {
            return addressed || conversationOpen || haltingWord;
        }

        /// <summary>The first <paramref name="max"/> word tokens, lower-cased, with the index just past each in the source.</summary>
        static List<Tok> Tokens(string s, int max)
        {
            var list = new List<Tok>(max);
            int i = 0, n = s.Length;
            while (i < n && list.Count < max)
            {
                while (i < n && !IsWordChar(s[i])) i++;
                if (i >= n) break;
                int start = i;
                while (i < n && IsWordChar(s[i])) i++;
                string w = s.Substring(start, i - start).ToLowerInvariant();
                if (w.EndsWith("'s")) w = w.Substring(0, w.Length - 2);
                list.Add(new Tok { w = w, end = i });
            }
            return list;
        }

        static bool IsWordChar(char c) { return char.IsLetterOrDigit(c) || c == '\''; }
    }
}
