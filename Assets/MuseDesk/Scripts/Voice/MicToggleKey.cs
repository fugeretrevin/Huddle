using TMPro;
using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Voice
{
    /// <summary>Per-device storage for the mic choice. Never throws.</summary>
    public sealed class PlayerPrefsMicStore : IMicPrefStore
    {
        public int GetInt(string key, int fallback) { try { return PlayerPrefs.GetInt(key, fallback); } catch { return fallback; } }
        public void SetInt(string key, int value) { try { PlayerPrefs.SetInt(key, value); PlayerPrefs.Save(); } catch { } }
    }

    /// <summary>
    /// The Mic mute/unmute key on the settings panel (beside Focus and Calm motion). It flips hands-free listening in
    /// VoiceCommandCenter and shows the truth in words AND the cap: "Mic off" (Paper), "Mic on" / "Mic hearing" /
    /// "Mic thinking" (Teal), "Voice offline" (Paper) when this build or network cannot recognise speech.
    /// Never hidden: it is the privacy control. Muted is the default.
    /// </summary>
    [DisallowMultipleComponent]
    public class MicToggleKey : MonoBehaviour
    {
        public VoiceCommandCenter center;
        public TMP_Text label;
        public KeyStateVisual visual;
        public Renderer cap;
        public Material capOn;
        public Material capOff;

        public MicPhase Shown { get; private set; } = (MicPhase)(-1);
        public string ShownText => label != null ? label.text : "";

        void OnEnable() { Bind(); Refresh(); }
        void OnDisable() { if (center != null) center.MicStateChanged -= Refresh; }

        void Bind()
        {
            if (center == null) center = VoiceCommandCenter.Instance != null ? VoiceCommandCenter.Instance : FindFirstObjectByType<VoiceCommandCenter>(FindObjectsInactive.Include);
            if (center != null) { center.MicStateChanged -= Refresh; center.MicStateChanged += Refresh; }
        }

        void Update()
        {
            // A center that came up after this key is picked up; its own events then keep the key current.
            if (center == null) { Bind(); if (center != null) Refresh(); }
        }

        /// <summary>Poke target.</summary>
        public void Press()
        {
            Bind();
            if (center == null) { Draw(MicPhase.Unavailable); return; }
            center.ToggleMic();
            if (visual != null) visual.FlashConfirmed();
            Refresh();
        }

        public void Refresh()
        {
            MicPhase p = center == null ? MicPhase.Unavailable : center.MicPhase;
            if (center != null && !center.MicUnmuted) p = MicPhase.Muted;
            Draw(p);
        }

        void Draw(MicPhase p)
        {
            if (p == Shown) return;
            Shown = p;
            if (label != null)
            {
                string t = MicPreference.KeyText(p);
                if (label.text != t) label.text = t;
            }
            bool teal = p == MicPhase.Waiting || p == MicPhase.Listening || p == MicPhase.Hearing || p == MicPhase.Processing;
            if (cap != null)
            {
                var m = teal ? capOn : capOff;
                if (m != null && cap.sharedMaterial != m)
                {
                    cap.sharedMaterial = m;
                    if (visual != null) visual.RefreshBaseColor();
                }
            }
        }
    }
}
