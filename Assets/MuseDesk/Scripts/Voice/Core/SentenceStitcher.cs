using System;
using System.Collections.Generic;
using System.Text;

namespace MuseDesk.Voice
{
    /// <summary>What the app already knows about a piece of speech that the service called final.</summary>
    public enum PieceVerdict
    {
        /// <summary>No opinion: the stitcher's own rules decide (very short = a command, longer = maybe more to come).</summary>
        Unknown = 0,
        /// <summary>A whole command ("stop", "approve", "floor mode"): act on it now.</summary>
        Complete,
        /// <summary>Clearly unfinished ("Manager," and nothing else): keep listening.</summary>
        Incomplete,
    }

    /// <summary>What the owner should do after telling the stitcher something.</summary>
    public enum StitchStep
    {
        /// <summary>Nothing to do (a repeat of the piece already taken, or still waiting).</summary>
        Ignore,
        /// <summary>Keep the microphone (re)open: the sentence may go on.</summary>
        Hold,
        /// <summary>The sentence is finished: take it with <see cref="SentenceStitcher.TakeSentence"/> and act on it.</summary>
        Emit,
    }

    /// <summary>
    /// rc21 (owner, worn rc20: "it's not waiting and reading my full sentence"). The speech service ends a request by itself
    /// when it hears the end of an utterance (its own end-of-speech detection; the client keep-alive settings cannot stop
    /// that), so "Manager, build a program ... (pause) ... that prints stars" came back as "Manager, build a program".
    ///
    /// This joins the pieces into one sentence. A final piece is treated as provisional: unless it already reads as a whole
    /// command (short, or the app says so), the microphone is opened again and the stitcher waits a short grace for more
    /// words. Words arriving cancel the wait; the next final is appended. When the grace passes with no new words, the
    /// joined sentence is emitted once. Short commands ("stop", "approve") are emitted at once, so they never feel slow.
    ///
    /// Pure logic: no Unity types, time is passed in. Owned by WitVoiceSource; tested in EditMode.
    /// </summary>
    public sealed class SentenceStitcher
    {
        public const float DefaultGraceSeconds = 1.6f;
        public const float DefaultHoldCapSeconds = 4f;
        public const float DefaultMaxSeconds = 30f;
        public const int DefaultMaxPieces = 5;
        public const int DefaultShortWords = 2;

        /// <summary>How long after the microphone is open again (or the last sound) to wait for more words.</summary>
        public readonly float GraceSeconds;
        /// <summary>Longest wait after a piece when no words come, whatever sounds are heard.</summary>
        public readonly float HoldCapSeconds;
        /// <summary>Longest one sentence may run, all pieces together.</summary>
        public readonly float MaxSeconds;
        public readonly int MaxPieces;
        /// <summary>A piece of at most this many words, with no other opinion, is a command and is emitted at once.</summary>
        public readonly int ShortWords;

        /// <summary>A sentence is being collected (between Begin and Emit / Finish / Reset).</summary>
        public bool Active { get; private set; }
        /// <summary>At least one piece is in and the sentence is not finished: the owner keeps listening for more.</summary>
        public bool Holding { get; private set; }
        /// <summary>Words arrived after the last piece (the user went on talking).</summary>
        public bool Resumed { get; private set; }
        public int Pieces { get; private set; }
        /// <summary>The pieces so far, joined.</summary>
        public string Text { get; private set; } = "";
        /// <summary>The newest partial words (of the piece in progress).</summary>
        public string LastPartial { get; private set; } = "";
        /// <summary>When the wait ends (only while Holding and not Resumed).</summary>
        public float Deadline { get; private set; }
        /// <summary>For drivers: how many times a piece was held in total.</summary>
        public int HoldCount { get; private set; }

        float _start, _holdStart;
        string _lastPiece = "";

        public SentenceStitcher(float graceSeconds = DefaultGraceSeconds, float holdCapSeconds = DefaultHoldCapSeconds,
                                float maxSeconds = DefaultMaxSeconds, int maxPieces = DefaultMaxPieces, int shortWords = DefaultShortWords)
        {
            GraceSeconds = graceSeconds;
            HoldCapSeconds = Math.Max(graceSeconds, holdCapSeconds);
            MaxSeconds = maxSeconds;
            MaxPieces = Math.Max(1, maxPieces);
            ShortWords = Math.Max(0, shortWords);
        }

        /// <summary>A new sentence starts (the microphone was opened by the user or the hands-free loop).</summary>
        public void Begin(float now)
        {
            Reset();
            Active = true;
            _start = now;
        }

        public void Reset()
        {
            Active = Holding = Resumed = false;
            Pieces = 0;
            Text = LastPartial = _lastPiece = "";
            Deadline = 0f;
        }

        /// <summary>Partial words of the piece in progress.</summary>
        public void OnPartial(string text, float now)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            LastPartial = text.Trim();
            if (Holding) Resumed = true;
        }

        /// <summary>The microphone is open again after a held piece: the grace counts from now (within the hold cap).</summary>
        public void OnMicReopened(float now) { Extend(now); }

        /// <summary>Sound (not yet words) on the reopened microphone: wait a little longer (within the hold cap).</summary>
        public void OnSound(float now) { Extend(now); }

        void Extend(float now)
        {
            if (!Holding || Resumed) return;
            float want = Math.Min(now + GraceSeconds, _holdStart + HoldCapSeconds);
            if (want > Deadline) Deadline = want;
        }

