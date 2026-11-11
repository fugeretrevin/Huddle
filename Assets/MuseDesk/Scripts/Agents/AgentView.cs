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

        [Header("Motion")]
        public float baseBobHeight = 0.004f;
        public float baseBobSpeed = 2f;

        static readonly int ColorId = Shader.PropertyToID("_BaseColor");

        enum Motion { Calm, Read, Type, Examine, Hop, Shake }

        string _state = AgentStates.Idle;
        float _bobAmp;
        float _bobSpeed;
        Motion _motion = Motion.Calm;
        Vector3 _bobHome;
        Quaternion _bobRot;
        float _hopT = 99f;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (bob != null) { _bobHome = bob.localPosition; _bobRot = bob.localRotation; }
            SetState(AgentStates.Idle, displayName);
        }

        void Update()
        {
            if (bob == null) return;
            float t = Time.time * _bobSpeed;
            Vector3 pos = _bobHome;
            Quaternion rot = _bobRot;

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

        public void SetState(string state, string progress = null)
        {
            _state = string.IsNullOrEmpty(state) ? _state : state;
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
            if (label != null)
                label.text = $"{displayName}\n<size=60%>{Pretty(_state)}</size>";
        }

        // state -> (color, busyFactor 0..~2)
        static (Color, float) Style(string s) => s switch
        {
            AgentStates.Researching => (new Color(0.30f, 0.55f, 0.95f), 1.4f),
            AgentStates.Coding      => (new Color(0.30f, 0.80f, 0.45f), 1.6f),
            AgentStates.Testing     => (new Color(0.95f, 0.65f, 0.25f), 1.4f),
            AgentStates.Reporting   => (new Color(0.70f, 0.70f, 0.95f), 1.0f),
            AgentStates.Waiting     => (new Color(0.85f, 0.80f, 0.35f), 0.4f),
            AgentStates.Approaching => (new Color(0.85f, 0.80f, 0.35f), 1.0f),
            AgentStates.Complete    => (new Color(0.40f, 0.85f, 0.55f), 0.3f),
            AgentStates.Error       => (new Color(0.90f, 0.35f, 0.35f), 0.6f),
            _                       => (new Color(0.72f, 0.72f, 0.76f), 0.3f), // idle
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
