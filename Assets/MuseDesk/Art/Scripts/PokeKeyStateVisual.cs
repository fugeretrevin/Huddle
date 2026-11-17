using UnityEngine;

namespace MuseDesk.Art.FinalRefinement
{
    public enum PokeKeyVisualState
    {
        Normal = 0,
        Hover = 1,
        Pressed = 2,
        Active = 3,
        Disabled = 4,
    }

    /// <summary>
    /// MaterialPropertyBlock controller for proportioned MuseDesk 3D Poke Keys.
    /// Drives both Cap_Body and optional Cap_StateRim (and optional active glow) across
    /// Normal, Hover, Pressed, Active, and Disabled states with zero per-instance materials.
    /// Compatible with MuseDesk/TactileKeyCap and URP Simple Lit / Unlit materials.
    /// </summary>
    [ExecuteAlways]
    public class PokeKeyStateVisual : MonoBehaviour
    {
        [Header("Renderers")]
        public Renderer capRenderer;
        public Renderer stateRimRenderer;

        [Header("Colors (Design System Palette)")]
        public Color normalCapColor   = new Color32(0x2F, 0xA3, 0x9A, 255);
        public Color hoverCapColor    = new Color32(0x47, 0xBD, 0xB4, 255);
        public Color pressedCapColor  = new Color32(0x21, 0x7A, 0x73, 255);
        public Color activeCapColor   = new Color32(0x2F, 0xA3, 0x9A, 255);
        public Color disabledCapColor = new Color32(0x63, 0x6A, 0x75, 255);

        [Header("Rim Collar Colors")]
        public Color normalRimColor   = new Color32(0xD8, 0xDF, 0xE8, 255);
        public Color hoverRimColor    = new Color32(0xFF, 0xFF, 0xFF, 255);
        public Color pressedRimColor  = new Color32(0xA6, 0xEE, 0xE6, 255);
        public Color activeRimColor   = new Color32(0x2F, 0xA3, 0x9A, 255);
        public Color disabledRimColor = new Color32(0x3D, 0x43, 0x4E, 255);

        [Header("State")]
        public PokeKeyVisualState currentState = PokeKeyVisualState.Normal;

        static readonly int BaseColorId      = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId          = Shader.PropertyToID("_Color");
        static readonly int EmissionColorId  = Shader.PropertyToID("_EmissionColor");
        static readonly int DisabledAmountId = Shader.PropertyToID("_DisabledAmount");

        MaterialPropertyBlock _mpb;

        void Reset()
        {
            AutoBindRenderers();
        }

        void OnEnable()
        {
            AutoBindRenderers();
            ApplyState(currentState);
        }

        void OnValidate()
        {
            AutoBindRenderers();
            ApplyState(currentState);
        }

        public void AutoBindRenderers()
        {
            if (capRenderer == null)
            {
                var body = transform.Find("ButtonVisual/Cap3D/Cap_Body")
                           ?? transform.Find("Cap_Body")
                           ?? transform.Find("ButtonVisual/Cap3D");
                if (body != null) capRenderer = body.GetComponent<Renderer>();
                if (capRenderer == null)
                {
                    foreach (var r in GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.name.Contains("Cap_Body") || r.name == "Cap3D")
                        {
                            capRenderer = r;
                            break;
                        }
                    }
                }
            }

            if (stateRimRenderer == null)
            {
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.Contains("Cap_StateRim"))
                    {
                        stateRimRenderer = r;
                        break;
                    }
                }
            }
        }

        public void ConfigurePalette(Color baseCap, Color activeAccent)
        {
            normalCapColor   = baseCap;
            hoverCapColor    = Color.Lerp(baseCap, Color.white, 0.24f);
            pressedCapColor  = Color.Lerp(baseCap, Color.black, 0.22f);
            activeCapColor   = Color.Lerp(baseCap, activeAccent, 0.35f);
            disabledCapColor = new Color32(0x5B, 0x62, 0x6E, 255);

            normalRimColor   = Color.Lerp(baseCap, Color.white, 0.45f);
            hoverRimColor    = Color.white;
            pressedRimColor  = activeAccent;
            activeRimColor   = activeAccent;
            disabledRimColor = new Color32(0x36, 0x3B, 0x45, 255);
            ApplyState(currentState);
        }

        public void SetState(PokeKeyVisualState state)
        {
            currentState = state;
            ApplyState(state);
        }

        public void ApplyState(PokeKeyVisualState state)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            Color capCol, rimCol, capEmit = Color.black, rimEmit = Color.black;
            float disabledAmt = 0f;

            switch (state)
            {
                case PokeKeyVisualState.Hover:
                    capCol = hoverCapColor;
                    rimCol = hoverRimColor;
                    capEmit = capCol * 0.14f;
                    rimEmit = rimCol * 0.45f;
                    break;
                case PokeKeyVisualState.Pressed:
                    capCol = pressedCapColor;
                    rimCol = pressedRimColor;
                    capEmit = capCol * 0.08f;
                    rimEmit = rimCol * 0.55f;
                    break;
                case PokeKeyVisualState.Active:
                    capCol = activeCapColor;
                    rimCol = activeRimColor;
                    capEmit = capCol * 0.18f;
                    rimEmit = rimCol * 0.75f;
                    break;
                case PokeKeyVisualState.Disabled:
                    capCol = disabledCapColor;
                    rimCol = disabledRimColor;
                    disabledAmt = 0.85f;
                    break;
                default:
                    capCol = normalCapColor;
                    rimCol = normalRimColor;
                    rimEmit = rimCol * 0.08f;
                    break;
            }

            ApplyToRenderer(capRenderer, capCol, capEmit, disabledAmt);
            ApplyToRenderer(stateRimRenderer, rimCol, rimEmit, disabledAmt);
        }

        void ApplyToRenderer(Renderer r, Color col, Color emit, float disabledAmt)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, col);
            _mpb.SetColor(ColorId, col);
            _mpb.SetColor(EmissionColorId, emit);
            _mpb.SetFloat(DisabledAmountId, disabledAmt);
            r.SetPropertyBlock(_mpb);
        }
    }
}