        /// <summary>The service called a piece final. <paramref name="closing"/>: the user (or a cap) ended the request, so
        /// whatever is in is the sentence.</summary>
        public StitchStep OnFinal(string text, float now, PieceVerdict verdict, bool closing = false)
        {
            string piece = (text ?? "").Trim();
            if (piece.Length == 0) return closing && Pieces > 0 ? Done() : StitchStep.Ignore;
            if (!Active) Begin(now);

            // The same piece again (the service repeats the transcript with its response) before any new words: not new speech.
            if (Holding && !Resumed && SameWords(piece, _lastPiece)) return closing ? Done() : StitchStep.Ignore;

            // Some services resend the whole sentence so far: then it replaces what was joined.
            if (Text.Length > 0 && Normalize(piece).StartsWith(Normalize(Text), StringComparison.Ordinal)) Text = piece;
            else Text = Join(Text, piece);
            _lastPiece = piece;
            Pieces++;
            Resumed = false;
            LastPartial = "";

            if (closing || Pieces >= MaxPieces || now - _start >= MaxSeconds) return Done();
            if (EndsDangling(Text)) return Hold(now);
            if (verdict == PieceVerdict.Complete) return Done();
            if (verdict == PieceVerdict.Incomplete) return Hold(now);
            return CountWords(Text) <= ShortWords ? Done() : Hold(now);
        }

        StitchStep Hold(float now)
        {
            Holding = true;
            Resumed = false;
            _holdStart = now;
            Deadline = now + GraceSeconds;
            HoldCount++;
            return StitchStep.Hold;
        }

        StitchStep Done()
        {
            Holding = false;
            return StitchStep.Emit;
        }

        /// <summary>Every frame while holding. Emit when the grace passed with no new words, or the sentence ran too long.</summary>
        public StitchStep Tick(float now)
        {
            if (!Holding) return StitchStep.Ignore;
            if (!Resumed && now >= Deadline) return Done();
            if (now - _start >= MaxSeconds + GraceSeconds) return Done();
            return StitchStep.Ignore;
        }

        /// <summary>What to show while listening: the pieces so far and the words in progress.</summary>
        public string Display(string partial)
        {
            return Join(Text, (partial ?? "").Trim());
        }

        /// <summary>After <see cref="StitchStep.Emit"/>: the whole sentence. Resets.</summary>
        public string TakeSentence()
        {
            string s = Text;
            Reset();
            return s;
        }

        /// <summary>The request ended without a final for the piece in progress (stopped, silence, an error): the sentence is
        /// what was joined plus any words still in progress. Resets.</summary>
        public string Finish()
        {
            string s = Text;
            if ((Resumed || Pieces == 0) && LastPartial.Length > 0) s = Join(s, LastPartial);
            Reset();
            return s;
        }

        // ───────────────────────── text helpers ─────────────────────────

        /// <summary>Words that cannot end a sentence: a piece ending on one of them surely goes on.</summary>
        static readonly HashSet<string> Dangling = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "and", "or", "but", "so", "that", "which", "who", "whose", "where", "to", "the", "a", "an", "of", "for", "with",
            "in", "on", "at", "from", "by", "about", "into", "onto", "then", "because", "if", "when", "while", "than", "as",
            "my", "your", "our", "their", "its", "this", "these", "those", "is", "are", "was", "were", "be", "can", "could",
            "should", "would", "will", "please", "um", "uh", "erm", "like", "also", "plus", "make", "build", "write", "add",
        };

        public static bool EndsDangling(string text)
        {
            string last = LastWord(text);
            return last.Length > 0 && Dangling.Contains(last);
        }

        public static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int n = 0; bool inWord = false;
            for (int i = 0; i < text.Length; i++)
            {
                bool w = char.IsLetterOrDigit(text[i]) || text[i] == '\'';
                if (w && !inWord) n++;
                inWord = w;
            }
            return n;
        }

        /// <summary>Joins two pieces into one sentence: a full stop the service put at the pause is dropped and the next
        /// piece's capital is lowered, so "Build a program." + "That prints stars" reads "Build a program that prints stars".</summary>
        public static string Join(string a, string b)
        {
            a = (a ?? "").Trim();
            b = (b ?? "").Trim();
            if (a.Length == 0) return b;
            if (b.Length == 0) return a;
            bool question = a.EndsWith("?", StringComparison.Ordinal) || a.EndsWith("!", StringComparison.Ordinal);
            if (!question) a = a.TrimEnd('.', ',', ';', ':', ' ');
            if (!question && b.Length > 1 && char.IsUpper(b[0]) && char.IsLower(b[1]) && !b.StartsWith("I ", StringComparison.Ordinal))
                b = char.ToLowerInvariant(b[0]) + b.Substring(1);
            return a + " " + b;
        }

        static string LastWord(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int end = text.Length - 1;
            while (end >= 0 && !char.IsLetterOrDigit(text[end])) end--;
            if (end < 0) return "";
            int start = end;
            while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '\'')) start--;
            return text.Substring(start, end - start + 1);
        }

        static bool SameWords(string a, string b) { return Normalize(a) == Normalize(b); }

        static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '\'') { if (space && sb.Length > 0) sb.Append(' '); sb.Append(char.ToLowerInvariant(c)); space = false; }
                else space = true;
            }
            return sb.ToString();
        }
    }
}
