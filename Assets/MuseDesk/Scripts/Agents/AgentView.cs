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

        string _state = AgentStates.Idle;
        float _bobAmp;
        float _bobSpeed;
        Vector3 _bobHome;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (bob != null) _bobHome = bob.localPosition;
            SetState(AgentStates.Idle, displayName);
        }

        void Update()
        {
            if (bob == null) return;
            float y = Mathf.Sin(Time.time * _bobSpeed) * _bobAmp;
            bob.localPosition = _bobHome + Vector3.up * y;
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

        static string Pretty(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace('_', ' ');
    }
}
