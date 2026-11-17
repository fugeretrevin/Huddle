using System;

namespace MuseDesk.Voice
{
    /// <summary>
    /// Pure tracker of WHICH agent is being spoken to while the microphone is open. Time is passed in
    /// (no Unity types), so it is fully testable. Sources, strongest first:
    ///  1. A name or role word in the transcript ("developer, ..."): sticky until the mic closes or the
    ///     timeout passes with no refresh.
    ///  2. A gaze / selection target the caller reports with <see cref="SetGaze"/>. Call it every frame
    ///     (or on change) while the user looks at an agent; repeated calls refresh the timeout.
    /// While nothing is addressed, AddressedAgent is None and no event fires: callers keep the
    /// "everyone listens" behaviour. ListeningStarted/ListeningEnded fire only on real transitions,
    /// and only while the mic is open, so the animation layer can play a truthful per-agent Listening
    /// clip.
    /// </summary>
    public sealed class VoiceAddressing
    {
        /// <summary>Process-wide instance the runtime feeds and the animation layer subscribes to.</summary>
        public static readonly VoiceAddressing Shared = new VoiceAddressing();

        public readonly float TimeoutSeconds;

        public bool MicOpen { get; private set; }
        public VoiceAgentTarget AddressedAgent { get; private set; }

        public event Action<VoiceAgentTarget> ListeningStarted;
        public event Action<VoiceAgentTarget> ListeningEnded;

        VoiceAgentTarget _mention;
        VoiceAgentTarget _gaze;
        float _mentionAt;
        float _gazeAt;

        public VoiceAddressing(float timeoutSeconds = 8f)
        {
            TimeoutSeconds = timeoutSeconds < 0.1f ? 0.1f : timeoutSeconds;
        }

        public void MicOpened(float now)
        {
            if (MicOpen) return;
            MicOpen = true;
            _mention = VoiceAgentTarget.None;
            // A gaze held before the mic opened counts, if it is still fresh.
            if (_gaze != VoiceAgentTarget.None && now - _gazeAt > TimeoutSeconds) _gaze = VoiceAgentTarget.None;
            Recompute(now);
        }

        public void MicClosed(float now)
        {
            if (!MicOpen) return;
            MicOpen = false;
            _mention = VoiceAgentTarget.None;
            Recompute(now);
        }

        /// <summary>The agent the user is looking at or has selected (None when none). Refreshes the timeout.</summary>
        public void SetGaze(VoiceAgentTarget agent, float now)
        {
            _gaze = agent;
            _gazeAt = now;
            if (MicOpen) Recompute(now);
        }

        /// <summary>Feed partial or final transcript text; a name mention addresses that agent.</summary>
        public void Hear(string transcript, float now)
        {
            if (!MicOpen || string.IsNullOrEmpty(transcript)) return;
            VoiceAgentTarget a = VoiceIntentClassifier.ResolveTarget(transcript);
            if (a == VoiceAgentTarget.None) return;
            _mention = a;
            _mentionAt = now;
            Recompute(now);
        }

        /// <summary>Call every frame (cheap) so a stale address times out.</summary>
        public void Tick(float now)
        {
            if (MicOpen && AddressedAgent != VoiceAgentTarget.None) Recompute(now);
        }

        void Recompute(float now)
        {
            if (_mention != VoiceAgentTarget.None && now - _mentionAt > TimeoutSeconds) _mention = VoiceAgentTarget.None;
            VoiceAgentTarget gaze = (_gaze != VoiceAgentTarget.None && now - _gazeAt <= TimeoutSeconds) ? _gaze : VoiceAgentTarget.None;

            VoiceAgentTarget next = VoiceAgentTarget.None;
            if (MicOpen) next = _mention != VoiceAgentTarget.None ? _mention : gaze;

            if (next == AddressedAgent) return;
            VoiceAgentTarget old = AddressedAgent;
            AddressedAgent = next;
            if (old != VoiceAgentTarget.None && ListeningEnded != null) ListeningEnded(old);
            if (next != VoiceAgentTarget.None && ListeningStarted != null) ListeningStarted(next);
        }
    }
}
