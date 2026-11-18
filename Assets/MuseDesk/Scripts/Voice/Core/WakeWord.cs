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
        static readonly HashSet<string> WeakTails = new HashSet<string> { "a", "uh", "or" };
        static readonly HashSet<string> QuestionWords = new HashSet<string>
        {
            "why", "what", "how", "when", "where", "who", "which", "are", "is", "can", "could", "do", "did", "have", "has", "will",
            "would", "come", "meet", "show", "tell",
            // rc22: a command right after the sound-alike name is an address too ("Managers plan a login page").
            "please", "plan", "build", "make", "create", "status", "pause", "resume", "stop", "start", "help", "explain", "give",
            "let", "lets",
        };

        // rc22 (owner, worn rc21: "the mic needs to be less picky"): more of the ways the speech service writes a name it heard
        // fast. Only ever read in the first three words of a sentence (after at most two lead-in words), never anywhere else.
        /// <summary>Single words that ARE the name (not English words, so no extra condition): "testa", "managa", "researcha".</summary>
        static readonly Dictionary<string, VoiceAgentTarget> StrongAliases = new Dictionary<string, VoiceAgentTarget>
        {
            { "researcha", VoiceAgentTarget.Researcher }, { "researchar", VoiceAgentTarget.Researcher }, { "researchor", VoiceAgentTarget.Researcher },
            { "reseacher", VoiceAgentTarget.Researcher }, { "reasearcher", VoiceAgentTarget.Researcher }, { "researcherr", VoiceAgentTarget.Researcher },
            { "developa", VoiceAgentTarget.Developer }, { "developar", VoiceAgentTarget.Developer }, { "developor", VoiceAgentTarget.Developer },
            { "devloper", VoiceAgentTarget.Developer }, { "developper", VoiceAgentTarget.Developer }, { "develloper", VoiceAgentTarget.Developer },
            { "testa", VoiceAgentTarget.Tester }, { "testor", VoiceAgentTarget.Tester }, { "testar", VoiceAgentTarget.Tester },
            { "managa", VoiceAgentTarget.Manager }, { "manajer", VoiceAgentTarget.Manager }, { "mannager", VoiceAgentTarget.Manager },
            { "managr", VoiceAgentTarget.Manager }, { "manageer", VoiceAgentTarget.Manager }, { "managah", VoiceAgentTarget.Manager },
        };
        /// <summary>A name the service split into two or three words ("man ager", "man age her", "re searcher").</summary>
        static readonly Dictionary<string, VoiceAgentTarget> SplitNames = new Dictionary<string, VoiceAgentTarget>
        {
            { "man ager", VoiceAgentTarget.Manager }, { "man agar", VoiceAgentTarget.Manager }, { "man ajer", VoiceAgentTarget.Manager },
            { "man age her", VoiceAgentTarget.Manager }, { "man age er", VoiceAgentTarget.Manager }, { "man a ger", VoiceAgentTarget.Manager },
            { "man a jer", VoiceAgentTarget.Manager },
            { "re searcher", VoiceAgentTarget.Researcher }, { "re search her", VoiceAgentTarget.Researcher },
            { "de veloper", VoiceAgentTarget.Developer }, { "dev eloper", VoiceAgentTarget.Developer },
            { "tess ter", VoiceAgentTarget.Tester },
        };
        /// <summary>"MuseDesk" as the service writes it: an address to everyone (no agent).</summary>
        static readonly HashSet<string> SplitGeneric = new HashSet<string>
        {
            "muse desk", "muse desks", "use desk", "news desk", "music desk", "mused desk", "muse deck", "muse disk", "mews desk", "hey muse",
        };
        static readonly HashSet<string> GenericAliases = new HashSet<string> { "muse", "usedesk", "musedesks", "musedeck", "musedisk", "newsdesk" };
        /// <summary>Words that may come before the name (at most two of them): greetings, hesitations, "the", "please",
        /// "excuse me". Anything else first ("tell the researcher", "my manager said") is not an address.</summary>
        static readonly HashSet<string> LeadIns = new HashSet<string>
        {
            "hey", "hi", "hello", "ok", "okay", "yo", "um", "uh", "so", "the", "please", "excuse", "me", "oh", "alright", "well",
            "er", "erm", "hmm", "oi", "dear", "and", "now",
        };
        /// <summary>After "the" ("the developer ..."): the name counts only when a pause, a question or a command follows,
        /// so "the manager said we need cuts" stays room chatter.</summary>
        static readonly HashSet<string> AfterTheName = new HashSet<string>
        {
            "why", "what", "how", "when", "where", "who", "which", "please", "plan", "build", "make", "create", "status", "pause",
            "resume", "stop", "start", "help", "explain", "give", "come", "meet", "show", "tell",
        };

        /// <summary>
        /// True when the sentence starts with an address: a name in the first three words, after at most two lead-in words
        /// such as "hey", "okay", "um", "the", "please" (rc22). <paramref name="agent"/> is the named agent (None for
        /// "MuseDesk", "team", "everyone"). <paramref name="rest"/> is the sentence after the address, trimmed of commas and spaces.
        /// </summary>
        public static bool Parse(string heard, out VoiceAgentTarget agent, out string rest)
        {
            int at; bool exact;
            return ParseCore(heard, out agent, out rest, out at, out exact);
        }

        /// <param name="nameAt">Index of the first word of the name.</param>
        /// <param name="exact">The name word is already an exact agent word ("Researcher", "dev", "QA") and only greetings come before it.</param>
        static bool ParseCore(string heard, out VoiceAgentTarget agent, out string rest, out int nameAt, out bool exact)
        {
            agent = VoiceAgentTarget.None;
            rest = "";
            nameAt = -1;
            exact = false;
            if (string.IsNullOrEmpty(heard)) return false;
            List<Tok> t = Tokens(heard, 6);
            // The name may start at any of the first three words, as long as everything before it is a lead-in word.
            for (int i = 0; i < t.Count && i <= 2; i++)
            {
                if (TryNameAt(heard, t, i, out agent, out int end))
                {
                    nameAt = i;
                    bool greetingsOnly = true;
                    for (int k = 0; k < i; k++) if (!Greetings.Contains(t[k].w)) greetingsOnly = false;
                    exact = greetingsOnly && agent != VoiceAgentTarget.None && VoiceIntentClassifier.FromId(t[i].w) != VoiceAgentTarget.None
                            && end == t[i].end;
                    rest = heard.Substring(end).TrimStart(' ', ',', '.', ':', ';', '!', '-', '?').Trim();
                    return true;
                }
                if (!LeadIns.Contains(t[i].w)) return false;
            }
            agent = VoiceAgentTarget.None;
            return false;
        }

        static bool TryNameAt(string heard, List<Tok> t, int i, out VoiceAgentTarget agent, out int end)
        {
            agent = VoiceAgentTarget.None;
            end = t[i].end;
            string w = t[i].w;
            bool afterThe = i > 0 && t[i - 1].w == "the";

            // Two or three words that together are the name ("man ager", "use desk").
            for (int len = 3; len >= 2; len--)
            {
                if (i + len > t.Count) continue;
                var sb = new System.Text.StringBuilder(24);
                for (int k = 0; k < len; k++) { if (k > 0) sb.Append(' '); sb.Append(t[i + k].w); }
                string joined = sb.ToString();
                VoiceAgentTarget sa;
                if (SplitNames.TryGetValue(joined, out sa)) { agent = sa; end = t[i + len - 1].end; return true; }
                if (!afterThe && SplitGeneric.Contains(joined)) { end = t[i + len - 1].end; return true; }
            }

            if (!afterThe && (Generic.Contains(w) || GenericAliases.Contains(w)))
            {
                if (w == "muse" && i + 1 < t.Count && t[i + 1].w == "desk") end = t[i + 1].end;
                return true;
            }

            VoiceAgentTarget a = VoiceIntentClassifier.FromId(w);
            if (a == VoiceAgentTarget.None) StrongAliases.TryGetValue(w, out a);
            if (a != VoiceAgentTarget.None)
            {
                // "the developer ..." needs a pause, a question or a command next ("the developer said ..." is chatter).
                if (afterThe && !(PauseOrEnd(heard, t, i) || (i + 1 < t.Count && AfterTheName.Contains(t[i + 1].w)))) return false;
                agent = a;
                return true;
            }

            bool tail = i + 1 < t.Count && Tails.Contains(t[i + 1].w);
            // rc22: "a" / "uh" / "or" after a stem are also ordinary words ("research a new library", "test a login page"):
            // they complete the name only when a pause, a question or command word, or nothing follows them.
            if (tail && WeakTails.Contains(t[i + 1].w))
                tail = PauseOrEnd(heard, t, i + 1) || (i + 2 < t.Count && QuestionWords.Contains(t[i + 2].w));
            if (Stems.TryGetValue(w, out a))
            {
                // A bare stem ("research shows ...") is ordinary speech; it is a name only with a sound-alike tail,
                // a pause mark right after it ("Research, why"), a question or command word next, or nothing after it.
                bool question = i + 1 < t.Count && (afterThe ? AfterTheName : QuestionWords).Contains(t[i + 1].w);
                if (tail) end = t[i + 1].end;
                else if (!(PauseOrEnd(heard, t, i) || question)) return false;
                agent = a;
                return true;
            }
            if (tail && TailOnlyStems.TryGetValue(w, out a)) { agent = a; end = t[i + 1].end; return true; }
            agent = VoiceAgentTarget.None;
            return false;
        }

        static readonly HashSet<string> StrongTails = new HashSet<string> { "her", "er", "your", "ur" };
        static readonly HashSet<string> PluralNames = new HashSet<string> { "developers", "testers", "managers", "researchers" };

        /// <summary>rc22: the agent named by the LAST words of a token list ("come down here, developer", "come here manage
        /// her", "get over here testa"), the same forgiving spellings as at the start. <paramref name="start"/> is the index of
        /// the name's first word. Tokens as <see cref="VoiceIntentClassifier.Tokenize"/> makes them (lower case, no apostrophes).
        /// Used only for sentences that are otherwise a "come here" request (PortalVoiceRequest), never as a general address.</summary>
        public static VoiceAgentTarget NameAtEnd(IList<string> t, out int start)
        {
            start = -1;
            if (t == null || t.Count == 0) return VoiceAgentTarget.None;
            int n = t.Count;
            VoiceAgentTarget a;
            for (int len = 3; len >= 2; len--)
            {
                if (n < len) continue;
                string joined = t[n - len];
                for (int k = n - len + 1; k < n; k++) joined += " " + t[k];
                if (SplitNames.TryGetValue(joined, out a)) { start = n - len; return a; }
            }
            if (n >= 2 && StrongTails.Contains(t[n - 1]) && (Stems.TryGetValue(t[n - 2], out a) || TailOnlyStems.TryGetValue(t[n - 2], out a)))
            { start = n - 2; return a; }
            string w = t[n - 1];
            a = VoiceIntentClassifier.FromId(w);
            if (a == VoiceAgentTarget.None && !StrongAliases.TryGetValue(w, out a) && PluralNames.Contains(w)) Stems.TryGetValue(w, out a);
            if (a == VoiceAgentTarget.None) return VoiceAgentTarget.None;
            start = n - 1;
            return a;
        }

        /// <summary>A pause mark right after word <paramref name="i"/>, or nothing after it.</summary>
        static bool PauseOrEnd(string heard, List<Tok> t, int i)
        {
            if (i + 1 >= t.Count) return true;
            int end = t[i].end;
            return end < heard.Length && (heard[end] == ',' || heard[end] == '.' || heard[end] == '?' || heard[end] == '!' || heard[end] == ':' || heard[end] == ';');
        }

        /// <summary>The sentence with a sound-alike leading name replaced by the real one ("Research your why" ->
        /// "Researcher, why"; rc22: "the developer, status" -> "Developer, status", "man ager plan X" -> "Manager, plan X"),
        /// so every later reader (grammar, classifier, portal) sees the name. Unchanged when the sentence does not start with
        /// an agent's name, or already starts with the exact word after nothing but greetings.</summary>
        public static string Canonical(string heard)
        {
            VoiceAgentTarget a; string rest; int at; bool exact;
            if (!ParseCore(heard, out a, out rest, out at, out exact) || a == VoiceAgentTarget.None) return heard;
            if (exact) return heard;   // already the name
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
                if (VoiceIntentClassifier.FromId(t[i].w) != VoiceAgentTarget.None || t[i].w == "research" || StrongAliases.ContainsKey(t[i].w)) return true;
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
