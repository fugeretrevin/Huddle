using System.Collections;
using TMPro;
using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// State colours for a poke key that actually render.
    ///
    /// The Interaction SDK's InteractableColorVisual writes "_Color" into a MaterialPropertyBlockEditor;
    /// the keys' caps use URP Simple Lit, which reads "_BaseColor", so nothing was ever seen. This
    /// component listens to the key's PokeInteractable (IInteractableView.WhenStateChanged) and tints
    /// the Cap3D renderer through its own MaterialPropertyBlock, writing "_BaseColor" (and "_Color").
    ///
    ///   Normal   the cap material's own base colour (the property block is removed, so a resting
    ///            key batches exactly as it did before)
    ///   Hover    lightened by <see cref="hoverLighten"/> (very light caps are darkened a little
    ///            instead, because a near-white cap cannot visibly get lighter)
    ///   Select   darkened by <see cref="pressDarken"/>
    ///   Disabled blended toward Housing grey by <see cref="disabledBlend"/>
    ///   plus a short flash toward white when a press is released (the key "activated"), and
    ///   <see cref="FlashRefused"/> / <see cref="FlashConfirmed"/> for the owner of the key.
    ///
    /// Nothing runs per frame at rest: a coroutine runs only while a blend or a flash is playing.
    /// No material instance is ever created. One property block per key, and only while the key is
    /// not at rest (a block takes that one renderer out of the SRP batch, which is acceptable for
    /// the one or two keys under a finger).
    ///
    /// Coexistence: the SDK's MaterialPropertyBlockEditor on the cap re-applies ITS block every
    /// frame, which would erase this one, so it is switched off here if the builder has not already
    /// done so. InteractableColorVisual may stay: it then writes into a block nobody applies.
    /// PokeVisualRestGuard is independent (it moves ButtonVisual, this only colours the cap).
    /// </summary>
    [DisallowMultipleComponent]
    public class KeyStateVisual : MonoBehaviour
    {
        [Tooltip("The key cap's renderer (Cap3D). Found by name under this object if left empty.")]
        public Renderer capRenderer;
        [Tooltip("Optional state collar around the cap (Cap_StateRim on the wide key models): light on hover.")]
        public Renderer rimRenderer;
        [Tooltip("The key's label. Optional: only used to keep it readable while the key looks disabled.")]
        public TMP_Text label;

        [Header("State colours (fractions)")]
        [Range(0f, 0.5f)] public float hoverLighten = 0.12f;
        [Range(0f, 0.5f)] public float pressDarken = 0.14f;
        [Range(0f, 1f)] public float disabledBlend = 0.55f;

        [Header("Timing (seconds)")]
        [Range(0.02f, 0.3f)] public float blendSeconds = 0.10f;
        [Range(0.05f, 0.4f)] public float activatedSeconds = 0.12f;
        [Range(0.1f, 0.6f)] public float signalSeconds = 0.25f;

        [Header("Flashes (fractions)")]
        [Range(0f, 0.6f)] public float activatedFlash = 0.18f;
        [Range(0f, 1f)] public float signalFlash = 0.60f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Caps lighter than this (0..1 luminance) show hover by darkening.</summary>
        const float LightCapLuminance = 0.78f;
        const float LightCapHoverDarken = 0.07f;

        Oculus.Interaction.IInteractableView _view;
        Behaviour _viewBehaviour;
        MaterialPropertyBlock _block;
        Coroutine _routine;
        bool _resolved;
        bool _subscribed;

        Oculus.Interaction.InteractableState _state = Oculus.Interaction.InteractableState.Normal;
        bool _disabledLook;

        Color _base = Color.white;      // the cap material's own colour
        Color _from = Color.white;      // blend start
        Color _to = Color.white;        // blend goal
        Color _shown = Color.white;     // blended state colour, before any flash
        Color _current = Color.white;   // what is on the renderer now
        float _blendT = 1f;             // 0..1, 1 = blend finished

        Color _flashColor = Color.white;
        float _flashAmount;
        float _flashSeconds = 0.12f;
        float _flashT = 1f;             // 0..1, 1 = no flash

        // The optional collar follows the same blend with its own colours.
        MaterialPropertyBlock _rimBlock;
        Color _rimBase = Color.white;
        Color _rimFrom = Color.white;
        Color _rimTo = Color.white;

        Color _labelAuthored = Color.white;
        bool _labelDimmed;

        /// <summary>The colour currently on the cap.</summary>
        public Color CurrentColor => _current;
        /// <summary>The cap material's own colour (the Normal state).</summary>
        public Color BaseColor => _base;
        /// <summary>True while <see cref="SetDisabled"/> has asked for the disabled look.</summary>
        public bool DisabledLook => _disabledLook;
        /// <summary>The interactable state this visual is showing.</summary>
        public Oculus.Interaction.InteractableState ShownState => _state;

        // ───────────────────────────── colour rules (shared with the builder's report) ─────────────────────────────

        public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>The colour of a cap whose own colour is <paramref name="baseColor"/> in a given state.</summary>
        public static Color StateColor(Color baseColor, Oculus.Interaction.InteractableState state,
            float hoverLighten = 0.12f, float pressDarken = 0.14f, float disabledBlend = 0.55f)
        {
            Color c;
            switch (state)
            {
                case Oculus.Interaction.InteractableState.Hover:
                    c = Luminance(baseColor) > LightCapLuminance
                        ? Color.Lerp(baseColor, Color.black, Mathf.Min(LightCapHoverDarken, pressDarken * 0.5f))
                        : Color.Lerp(baseColor, Color.white, hoverLighten);
                    break;
                case Oculus.Interaction.InteractableState.Select:
                    c = Color.Lerp(baseColor, Color.black, pressDarken);
                    break;
                case Oculus.Interaction.InteractableState.Disabled:
                    c = Color.Lerp(baseColor, OfficeTokens.Housing, disabledBlend);
                    break;
                default:
                    c = baseColor;
                    break;
            }
            c.a = baseColor.a;
            return c;
        }

        // Plain-colour forms of the rules above, with the default amounts (used by the Editor builder's report).
        public static Color HoverColor(Color baseColor) => StateColor(baseColor, Oculus.Interaction.InteractableState.Hover);
        public static Color PressedColor(Color baseColor) => StateColor(baseColor, Oculus.Interaction.InteractableState.Select);
        public static Color DisabledColor(Color baseColor) => StateColor(baseColor, Oculus.Interaction.InteractableState.Disabled);

        /// <summary>The colour of the optional collar in a given state: it lights up on hover.</summary>
        public static Color RimStateColor(Color rimBase, Oculus.Interaction.InteractableState state, float disabledBlend = 0.55f)
        {
            Color c;
            switch (state)
            {
                case Oculus.Interaction.InteractableState.Hover: c = Color.Lerp(rimBase, OfficeTokens.OnPlate, 0.80f); break;
                case Oculus.Interaction.InteractableState.Select: c = Color.Lerp(rimBase, OfficeTokens.OnPlate, 0.45f); break;
                case Oculus.Interaction.InteractableState.Disabled: c = Color.Lerp(rimBase, OfficeTokens.Housing, disabledBlend); break;
                default: c = rimBase; break;
            }
            c.a = rimBase.a;
            return c;
        }

        /// <summary>A label colour that stays readable on a cap that looks disabled.</summary>
        public static Color DisabledLabelColor(Color disabledCap)
        {
            Color strong = Luminance(disabledCap) < 0.45f ? OfficeTokens.OnPlate : OfficeTokens.Ink;
            Color c = Color.Lerp(strong, disabledCap, 0.25f);      // dimmed, still clearly legible
            c.a = 1f;
            return c;
        }

        // ───────────────────────────── public API ─────────────────────────────

        /// <summary>
        /// Show the key as unavailable (greyed cap, dimmed label) or restore it. This changes the
        /// LOOK only: the PokeInteractable is not touched and the key still fires if poked, so the
        /// owner must ignore the press itself (or hide the key by scale as before).
        /// </summary>
        public void SetDisabled(bool disabled)
        {
            Resolve();
            if (_disabledLook == disabled) return;
            _disabledLook = disabled;
            UpdateLabel();
            BeginBlend();
        }

        /// <summary>A brief coral blend: the press was understood but refused.</summary>
        public void FlashRefused() => StartFlash(OfficeTokens.Coral, signalFlash, signalSeconds);

        /// <summary>A brief success-green blend: the press was accepted and confirmed.</summary>
        public void FlashConfirmed() => StartFlash(OfficeTokens.Success, signalFlash, signalSeconds);

        /// <summary>Re-read the cap material's colour (call after swapping the cap's shared material).</summary>
        public void RefreshBaseColor()
        {
            _base = Color.white;
            Material m = capRenderer != null ? capRenderer.sharedMaterial : null;
            if (m != null)
            {
                if (m.HasProperty(BaseColorId)) _base = m.GetColor(BaseColorId);
                else if (m.HasProperty(ColorId)) _base = m.GetColor(ColorId);
            }
            _rimBase = Color.white;
            Material rm = rimRenderer != null ? rimRenderer.sharedMaterial : null;
            if (rm != null)
            {
                if (rm.HasProperty(BaseColorId)) _rimBase = rm.GetColor(BaseColorId);
                else if (rm.HasProperty(ColorId)) _rimBase = rm.GetColor(ColorId);
            }
        }

        /// <summary>
        /// Show a state as if the interactable had entered it. For Play Mode drivers and captures
        /// (there is no finger in the Editor); the next real state change takes over again.
        /// </summary>
        public void SimulateState(Oculus.Interaction.InteractableState state)
        {
            Resolve();
            ChangeState(_state, state);
        }

        // ───────────────────────────── lifecycle ─────────────────────────────

        void Awake() => Resolve();

        void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _block = new MaterialPropertyBlock();

            if (capRenderer == null)
            {
                var all = GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].name == "Cap3D") { capRenderer = all[i]; break; }
            }
            if (label == null) label = GetComponentInChildren<TMP_Text>(true);

            Oculus.Interaction.PokeInteractable poke = GetComponent<Oculus.Interaction.PokeInteractable>();
            if (poke == null) poke = GetComponentInParent<Oculus.Interaction.PokeInteractable>(true);
            if (poke == null) poke = GetComponentInChildren<Oculus.Interaction.PokeInteractable>(true);
            _view = poke;
            _viewBehaviour = poke;

            // The SDK editor on the cap re-applies its own block every frame and would erase ours.
            if (capRenderer != null)
            {
                var editor = capRenderer.GetComponent<Oculus.Interaction.MaterialPropertyBlockEditor>();
                if (editor != null && editor.enabled) editor.enabled = false;
            }
            RefreshBaseColor();
        }

        void OnEnable()
        {
            Resolve();
            RefreshBaseColor();
            if (_view != null && !_subscribed)
            {
                _view.WhenStateChanged += HandleStateChanged;
                _subscribed = true;
            }
            // An interactable reports Disabled until its own Start/OnEnable has run. If it is about
            // to be enabled, start from Normal rather than fading in from grey on the first frame.
            _state = Oculus.Interaction.InteractableState.Normal;
            if (_view != null)
            {
                var s = _view.State;
                bool pending = s == Oculus.Interaction.InteractableState.Disabled
                               && _viewBehaviour != null && _viewBehaviour.isActiveAndEnabled;
                if (!pending) _state = s;
            }
            Snap();
        }

        void Start() => Snap();     // after every Awake in the scene, including the SDK editor's

        void OnDisable()
        {
            if (_view != null && _subscribed)
            {
                _view.WhenStateChanged -= HandleStateChanged;
                _subscribed = false;
            }
            if (_routine != null) { StopCoroutine(_routine); _routine = null; }
            _flashT = 1f;
            _blendT = 1f;
        }

        void HandleStateChanged(Oculus.Interaction.InteractableStateChangeArgs args)
        {
            ChangeState(args.PreviousState, args.NewState);
        }

        void ChangeState(Oculus.Interaction.InteractableState previous, Oculus.Interaction.InteractableState next)
        {
            _state = next;
            BeginBlend();
            // Select -> released (back to Hover or Normal): the key fired.
            if (previous == Oculus.Interaction.InteractableState.Select
                && next != Oculus.Interaction.InteractableState.Select
                && next != Oculus.Interaction.InteractableState.Disabled
                && !_disabledLook)
                StartFlash(Color.white, activatedFlash, activatedSeconds);
        }

        // ───────────────────────────── blending ─────────────────────────────

        Color Target()
        {
            var s = _disabledLook ? Oculus.Interaction.InteractableState.Disabled : _state;
            return StateColor(_base, s, hoverLighten, pressDarken, disabledBlend);
        }

        Color RimTarget()
        {
            var s = _disabledLook ? Oculus.Interaction.InteractableState.Disabled : _state;
            return RimStateColor(_rimBase, s, disabledBlend);
        }

        /// <summary>Where the blend is now (ease-out of the 0..1 progress).</summary>
        float Eased()
        {
            float inv = 1f - _blendT;
            return 1f - inv * inv;
        }

        void Snap()
        {
            _from = _to = _shown = Target();
            _rimFrom = _rimTo = RimTarget();
            _blendT = 1f;
            _flashT = 1f;
            Apply();
        }

        void BeginBlend()
        {
            _rimFrom = Color.Lerp(_rimFrom, _rimTo, Eased());      // the collar's colour right now
            _rimTo = RimTarget();
            _from = _shown;
            _to = Target();
            _blendT = 0f;
            Run();
        }

        void StartFlash(Color toward, float amount, float seconds)
        {
            Resolve();
            if (!isActiveAndEnabled || amount <= 0f) return;
            // A flash toward the cap's own colour would not be seen (green on the green Approve key):
            // use a lighter version of the signal colour there.
            Color d = toward - _base;
            if (d.r * d.r + d.g * d.g + d.b * d.b < 0.04f) toward = Color.Lerp(toward, Color.white, 0.4f);
            toward.a = _base.a;
            _flashColor = toward;
            _flashAmount = Mathf.Clamp01(amount);
            _flashSeconds = Mathf.Max(0.02f, seconds);
            _flashT = 0f;
            Run();
        }

        void Run()
        {
            // activeInHierarchy as well: while a parent is being deactivated the interactable's own
            // OnDisable reports a state change before this component has been disabled.
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                // No coroutine on an inactive object: go straight to the goal.
                _blendT = 1f; _flashT = 1f;
                Apply();
                return;
            }
            // Only started while something is left to play (the callers set _blendT or _flashT to 0),
            // so the coroutine always yields at least once before it clears _routine.
            if (_routine == null) _routine = StartCoroutine(Animate());
        }

        IEnumerator Animate()
        {
            while (_blendT < 1f || _flashT < 1f)
            {
                float dt = Time.deltaTime;
                if (_blendT < 1f) _blendT = Mathf.Min(1f, _blendT + dt / Mathf.Max(0.02f, blendSeconds));
                if (_flashT < 1f) _flashT = Mathf.Min(1f, _flashT + dt / _flashSeconds);
                Apply();
                yield return null;
            }
            Apply();
            _routine = null;
        }

        void Apply()
        {
            float eased = Eased();
            _shown = Color.Lerp(_from, _to, eased);
            Color c = _shown;
            Color rim = Color.Lerp(_rimFrom, _rimTo, eased);
            bool flashing = _flashT < 1f;
            if (flashing)
            {
                // Quick rise over the first quarter, then a linear fall.
                float env = _flashT < 0.25f ? _flashT / 0.25f : 1f - (_flashT - 0.25f) / 0.75f;
                c = Color.Lerp(c, _flashColor, _flashAmount * env);
                rim = Color.Lerp(rim, _flashColor, _flashAmount * env);
            }
            _current = c;

            // At rest nothing overrides the materials: the blocks are removed.
            bool atRest = !flashing && _blendT >= 1f && !_disabledLook
                          && _state == Oculus.Interaction.InteractableState.Normal;
            if (capRenderer != null)
            {
                if (atRest) capRenderer.SetPropertyBlock(null);
                else
                {
                    if (_block == null) _block = new MaterialPropertyBlock();
                    _block.SetColor(BaseColorId, c);
                    _block.SetColor(ColorId, c);
                    capRenderer.SetPropertyBlock(_block);
                }
            }
            if (rimRenderer != null)
            {
                if (atRest) rimRenderer.SetPropertyBlock(null);
                else
                {
                    if (_rimBlock == null) _rimBlock = new MaterialPropertyBlock();
                    _rimBlock.SetColor(BaseColorId, rim);
                    _rimBlock.SetColor(ColorId, rim);
                    rimRenderer.SetPropertyBlock(_rimBlock);
                }
            }
        }

        /// <summary>Dim the label while the key looks disabled; restore the authored colour afterwards.</summary>
        void UpdateLabel()
        {
            if (label == null) return;
            if (_disabledLook && !_labelDimmed)
            {
                _labelAuthored = label.color;
                _labelDimmed = true;
                label.color = DisabledLabelColor(
                    StateColor(_base, Oculus.Interaction.InteractableState.Disabled, hoverLighten, pressDarken, disabledBlend));
            }
            else if (!_disabledLook && _labelDimmed)
            {
                _labelDimmed = false;
                label.color = _labelAuthored;
            }
        }
    }
}
