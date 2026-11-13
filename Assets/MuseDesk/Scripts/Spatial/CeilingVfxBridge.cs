using System.Text;
using UnityEngine;
using MuseDesk.Art;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Drives Antigravity's deployment effects (CeilingDeploymentVFXController: halo, portal cone,
    /// ripples, tether motes, aperture and arrival particles) from the project's own pod state
    /// machine. The art-side CeilingWorkspacePodController is NOT used. Binding follows CeilingPodArt:
    /// CeilingPodController.Machine is looked up lazily in Update (it is null until the pod has its
    /// transition controller) and its StateChanged event is subscribed once; nothing is polled for the
    /// state. The effects are hidden and stopped outside Ceiling mode and while the pod's world-fixed
    /// visuals are off.
    ///
    /// Quest budget: the cone, halo and rings are additive transparents over passthrough. Switch
    /// <see cref="vfxEnabled"/> off to remove all of it, or <see cref="particlesEnabled"/> off to
    /// remove only the three particle systems (their renderers and emission are switched off).
    /// Both can be flipped in Play Mode. The effects move nothing and contain no interactables.
    /// </summary>
    public class CeilingVfxBridge : MonoBehaviour
    {
        public CeilingPodController pod;
        public CeilingDeploymentVFXController vfx;

        [Tooltip("Master switch. Off: every effect is hidden and stopped.")]
        public bool vfxEnabled = true;
        [Tooltip("Off: only the particle systems are removed (the cheaper option); cone, halo and rings stay.")]
        public bool particlesEnabled = true;
        [Tooltip("Reversible quality preset. Switch to LowCostNoParticles or MinimalIrisHalo if Quest GPU profiling shows overdraw cost.")]
        public CeilingVfxQualityPreset qualityPreset = CeilingVfxQualityPreset.Full;

        CeilingPodMachine _machine;
        bool _bound;
        CeilingPodState _state = CeilingPodState.Docked;
        CeilingVfxPhase _phase = CeilingVfxPhase.DockedIdle;
        bool _shown;
        bool _applied;               // the hidden or shown state has been applied at least once
        bool _lastParticles = true;
        bool _particlesApplied;
        CeilingVfxQualityPreset _lastPreset = CeilingVfxQualityPreset.Full;

        ParticleSystem[] _systems;
        ParticleSystemRenderer[] _systemRenderers;
        GameObject[] _meshEffects;   // cone, halo and rings (non-particle renderers)

        /// <summary>The pod machine has been found and subscribed to.</summary>
        public bool Bound => _bound;
        /// <summary>The effects are currently shown (controller running).</summary>
        public bool Shown => _shown;
        public CeilingPodState PodState => _state;
        public CeilingVfxPhase Phase => _phase;

        void Awake()
        {
            Collect();
            Apply(false);
            _applied = true;
        }

        void OnEnable()
        {
            SpatialWorkspaceState.Changed += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            SpatialWorkspaceState.Changed -= Refresh;
            if (_bound && _machine != null) _machine.StateChanged -= OnPodChanged;
            _machine = null;
            _bound = false;
            Apply(false);
            _shown = false;
        }

        void Collect()
        {
            if (vfx == null || _systems != null) return;
            _systems = vfx.GetComponentsInChildren<ParticleSystem>(true);
            _systemRenderers = new ParticleSystemRenderer[_systems.Length];
            for (int i = 0; i < _systems.Length; i++) _systemRenderers[i] = _systems[i].GetComponent<ParticleSystemRenderer>();
            var all = vfx.GetComponentsInChildren<Renderer>(true);
            int n = 0;
            for (int i = 0; i < all.Length; i++) if (!(all[i] is ParticleSystemRenderer)) n++;
            _meshEffects = new GameObject[n];
            n = 0;
            for (int i = 0; i < all.Length; i++) if (!(all[i] is ParticleSystemRenderer)) _meshEffects[n++] = all[i].gameObject;
        }

        void Update()
        {
            if (!_bound && pod != null)
            {
                _machine = pod.Machine;                  // null until the pod has its transition controller
                if (_machine != null)
                {
                    _machine.StateChanged += OnPodChanged;
                    _bound = true;
                    OnPodChanged();
                }
            }
            // The pod switches its visuals in LateUpdate, and the two switches can be flipped in Play Mode.
            if (ShouldShow() != _shown || particlesEnabled != _lastParticles || qualityPreset != _lastPreset) Refresh();
        }

        bool ShouldShow()
        {
            return CeilingVfxMap.ShouldShow(SpatialWorkspaceState.Mode == WorkspaceMode.Ceiling, pod != null && pod.VisualsShown, vfxEnabled);
        }

        void Refresh()
        {
            if (vfx == null) return;
            bool show = ShouldShow();
            if (show != _shown || !_applied)
            {
                _shown = show;
                _applied = true;
                Apply(show);
                if (show) vfx.OnPodState(ToArt(_phase));
            }
            bool wantParticles = particlesEnabled && qualityPreset == CeilingVfxQualityPreset.Full;
            if (!_particlesApplied || wantParticles != _lastParticles || qualityPreset != _lastPreset)
            {
                _lastPreset = qualityPreset;
                vfx.SetQualityPreset(qualityPreset);
                ApplyParticles(wantParticles);
            }
        }

        void OnPodChanged()
        {
            if (_machine == null) return;
            CeilingPodState next = _machine.State;
            if (_stateSeen && next == _state) return;   // StateChanged also fires for status text
            _stateSeen = true;
            _state = next;
            _phase = CeilingVfxMap.Map(next);
            if (_shown && vfx != null) vfx.OnPodState(ToArt(_phase));
        }
        bool _stateSeen;

        void Apply(bool show)
        {
            if (vfx == null) return;
            Collect();
            vfx.enabled = show;
            _lastPreset = qualityPreset;
            vfx.SetQualityPreset(qualityPreset);
            if (!show) StopAll();
            ApplyParticles(particlesEnabled && qualityPreset == CeilingVfxQualityPreset.Full);
        }

        void ApplyParticles(bool enableParticles)
        {
            _lastParticles = enableParticles;
            _particlesApplied = true;
            if (_systems == null) return;
            for (int i = 0; i < _systems.Length; i++)
            {
                var ps = _systems[i];
                if (ps == null) continue;
                var em = ps.emission;
                em.enabled = enableParticles;
                if (_systemRenderers[i] != null) _systemRenderers[i].enabled = enableParticles;
                if (!enableParticles) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        void StopAll()
        {
            if (_systems != null)
                for (int i = 0; i < _systems.Length; i++)
                    if (_systems[i] != null) _systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (_meshEffects != null)
                for (int i = 0; i < _meshEffects.Length; i++)
                    if (_meshEffects[i] != null && _meshEffects[i].activeSelf) _meshEffects[i].SetActive(false);
        }

        static CeilingWorkspacePodController.PodState ToArt(CeilingVfxPhase p)
        {
            switch (p)
            {
                case CeilingVfxPhase.Unlocking: return CeilingWorkspacePodController.PodState.Unlocking;
                case CeilingVfxPhase.Descending: return CeilingWorkspacePodController.PodState.Descending;
                case CeilingVfxPhase.Stabilizing: return CeilingWorkspacePodController.PodState.Stabilizing;
                case CeilingVfxPhase.DeployedIdle: return CeilingWorkspacePodController.PodState.DeployedIdle;
                case CeilingVfxPhase.ClosingShelf: return CeilingWorkspacePodController.PodState.ClosingShelf;
                case CeilingVfxPhase.ReturningToDock: return CeilingWorkspacePodController.PodState.ReturningToDock;
                case CeilingVfxPhase.DockLocking: return CeilingWorkspacePodController.PodState.DockLocking;
                default: return CeilingWorkspacePodController.PodState.DockedIdle;
            }
        }

        /// <summary>Driver report: binding, state and which effects are active.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append($"bridge bound={_bound} shown={_shown} preset={qualityPreset} vfxEnabled={vfxEnabled} particlesEnabled={particlesEnabled} podState={_state} phase={_phase} controller={(vfx != null ? vfx.CurrentVfxState.ToString() : "MISSING")} enabled={(vfx != null && vfx.enabled)}");
            if (_meshEffects != null)
                foreach (var go in _meshEffects) if (go != null) sb.Append($"\n  {go.name}: {(go.activeSelf ? "ACTIVE" : "off")}");
            if (_systems != null)
                foreach (var ps in _systems) if (ps != null) sb.Append($"\n  {ps.name}: playing={ps.isPlaying} particles={ps.particleCount} emission={ps.emission.enabled}");
            return sb.ToString();
        }
    }
}
