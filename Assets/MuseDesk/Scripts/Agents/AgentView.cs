using UnityEngine;
using TMPro;
using MuseDesk.Models;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Visual projection of ONE backend agent. Never advances task state itself — it only
    /// reflects the state the reducer gives it (AGENTS invariant: animation is not authority).
    /// Foundation version uses a primitive body + label + procedural motion; swap in art/Animator later.
    /// </summary>
    public class AgentView : MonoBehaviour
    {
        [Header("Identity")]
        public string agentId = "researcher";
        public string displayName = "Researcher";

        [Header("Visuals")]
        public Renderer bodyRenderer;
        public TMP_Text label;
        public Transform bob;          // child that bobs while working
        [Tooltip("Exclamation-point indicator shown when this agent needs a decision.")]
        public GameObject alert;

        public void SetAlert(bool on) { if (alert) alert.SetActive(on); }

        [Header("Walk")]
        [Tooltip("Set by the builder; drives a walk cycle (bounce + arm swing) while the agent translates.")]
        public AgentMovement movement;
        public Transform armL;
        public Transform armR;
        public float walkSpeed = 9f;
        public float walkBounce = 0.014f;
        public float armSwingDeg = 38f;

        [Header("Motion")]
        public float baseBobHeight = 0.004f;
        public float baseBobSpeed = 2f;

        static readonly int ColorId = Shader.PropertyToID("_BaseColor");

        enum Motion { Calm, Read, Type, Examine, Hop, Shake }

        /// <summary>Current backend-driven state (set only via SetState). Rigged visuals read this.</summary>
        public string State => _state;
        /// <summary>The agent's own latest progress line from the backend, or null if none was sent.</summary>
        public string Progress { get; private set; }
        /// <summary>Raised whenever SetState changes/sets the state, so an Animator bridge can react.</summary>
        public event System.Action<string> StateChanged;

        string _state = AgentStates.Idle;
        string _labelState, _labelName, _labelText;   // last label content, to skip rebuilding an unchanged string
        float _bobAmp;
        float _bobSpeed;
        Motion _motion = Motion.Calm;
        Vector3 _bobHome;
        Quaternion _bobRot;
        Quaternion _armLHome = Quaternion.identity, _armRHome = Quaternion.identity;
        float _hopT = 99f;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (bob != null) { _bobHome = bob.localPosition; _bobRot = bob.localRotation; }
            if (armL != null) _armLHome = armL.localRotation;
            if (armR != null) _armRHome = armR.localRotation;
            if (alert != null) alert.SetActive(false);
            SetState(AgentStates.Idle, displayName);
        }

        void Update()
        {
            if (bob == null) return;

            // Walking overrides idle/working motion: a little bounce + opposed arm swing so the
            // agent reads as a character strolling up, not a prop sliding across the desk.
            if (movement != null && movement.IsMoving) { Walk(); return; }

            float t = Time.time * _bobSpeed;
            Vector3 pos = _bobHome;
            Quaternion rot = _bobRot;

            // Keep the hands alive even while standing: every state gets a little arm motion so
            // the agents never look like frozen props. Walking still overrides this (above).
            IdleArms(Time.time);

            switch (_motion)
            {
                case Motion.Read:    // gentle side-to-side sway
                    rot = _bobRot * Quaternion.Euler(0f, Mathf.Sin(t) * 10f, 0f);
                    pos += Vector3.up * Mathf.Abs(Mathf.Sin(t * 0.5f)) * _bobAmp;
                    break;
                case Motion.Type:    // fast small vertical taps
                    pos += Vector3.up * Mathf.Abs(Mathf.Sin(t * 2f)) * _bobAmp;
                    rot = _bobRot * Quaternion.Euler(Mathf.Sin(t * 2f) * 4f, 0f, 0f);
                    break;
                case Motion.Examine: // slow forward/back tilt
                    rot = _bobRot * Quaternion.Euler(Mathf.Sin(t) * 12f, 0f, 0f);
                    break;
                case Motion.Hop:     // one-time celebratory hop then settle
                    _hopT += Time.deltaTime;
                    pos += Vector3.up * Mathf.Max(0f, Mathf.Sin(_hopT * 6f)) * (_hopT < 1.2f ? 0.02f : 0f);
                    break;
                case Motion.Shake:   // quick nervous shake
                    pos += Vector3.right * Mathf.Sin(t * 8f) * _bobAmp;
                    break;
                default:             // Calm idle breathing
                    pos += Vector3.up * Mathf.Sin(t) * _bobAmp;
                    break;
            }
            bob.localPosition = pos;
            bob.localRotation = rot;
        }

        void Walk()
        {
            float t = Time.time * walkSpeed;
            // Two bounces per stride, with a tiny forward-lean into the step.
            bob.localPosition = _bobHome + Vector3.up * Mathf.Abs(Mathf.Sin(t)) * walkBounce;
            bob.localRotation = _bobRot * Quaternion.Euler(7f, 0f, Mathf.Sin(t) * 3f);
            float swing = Mathf.Sin(t) * armSwingDeg;
            if (armL != null) armL.localRotation = _armLHome * Quaternion.Euler(swing, 0f, 0f);
            if (armR != null) armR.localRotation = _armRHome * Quaternion.Euler(-swing, 0f, 0f);
        }

        void RestArms()
        {
            if (armL != null) armL.localRotation = Quaternion.Slerp(armL.localRotation, _armLHome, Time.deltaTime * 8f);
            if (armR != null) armR.localRotation = Quaternion.Slerp(armR.localRotation, _armRHome, Time.deltaTime * 8f);
        }

        // Subtle, always-on hand/arm life keyed to what the agent is "doing". Small angles so it
        // reads as breathing/fidgeting, not flailing. Opposed on left/right for a natural look.
        void IdleArms(float time)
        {
            if (armL == null && armR == null) return;
            float a, b;   // degrees for L and R
            switch (_motion)
            {
                case Motion.Type:     // quick alternating taps, like working a keyboard
                    a = (Mathf.Sin(time * 11f) * 0.5f + 0.5f) * 22f;
                    b = (Mathf.Sin(time * 11f + Mathf.PI) * 0.5f + 0.5f) * 22f;
                    break;
                case Motion.Read:     // slow page-holding sway
                    a = Mathf.Sin(time * 1.6f) * 8f + 6f;
                    b = Mathf.Sin(time * 1.6f + 0.6f) * 8f + 6f;
                    break;
                case Motion.Examine:  // one hand raised, inspecting
                    a = 30f + Mathf.Sin(time * 2f) * 6f;
                    b = Mathf.Sin(time * 2f) * 5f;
                    break;
                case Motion.Shake:    // agitated
                    a = Mathf.Sin(time * 9f) * 16f;
                    b = Mathf.Sin(time * 9f + 0.3f) * 16f;
                    break;
                default:              // calm breathing fidget
                    a = Mathf.Sin(time * 1.3f) * 6f;
                    b = Mathf.Sin(time * 1.3f + Mathf.PI) * 6f;
                    break;
            }
            if (armL != null)
                armL.localRotation = Quaternion.Slerp(armL.localRotation, _armLHome * Quaternion.Euler(a, 0f, 0f), Time.deltaTime * 6f);
            if (armR != null)
                armR.localRotation = Quaternion.Slerp(armR.localRotation, _armRHome * Quaternion.Euler(b, 0f, 0f), Time.deltaTime * 6f);
        }

        public void SetState(string state, string progress = null)
        {
            _state = string.IsNullOrEmpty(state) ? _state : state;
            Progress = progress;
            var (color, busy) = Style(_state);
            if (bodyRenderer != null)
            {
                bodyRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(ColorId, color);
                bodyRenderer.SetPropertyBlock(_mpb);
            }
            _bobAmp = busy * baseBobHeight;
            _bobSpeed = baseBobSpeed * Mathf.Max(0.5f, busy);
            var newMotion = MotionFor(_state);
            if (newMotion == Motion.Hop && _motion != Motion.Hop) _hopT = 0f;
            _motion = newMotion;
            // Rebuild the label string only when what it shows changed (SetState is called on every progress event).
            if (label != null && (_labelText == null || _labelState != _state || _labelName != displayName))
            {
                _labelState = _state;
                _labelName = displayName;
                _labelText = $"{displayName}\n<size=60%>{Pretty(_state)}</size>";
                label.text = _labelText;
            }
            StateChanged?.Invoke(_state);
        }

        // state -> (color, busyFactor 0..~2)
        // Colours come from the shared state palette (working = teal for every role, needs you = amber, done =
        // success green, error = coral). Previously coding was "completed" green and testing "needs you" amber.
        static (Color, float) Style(string s) => s switch
        {
            AgentStates.Researching => (MuseDesk.Workspace.OfficeTokens.Teal, 1.4f),
            AgentStates.Coding      => (MuseDesk.Workspace.OfficeTokens.Teal, 1.6f),
            AgentStates.Testing     => (MuseDesk.Workspace.OfficeTokens.Teal, 1.4f),
            AgentStates.Reporting   => (MuseDesk.Workspace.OfficeTokens.Teal, 1.0f),
            AgentStates.Waiting     => (MuseDesk.Workspace.OfficeTokens.Amber, 0.4f),
            AgentStates.Approaching => (MuseDesk.Workspace.OfficeTokens.Amber, 1.0f),
            AgentStates.Complete    => (MuseDesk.Workspace.OfficeTokens.Success, 0.3f),
            AgentStates.Error       => (MuseDesk.Workspace.OfficeTokens.Coral, 0.6f),
            _                       => (MuseDesk.Workspace.OfficeTokens.Neutral, 0.3f), // idle
        };

        static Motion MotionFor(string s) => s switch
        {
            AgentStates.Researching => Motion.Read,
            AgentStates.Coding      => Motion.Type,
            AgentStates.Testing     => Motion.Examine,
            AgentStates.Complete    => Motion.Hop,
            AgentStates.Error       => Motion.Shake,
            _                       => Motion.Calm,
        };

        static string Pretty(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace('_', ' ');
    }
}
