using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MuseDesk.Voice
{
    /// <summary>What a spoken sentence asks for.</summary>
    public enum VoiceIntentKind
    {
        None = 0,
        Stop, Cancel, Confirm,
        Approve, Reject, RequestChanges, DecisionNotPending,
        AskWhy, ShowDetails, HideDetails, TakePaper,
        Start, RunAgain,
        OpenBoard, CloseBoard,
        ViewResult, Close, Next, Previous, GoToPage,
        FocusOn, FocusOff,
        ModeDesk, ModeFloor, ModeCeiling, NextSpot, SpotNumber,
        LowerPod, RaisePod, BringHere, SendBack,
        Status, WhoNeedsMe, AskAgent,
        ResetLayout, Help,
        CreateTask,
    }

    /// <summary>What is true in the office when the sentence was heard. The parser uses it only to
    /// make a sentence MORE restricted (a decision word means nothing without a decision; "yes"
    /// means nothing without a question), never less.</summary>
    [Flags]
    public enum VoiceContext
    {
        None = 0,
        /// <summary>A decision is awaiting the user.</summary>
        DecisionPending = 1,
        /// <summary>The voice layer asked "confirm or cancel?" (a new task title).</summary>
        ConfirmationPending = 2,
        /// <summary>A floor / ceiling placement preview is open.</summary>
        Previewing = 4,
        /// <summary>The office is moving.</summary>
        Moving = 8,
        ViewerOpen = 16,
        BoardOpen = 32,
        Live = 64,
        /// <summary>A spoken decision is counting down ("Approving... say cancel").</summary>
        UndoWindow = 128,
    }

    /// <summary>The result of parsing one transcript.</summary>
    public struct VoiceIntent
    {
        public VoiceIntentKind Kind;
        /// <summary>Agent id the sentence named ("researcher", "developer", "tester"), or null.</summary>
        public string Role;
        /// <summary>The task title for <see cref="VoiceIntentKind.CreateTask"/>; the decision word for
        /// <see cref="VoiceIntentKind.DecisionNotPending"/>; otherwise null.</summary>
        public string Text;
        /// <summary>The number for GoToPage / SpotNumber (1-based), else 0.</summary>
        public int Number;
        /// <summary>CreateTask: true when the sentence said "new task ..." (not a bare instruction).</summary>
        public bool Explicit;
        /// <summary>The transcript as heard.</summary>
        public string Raw;

        public override string ToString()
        {
            switch (Kind)
            {
                case VoiceIntentKind.AskAgent: return Kind + ":" + Role;
                case VoiceIntentKind.AskWhy: return string.IsNullOrEmpty(Role) ? Kind.ToString() : Kind + ":" + Role;
                case VoiceIntentKind.CreateTask: return Kind + ":" + (Text ?? "");
                case VoiceIntentKind.DecisionNotPending: return Kind + ":" + (Text ?? "");
                case VoiceIntentKind.GoToPage:
                case VoiceIntentKind.SpotNumber: return Kind + ":" + Number;
                default: return Kind.ToString();
            }
        }
    }

    /// <summary>
    /// Turns a transcript into an intent with a local keyword grammar, so voice works with any
    /// Wit.ai app token (nothing depends on trained Wit intents). Plain C#: no Unity types, tested in
    /// EditMode (VoiceIntentParserTests).
    ///
    /// Design rules:
    ///  - A decision (approve / reject / request changes) is the hardest thing to say. EVERY word of
    ///    the sentence must be on a short allow list and one of them must be the decision word, so
    ///    "I don't approve of this layout", "should I approve it?" and "they rejected my idea" are
    ///    not decisions. Without a pending decision the result is DecisionNotPending, never Approve.
    ///  - "yes" / "no" mean Confirm / Cancel only while the voice layer is waiting for an answer.
    ///  - "stop" wins over everything while the office is moving.
    ///  - Other commands are matched as whole sentences (after polite lead-ins are removed), so a
    ///    command word inside ordinary speech does nothing.
    /// </summary>
    public static class VoiceIntentParser
    {
        // ───────────────────────── entry points ─────────────────────────

        public static VoiceIntent Parse(string transcript, VoiceContext context)
        {
            var intent = new VoiceIntent { Kind = VoiceIntentKind.None, Raw = transcript ?? "" };
            if (string.IsNullOrWhiteSpace(transcript)) return intent;

            // Words as heard (for a task title) and their matching forms, index for index.
            var rawAll = new List<string>(16);
            var all = new List<string>(16);
            Tokenize(transcript, rawAll, all);
            if (all.Count == 0) return intent;

            // The agent the sentence names, wherever it is.
            string roleAnywhere = null;
            for (int i = 0; i < all.Count && roleAnywhere == null; i++) roleAnywhere = RoleOf(all[i]);

            // Working copy: polite lead-ins and trailers removed, a leading / trailing agent name removed.
            var raw = new List<string>(rawAll);
            var t = new List<string>(all);
            StripLead(raw, t);
            string role = null;
            if (t.Count > 0 && RoleOf(t[0]) != null)
            {
                role = RoleOf(t[0]);
                raw.RemoveAt(0); t.RemoveAt(0);
                if (t.Count > 0 && (t[0] == "agent" || t[0] == "bot")) { raw.RemoveAt(0); t.RemoveAt(0); }
                StripLead(raw, t);
            }
            StripTrail(raw, t);
            // A trailing agent name ("status, Developer") is dropped for command matching only; the
            // task title keeps it ("add a login page for the tester").
            var m = new List<string>(t);
            if (m.Count > 0 && RoleOf(m[m.Count - 1]) != null && role == null)
            {
                role = RoleOf(m[m.Count - 1]);
                m.RemoveAt(m.Count - 1);
                if (m.Count > 0 && m[m.Count - 1] == "the") m.RemoveAt(m.Count - 1);
                StripTrail(null, m);
            }
            if (role == null) role = roleAnywhere;
            intent.Role = role;

            string c = Merge(string.Join(" ", m));          // the sentence the command patterns see

            // 1. "stop focus" is not a movement stop.
            if (FocusOff.IsMatch(c)) return With(intent, VoiceIntentKind.FocusOff);

            // 2. Stop. Highest priority; liberal while the office moves.
            VoiceIntentKind stop = StopRule(t, c, context);
            if (stop != VoiceIntentKind.None) return With(intent, stop);

            // 3. An answer to "confirm or cancel?" (only while one is being asked).
            if ((context & (VoiceContext.ConfirmationPending | VoiceContext.UndoWindow)) != 0)
            {
                VoiceIntentKind answer = AnswerRule(all);
                if (answer != VoiceIntentKind.None) return With(intent, answer);
            }

            // 4. A decision. Strict allow list over the whole sentence (agent names aside).
            string decisionWord = null;
            VoiceIntentKind decision = VoiceIntentKind.None;
            // "approve it?" is a question, not a decision.
            if (!transcript.TrimEnd().EndsWith("?", StringComparison.Ordinal)) decision = DecisionRule(all, out decisionWord);
            if (decision != VoiceIntentKind.None)
            {
                if ((context & VoiceContext.DecisionPending) == 0)
                {
                    intent.Text = decisionWord;
                    return With(intent, VoiceIntentKind.DecisionNotPending);
                }
                return With(intent, decision);
            }

            if (m.Count == 0)
            {
                // Only an agent's name (and polite words): "Developer?"
                if (role != null) return With(intent, VoiceIntentKind.AskAgent);
                return intent;
            }

            // 5. Whole-sentence commands, in priority order.
            for (int i = 0; i < Table.Length; i++)
            {
                if (!Table[i].Pattern.IsMatch(c)) continue;
                VoiceIntentKind kind = Table[i].Kind;
                if (kind == VoiceIntentKind.Previous && (context & VoiceContext.ViewerOpen) == 0)
                {
                    // "back" / "go back" with nothing to page through: only "go back" means the office.
                    if (c == "go back") return With(intent, VoiceIntentKind.SendBack);
                    continue;
                }
                if ((kind == VoiceIntentKind.Status || kind == VoiceIntentKind.WhoNeedsMe) && role != null)
                    kind = VoiceIntentKind.AskAgent;
                return With(intent, kind);
            }

            // 6. Numbers: "page two", "spot 3".
            Match pm = PageNumber.Match(c);
            if (pm.Success)
            {
                int n = NumberOf(pm.Groups[2].Value);
                if (n > 0)
                {
                    intent.Number = n;
                    return With(intent, pm.Groups[1].Value == "page" ? VoiceIntentKind.GoToPage : VoiceIntentKind.SpotNumber);
                }
            }

            // 7. Pod (any sentence that names the pod and a direction, questions aside).
            VoiceIntentKind pod = PodRule(t);
            if (pod != VoiceIntentKind.None) return With(intent, pod);

            // 8. A question about the decision: "why ...", "what if ...".
            if (WhyQuestion.IsMatch(c)) return With(intent, VoiceIntentKind.AskWhy);

            // 9. A question to one agent: "Developer, what are you working on?", "is the tester done?"
            if (role != null && AgentCue.IsMatch(c)) return With(intent, VoiceIntentKind.AskAgent);

            // 10. A new task: "new task: ..." or a bare instruction ("add a dark mode toggle").
            string title; bool isExplicit;
            if (TaskRule(raw, t, out title, out isExplicit))
            {
                intent.Text = title;
                intent.Explicit = isExplicit;
                return With(intent, VoiceIntentKind.CreateTask);
            }
            return intent;
        }

        /// <summary>Parse and describe as text ("Approve", "AskAgent:developer", "CreateTask:Add a
        /// dark mode toggle", "GoToPage:2", "None"). <paramref name="context"/> is a
        /// <see cref="VoiceContext"/> value. For the EditMode tests, which call it by reflection.</summary>
        public static string Describe(string transcript, int context)
        {
            return Parse(transcript, (VoiceContext)context).ToString();
        }

        /// <summary>The words as the matcher sees them (lower case, no punctuation).</summary>
        public static string Normalize(string transcript)
        {
            var raw = new List<string>(); var all = new List<string>();
            Tokenize(transcript ?? "", raw, all);
            return string.Join(" ", all);
        }

        static VoiceIntent With(VoiceIntent intent, VoiceIntentKind kind) { intent.Kind = kind; return intent; }

        // ───────────────────────── words ─────────────────────────

        static void Tokenize(string text, List<string> raw, List<string> norm)
        {
            var sb = new StringBuilder(24);
            int i = 0, n = text.Length;
            while (i < n)
            {
                while (i < n && char.IsWhiteSpace(text[i])) i++;
                int start = i;
                while (i < n && !char.IsWhiteSpace(text[i])) i++;
                if (i <= start) break;
                // Trim punctuation from both ends of the piece, keep what is inside ("don't").
                int a = start, b = i - 1;
                while (a <= b && !char.IsLetterOrDigit(text[a])) a++;
                while (b >= a && !char.IsLetterOrDigit(text[b])) b--;
                if (b < a) continue;
                sb.Length = 0;
                for (int k = a; k <= b; k++)
                {
                    char ch = text[k];
                    if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
                    else if (ch == '/' || ch == ',' || ch == ';' || ch == ':')
                    {
                        // "approve/reject", "developer,what": a boundary the recogniser did not space.
                        if (sb.Length > 0) { norm.Add(Canon(sb.ToString())); raw.Add(text.Substring(a, k - a)); }
                        sb.Length = 0;
                        a = k + 1;
                    }
                    // apostrophes, hyphens and dots inside a word are dropped: don't -> dont, o.k. -> ok
                }
                if (sb.Length > 0) { norm.Add(Canon(sb.ToString())); raw.Add(text.Substring(a, b - a + 1)); }
            }
        }

        static string Canon(string w)
        {
            switch (w)
            {
                case "okay": case "okey": case "kay": return "ok";
                case "yea": case "yeh": case "ya": return "yeah";
                case "cannot": return "cant";
                case "gonna": return "going";
                default: return w;
            }
        }

        // Two-word forms the recogniser may or may not contract.
        static string Merge(string s)
        {
            if (s.Length == 0) return s;
            s = " " + s + " ";
            s = s.Replace(" what is ", " whats ").Replace(" how is ", " hows ").Replace(" who is ", " whos ")
                 .Replace(" i am ", " im ").Replace(" do not ", " dont ").Replace(" that is ", " thats ")
                 .Replace(" let us ", " lets ").Replace(" can not ", " cant ");
            return s.Trim();
        }

        static string RoleOf(string w)
        {
            switch (w)
            {
                case "researcher": case "researchers": return "researcher";
                case "developer": case "developers": case "dev": case "coder": case "programmer": case "engineer": return "developer";
                case "tester": case "testers": case "qa": return "tester";
                default: return null;
            }
        }

        static readonly HashSet<string> LeadWords = new HashSet<string>
        {
            "hey", "hi", "hello", "ok", "please", "um", "uh", "er", "erm", "so", "well", "now", "and", "then",
            "alright", "computer", "muse", "musedesk", "just", "kindly", "yo",
        };
        static readonly string[][] LeadPhrases =
        {
            new[] { "can", "you" }, new[] { "could", "you" }, new[] { "would", "you" }, new[] { "will", "you" },
            new[] { "can", "we" }, new[] { "could", "we" },
            new[] { "i", "want", "you", "to" }, new[] { "i", "want", "to" }, new[] { "i", "need", "you", "to" },
            new[] { "id", "like", "you", "to" }, new[] { "id", "like", "to" },
            new[] { "i", "would", "like", "you", "to" }, new[] { "i", "would", "like", "to" },
            new[] { "go", "ahead", "and" }, new[] { "you", "can" }, new[] { "muse", "desk" },
        };

        static void StripLead(List<string> raw, List<string> t)
        {
            bool again = true;
            while (again && t.Count > 0)
            {
                again = false;
                if (LeadWords.Contains(t[0])) { raw.RemoveAt(0); t.RemoveAt(0); again = true; continue; }
                for (int p = 0; p < LeadPhrases.Length; p++)
                {
                    string[] ph = LeadPhrases[p];
                    if (t.Count <= ph.Length) continue;         // never strip the whole sentence
                    bool ok = true;
                    for (int k = 0; k < ph.Length; k++) if (t[k] != ph[k]) { ok = false; break; }
                    if (!ok) continue;
                    raw.RemoveRange(0, ph.Length); t.RemoveRange(0, ph.Length);
                    again = true;
                    break;
                }
            }
        }

        static void StripTrail(List<string> raw, List<string> t)
        {
            bool again = true;
            while (again && t.Count > 1)
            {
                again = false;
                int n = t.Count;
                string last = t[n - 1];
                if (last == "please" || last == "thanks" || last == "ok" || last == "then" || last == "already")
                { if (raw != null) raw.RemoveAt(n - 1); t.RemoveAt(n - 1); again = true; continue; }
                if (n > 2)
                {
                    string two = t[n - 2] + " " + last;
                    if (two == "thank you" || two == "right now")
                    { if (raw != null) raw.RemoveRange(n - 2, 2); t.RemoveRange(n - 2, 2); again = true; }
                }
            }
        }

        static bool AllIn(List<string> words, HashSet<string> allowed, HashSet<string> alsoAllowed = null)
        {
            for (int i = 0; i < words.Count; i++)
            {
                string w = words[i];
                if (allowed.Contains(w)) continue;
                if (alsoAllowed != null && alsoAllowed.Contains(w)) continue;
                if (RoleOf(w) != null) continue;                // "Researcher, approve it"
                return false;
            }
            return true;
        }

        static bool Has(List<string> words, HashSet<string> any)
        {
            for (int i = 0; i < words.Count; i++) if (any.Contains(words[i])) return true;
            return false;
        }

        static bool HasPhrase(List<string> words, string a, string b)
        {
            for (int i = 0; i + 1 < words.Count; i++) if (words[i] == a && words[i + 1] == b) return true;
            return false;
        }

        // ───────────────────────── stop ─────────────────────────

        static readonly HashSet<string> StopWords = new HashSet<string> { "stop", "halt", "freeze", "whoa" };
        static readonly HashSet<string> StopNegators = new HashSet<string> { "dont", "not", "never", "cant", "without", "wont", "non", "doesnt", "didnt" };
        static readonly HashSet<string> StopOtherThings = new HashSet<string>
        {
            "focus", "focusing", "listening", "recording", "talking", "demo", "run", "task", "tests", "test",
            "work", "working", "sign", "button", "word",
        };
        static readonly HashSet<string> StopSentence = new HashSet<string>
        {
            "stop", "halt", "freeze", "whoa", "it", "that", "moving", "the", "office", "pod", "now", "right",
            "there", "everything", "please", "move", "hold", "all", "movement", "ok", "no", "wait", "hey",
        };

        static VoiceIntentKind StopRule(List<string> t, string c, VoiceContext context)
        {
            bool moving = (context & VoiceContext.Moving) != 0;
            int at = -1;
            for (int i = 0; i < t.Count; i++) if (StopWords.Contains(t[i])) { at = i; break; }
            if (at >= 0)
            {
                bool negated = at > 0 && StopNegators.Contains(t[at - 1]);
                if (!negated)
                {
                    string next = at + 1 < t.Count ? t[at + 1] : null;
                    if (next == "the" && at + 2 < t.Count) next = t[at + 2];
                    if (next == "listening" || next == "recording" || next == "talking") return VoiceIntentKind.Cancel;
                    bool other = next != null && StopOtherThings.Contains(next);
                    if (moving && !other) return VoiceIntentKind.Stop;
                    if (!other && t.Count <= 5 && AllIn(t, StopSentence)) return VoiceIntentKind.Stop;
                }
            }
            // While the office moves, "wait" and "hold on" mean the same thing.
            if (moving && (c == "wait" || c == "wait wait" || c == "hold on" || c == "hold it" || c == "hold" || c == "no no" || c == "no wait"))
                return VoiceIntentKind.Stop;
            return VoiceIntentKind.None;
        }

        // ───────────────────────── confirm / cancel while a question is open ─────────────────────────

        static readonly HashSet<string> CancelKeys = new HashSet<string>
        { "cancel", "no", "nope", "nah", "nevermind", "undo", "wait", "abort", "stop", "dont", "not", "scrap", "discard" };
        static readonly HashSet<string> CancelSentence = new HashSet<string>
        {
            "cancel", "no", "nope", "nah", "never", "mind", "nevermind", "undo", "wait", "hold", "on", "dont", "do",
            "not", "scrap", "forget", "it", "that", "stop", "abort", "back", "go", "please", "actually", "discard",
            "send", "this", "the", "task", "oh", "sorry", "um", "uh", "ok", "hey", "yet", "now",
        };
        static readonly HashSet<string> ConfirmKeys = new HashSet<string>
        { "confirm", "confirmed", "yes", "yeah", "yep", "yup", "ok", "sure", "correct", "affirmative", "absolutely", "definitely" };
        static readonly HashSet<string> ConfirmSentence = new HashSet<string>
        {
            "confirm", "confirmed", "yes", "yeah", "yep", "yup", "ok", "sure", "correct", "right", "do", "it", "go",
            "ahead", "send", "thats", "that", "is", "please", "create", "add", "sounds", "good", "looks",
            "affirmative", "absolutely", "definitely", "the", "task", "great", "perfect", "fine", "hey", "um", "uh",
        };

        static VoiceIntentKind AnswerRule(List<string> all)
        {
            if (all.Count == 0 || all.Count > 6) return VoiceIntentKind.None;
            bool cancel = Has(all, CancelKeys) || HasPhrase(all, "never", "mind") || HasPhrase(all, "hold", "on")
                          || HasPhrase(all, "forget", "it") || HasPhrase(all, "go", "back");
            if (cancel) return AllIn(all, CancelSentence, ConfirmSentence) ? VoiceIntentKind.Cancel : VoiceIntentKind.None;
            bool confirm = Has(all, ConfirmKeys) || HasPhrase(all, "do", "it") || HasPhrase(all, "go", "ahead")
                           || HasPhrase(all, "send", "it") || HasPhrase(all, "thats", "right") || HasPhrase(all, "that", "is")
                           || HasPhrase(all, "sounds", "good") || HasPhrase(all, "looks", "good")
                           || HasPhrase(all, "create", "it") || HasPhrase(all, "add", "it");
            if (confirm && AllIn(all, ConfirmSentence)) return VoiceIntentKind.Confirm;
            return VoiceIntentKind.None;
        }

        // ───────────────────────── decisions ─────────────────────────

        static readonly HashSet<string> ApproveKeys = new HashSet<string> { "approve", "approved", "approving", "accept", "accepted" };
        static readonly HashSet<string> RejectKeys = new HashSet<string> { "reject", "rejected", "rejecting", "decline", "declined", "deny", "denied" };
        static readonly HashSet<string> DecisionFiller = new HashSet<string>
        {
            "i", "we", "ill", "im", "it", "this", "that", "the", "plan", "proposal", "decision", "paper", "request",
            "recommendation", "change", "option", "one", "lets", "just", "please", "now", "ok", "so", "well", "um",
            "uh", "hey", "then", "alright",
        };
        // A past-tense word is a command only on its own ("Approved."), never inside a sentence
        // ("the developer approved the plan").
        static readonly HashSet<string> PastKeys = new HashSet<string> { "approved", "accepted", "rejected", "declined", "denied" };
        static readonly HashSet<string> ChangeKeys = new HashSet<string> { "changes", "change", "revise", "revision", "revisions" };
        static readonly HashSet<string> ChangeVerbs = new HashSet<string>
        { "request", "requesting", "requested", "needs", "need", "make", "ask", "revise", "revision", "revisions" };

        // Every word of a decision sentence must be on its list.
        static readonly HashSet<string> ApproveSentence = Join(DecisionFiller, ApproveKeys, new HashSet<string>
        { "go", "ahead", "and", "do", "yes", "yeah", "yep", "sure", "fine", "looks", "sounds", "good", "great", "ship", "changes" });
        static readonly HashSet<string> RejectSentence = Join(DecisionFiller, RejectKeys, new HashSet<string> { "no", "nope" });
        static readonly HashSet<string> ChangeSentence = Join(DecisionFiller, Join(ChangeKeys, ChangeVerbs, null), new HashSet<string>
        { "id", "like", "to", "want", "a", "some", "send", "back", "more", "work", "for" });

        static HashSet<string> Join(HashSet<string> a, HashSet<string> b, HashSet<string> c)
        {
            var u = new HashSet<string>(a);
            if (b != null) u.UnionWith(b);
            if (c != null) u.UnionWith(c);
            return u;
        }

        static VoiceIntentKind DecisionRule(List<string> all, out string word)
        {
            word = null;
            if (all.Count == 0 || all.Count > 9) return VoiceIntentKind.None;
            bool approve = Has(all, ApproveKeys) || HasPhrase(all, "ship", "it");
            bool reject = Has(all, RejectKeys);
            // "changes" on its own is the paper's Changes key; otherwise a verb is needed.
            bool change = (Has(all, ChangeKeys) && Has(all, ChangeVerbs)) || (all.Count <= 2 && all[0] == "changes");
            if (approve && change && !reject) change = false;       // "approve the change" names what is approved
            int kinds = (approve ? 1 : 0) + (reject ? 1 : 0) + (change ? 1 : 0);
            if (kinds != 1) return VoiceIntentKind.None;
            if (Has(all, PastKeys) && all.Count > 2) return VoiceIntentKind.None;
            if (approve)
            {
                if (Has(all, ChangeVerbs) || !AllIn(all, ApproveSentence)) return VoiceIntentKind.None;
                word = "approve";
                return VoiceIntentKind.Approve;
            }
            if (reject)
            {
                if (!AllIn(all, RejectSentence)) return VoiceIntentKind.None;
                word = "reject";
                return VoiceIntentKind.Reject;
            }
            if (!AllIn(all, ChangeSentence)) return VoiceIntentKind.None;
            word = "request changes";
            return VoiceIntentKind.RequestChanges;
        }

        // ───────────────────────── whole-sentence commands ─────────────────────────

        struct Rule
        {
            public VoiceIntentKind Kind; public Regex Pattern;
            public Rule(VoiceIntentKind kind, string pattern)
            {
                Kind = kind;
                Pattern = new Regex("^(?:" + pattern + ")$", RegexOptions.CultureInvariant);
            }
        }

        const string It = "(?:it|that|this|the office|my office|the workspace|them|everyone|the agents|everything|the team)";

        static readonly Regex FocusOff = new Regex(
            "^(?:focus off|focus mode off|turn off (?:the )?focus(?: mode)?|turn (?:the )?focus(?: mode)? off|disable focus(?: mode)?|stop focus(?: mode)?|stop focusing|end focus(?: mode)?|exit focus(?: mode)?|leave focus(?: mode)?|unfocus|im back|im done focusing|quiet mode off|normal mode|show (?:me )?(?:the |my )?updates)$",
            RegexOptions.CultureInvariant);

        static readonly Rule[] Table =
        {
            new Rule(VoiceIntentKind.Help,
                "help|help me|what can i say|what can i do|what can you do|what do i say|what are (?:the|my) (?:commands|options)|(?:show |list )?(?:the )?(?:voice )?commands|options|how does this work|what can i ask"),
            new Rule(VoiceIntentKind.ResetLayout,
                "(?:reset|restore) (?:the |my )?(?:layout|panels|panel positions|panel layout|positions|windows)|put everything back|put (?:the |my )?panels back|reset (?:the |my )?(?:panel|window) (?:layout|positions)"),
            new Rule(VoiceIntentKind.FocusOn,
                "focus|focus mode|focus on|focus mode on|turn on (?:the )?focus(?: mode)?|turn (?:the )?focus(?: mode)? on|enable focus(?: mode)?|start focus(?: mode)?|enter focus(?: mode)?|do not disturb|dont disturb(?: me)?|quiet mode|quiet mode on|go quiet|i need to focus|let me focus|hold (?:my |the )?updates|mute (?:the )?updates"),

            // The office: bring / send (the director maps these to the pod in Ceiling mode).
            new Rule(VoiceIntentKind.BringHere,
                "(?:bring|summon|call)(?: me)? " + It + "(?: (?:here|down|over|closer|back|to me|over here|back here|down here))?|bring (?:down|over|back) " + It + "|come here|come closer|come back|come down|come over|come to me|over here|lower the office|lower it"),
            new Rule(VoiceIntentKind.SendBack,
                "send(?: " + It + ")? (?:back|away|back up|back there|back again)|(?:put|move|take) " + It + " (?:back|away|back up|back there|back again)|send " + It + " up|go away|go back up|back up|raise the office|raise it|send back|you can go(?: back)?|off you go"),

            // Modes. A mode opens a preview; nothing moves until Confirm.
            new Rule(VoiceIntentKind.ModeDesk, ModePattern("desk")),
            new Rule(VoiceIntentKind.ModeFloor, ModePattern("floor")),
            new Rule(VoiceIntentKind.ModeCeiling, ModePattern("ceiling")),
            new Rule(VoiceIntentKind.NextSpot,
                "next spot|next one|next place|next location|next position|next candidate|next option|another spot|another one|another place|different spot|different place|different one|try another(?: spot| place| one)?|somewhere else|other spot|show (?:me )?another(?: spot| one)?|not there|try somewhere else"),
            new Rule(VoiceIntentKind.Next, "next|next page|page down|go on|continue|forward|next part|keep going|turn the page"),
            new Rule(VoiceIntentKind.Previous, "previous|previous page|back|go back|page up|prev|last page|back a page|back one"),
            new Rule(VoiceIntentKind.Confirm,
                "confirm|confirmed|confirm (?:it|that|this|placement|the placement|spot|the spot|position|the position|location)|(?:place|put|set|leave|keep|park) it (?:here|there)|(?:that|this) (?:spot|one|place)|(?:here|there) is (?:good|fine)|thats the spot|thats good there|looks good there|use (?:this|that) (?:spot|one|place)"),
            new Rule(VoiceIntentKind.Cancel,
                "cancel|cancel (?:it|that|this|placement|the placement|preview|the preview|the move)|never mind|nevermind|forget it|abort|scrap that"),

            // Board.
            new Rule(VoiceIntentKind.OpenBoard,
                "(?:open|show|see|view|display|bring up|pull up|expand)(?: me)?(?: the| my)?(?: task| kanban)? (?:board|tasks|task list|cards)|(?:the )?(?:task )?board|(?:the |my )?tasks|what are (?:the|my) tasks|whats on the board"),
            new Rule(VoiceIntentKind.CloseBoard,
                "(?:close|hide|collapse|put away|dismiss|shut)(?: the| my)?(?: task| kanban)? (?:board|tasks|task list|cards)|board (?:off|away|closed)|put the board away"),

            // The delivered result.
            new Rule(VoiceIntentKind.ViewResult,
                "(?:view|show|open|see|read|display|bring up|pull up)(?: me)?(?: the| my| that)?(?: final| latest)? (?:result|results|artifact|artifacts|report|output|deliverable|work|code|diff)|what did (?:you|they|the agents|we) (?:build|make|do|deliver|change)|(?:the )?results?|show me what (?:you|they) (?:built|made|did|changed)|let me see the (?:result|results|work)"),
            new Rule(VoiceIntentKind.Close,
                "(?:close|hide|dismiss|put away|shut)(?: it| that| this| the result| the results| the artifact| the report| the viewer| the sheet| the window| the panel| the card| help)?|done reading|im done reading|thats enough"),

            // The decision paper.
            new Rule(VoiceIntentKind.TakePaper,
                "(?:show|open|take|read|see|give|hand|unfold|bring)(?: me| it)?(?: the| that| this| over the)? (?:paper|document|proposal|decision|sheet|recommendation)(?: to me| over| here)?|let me see(?: it)?|let me read(?: it)?|hand it over|give it to me|give it here|ill take it|read it|what does it say|show me"),
            new Rule(VoiceIntentKind.HideDetails, "(?:hide|close|collapse)(?: the)? (?:details|detail|info|information)|less detail|fewer details"),
            new Rule(VoiceIntentKind.ShowDetails,
                "(?:(?:show|open|see|view|give|expand|read) )?(?:me )?(?:(?:the|some) )?(?:more )?(?:details|detail|info|information|rationale|reasoning)|tell me more|more about (?:it|this|that)|go into detail"),
            new Rule(VoiceIntentKind.AskWhy,
                "why|explain|explain (?:it|this|that|why|yourself|the reasoning|your reasoning|the recommendation)|whats the reason|tell me why|how come|justify (?:it|this|that)|whats your reasoning|reason|whats the risk|what are the risks|what are the alternatives|what else could we do"),

            // Questions about the run.
            new Rule(VoiceIntentKind.WhoNeedsMe,
                "who needs me|does (?:anyone|anybody|someone|somebody) need me|do you need me|do you need anything|anything for me|what needs me|what needs my attention|is (?:anything|something) waiting(?: for me)?|whats waiting(?: for me)?|do i need to do anything|what do you need(?: from me)?|need anything|anything waiting|whos waiting(?: for me)?|any decisions(?: for me)?|whats next for me|what should i do(?: now| next)?|am i needed"),
            new Rule(VoiceIntentKind.Status,
                "status|status report|status update|report|whats the status|hows it going|how are things(?: going)?|how are we doing|hows everyone doing|hows the team(?: doing)?|whats happening|whats going on|any updates?|any news|any progress|update me|update|give me an update|progress|progress report|whats the progress|hows progress|where are we|where are things|whats everyone (?:doing|working on|up to)|what are (?:you all|you guys|they|the agents|you) (?:doing|working on|up to)(?: right now| now)?|hows the (?:task|run|work|build) (?:going|coming along)|is it done(?: yet)?|are (?:you|we|they) done(?: yet)?|did (?:it|the tests) pass|how did (?:it|the tests) go|(?:check|show|give me|tell me|get)(?: me)? (?:the |a |an )?(?:status|progress|update)(?: report)?|what are you working on|whats the latest|how is the work going"),

            // Runs.
            new Rule(VoiceIntentKind.RunAgain,
                "run (?:it |that |the (?:task|demo|run) )?again|again|replay|replay (?:it|the demo|that)|restart|restart (?:it|the demo|the run|the task)|start over|start again|do it again|do that again|one more time|once more|reset|reset the demo|rerun|rerun it|go again|another run"),
            new Rule(VoiceIntentKind.Start,
                "start|begin|launch|kick off|kick it off|get started|get going|get to work|lets go|lets start|lets begin|start working|start work|begin work|start it|run it|start the (?:task|run|demo|work|job|pipeline|ready task|next task)|begin the (?:task|run|demo|work)|run the (?:task|demo|pipeline)|start (?:a |the )?demo|start task|run task|start that|start the agents|put them to work"),
        };

        static string ModePattern(string place)
        {
            return "(?:(?:go|move|switch|change|put|place|set|send|bring|take|use|try|back|return|come|office|mode) )?"
                 + "(?:(?:back|it|the office|my office|everything|up|down|over|the workspace) )*"
                 + "(?:(?:to|on|onto|into|in) )?(?:(?:the|my) )?" + place + "(?: (?:mode|pod|office))*";
        }

        static readonly Regex PageNumber = new Regex(
            "^(?:go to |open |show |jump to |try |use |pick |choose )?(?:the )?(page|spot|option|place|candidate|number|location) (?:number )?(\\w+)$",
            RegexOptions.CultureInvariant);

        static readonly Regex WhyQuestion = new Regex("^(?:why|what if|what about|what happens if|how come|explain) .{1,160}$", RegexOptions.CultureInvariant);

        static readonly Regex AgentCue = new Regex(
            "\\b(?:what|whats|how|hows|status|doing|working|progress|update|updates|going|done|finished|stuck|busy|there|report|anything|where|need|needs|ok|hello|hi|ready|waiting|blocked|news)\\b",
            RegexOptions.CultureInvariant);

        static int NumberOf(string w)
        {
            int n;
            if (int.TryParse(w, out n)) return n > 0 && n < 100 ? n : 0;
            switch (w)
            {
                case "one": case "first": return 1;
                case "two": case "second": case "to": case "too": return 2;
                case "three": case "third": return 3;
                case "four": case "fourth": case "for": return 4;
                case "five": case "fifth": return 5;
                case "six": case "sixth": return 6;
                case "seven": case "seventh": return 7;
                case "eight": case "eighth": return 8;
                case "nine": case "ninth": return 9;
                case "ten": case "tenth": return 10;
                default: return 0;
            }
        }

        // ───────────────────────── pod ─────────────────────────

        static readonly HashSet<string> PodDown = new HashSet<string> { "lower", "down", "descend", "drop" };
        static readonly HashSet<string> PodUp = new HashSet<string> { "raise", "up", "retract", "lift", "back", "return", "away" };
        static readonly HashSet<string> PodCome = new HashSet<string> { "bring", "come", "here", "closer" };
        static readonly HashSet<string> QuestionStart = new HashSet<string>
        { "what", "whats", "where", "wheres", "why", "how", "hows", "is", "does", "did", "which", "when", "who", "are", "can", "should" };

        static VoiceIntentKind PodRule(List<string> t)
        {
            bool pod = false;
            for (int i = 0; i < t.Count; i++) if (t[i] == "pod") { pod = true; break; }
            if (!pod || t.Count > 8 || QuestionStart.Contains(t[0])) return VoiceIntentKind.None;
            for (int i = 0; i < t.Count; i++) if (StopNegators.Contains(t[i])) return VoiceIntentKind.None;
            if (Has(t, PodDown)) return VoiceIntentKind.LowerPod;
            if (Has(t, PodUp)) return VoiceIntentKind.RaisePod;
            if (Has(t, PodCome)) return VoiceIntentKind.LowerPod;
            return VoiceIntentKind.None;
        }

        // ───────────────────────── new task ─────────────────────────

        static readonly Regex TaskPrefix = new Regex(
            "^(?:new task|(?:add|create|make|start|open|file|log)(?: a| another| one)?(?: new)? task|task|todo|to do|add to (?:the )?(?:board|backlog|list)|put on the board|queue up)(?: (?:to|for|called|named|titled|saying|is|that says))?(?= |$)",
            RegexOptions.CultureInvariant);

        static readonly HashSet<string> Imperatives = new HashSet<string>
        {
            "add", "build", "create", "make", "implement", "fix", "write", "update", "change", "refactor", "remove",
            "delete", "design", "improve", "investigate", "research", "rename", "support", "migrate", "document",
            "optimize", "draft", "prototype", "integrate", "generate", "convert", "replace", "upgrade", "set",
            "setup", "enable", "disable", "clean",
        };

        static bool TaskRule(List<string> raw, List<string> t, out string title, out bool isExplicit)
        {
            title = null; isExplicit = false;
            if (t.Count == 0) return false;
            string joined = string.Join(" ", t);
            Match m = TaskPrefix.Match(joined);
            if (m.Success)
            {
                int words = 1;
                for (int i = 0; i < m.Length; i++) if (joined[i] == ' ') words++;
                isExplicit = true;
                title = Title(raw, words);
                return true;
            }
            if (t.Count >= 3 && Imperatives.Contains(t[0]))
            {
                title = Title(raw, 0);
                return true;
            }
            return false;
        }

        static string Title(List<string> raw, int from)
        {
            if (from >= raw.Count) return "";
            var sb = new StringBuilder(64);
            for (int i = from; i < raw.Count; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(raw[i]);
            }
            if (sb.Length > 0) sb[0] = char.ToUpperInvariant(sb[0]);
            if (sb.Length > 80) { sb.Length = 77; sb.Append("..."); }
            return sb.ToString();
        }
    }
}
