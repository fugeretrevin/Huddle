using System;
using System.Collections.Generic;
using System.Text;

namespace MuseDesk.Voice
{
    /// <summary>
    /// rc22 (owner, worn rc21: "the mic needs to be less picky so it actually reads better"). People do not speak in
    /// commands: "um, okay so can you open the, uh, board for me please". The grammar (VoiceIntentParser) matches whole
    /// sentences, so the disfluencies and the politeness around a command made it miss.
    ///
    /// <see cref="Clean"/> removes, without touching the words that carry meaning:
    ///  - hesitation sounds anywhere ("um", "uh", "erm", "hmm");
    ///  - a polite or chatty lead-in ("okay so", "can you", "could you please", "would you mind", "hey team");
    ///  - a polite tail ("please", "for me", "thanks", "if you can", "real quick").
    /// It never empties a sentence, never removes a negation ("don't", "not") and keeps the words as heard (a task title
    /// keeps its spelling). VoiceCommandCenter only uses the cleaned sentence when the sentence as heard means nothing, and
    /// never lets the cleaned sentence become a decision (approve / reject): a decision is still said plainly.
    ///
    /// <see cref="ContentWords"/> and <see cref="IsOnlyFiller"/> say whether a sentence carries anything at all (for the
    /// "ask the Manager" fallback and for the sentence stitcher's "keep listening" verdict).
    /// Pure: no Unity types, tested in EditMode.
    /// </summary>
    public static class VoiceFiller
    {
        /// <summary>Hesitation sounds: removed wherever they are.</summary>
        static readonly HashSet<string> Disfluencies = new HashSet<string>
        {
            "um", "umm", "ummm", "uh", "uhh", "uhhh", "uhm", "er", "erm", "hmm", "hm", "hmmm", "mm", "mmm", "ah", "ahh", "eh",
        };

        /// <summary>Lead-ins removed from the start, longest first, repeatedly ("okay so um can you please ...").</summary>
        static readonly string[][] LeadPhrases = Split(
            "i was wondering if you could", "i was wondering if you can", "do you think you could", "do you think you can",
            "would you be able to", "could you be able to", "would you mind", "do you mind", "if you could", "if you can",
            "can you please", "could you please", "would you please", "will you please",
            "can you", "could you", "would you", "will you", "can we", "could we",
            "i want you to", "i need you to", "id like you to", "i would like you to", "go ahead and",
            "hey team", "hey everyone", "hey everybody", "hey guys", "hey folks", "hey all", "you guys", "okay so", "ok so",
            "alright so", "all right so", "right so", "so yeah", "and then", "okay", "ok", "alright", "so", "well", "now",
            "please", "just", "kindly", "oh", "hey", "hi", "hello", "yo", "guys", "team", "everyone", "everybody", "folks");

        /// <summary>Tails removed from the end, longest first, repeatedly ("... for me please").</summary>
        static readonly string[][] TailPhrases = Split(
            "if you dont mind", "if you could", "if you can", "if you would", "when you can", "real quick", "right now",
            "thank you", "for me", "please", "thanks", "okay", "ok", "quickly", "now", "would you", "could you", "will you", "can you");

        /// <summary>Words that carry nothing on their own (greetings, yes/no, pronouns, mic checks): see <see cref="ContentWords"/>.</summary>
        static readonly HashSet<string> SmallTalk = new HashSet<string>
        {
            "hey", "hi", "hello", "ok", "okay", "yes", "yeah", "yep", "yup", "no", "nope", "nah", "thanks", "thank", "you", "please",
            "sorry", "oh", "wow", "cool", "nice", "great", "good", "right", "sure", "alright", "so", "well", "bye", "huh", "what",
            "the", "a", "an", "and", "it", "that", "this", "is", "i", "me", "we", "us", "can", "could", "do", "hear", "testing",
            "test", "mic", "check", "one", "two", "three", "on", "yo", "just", "now", "then", "there", "here", "guys", "team",
            "everyone", "everybody", "really", "okay", "fine", "awesome", "perfect", "hmm", "lol", "haha",
        };

        /// <summary>A sentence made only of these is someone starting to talk ("okay so", "hey", "um"): keep listening.</summary>
        static readonly HashSet<string> Openers = new HashSet<string>
        {
            "hey", "hi", "hello", "ok", "okay", "so", "well", "alright", "oh", "yo", "please", "and", "now", "right", "then",
            "guys", "team", "everyone", "everybody", "folks", "just", "like",
        };

        static readonly HashSet<string> Negations = new HashSet<string>
        {
            "dont", "not", "never", "no", "cant", "cannot", "wont", "shouldnt", "wouldnt", "couldnt", "doesnt", "didnt", "isnt", "arent",
        };

        struct Word { public string Raw; public string Norm; }

