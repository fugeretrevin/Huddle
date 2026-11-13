using Oculus.Interaction;
using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// "You can pick this up" feedback for a hand-grabbable object. While a hand hovers within grab
    /// range (the Interaction SDK interactable reports Hover) an indicator object is shown and the
    /// listed renderers brighten; while it is held (Select) the indicator switches to the held look.
    /// A short enter delay and a longer exit delay stop it flickering with hand-tracking jitter.
    ///
    /// Presentation only: it never grabs, moves or changes anything.
    /// </summary>
    [DisallowMultipleComponent]
    public class GrabHoverHighlight : MonoBehaviour
    {
        [Tooltip("Component implementing the SDK's IInteractableView (e.g. HandGrabInteractable). Auto-found on this object if empty.")]
        public MonoBehaviour interactable;
        [Tooltip("Shown while hovered or held (an outline frame, a ring, an arrow). Optional.")]
        public GameObject indicator;
        [Tooltip("Renderers that brighten slightly while hovered. Optional.")]
        public Renderer[] brighten;
        [Range(1f, 1.6f)] public float hoverBrightness = 1.15f;
        [Tooltip("Colour of the indicator while hovered / while held.")]
        public Color hoverColor = new Color(0.35f, 0.85f, 0.95f, 0.22f);
        public Color heldColor = new Color(0.98f, 0.82f, 0.30f, 0.36f);
        [Tooltip("Seconds a hover must persist before it shows (debounce).")]
        public float enterDelay = 0.06f;
        [Tooltip("Seconds the highlight lingers after the hand leaves (hysteresis).")]
        public float exitDelay = 0.18f;
        [Tooltip("Soft tick when the highlight appears.")]
        [Range(0f, 1f)] public float tickVolume = 0.12f;

        /// <summary>True while a hand is within grab range or holding the object (after debounce).</summary>
        public bool Highlighted { get; private set; }
        /// <summary>True while the object is held.</summary>
        public bool Held { get; private set; }

        IInteractableView _view;
        Renderer _indicatorRenderer;
        MaterialPropertyBlock _block;
        Color[] _baseColors;
        float _hoverFor, _clearFor;
        bool _lastHeld;
        AudioSource _audio;
        static AudioClip s_tick;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            ResolveView();
            if (indicator != null)
            {
                _indicatorRenderer = indicator.GetComponent<Renderer>();
                indicator.SetActive(false);
            }
            if (brighten != null)
            {
                _baseColors = new Color[brighten.Length];
                for (int i = 0; i < brighten.Length; i++)
                {
                    var m = brighten[i] != null ? brighten[i].sharedMaterial : null;
                    _baseColors[i] = m == null ? Color.white
                        : m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId)
                        : m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white;
                }
            }
        }

        void ResolveView()
        {
            if (interactable != null) _view = interactable as IInteractableView;
            if (_view != null) return;
            foreach (var mb in GetComponents<MonoBehaviour>())
                if (mb is IInteractableView v) { _view = v; interactable = mb; return; }
        }

        void OnDisable()
        {
            _hoverFor = _clearFor = 0f;
            Apply(false, false);
        }

        void Update()
        {
            if (_view == null) return;
            var s = _view.State;
            bool held = s == InteractableState.Select;
            bool near = held || s == InteractableState.Hover;

            if (near) { _hoverFor += Time.deltaTime; _clearFor = 0f; }
            else { _clearFor += Time.deltaTime; _hoverFor = 0f; }

            bool want = Highlighted;
            if (!Highlighted && near && (held || _hoverFor >= enterDelay)) want = true;
            else if (Highlighted && !near && _clearFor >= exitDelay) want = false;

            if (want != Highlighted || held != _lastHeld) Apply(want, held);
        }

        void Apply(bool on, bool held)
        {
            bool appeared = on && !Highlighted;
            Highlighted = on;
            Held = on && held;
            _lastHeld = held;

            if (indicator != null)
            {
                indicator.SetActive(on);
                if (on && _indicatorRenderer != null)
                {
                    Color c = held ? heldColor : hoverColor;
                    _indicatorRenderer.GetPropertyBlock(_block);
                    _block.SetColor(BaseColorId, c);
                    _block.SetColor(ColorId, c);
                    _indicatorRenderer.SetPropertyBlock(_block);
                }
            }

            if (brighten != null && _baseColors != null)
                for (int i = 0; i < brighten.Length; i++)
                {
                    var r = brighten[i];
                    if (r == null) continue;
                    Color c = on && !held ? _baseColors[i] * hoverBrightness : _baseColors[i];
                    c.a = _baseColors[i].a;
                    r.GetPropertyBlock(_block);
                    _block.SetColor(BaseColorId, c);
                    _block.SetColor(ColorId, c);
                    r.SetPropertyBlock(_block);
                }

            if (appeared && tickVolume > 0f && isActiveAndEnabled) Tick();
        }

        void Tick()
        {
            if (_audio == null)
            {
                _audio = GetComponent<AudioSource>();
                if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 1f;
            }
            if (s_tick == null)
            {
                int rate = 44100, n = (int)(rate * 0.05f);
                var d = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / rate;
                    d[i] = 0.6f * Mathf.Exp(-60f * t) * Mathf.Sin(2f * Mathf.PI * 1250f * t);
                }
                s_tick = AudioClip.Create("hover_tick", n, 1, rate, false);
                s_tick.SetData(d, 0);
            }
            _audio.PlayOneShot(s_tick, tickVolume);
        }
    }
}
