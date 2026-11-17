using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Calm, state-gated MaterialPropertyBlock pulse for workstation lamps and screens.
    /// Produces strictly zero animation and zero per-frame property updates when idle.
    /// </summary>
    [DisallowMultipleComponent]
    public class WallHabitatAmbientLifeVisualController : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Target Renderers")]
        [SerializeField] private Renderer[] lampRenderers;
        [SerializeField] private Renderer[] screenRenderers;

        [Header("Palette & Pulse Tuning")]
        [SerializeField] private Color activeLampColor = new Color(1.00f, 0.94f, 0.76f, 1f);
        [SerializeField] private Color idleLampColor = new Color(0.55f, 0.52f, 0.46f, 1f);
        [SerializeField] private Color activeScreenColor = new Color(0.184f, 0.639f, 0.604f, 1f);
        [SerializeField] private Color idleScreenColor = new Color(0.120f, 0.160f, 0.220f, 1f);

        [SerializeField, Min(0.5f)] private float pulsePeriodSeconds = 3.2f;
        [SerializeField, Range(0f, 2f)] private float activeMinEmission = 0.45f;
        [SerializeField, Range(0f, 2f)] private float activeMaxEmission = 0.95f;
        [SerializeField, Range(0f, 1f)] private float idleEmission = 0.08f;

        [Header("State & Comfort")]
        [SerializeField] private bool workActive;
        [SerializeField] private bool reducedMotion;

        private MaterialPropertyBlock _mpb;
        private float _pulsePhaseSeconds;
        private bool _initialized;

        public bool IsWorkActive => workActive;
        public bool ReducedMotion => reducedMotion;

        private void Awake()
        {
            ResolveReferences();
            ApplyStaticState();
        }

        public void ResolveReferences()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            bool needLamps = lampRenderers == null || lampRenderers.Length == 0;
            bool needScreens = screenRenderers == null || screenRenderers.Length == 0;
            if (needLamps || needScreens)
            {
                Renderer[] all = GetComponentsInChildren<Renderer>(true);
                List<Renderer> lamps = needLamps ? new List<Renderer>(4) : null;
                List<Renderer> screens = needScreens ? new List<Renderer>(4) : null;

                for (int i = 0; i < all.Length; i++)
                {
                    Renderer r = all[i];
                    if (r == null) continue;
                    string n = r.name;
                    if (lamps != null && (n.IndexOf("LampBulb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          n.IndexOf("Lamp_Bulb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          n.IndexOf("CabCeilingLamp", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        lamps.Add(r);
                    }
                    else if (screens != null && (n.IndexOf("MonitorScreen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 n.IndexOf("DisplayPanelScreen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 n.IndexOf("MiniMon_Screen", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        screens.Add(r);
                    }
                }

                if (lamps != null) lampRenderers = lamps.ToArray();
                if (screens != null) screenRenderers = screens.ToArray();
            }

            _initialized = true;
        }

        /// <summary>
        /// Enables or disables the calm active workstation pulse.
        /// Idle state is completely static with zero per-frame animation.
        /// </summary>
        public void SetWorkActive(bool active)
        {
            if (!_initialized) ResolveReferences();
            if (workActive == active) return;

            workActive = active;
            _pulsePhaseSeconds = 0f;
            ApplyStaticState();
        }

        public void SetReducedMotion(bool reduced)
        {
            if (!_initialized) ResolveReferences();
            if (reducedMotion == reduced) return;

            reducedMotion = reduced;
            ApplyStaticState();
        }

        private void Update()
        {
            if (!workActive || reducedMotion || !_initialized) return;

            float period = Mathf.Max(0.5f, pulsePeriodSeconds);
            _pulsePhaseSeconds += Time.deltaTime;
            if (_pulsePhaseSeconds >= period)
            {
                _pulsePhaseSeconds -= period;
            }

            float wave = 0.5f - 0.5f * Mathf.Cos((_pulsePhaseSeconds / period) * (Mathf.PI * 2f));
            float emission = Mathf.Lerp(activeMinEmission, activeMaxEmission, wave);
            ApplyGroup(lampRenderers, activeLampColor, emission);
            ApplyGroup(screenRenderers, activeScreenColor, emission);
        }

        private void ApplyStaticState()
        {
            if (workActive)
            {
                float steadyEmission = reducedMotion
                    ? (activeMinEmission + activeMaxEmission) * 0.5f
                    : activeMinEmission;
                ApplyGroup(lampRenderers, activeLampColor, steadyEmission);
                ApplyGroup(screenRenderers, activeScreenColor, steadyEmission);
            }
            else
            {
                ApplyGroup(lampRenderers, idleLampColor, idleEmission);
                ApplyGroup(screenRenderers, idleScreenColor, idleEmission);
            }
        }

        private void ApplyGroup(Renderer[] renderers, Color color, float emissionIntensity)
        {
            if (renderers == null || renderers.Length == 0) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            Color emissionCol = color * emissionIntensity;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, color);
                _mpb.SetColor(ColorId, color);
                _mpb.SetColor(EmissionColorId, emissionCol);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
