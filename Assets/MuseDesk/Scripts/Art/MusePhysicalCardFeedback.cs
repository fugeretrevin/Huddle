using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Tactile MaterialPropertyBlock tinting and smooth settle-curve visual feedback
    /// for physical cards, decision papers, and approval/rejection stamps.
    /// Zero per-frame allocations; Update() early-exits when resting.
    /// </summary>
    [DisallowMultipleComponent]
    public class MusePhysicalCardFeedback : MonoBehaviour
    {
        private enum FeedbackKind
        {
            None = 0,
            PickedUp = 1,
            ConfirmedSnap = 2,
            RejectedSoftReturn = 3,
            ApprovalConfirmed = 4,
            RejectionConfirmed = 5
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        // Restrained MuseDesk studio palette
        private static readonly Color ColHoverTeal = new Color(0.184f, 0.639f, 0.604f, 1f);
        private static readonly Color ColPickupGlow = new Color(0.320f, 0.800f, 0.780f, 1f);
        private static readonly Color ColApproveGreen = new Color(0.180f, 0.608f, 0.373f, 1f);
        private static readonly Color ColSoftAmber = new Color(0.878f, 0.627f, 0.188f, 1f);
        private static readonly Color ColRejectCoral = new Color(0.851f, 0.325f, 0.290f, 1f);

        [Header("Visual Targets")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Renderer[] targetRenderers;

        [Header("Timing (80-480 ms MuseDesk Range)")]
        [SerializeField, Range(0.08f, 0.48f)] private float hoverBlendSeconds = 0.10f;
        [SerializeField, Range(0.08f, 0.48f)] private float pickedUpSeconds = 0.12f;
        [SerializeField, Range(0.08f, 0.48f)] private float confirmedSnapSeconds = 0.16f;
        [SerializeField, Range(0.08f, 0.48f)] private float rejectedReturnSeconds = 0.28f;
        [SerializeField, Range(0.08f, 0.48f)] private float approvalConfirmedSeconds = 0.36f;
        [SerializeField, Range(0.08f, 0.48f)] private float rejectionConfirmedSeconds = 0.34f;

        [Header("Motion & Comfort")]
        [SerializeField] private bool animateScale = true;
        [SerializeField] private bool reducedMotion;

        [Header("Optional VFX & Audio Hooks")]
        [SerializeField] private ParticleSystem optionalFeedbackParticles;
        [SerializeField] private AudioSource optionalAudioSource;
        [SerializeField] private AudioClip pickupClip;
        [SerializeField] private AudioClip snapClip;
        [SerializeField] private AudioClip softReturnClip;
        [SerializeField] private AudioClip approvalClip;
        [SerializeField] private AudioClip rejectionClip;

        private MaterialPropertyBlock _mpb;
        private Color[] _authoredBaseColors;
        private Color[] _authoredEmissions;
        private bool[] _hasBaseColorProp;
        private bool[] _hasColorProp;
        private bool[] _hasEmissionProp;

        private Vector3 _baseLocalScale = Vector3.one;
        private Vector3 _baseLocalPos;
        private bool _resolved;

        private bool _isHovered;
        private float _hoverWeight;
        private FeedbackKind _activeKind = FeedbackKind.None;
        private float _elapsed;
        private float _duration;
        private Color _pulseColor = Color.white;
        private bool _dirtyVisuals;

        public bool IsHovered => _isHovered;
        public bool IsAnimating => _activeKind != FeedbackKind.None || _dirtyVisuals;

        private void Awake()
        {
            ResolveReferences();
        }

        public void ResolveReferences()
        {
            if (visualRoot == null)
            {
                visualRoot = transform;
            }
            _baseLocalScale = visualRoot.localScale;
            _baseLocalPos = visualRoot.localPosition;

            if (targetRenderers == null || targetRenderers.Length == 0)
            {
                targetRenderers = GetComponentsInChildren<Renderer>(true);
            }

            int count = targetRenderers != null ? targetRenderers.Length : 0;
            _authoredBaseColors = new Color[count];
            _authoredEmissions = new Color[count];
            _hasBaseColorProp = new bool[count];
            _hasColorProp = new bool[count];
            _hasEmissionProp = new bool[count];

            for (int i = 0; i < count; i++)
            {
                Renderer r = targetRenderers[i];
                if (r == null) continue;
                Material mat = r.sharedMaterial;
                if (mat == null)
                {
                    _authoredBaseColors[i] = Color.white;
                    _authoredEmissions[i] = Color.black;
                    continue;
                }

                bool hasBase = mat.HasProperty(BaseColorId);
                bool hasCol = mat.HasProperty(ColorId);
                bool hasEm = mat.HasProperty(EmissionColorId);
                _hasBaseColorProp[i] = hasBase;
                _hasColorProp[i] = hasCol;
                _hasEmissionProp[i] = hasEm;

                _authoredBaseColors[i] = hasBase
                    ? mat.GetColor(BaseColorId)
                    : (hasCol ? mat.GetColor(ColorId) : Color.white);
                _authoredEmissions[i] = hasEm ? mat.GetColor(EmissionColorId) : Color.black;
            }

            if (_mpb == null)
            {
                _mpb = new MaterialPropertyBlock();
            }
            _resolved = true;
        }

        public void SetReducedMotion(bool reduced)
        {
            reducedMotion = reduced;
            if (reduced && optionalFeedbackParticles != null)
            {
                optionalFeedbackParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        public void SetHovered(bool hovered)
        {
            if (!_resolved) ResolveReferences();
            if (_isHovered == hovered) return;
            _isHovered = hovered;
            _dirtyVisuals = true;
        }

        public void PlayPickedUp()
        {
            TriggerFeedback(FeedbackKind.PickedUp, pickedUpSeconds, ColPickupGlow, pickupClip, emitBurst: false);
        }

        public void PlayConfirmedSnap()
        {
            TriggerFeedback(FeedbackKind.ConfirmedSnap, confirmedSnapSeconds, ColHoverTeal, snapClip, emitBurst: true);
        }

        public void PlayRejectedSoftReturn()
        {
            TriggerFeedback(FeedbackKind.RejectedSoftReturn, rejectedReturnSeconds, ColSoftAmber, softReturnClip, emitBurst: false);
        }

        public void PlayApprovalConfirmed()
        {
            TriggerFeedback(FeedbackKind.ApprovalConfirmed, approvalConfirmedSeconds, ColApproveGreen, approvalClip, emitBurst: true);
        }

        public void PlayRejectionConfirmed()
        {
            TriggerFeedback(FeedbackKind.RejectionConfirmed, rejectionConfirmedSeconds, ColRejectCoral, rejectionClip, emitBurst: false);
        }

        private void TriggerFeedback(FeedbackKind kind, float seconds, Color tint, AudioClip clip, bool emitBurst)
        {
            if (!_resolved) ResolveReferences();
            _activeKind = kind;
            _duration = Mathf.Clamp(seconds, 0.08f, 0.48f);
            _elapsed = 0f;
            _pulseColor = tint;
            _dirtyVisuals = true;

            if (optionalAudioSource != null && clip != null)
            {
                optionalAudioSource.PlayOneShot(clip);
            }

            if (emitBurst && !reducedMotion && optionalFeedbackParticles != null)
            {
                optionalFeedbackParticles.Play(true);
            }
        }

        private void Update()
        {
            if (!_resolved) return;

            float targetHover = _isHovered ? 1f : 0f;
            bool hoverSettled = Mathf.Abs(_hoverWeight - targetHover) <= 0.001f;
            if (!_dirtyVisuals && _activeKind == FeedbackKind.None && hoverSettled)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (!hoverSettled)
            {
                float step = dt / Mathf.Max(0.08f, hoverBlendSeconds);
                _hoverWeight = Mathf.MoveTowards(_hoverWeight, targetHover, step);
                hoverSettled = Mathf.Abs(_hoverWeight - targetHover) <= 0.001f;
                if (hoverSettled) _hoverWeight = targetHover;
            }

            float pulseStrength = 0f;
            float scaleMul = 1f + (_isHovered && !reducedMotion ? 0.025f * _hoverWeight : 0f);
            Vector3 localOffset = Vector3.zero;

            if (_activeKind != FeedbackKind.None)
            {
                _elapsed += dt;
                float u = Mathf.Clamp01(_elapsed / Mathf.Max(0.08f, _duration));
                EvaluateCurve(_activeKind, u, out pulseStrength, out float animScaleDelta, out localOffset);

                if (!reducedMotion)
                {
                    scaleMul += animScaleDelta;
                }

                if (u >= 1f)
                {
                    _activeKind = FeedbackKind.None;
                    pulseStrength = 0f;
                    localOffset = Vector3.zero;
                }
            }

            ApplyTransforms(scaleMul, localOffset);
            ApplyTint(_hoverWeight, pulseStrength);

            _dirtyVisuals = !hoverSettled || _activeKind != FeedbackKind.None;
        }

        private static void EvaluateCurve(
            FeedbackKind kind,
            float u,
            out float pulseStrength,
            out float scaleDelta,
            out Vector3 localOffset)
        {
            float easeOut = 1f - (1f - u) * (1f - u);
            float bell = Mathf.Sin(Mathf.PI * u);
            localOffset = Vector3.zero;

            switch (kind)
            {
                case FeedbackKind.PickedUp:
                    pulseStrength = (1f - easeOut) * 0.55f;
                    scaleDelta = 0.05f * bell;
                    localOffset = new Vector3(0f, 0.006f * bell, 0f);
                    break;

                case FeedbackKind.ConfirmedSnap:
                    // Quick crisp overshoot settling smoothly to 1.0
                    float snapWave = Mathf.Sin(u * Mathf.PI * 1.5f) * (1f - u);
                    pulseStrength = (1f - easeOut) * 0.65f;
                    scaleDelta = 0.055f * snapWave;
                    break;

                case FeedbackKind.RejectedSoftReturn:
                    // Gentle damped lateral nudge (never harsh)
                    float damp = (1f - u) * (1f - u);
                    float nudge = Mathf.Sin(u * Mathf.PI * 3f) * damp;
                    pulseStrength = bell * 0.50f;
                    scaleDelta = -0.03f * bell;
                    localOffset = new Vector3(0.008f * nudge, 0f, 0f);
                    break;

                case FeedbackKind.ApprovalConfirmed:
                    pulseStrength = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.7f)) * 0.75f;
                    scaleDelta = 0.065f * bell;
                    localOffset = new Vector3(0f, 0.005f * bell, 0f);
                    break;

                case FeedbackKind.RejectionConfirmed:
                    float rejDamp = (1f - u);
                    pulseStrength = bell * 0.65f;
                    scaleDelta = -0.04f * bell;
                    localOffset = new Vector3(0.006f * Mathf.Sin(u * Mathf.PI * 2.5f) * rejDamp, 0f, 0f);
                    break;

                default:
                    pulseStrength = 0f;
                    scaleDelta = 0f;
                    break;
            }
        }

        private void ApplyTransforms(float scaleMul, Vector3 localOffset)
        {
            if (visualRoot == null) return;
            if (animateScale)
            {
                visualRoot.localScale = _baseLocalScale * scaleMul;
            }
            // Only offset localPosition if visualRoot is a dedicated child transform, avoiding conflicts with grab/snap roots.
            if (visualRoot != transform)
            {
                visualRoot.localPosition = reducedMotion ? _baseLocalPos : (_baseLocalPos + localOffset);
            }
        }

        private void ApplyTint(float hoverWeight, float pulseStrength)
        {
            if (targetRenderers == null || targetRenderers.Length == 0) return;

            for (int i = 0; i < targetRenderers.Length; i++)
            {
                Renderer r = targetRenderers[i];
                if (r == null) continue;

                Color baseCol = _authoredBaseColors[i];
                Color targetCol = Color.Lerp(baseCol, ColHoverTeal, hoverWeight * 0.22f);
                targetCol = Color.Lerp(targetCol, _pulseColor, Mathf.Clamp01(pulseStrength * 0.45f));
                targetCol.a = baseCol.a;

                Color baseEm = _authoredEmissions[i];
                Color hoverEm = ColHoverTeal * (hoverWeight * 0.18f);
                Color pulseEm = _pulseColor * (pulseStrength * 0.45f);
                Color finalEm = baseEm + hoverEm + pulseEm;

                r.GetPropertyBlock(_mpb);
                if (_hasBaseColorProp[i] || !_hasColorProp[i]) _mpb.SetColor(BaseColorId, targetCol);
                if (_hasColorProp[i]) _mpb.SetColor(ColorId, targetCol);
                if (_hasEmissionProp[i]) _mpb.SetColor(EmissionColorId, finalEm);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
