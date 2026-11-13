using System;

namespace MuseDesk.Voice
{
    /// <summary>
    /// Provider-agnostic source of recognized voice intents. A concrete implementation (e.g. a
    /// Wit.ai-backed one using the Meta Voice SDK) raises these events; VoiceDecisionController
    /// turns them into the SAME actions the poke buttons do — always through the backend gate,
    /// never bypassing approval. Keeping this an interface means the app compiles and runs with no
    /// voice provider at all (hands-first stays intact), and the offline demo never depends on it.
    /// </summary>
    public interface IVoiceCommandSource
    {
        /// <summary>User affirmed (e.g. built-in wit/confirmation, "approve", "yes, ship it").</summary>
        event Action Approve;
        /// <summary>User declined (e.g. built-in wit/rejection, "reject", "no").</summary>
        event Action Reject;
        /// <summary>User requested changes / revision ("request changes", "revise").</summary>
        event Action RequestChanges;
        /// <summary>User asked a contextual question about the pending decision; carries the text.</summary>
        event Action<string> AskWhy;
        /// <summary>Mic listening state changed — drives a visible "listening" indicator.</summary>
        event Action<bool> ListeningChanged;
        /// <summary>Human-readable status message (e.g. errors, permission state, heard transcript).</summary>
        event Action<string> StatusChanged;

        /// <summary>
        /// True only when the provider can actually recognize speech right now: configured with a
        /// Wit token, network reachable, and consent granted. Inert (false) otherwise, so nothing
        /// in the experience silently depends on a cloud service that isn't there.
        /// </summary>
        bool IsAvailable { get; }

        void StartListening();
        void StopListening();
    }
}
