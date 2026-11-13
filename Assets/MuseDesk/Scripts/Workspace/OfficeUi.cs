using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// Design tokens of the redesign (docs/Design/REDESIGN_BRIEF_FOR_AGENTS.md, section 4) for the
    /// few places that tint at runtime. Tinting goes through a MaterialPropertyBlock, never through
    /// a per-object material instance.
    /// </summary>
    public static class OfficeTokens
    {
        public static readonly Color Paper = new Color32(0xF7, 0xF2, 0xE3, 0xFF);
        public static readonly Color Ink = new Color32(0x1F, 0x25, 0x30, 0xFF);
        public static readonly Color InkSoft = new Color32(0x4B, 0x55, 0x63, 0xFF);
        public static readonly Color Plate = new Color32(0x1B, 0x1F, 0x27, 0xFF);
        public static readonly Color OnPlate = new Color32(0xF3, 0xF4, 0xF6, 0xFF);
        public static readonly Color Housing = new Color32(0x2A, 0x2F, 0x3A, 0xFF);
        public static readonly Color Teal = new Color32(0x2F, 0xA3, 0x9A, 0xFF);
        public static readonly Color Lavender = new Color32(0x8C, 0x7B, 0xD6, 0xFF);
        public static readonly Color InfoBlue = new Color32(0x4C, 0x85, 0xDB, 0xFF);
        public static readonly Color Success = new Color32(0x2E, 0x9B, 0x5F, 0xFF);
        public static readonly Color Coral = new Color32(0xD9, 0x53, 0x4A, 0xFF);
        public static readonly Color Amber = new Color32(0xE0, 0xA0, 0x30, 0xFF);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Tint a renderer through a property block (no material instance is created).</summary>
        public static void Tint(Renderer renderer, MaterialPropertyBlock block, Color color)
        {
            if (renderer == null || block == null) return;
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }
    }

    /// <summary>
    /// Shows and hides a small sign by scale, with an eased pop and an optional brief pulse.
    /// Hiding by scale (not SetActive) keeps Interaction SDK poke keys under the sign registered
    /// (brief, gotcha 2). Plain class, no allocation after construction; the owner calls
    /// <see cref="Tick"/> from its Update, which does nothing while the sign is at rest.
    /// </summary>
    public sealed class ScaleEase
    {
        /// <summary>Scale factor of a hidden sign. Not zero: a zero scale breaks poke-surface maths.</summary>
        public const float HiddenFactor = 0.001f;

        Transform _target;
        Vector3 _full = Vector3.one;
        float _seconds = 0.22f;
        float _k;                 // 0 hidden .. 1 shown
        float _goal;
        float _pulse = 1f;        // 0..1 progress of the pulse, 1 = no pulse
        float _pulseAmount;
        float _pulseSeconds = 0.25f;
        bool _moving;

        /// <summary>True when the sign is shown or on its way to being shown.</summary>
        public bool Shown => _goal > 0.5f;
        /// <summary>True while a show, hide or pulse is still playing.</summary>
        public bool Animating => _moving;

        /// <summary>
        /// The scale a sign has when shown. A builder may save the sign already hidden (scaled to
        /// almost nothing) so it is not seen before the first frame; in that case the serialized
        /// fallback is the real size, otherwise the scale found in the scene is.
        /// </summary>
        public static Vector3 ResolveFull(Transform target, Vector3 fallback)
        {
            if (target == null) return fallback;
            Vector3 s = target.localScale;
            return s.sqrMagnitude < 1e-4f ? fallback : s;
        }

        public void Init(Transform target, Vector3 fullScale, float seconds, bool shown)
        {
            _target = target;
            _full = fullScale;
            _seconds = Mathf.Max(0.01f, seconds);
            _k = _goal = shown ? 1f : 0f;
            _pulse = 1f;
            _moving = false;
            Apply();
        }

        public void Show() { if (_goal < 0.5f) { _goal = 1f; _moving = true; } }
        public void Hide() { if (_goal > 0.5f) { _goal = 0f; _moving = true; } }

        public void Snap(bool shown)
        {
            _k = _goal = shown ? 1f : 0f;
            _pulse = 1f;
            _moving = false;
            Apply();
        }

        /// <summary>A brief, subtle swell (default 5 percent) to mark a change. Ignored while hidden.</summary>
        public void Pulse(float amount = 0.05f, float seconds = 0.25f)
        {
            if (!Shown) return;
            _pulseAmount = amount;
            _pulseSeconds = Mathf.Max(0.05f, seconds);
            _pulse = 0f;
            _moving = true;
        }

        public void Tick(float deltaTime)
        {
            if (!_moving) return;
            _k = Mathf.MoveTowards(_k, _goal, deltaTime / _seconds);
            if (_pulse < 1f) _pulse = Mathf.Min(1f, _pulse + deltaTime / _pulseSeconds);
            Apply();
            if (Mathf.Approximately(_k, _goal) && _pulse >= 1f) _moving = false;
        }

        void Apply()
        {
            if (_target == null) return;
            float inv = 1f - _k;
            float eased = 1f - inv * inv * inv;                       // ease-out cubic
            float s = Mathf.Lerp(HiddenFactor, 1f, eased);
            if (_pulse < 1f) s *= 1f + _pulseAmount * Mathf.Sin(_pulse * Mathf.PI);
            _target.localScale = _full * s;
        }
    }
}
