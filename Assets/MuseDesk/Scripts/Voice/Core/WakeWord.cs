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

        // Worn rc15: the owner said "Researcher, why?" and nothing happened; the speech service can split a name it hears
        // fast ("Research your why", "Research, why", "Develop her ...", "Test her ...", "Manage her ..."). Only at the START
        // of a sentence, a stem plus an optional sound-alike tail is read as the name. Never anywhere else in a sentence.
        static readonly Dictionary<string, VoiceAgentTarget> Stems = new Dictionary<string, VoiceAgentTarget>
        {
            { "research", VoiceAgentTarget.Researcher }, { "researching", VoiceAgentTarget.Researcher },
            { "develop", VoiceAgentTarget.Developer }, { "developers", VoiceAgentTarget.Developer }, { "developed", VoiceAgentTarget.Developer },
            { "testa", VoiceAgentTarget.Tester }, { "testers", VoiceAgentTarget.Tester }, { "taster", VoiceAgentTarget.Tester }, { "toaster", VoiceAgentTarget.Tester },
            { "managers", VoiceAgentTarget.Manager }, { "manger", VoiceAgentTarget.Manager },
        };
        // "test"/"manage" alone are real words ("test the login"), so they only count with a sound-alike tail.
        static readonly Dictionary<string, VoiceAgentTarget> TailOnlyStems = new Dictionary<string, VoiceAgentTarget>
        {
            { "test", VoiceAgentTarget.Tester }, { "manage", VoiceAgentTarget.Manager },
        };
        static readonly HashSet<string> Tails = new HashSet<string> { "her", "er", "your", "ur", "a", "uh", "or" };
        static readonly HashSet<string> QuestionWords = new HashSet<string>
            { "why", "what", "how", "when", "where", "who", "which", "are", "is", "can", "could", "do", "did", "have", "has", "will", "would", "come", "meet", "show", "tell" };

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
                end = t[i].end;
                if (a == VoiceAgentTarget.None)
                {
                    bool tail = i + 1 < t.Count && Tails.Contains(t[i + 1].w);
                    if (Stems.TryGetValue(w, out a))
                    {
                        // A bare stem ("research shows ...") is ordinary speech; it is a name only with a sound-alike tail,
                        // a pause mark right after it ("Research, why"), a question word next, or nothing after it.
                        bool pause = end < heard.Length && (heard[end] == ',' || heard[end] == '.' || heard[end] == '?' || heard[end] == '!');
                        bool question = i + 1 < t.Count && QuestionWords.Contains(t[i + 1].w);
                        bool alone = i + 1 >= t.Count;
                        if (tail) end = t[i + 1].end;
                        else if (!(pause || question || alone)) return false;
                    }
                    else if (tail && TailOnlyStems.TryGetValue(w, out a)) end = t[i + 1].end;
                    else return false;
                }
                agent = a;
            }
            rest = heard.Substring(end).TrimStart(' ', ',', '.', ':', ';', '!', '-').Trim();
            return true;
        }

        /// <summary>The sentence with a sound-alike leading name replaced by the real one ("Research your why" ->
        /// "Researcher, why"), so every later reader (grammar, classifier, portal) sees the name. Unchanged when the
        /// sentence does not start with an agent's name or already uses the exact word.</summary>
        public static string Canonical(string heard)
        {
            VoiceAgentTarget a; string rest;
            if (!Parse(heard, out a, out rest) || a == VoiceAgentTarget.None) return heard;
            List<Tok> t = Tokens(heard, 3);
            int i = 0;
            while (i < t.Count && i < 2 && Greetings.Contains(t[i].w)) i++;
            if (i < t.Count && VoiceIntentClassifier.FromId(t[i].w) != VoiceAgentTarget.None) return heard;   // already the name
            string name = a == VoiceAgentTarget.Researcher ? "Researcher" : a == VoiceAgentTarget.Developer ? "Developer"
                        : a == VoiceAgentTarget.Tester ? "Tester" : "Manager";
            return rest.Length > 0 ? name + ", " + rest : name;
        }

        /// <summary>A hands-free sentence that was NOT addressed but clearly mentions an agent ("why researcher", "the
        /// developer should..."): worth a quiet "start with a name" hint instead of silence.</summary>
        public static bool MentionsAgent(string heard)
        {
            if (string.IsNullOrEmpty(heard)) return false;
            List<Tok> t = Tokens(heard, 12);
            for (int i = 0; i < t.Count; i++)
                if (VoiceIntentClassifier.FromId(t[i].w) != VoiceAgentTarget.None || t[i].w == "research") return true;
            return false;
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
