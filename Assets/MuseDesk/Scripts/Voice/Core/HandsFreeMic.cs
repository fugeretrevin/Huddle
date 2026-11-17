using System;

namespace MuseDesk.Voice
{
    /// <summary>What the mic key says. One of these at any time.</summary>
    public enum MicPhase
    {
        /// <summary>Muted (the default): nothing listens.</summary>
        Muted,
        /// <summary>Unmuted but voice cannot be used right now (no service, no token, no internet, permission denied).</summary>
        Unavailable,
        /// <summary>Unmuted and idle between utterances (also while backing off after an error).</summary>
        Waiting,
        /// <summary>The microphone is open, nobody has spoken yet.</summary>
        Listening,
        /// <summary>The microphone is open and words are arriving.</summary>
        Hearing,
        /// <summary>The microphone closed, the sentence is being transcribed.</summary>
        Processing,
    }

    /// <summary>Mirror of MuseDesk.Voice.VoiceProblem (same order; the Core assembly cannot see that one). Cast with (MicProblem)(int)p.</summary>
    public enum MicProblem { None = 0, NoRecognizer, NoToken, NoInternet, PermissionDenied, ServiceError, Timeout, NoSpeech }

    public enum MicAction { None, Begin }

    /// <summary>Minimal int store so the mute preference tests without PlayerPrefs.</summary>
    public interface IMicPrefStore
    {
        int GetInt(string key, int fallback);
        void SetInt(string key, int value);
    }

    /// <summary>Per-device mic choice. Absent means muted: the microphone is never open until the user unmutes it.</summary>
    public static class MicPreference
    {
        public const string Key = "MuseDesk.MicUnmuted";
        public static bool Load(IMicPrefStore s) { return s != null && s.GetInt(Key, 0) == 1; }
        public static void Save(IMicPrefStore s, bool unmuted) { if (s != null) s.SetInt(Key, unmuted ? 1 : 0); }

        public static string KeyText(MicPhase p)
        {
            switch (p)
            {
                case MicPhase.Muted: return "Mic\noff";
                case MicPhase.Unavailable: return "Voice\noffline";
                case MicPhase.Hearing: return "Mic\nhearing";
                case MicPhase.Processing: return "Mic\nwait";
                default: return "Mic\non";
            }
        }
    }

    /// <summary>
    /// Pure arming logic for hands-free listening. No Unity types; time is passed in.
    /// The owner calls <see cref="Tick"/> every frame with the facts and does what it returns.
    ///
    /// - Muted (default): Tick never returns Begin.
    /// - Unmuted: after each utterance / silence / error the next listen is armed after a delay.
    ///   Errors back off exponentially (base .. max) so a failing service is never hammered; a
    ///   transcript or a silent timeout resets the backoff.
    /// - Permanent problems (no service, no token, permission denied) latch Unavailable: no retries
    ///   until the user toggles again. Missing internet is polled slowly and recovers by itself.
    /// </summary>
    public sealed class HandsFreeMic
    {
        public readonly float RearmDelay, NoSpeechDelay, BackoffBase, BackoffMax, StartGrace;

        public bool Unmuted { get; private set; }
        public MicPhase Phase { get; private set; }
        public MicProblem Blocked { get; private set; }
        public int ConsecutiveErrors { get; private set; }
        public float NextArmAt { get; private set; }
        public int BeginCount { get; private set; }

        bool _latched;                 // permanent problem: stay Unavailable until the next toggle
        bool _heard;                   // words arrived during the current open microphone

        public HandsFreeMic(float rearmDelay = 0.5f, float noSpeechDelay = 0.6f, float backoffBase = 1f, float backoffMax = 30f, float startGrace = 4f)
        {
            RearmDelay = rearmDelay; NoSpeechDelay = noSpeechDelay; BackoffBase = backoffBase; BackoffMax = backoffMax; StartGrace = startGrace;
            Phase = MicPhase.Muted;
        }

        public static bool IsPermanent(MicProblem p)
        {
            return p == MicProblem.NoRecognizer || p == MicProblem.NoToken || p == MicProblem.PermissionDenied;
        }

        /// <summary>Mute or unmute. Unmuting resets backoff and any latched problem and arms at once.</summary>
        public void SetUnmuted(bool on, float now)
        {
            Unmuted = on;
            _latched = false; _heard = false;
            ConsecutiveErrors = 0;
            Blocked = MicProblem.None;
            NextArmAt = now;
            Phase = on ? MicPhase.Waiting : MicPhase.Muted;
        }

        public void NotifyPartial() { _heard = true; }

        /// <summary>The microphone opened (true) or closed (false).</summary>
        public void NotifyListeningChanged(bool on, float now)
        {
            if (on) { _heard = false; return; }
            float at = now + RearmDelay;
            if (at > NextArmAt) NextArmAt = at;
        }

        /// <summary>A transcript arrived: back to normal pace.</summary>
        public void NotifyFinal(float now)
        {
            ConsecutiveErrors = 0;
            NextArmAt = now + RearmDelay;
        }

        /// <summary>The service heard nothing. Not an error.</summary>
        public void NotifyNoSpeech(float now)
        {
            ConsecutiveErrors = 0;
            NextArmAt = now + NoSpeechDelay;
        }

        /// <summary>A real problem: permanent ones latch, the rest back off.</summary>
        public void NotifyProblem(MicProblem p, float now)
        {
            if (p == MicProblem.None) return;
            if (p == MicProblem.NoSpeech) { NotifyNoSpeech(now); return; }
            Blocked = p;
            if (IsPermanent(p)) { _latched = true; return; }
            ConsecutiveErrors++;
            NextArmAt = now + BackoffFor(ConsecutiveErrors);
        }

        public float BackoffFor(int errors)
        {
            if (errors <= 0) return 0f;
            float d = BackoffBase;
            for (int i = 1; i < errors && d < BackoffMax; i++) d *= 2f;
            return d > BackoffMax ? BackoffMax : d;
        }

        /// <param name="availability">The source's current availability (None when a request could start).</param>
        /// <param name="sourceListening">The microphone is open now.</param>
        /// <param name="thinking">Closed and waiting for the sentence.</param>
        /// <param name="mayBegin">The owner allows a new request now (nothing else is using the microphone).</param>
        public MicAction Tick(float now, MicProblem availability, bool sourceListening, bool thinking, bool mayBegin)
        {
            if (!Unmuted) { Phase = MicPhase.Muted; return MicAction.None; }
            if (sourceListening) { Phase = _heard ? MicPhase.Hearing : MicPhase.Listening; return MicAction.None; }
            if (thinking) { Phase = MicPhase.Processing; return MicAction.None; }

            if (_latched || IsPermanent(availability))
            {
                if (IsPermanent(availability)) Blocked = availability;
                Phase = MicPhase.Unavailable;
                return MicAction.None;
            }
            if (availability != MicProblem.None)
            {
                // No internet: shown as offline, polled again a little later; it recovers by itself.
                Blocked = availability;
                Phase = MicPhase.Unavailable;
                return MicAction.None;
            }
            Blocked = MicProblem.None;
            Phase = MicPhase.Waiting;
            if (!mayBegin || now < NextArmAt) return MicAction.None;
            NextArmAt = now + StartGrace;    // the microphone takes a moment to open; do not ask twice
            BeginCount++;
            return MicAction.Begin;
        }
    }
}


