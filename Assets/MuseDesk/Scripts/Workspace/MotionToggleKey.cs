using TMPro;
using UnityEngine;
using MuseDesk.Config;
using MuseDesk.Motion;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// The in-app "Calm motion" (Reduced Motion) key: a clone of the Focus key that flips
    /// <see cref="MotionSettings.ReducedMotion"/> live and saves the choice for this device
    /// (<see cref="ReleaseConfigApplier.SetReducedMotionPreference"/>). It only changes how fast and how softly things move;
    /// it never touches task state.
    ///
    /// Visibility follows the release flag <c>reducedMotionToggle</c> (0 in the committed release): with the flag off the
    /// key sits at its hidden scale (never SetActive, so the poke interactable stays registered) and nothing else is
    /// different from the release. The state is always in words (Calm motion / ON or OFF) AND in the cap: Teal when on
    /// (same as an active console key), Paper when off, through the standard KeyStateVisual hover and press tints.
    /// </summary>
    [DisallowMultipleComponent]
    public class MotionToggleKey : MonoBehaviour
    {
        [Tooltip("The key's own Transform (this one when empty).")]
        public Transform keyRoot;
        [Tooltip("Scale of the shown key.")]
        public Vector3 shownScale = Vector3.one * 0.032f;
        public TMP_Text label;
        public KeyStateVisual visual;
        public Renderer cap;
        [Tooltip("Cap material while Calm motion is ON (Teal, like an active console key).")]
        public Material capOn;
        [Tooltip("Cap material while Calm motion is OFF (Paper).")]
        public Material capOff;

        public bool Shown { get; private set; }
        /// <summary>The state the key currently shows.</summary>
        public bool ShownOn { get; private set; }

        Vector3 _full;
        bool _inited;

        void Init()
        {
            if (_inited) return;
            _inited = true;
            if (keyRoot == null) keyRoot = transform;
            // A builder may have saved the key hidden: the serialized shownScale is then the real size.
            _full = shownScale.sqrMagnitude > 1e-8f ? shownScale : keyRoot.localScale;
        }

        void OnEnable()
        {
            Init();
            MotionSettings.Changed += OnMotionChanged;
            Refresh();
        }

        void OnDisable() { MotionSettings.Changed -= OnMotionChanged; }

        void Start() { Refresh(); }

        void OnMotionChanged() { Refresh(); }

        /// <summary>Visibility follows the release flag; the label and cap follow MotionSettings.</summary>
        public void Refresh()
        {
            Init();
            var cfg = ReleaseConfigApplier.Active;
            SetShown(ReducedMotionPreference.KeyVisible(cfg != null && cfg.reducedMotionToggle));
            ShowState(MotionSettings.ReducedMotion);
        }

        public void SetShown(bool shown)
        {
            Init();
            Shown = shown;
            float k = shown ? 1f : ScaleEase.HiddenFactor;
            keyRoot.localScale = _full * k;
        }

        void ShowState(bool on)
        {
            ShownOn = on;
            if (label != null)
            {
                string t = ReducedMotionPreference.KeyText(on);
                if (label.text != t) label.text = t;
            }
            if (cap != null)
            {
                var m = on ? capOn : capOff;
                if (m != null && cap.sharedMaterial != m)
                {
                    cap.sharedMaterial = m;
                    if (visual != null) visual.RefreshBaseColor();
                }
            }
        }

        /// <summary>Poke target: flip Reduced Motion, apply it now and save it.</summary>
        public void Press()
        {
            if (!Shown) return;     // a hidden key must not act if something pokes through its tiny interactable
            Toggle();
        }

        /// <summary>Flips and saves the choice (also what the Play driver calls). Returns the new state.</summary>
        public static event System.Action<bool> Toggled;

        public bool Toggle()
        {
            bool now = !MotionSettings.ReducedMotion;
            ReleaseConfigApplier.SetReducedMotionPreference(now);     // saves for this device and applies now (raises Changed)
            ShowState(now);
            Toggled?.Invoke(now);
            if (visual != null) visual.FlashConfirmed();
            return now;
        }

        /// <summary>Re-applies the flag to every key in the scene (ReleaseConfigApplier.Apply calls this).</summary>
        public static void RefreshAll()
        {
            foreach (var k in FindObjectsByType<MotionToggleKey>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (k != null) k.Refresh();
        }
    }
}
