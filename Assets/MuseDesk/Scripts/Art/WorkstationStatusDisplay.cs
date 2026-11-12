using TMPro;
using UnityEngine;
using MuseDesk.Agents;

namespace MuseDesk.Art
{
    /// <summary>
    /// A workstation's computer monitor: a lightweight world-space readout that reflects the
    /// REAL backend agent state for this station (never a scripted timer). It subscribes to the
    /// station's own AgentView (which the AgentRegistry drives from reducer AgentStatusChanged),
    /// so the screen only ever shows what the backend reported. Presentation only — it authorizes
    /// nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorkstationStatusDisplay : MonoBehaviour
    {
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
            string s = (state ?? "idle").ToLowerInvariant();
            string line;
            Color c;

            if (s.Contains("research")) { line = "Researching…"; c = Working; }
            else if (s.Contains("cod") || s.Contains("implement") || s.Contains("build")) { line = "Implementing…"; c = Working; }
            else if (s.Contains("test")) { line = "Running tests…"; c = Working; }
            else if (s.Contains("report") || s.Contains("review")) { line = "Reporting…"; c = Working; }
            else if (s.Contains("approach") || s.Contains("deliver")) { line = "Delivering…"; c = Alert; }
            else if (s.Contains("wait") || s.Contains("block")) { line = "Waiting"; c = Alert; }
            else if (s.Contains("complete") || s.Contains("done") || s.Contains("pass")) { line = "Complete"; c = Done; }
            else if (s.Contains("error") || s.Contains("fail")) { line = "Error"; c = Alert; }
            else { line = "Idle"; c = Idle; }

            if (statusText != null)
            {
                // Under the generic state line, show what the backend says the agent is actually
                // doing ("Running tests on new code", "Baseline tests: 3 passed / 0 failed", …).
                string progress = agent != null ? agent.Progress : null;
                if (!string.IsNullOrEmpty(progress))
                {
                    if (progress.Length > 60) progress = progress.Substring(0, 59) + "…";
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
