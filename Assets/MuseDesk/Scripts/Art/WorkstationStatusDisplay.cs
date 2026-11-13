using System;
using TMPro;
using UnityEngine;
using MuseDesk.Agents;

namespace MuseDesk.Art
{
    /// <summary>
    /// A workstation's computer monitor: a lightweight world-space readout that reflects the
    /// REAL backend agent state for this station (never a scripted timer). It subscribes to the
    /// station's own AgentView (which the AgentRegistry drives from reducer AgentStatusChanged),
    /// so the screen only ever shows what the backend reported. Presentation only: it authorizes
    /// nothing.
    ///
    /// The state-to-words mapping lives here as static helpers (<see cref="Classify"/>,
    /// <see cref="Words"/>, <see cref="ActivityWords"/>) and is shared with the desk nameplates
    /// (AgentNameplate) and the touch summary, so every surface describes a state the same way.
    /// In the redesigned office the floating Monitor panels are switched off and the nameplates
    /// carry this information; the component still works if a Monitor is switched back on.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorkstationStatusDisplay : MonoBehaviour
    {
        /// <summary>What an agent is doing, in the terms a person would use.</summary>
        public enum Activity
        {
            Idle, Researching, Coding, Testing, Reporting, Approaching,
            Waiting, WaitingForUser, Complete, Error
        }

        [Tooltip("The agent whose backend state this monitor mirrors.")]
        public AgentView agent;

        [Tooltip("Headline label, e.g. RESEARCH / DEVELOP / TEST.")]
        public string stationLabel = "STATION";

        [Tooltip("Main status line (set automatically from the agent state).")]
        public TMP_Text statusText;

        [Tooltip("Optional small activity lamp recoloured by state.")]
        public Renderer activityLamp;

        static readonly Color Idle    = new Color(0.45f, 0.5f, 0.56f);
        static readonly Color Working = new Color(0.25f, 0.62f, 0.95f);
        static readonly Color Done    = new Color(0.22f, 0.76f, 0.45f);
        static readonly Color Alert   = new Color(0.95f, 0.45f, 0.25f);

        AgentView _bound;
        MaterialPropertyBlock _mpb;

        // ── shared state-to-words mapping ──

        static bool Has(string s, string part) => s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Map a backend agent state string to an activity. Tolerant of unknown or compound values
        /// (it looks for the known stems), so a new backend state degrades to Idle instead of
        /// showing a raw identifier. <paramref name="decisionPendingOnThisAgent"/> turns a plain
        /// "waiting" into "waiting for the user" when a decision from this agent is awaiting them.
        /// </summary>
        public static Activity Classify(string state, bool decisionPendingOnThisAgent = false)
        {
            if (string.IsNullOrEmpty(state)) return Activity.Idle;
            if (Has(state, "research")) return Activity.Researching;
            if (Has(state, "cod") || Has(state, "implement") || Has(state, "build")) return Activity.Coding;
            if (Has(state, "test")) return Activity.Testing;
            if (Has(state, "report") || Has(state, "review")) return Activity.Reporting;
            if (Has(state, "approach") || Has(state, "deliver")) return Activity.Approaching;
            if (Has(state, "wait") || Has(state, "block"))
                return decisionPendingOnThisAgent ? Activity.WaitingForUser : Activity.Waiting;
            if (Has(state, "complete") || Has(state, "done") || Has(state, "pass")) return Activity.Complete;
            if (Has(state, "error") || Has(state, "fail")) return Activity.Error;
            return Activity.Idle;
        }

        /// <summary>Plain words for an activity (constant strings, no allocation).</summary>
        public static string Words(Activity activity)
        {
            switch (activity)
            {
                case Activity.Researching: return "Reading the brief";
                case Activity.Coding: return "Writing code";
                case Activity.Testing: return "Running tests";
                case Activity.Reporting: return "Writing the report";
                case Activity.Approaching: return "Bringing you a paper";
                case Activity.Waiting: return "Waiting";
                case Activity.WaitingForUser: return "Waiting for you";
                case Activity.Complete: return "Done";
                case Activity.Error: return "Needs attention";
                default: return "Idle";
            }
        }

        /// <summary>Plain words for a backend agent state string.</summary>
        public static string ActivityWords(string state, bool decisionPendingOnThisAgent = false)
            => Words(Classify(state, decisionPendingOnThisAgent));

        // ── the monitor itself ──

        void OnEnable()
        {
            _mpb = new MaterialPropertyBlock();
            TryBind();
            // Reflect whatever the backend state already is.
            if (agent != null) Render(agent.State);
        }

        void Update()
        {
            if (_bound != agent) TryBind();
        }

        void TryBind()
        {
            if (_bound != null) _bound.StateChanged -= Render;
            _bound = agent;
            if (_bound != null) _bound.StateChanged += Render;
        }

        void OnDisable()
        {
            if (_bound != null) _bound.StateChanged -= Render;
            _bound = null;
        }

        void Render(string state)
        {
            Activity activity = Classify(state);
            string line = Words(activity);
            Color c;
            switch (activity)
            {
                case Activity.Researching:
                case Activity.Coding:
                case Activity.Testing:
                case Activity.Reporting: c = Working; break;
                case Activity.Approaching:
                case Activity.Waiting:
                case Activity.WaitingForUser:
                case Activity.Error: c = Alert; break;
                case Activity.Complete: c = Done; break;
                default: c = Idle; break;
            }

            if (statusText != null)
            {
                // Under the generic state line, show what the backend says the agent is actually
                // doing ("Running tests on new code", "Baseline tests: 3 passed / 0 failed", ...).
                string progress = agent != null ? agent.Progress : null;
                if (!string.IsNullOrEmpty(progress))
                {
                    if (progress.Length > 60) progress = progress.Substring(0, 57) + "...";
                    line += $"\n<size=80%><color=#D8DEE6>{progress}</color></size>";
                }
                statusText.text = $"<b>{stationLabel}</b>\n<size=70%>{line}</size>";
                statusText.color = c;
            }
            if (activityLamp != null)
            {
                activityLamp.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", c);
                _mpb.SetColor("_Color", c);
                _mpb.SetColor("_EmissionColor", c * 1.4f);
                activityLamp.SetPropertyBlock(_mpb);
            }
        }
    }
}
