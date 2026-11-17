using System.Collections.Generic;

namespace MuseDesk.Voice
{
    /// <summary>
    /// rc18 (owner, worn rc17): "with the actual live version we want the talking to be less like 'x, why' and more natural
    /// like 'hey why are you doing this instead of this'". Owner decision: while an agent is WITH the user (picked up and
    /// perched on the desk, or out of the portal), the user can just talk to it: no name needed. Name-first stays for agents
    /// across the room, so room chatter still does nothing when nobody is visiting.
    ///
    /// A sentence goes to the companion when:
    ///  - an agent is with the user (<paramref name="companion"/> is not None), and
    ///  - it is not addressed to SOMEONE ELSE by name ("Tester, come here" while the Developer visits stays a Meet request), and
    ///  - it is not an app command (approve, stop, cancel, a mode change...: <paramref name="isCommand"/>), and
    ///  - it is a real sentence: two words or more, or a single question word ("why?").
    /// The question text is the sentence without a leading greeting or the companion's own name.
    /// Pure: no Unity types, fully testable.
    /// </summary>
    public static class CompanionAddressing
    {
        static readonly HashSet<string> Fillers = new HashSet<string> { "hey", "hi", "hello", "ok", "okay", "yo", "um", "uh", "so", "and", "well" };
        static readonly HashSet<string> SingleQuestions = new HashSet<string> { "why", "how", "what", "really", "when", "where", "who" };

        public static bool ForCompanion(string heard, VoiceAgentTarget companion, bool isCommand, out string question)
        {
            question = null;
            if (companion == VoiceAgentTarget.None || string.IsNullOrEmpty(heard) || isCommand) return false;
            string text = heard.Trim();
            VoiceAgentTarget named; string rest;
            if (WakeWord.Parse(text, out named, out rest))
            {
                if (named != VoiceAgentTarget.None && named != companion) return false;   // for another agent
                if (named == companion) text = rest;                                         // "Developer, why ..." -> "why ..."
            }
            text = StripFillers(text);
            if (text.Length == 0) return false;
            int words = CountWords(text);
            if (words < 2)
            {
                string w = text.Trim('?', '!', '.', ',', ' ').ToLowerInvariant();
                if (!SingleQuestions.Contains(w)) return false;
            }
            question = char.ToUpperInvariant(text[0]) + text.Substring(1);
            return true;
        }

        static string StripFillers(string s)
        {
            string t = s.TrimStart(' ', ',', '.', '!', '-');
            for (int guard = 0; guard < 3; guard++)
            {
                int sp = 0;
                while (sp < t.Length && char.IsLetter(t[sp])) sp++;
                if (sp == 0 || !Fillers.Contains(t.Substring(0, sp).ToLowerInvariant())) break;
                if (sp >= t.Length) return "";
                t = t.Substring(sp).TrimStart(' ', ',', '.', '!', '-');
            }
            return t.Trim();
        }

        static int CountWords(string s)
        {
            int n = 0; bool inWord = false;
            for (int i = 0; i < s.Length; i++)
            {
                bool letter = char.IsLetterOrDigit(s[i]) || s[i] == '\'';
                if (letter && !inWord) n++;
                inWord = letter;
            }
            return n;
        }
    }
}
