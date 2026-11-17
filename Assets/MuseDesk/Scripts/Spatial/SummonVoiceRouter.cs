using UnityEngine;
using MuseDesk.Networking;
using MuseDesk.Voice;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Optional: routes two spoken phrases ("bring down the office", "send the office back") to
    /// <see cref="SummonRequests"/> without editing the voice layer. The voice source has no
    /// transcript event, but it reports what it heard through
    /// <see cref="IVoiceCommandSource.StatusChanged"/> ("Heard: ..."), which this listens to.
    ///
    /// It follows the same policy as VoiceDecisionController (its master switch and live-mode
    /// rule) and does nothing unless the source reports itself available. It never starts the
    /// microphone: listening is started today only by the paper's Voice chip, so in practice a
    /// phrase is heard only while a paper is open (see the integration document for the edit
    /// that would lift that). Hands-first is unaffected: the same requests are on the keys.
    /// </summary>
    public class SummonVoiceRouter : MonoBehaviour
    {
        public SessionController controller;
        [Tooltip("The existing voice controller, for its policy (voiceEnabled, requireLiveMode).")]
        public VoiceDecisionController policy;
        [Tooltip("A component implementing IVoiceCommandSource (the Wit bridge).")]
        public MonoBehaviour sourceBehaviour;

        IVoiceCommandSource _source;
        bool _bound;

        /// <summary>The last phrase acted on (drivers and tests), or None.</summary>
        public SummonVoiceCommand LastCommand { get; private set; }

        void OnEnable()
        {
            _source = sourceBehaviour as IVoiceCommandSource;
            if (_source == null) return;
            _source.StatusChanged += OnStatus;
            _bound = true;
        }

        void OnDisable()
        {
            if (_bound && _source != null) _source.StatusChanged -= OnStatus;
            _bound = false;
        }

        bool CanAct()
        {
            if (_source == null || !_source.IsAvailable) return false;
            if (policy == null || !policy.voiceEnabled) return false;
            if (policy.requireLiveMode && (controller == null || !controller.IsLive)) return false;
            return SpatialWorkspaceState.Mode != WorkspaceMode.Desk;
        }

        void OnStatus(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            // Only final and partial transcripts the source reports as heard ("Heard: ...", "Heard ...").
            if (!message.StartsWith("Heard", System.StringComparison.OrdinalIgnoreCase)) return;
            if (!CanAct()) return;
            SummonVoiceCommand command = SummonVoicePhrases.Match(message);
            if (command == SummonVoiceCommand.None) return;
            // The source reports one utterance more than once (full transcription, then the
            // response): act on it once.
            float now = Time.unscaledTime;
            if (command == LastCommand && now - _lastRoutedAt < 2.5f) return;
            _lastRoutedAt = now;
            Route(command);
        }

        float _lastRoutedAt = -10f;

        /// <summary>Act on a recognised command. Public so a driver can exercise the routing.</summary>
        public void Route(SummonVoiceCommand command)
        {
            if (command == SummonVoiceCommand.None) return;
            LastCommand = command;
            if (command == SummonVoiceCommand.BringOfficeHere) SummonRequests.RaiseBringOfficeHere();
            else SummonRequests.RaiseSendOfficeBack();
        }
    }
}
