using System;
using UnityEngine;
using MuseDesk.Networking;

namespace MuseDesk.Voice
{
    /// <summary>
    /// The voice policy switch the decision paper and SummonVoiceRouter read (<see cref="voiceEnabled"/>,
    /// <see cref="requireLiveMode"/>, <see cref="sourceBehaviour"/>).
    ///
    /// Since Voice V2 this component no longer acts on anything it hears. It used to approve on
    /// "yes" / "sure" / "sounds good"; a decision is now spoken only as "approve" / "reject" through
    /// VoiceCommandCenter, with an undo window, and only while a decision is pending and the paper
    /// has been taken. Nothing here calls the session.
    /// </summary>
    public class VoiceDecisionController : MonoBehaviour
    {
        [Header("Wiring")]
        public SessionController controller;
        [Tooltip("The recogniser (WitVoiceSource). Read by the paper to decide whether to show its Voice chip.")]
        public MonoBehaviour sourceBehaviour;

        [Header("Policy")]
        [Tooltip("False: voice also works in the scripted demo (it only needs internet, not the desktop).")]
        public bool requireLiveMode = false;
        [Tooltip("Master switch for the paper's Voice chip.")]
        public bool voiceEnabled = false;
        [Tooltip("Decision paper (kept for existing scene wiring; unused).")]
        public MuseDesk.Documents.DecisionPanel panel;

        /// <summary>Kept for listeners of the first voice layer; raised by VoiceCommandCenter with the
        /// line it shows.</summary>
        public event Action<string> Heard;

        public void NotifyHeard(string line) { Heard?.Invoke(line); }

        /// <summary>"Developer, what are you changing?" targets the developer. Unaddressed questions
        /// go to the agent that raised the decision (the researcher).</summary>
        public static string AgentFor(string question)
        {
            string q = (question ?? "").ToLowerInvariant();
            int best = int.MaxValue; string who = "researcher";
            foreach (var id in new[] { "researcher", "developer", "tester" })
            {
                int at = q.IndexOf(id, StringComparison.Ordinal);
                if (at >= 0 && at < best) { best = at; who = id; }
            }
            return who;
        }
    }
}