        /// <summary>The sentence without hesitation sounds, polite lead-ins and polite tails (see the class summary). The
        /// words kept are as heard; returns the input unchanged when nothing would be left or nothing was removed.</summary>
        public static string Clean(string heard)
        {
            if (string.IsNullOrWhiteSpace(heard)) return heard ?? "";
            List<Word> w = Words(heard);
            if (w.Count == 0) return heard;
            int before = w.Count;

            // 1. Hesitation sounds anywhere (never all of them: "um" alone stays "um").
            var kept = new List<Word>(w.Count);
            for (int i = 0; i < w.Count; i++) if (!Disfluencies.Contains(w[i].Norm)) kept.Add(w[i]);
            if (kept.Count == 0) return heard;
            w = kept;

            // 2. Lead-ins and 3. tails, repeatedly, never down to nothing.
            bool again = true;
            while (again)
            {
                again = false;
                for (int p = 0; p < LeadPhrases.Length; p++)
                {
                    string[] ph = LeadPhrases[p];
                    if (w.Count <= ph.Length || !StartsWith(w, ph)) continue;
                    w.RemoveRange(0, ph.Length);
                    again = true;
                    break;
                }
                for (int p = 0; p < TailPhrases.Length; p++)
                {
                    string[] ph = TailPhrases[p];
                    if (w.Count <= ph.Length || !EndsWith(w, ph)) continue;
                    // "... not now" / "don't ... please": a tail never takes a negation's word away with it.
                    if (w.Count > ph.Length && Negations.Contains(w[w.Count - ph.Length - 1].Norm) && ph.Length == 1 && ph[0] == "now") continue;
                    w.RemoveRange(w.Count - ph.Length, ph.Length);
                    again = true;
                    break;
                }
            }
            if (w.Count == before) return heard.Trim();

            var sb = new StringBuilder(heard.Length);
            for (int i = 0; i < w.Count; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(w[i].Raw);
            }
            string s = sb.ToString().Trim().TrimEnd(',', ';', ':', '-', '.').Trim();
            if (heard.TrimEnd().EndsWith("?", StringComparison.Ordinal) && !s.EndsWith("?", StringComparison.Ordinal)) s += "?";
            return s.Length == 0 ? heard.Trim() : s;
        }

        /// <summary>Words that carry meaning (not greetings, yes/no, pronouns, hesitations or a mic check). "thank you" and
        /// "can you hear me" have none; "plan a login page" has three.</summary>
        public static int ContentWords(string heard)
        {
            if (string.IsNullOrWhiteSpace(heard)) return 0;
            int n = 0;
            foreach (Word w in Words(heard))
                if (!Disfluencies.Contains(w.Norm) && !SmallTalk.Contains(w.Norm)) n++;
            return n;
        }

        /// <summary>True when the sentence is only someone starting to talk ("okay so", "hey", "um", "and"): the sentence
        /// stitcher should keep listening instead of acting on it.</summary>
        public static bool IsOnlyFiller(string heard)
        {
            if (string.IsNullOrWhiteSpace(heard)) return false;
            List<Word> w = Words(heard);
            if (w.Count == 0) return false;
            for (int i = 0; i < w.Count; i++)
                if (!Disfluencies.Contains(w[i].Norm) && !Openers.Contains(w[i].Norm)) return false;
            return true;
        }

        // ───────────────────────── words ─────────────────────────

        /// <summary>Whitespace-separated pieces with their edge punctuation trimmed (kept as heard) and a matching form
        /// (lower case, apostrophes dropped: "don't" -> "dont", "Um," -> "um").</summary>
        static List<Word> Words(string s)
        {
            var list = new List<Word>(12);
            int i = 0, n = s.Length;
            var sb = new StringBuilder(16);
            while (i < n)
            {
                while (i < n && char.IsWhiteSpace(s[i])) i++;
                int start = i;
                while (i < n && !char.IsWhiteSpace(s[i])) i++;
                if (i <= start) break;
                int a = start, b = i - 1;
                while (a <= b && !char.IsLetterOrDigit(s[a])) a++;
                while (b >= a && !char.IsLetterOrDigit(s[b])) b--;
                if (b < a) continue;
                sb.Length = 0;
                for (int k = a; k <= b; k++) if (char.IsLetterOrDigit(s[k])) sb.Append(char.ToLowerInvariant(s[k]));
                if (sb.Length == 0) continue;
                list.Add(new Word { Raw = s.Substring(a, b - a + 1), Norm = sb.ToString() });
            }
            return list;
        }

        static bool StartsWith(List<Word> w, string[] ph)
        {
            for (int k = 0; k < ph.Length; k++) if (w[k].Norm != ph[k]) return false;
            return true;
        }

        static bool EndsWith(List<Word> w, string[] ph)
        {
            int off = w.Count - ph.Length;
            for (int k = 0; k < ph.Length; k++) if (w[off + k].Norm != ph[k]) return false;
            return true;
        }

        static string[][] Split(params string[] phrases)
        {
            var r = new string[phrases.Length][];
            for (int i = 0; i < phrases.Length; i++) r[i] = phrases[i].Split(' ');
            // Longest first, so "can you please" wins over "can you".
            Array.Sort(r, (x, y) => y.Length.CompareTo(x.Length));
            return r;
        }
    }
}
