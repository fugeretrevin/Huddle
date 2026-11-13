using System;

namespace MuseDesk.Voice
{
    /// <summary>
    /// The first voice layer's contract (decision words as events). Kept so the decision paper's
    /// Voice chip (DecisionPanel) and SummonVoiceRouter still compile and can ask whether a
    /// recogniser is present. Since Voice V2 the recogniser only reports TRANSCRIPTS
    /// (<see cref="IVoiceTranscriptSource"/>); the decision events here are never raised, and every
    /// spoken command goes through VoiceCommandCenter and the local grammar.
    /// </summary>
    public interface IVoiceCommandSource
    {
        event Action Approve;
        event Action Reject;
        event Action RequestChanges;
        event Action<string> AskWhy;
        event Action<bool> ListeningChanged;
        event Action<string> StatusChanged;

        /// <summary>True when a recogniser is configured (a Voice SDK service with a token).</summary>
        bool IsAvailable { get; }

        void StartListening();
        void StopListening();
    }

    /// <summary>Why voice cannot be used, or why one request ended without words.</summary>
    public enum VoiceProblem
    {
        None = 0,
        /// <summary>No speech service in the scene (the voice-free build).</summary>
        NoRecognizer,
        /// <summary>The speech service has no token in this build.</summary>
        NoToken,
        NoInternet,
        PermissionDenied,
        /// <summary>The speech service answered with an error.</summary>
        ServiceError,
        /// <summary>The speech service did not answer in time.</summary>
        Timeout,
        /// <summary>The microphone opened and closed without any words.</summary>
        NoSpeech,
    }

    /// <summary>
    /// A recogniser that turns the microphone into text. It does no intent matching. All events are
    /// raised on the main thread.
    /// </summary>
    public interface IVoiceTranscriptSource
    {
        /// <summary>Words so far (display only).</summary>
        event Action<string> PartialTranscript;
        /// <summary>The finished sentence. Raised at most once per request.</summary>
        event Action<string> FinalTranscript;
        /// <summary>The microphone opened (true) or closed (false).</summary>
        event Action<bool> ListeningChanged;
        /// <summary>Microphone level, 0..1, while listening.</summary>
        event Action<float> MicLevelChanged;
        /// <summary>A request ended without a transcript, or could not start.</summary>
        event Action<VoiceProblem, string> Problem;

        /// <summary>None when a request could be started right now; otherwise the reason it cannot.
        /// Permission is not part of this: it is asked for on the first request.</summary>
        VoiceProblem Availability { get; }
        bool IsListening { get; }

        /// <summary>Open the microphone (asks for the permission on first use). No-op while listening.</summary>
        void BeginRequest();
        /// <summary>Close the microphone and transcribe what was said.</summary>
        void EndRequest();
        /// <summary>Close the microphone and drop the request.</summary>
        void AbortRequest();
    }
}
