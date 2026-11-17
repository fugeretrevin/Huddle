using System;
using TMPro;
using UnityEngine;
using MuseDesk.Workspace;

namespace MuseDesk.Voice
{
    /// <summary>What the microphone key shows.</summary>
    public enum VoiceMicVisual { Idle, Listening, Thinking, Off }

    /// <summary>Colour of the readout's title strip.</summary>
    public enum VoiceTone { Neutral, Listening, Good, Warn, Bad, Agent }

    /// <summary>
    /// The visible side of voice: the microphone key's halo and word, and one readout plate that
    /// says what voice is doing (listening and the words so far, thinking, what it understood and
    /// did, what it did not understand and two things to try, why it is unavailable, a question
    /// with Confirm / Cancel keys, the help card).
    ///
    /// Presentation only: VoiceCommandCenter decides everything. Built by AgentScripts/BuildVoiceV2.
    /// The readout and its two keys are hidden by scale (never deactivated), so the keys' poke
    /// interactables stay registered. Every reference is optional; with none wired this does nothing.
    /// </summary>
    public class VoiceHud : MonoBehaviour
    {
        [Header("Microphone key")]
        [Tooltip("The word on the microphone key's cap.")]
        public TMP_Text micLabel;
        [Tooltip("Disc behind the key: its rim shows as a ring and swells with the microphone level.")]
        public Transform halo;
        public Renderer haloRenderer;
        [Tooltip("Halo scale at rest (set by the builder).")]
        public Vector3 haloRestScale = Vector3.one;
        [Tooltip("How much the halo grows at full level (fraction of its rest size).")]
        public float haloSwell = 0.32f;

        [Header("Readout")]
        public Transform readout;
        public Vector3 readoutShownScale = Vector3.one;
        public Renderer toneStrip;
        public TMP_Text titleText;
        public TMP_Text modeText;
        public TMP_Text bodyText;
        public TMP_Text footText;

        [Header("Answer keys (in front of the readout's lower edge; siblings of the readout, not children)")]
        public Transform confirmKey;
        public Transform cancelKey;
        public Vector3 answerKeyScale = new Vector3(0.032f, 0.032f, 0.032f);
        public TMP_Text confirmLabel;
        public TMP_Text cancelLabel;

        /// <summary>rc20 (owner, worn rc19: "the panel with what was heard was intersecting with the panel, bring that bit
        /// out"). Set by whoever shows cards near the user (the Wall habitat): given the readout's world bounds, the world
        /// offset that brings it out in front of anything it would cut into (zero when it is clear).</summary>
        public static Func<Bounds, Vector3> Clearance;
        Vector3 _readoutRest, _confirmRest, _cancelRest, _nudge;
        bool _restSet;
        /// <summary>For drivers: how far the readout is brought out right now (m).</summary>
        public float NudgeMetres => _nudge.magnitude;

        readonly ScaleEase _readoutEase = new ScaleEase();
        readonly ScaleEase _confirmEase = new ScaleEase();
        readonly ScaleEase _cancelEase = new ScaleEase();
        MaterialPropertyBlock _block;
        bool _ready, _wasAnimating;
        VoiceMicVisual _mic = VoiceMicVisual.Idle;
        bool _micSet;
        float _level, _levelShown;
        string _title, _mode, _body, _foot, _micWord, _confirmWord, _cancelWord;
        VoiceTone _tone;
        bool _toneSet;

        /// <summary>True while the readout is on show.</summary>
        public bool ReadoutShown => _ready && _readoutEase.Shown;
        public string Title => _title ?? "";
        public string Body => _body ?? "";
        public string Foot => _foot ?? "";
        public VoiceMicVisual Mic => _mic;
        public bool ConfirmKeyShown => _ready && confirmKey != null && _confirmEase.Shown;
        public bool CancelKeyShown => _ready && cancelKey != null && _cancelEase.Shown;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (readout != null) _readoutEase.Init(readout, ScaleEase.ResolveFull(readout, readoutShownScale), 0.2f, false);
            if (confirmKey != null) _confirmEase.Init(confirmKey, ScaleEase.ResolveFull(confirmKey, answerKeyScale), 0.15f, false);
            if (cancelKey != null) _cancelEase.Init(cancelKey, ScaleEase.ResolveFull(cancelKey, answerKeyScale), 0.15f, false);
            _ready = true;
            SetMic(VoiceMicVisual.Idle);
        }

        void Update()
        {
            if (!_ready) return;
            float dt = Time.unscaledDeltaTime;
            _readoutEase.Tick(dt);
            _confirmEase.Tick(dt);
            _cancelEase.Tick(dt);
            bool animating = _confirmEase.Animating || _cancelEase.Animating;
            // When a key's pop has finished, its press visual re-reads its resting offset at the
            // final scale (PokeVisualRestGuard.SyncToCurrentScale), as the receipt and summon keys do.
            if (_wasAnimating && !animating)
            {
                if (confirmKey != null && _confirmEase.Shown) confirmKey.BroadcastMessage("SyncToCurrentScale", SendMessageOptions.DontRequireReceiver);
                if (cancelKey != null && _cancelEase.Shown) cancelKey.BroadcastMessage("SyncToCurrentScale", SendMessageOptions.DontRequireReceiver);
            }
            _wasAnimating = animating;
            KeepClear(dt);

            if (halo == null) return;
            float target;
            if (_mic == VoiceMicVisual.Listening) target = Mathf.Clamp01(_level * 3f) ;
            else if (_mic == VoiceMicVisual.Thinking) target = 0.25f + 0.2f * Mathf.Sin(Time.unscaledTime * 6f);
            else target = 0f;
            // Fast up, slow down, so a word reads as a swell instead of a flicker.
            float rate = target > _levelShown ? 14f : 4f;
            float next = Mathf.MoveTowards(_levelShown, target, rate * dt);
            if (Mathf.Abs(next - _levelShown) < 0.0005f) return;
            _levelShown = next;
            halo.localScale = haloRestScale * (1f + haloSwell * _levelShown);
        }

        void KeepClear(float dt)
        {
            if (readout == null) return;
            if (!_restSet)
            {
                _readoutRest = readout.localPosition;
                if (confirmKey != null) _confirmRest = confirmKey.localPosition;
                if (cancelKey != null) _cancelRest = cancelKey.localPosition;
                _restSet = true;
            }
            Vector3 want = Vector3.zero;
            if (Clearance != null && _readoutEase.Shown)
            {
                // The readout's bounds at its rest place (the nudge is taken off first so it does not chase itself).
                var rs = readout.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                    b.center -= _nudge;
                    try { want = Clearance(b); } catch (Exception) { want = Vector3.zero; }
                }
            }
            Vector3 next = Vector3.Lerp(_nudge, want, 1f - Mathf.Exp(-dt * 10f));
            if ((next - _nudge).sqrMagnitude < 1e-10f && want.sqrMagnitude == _nudge.sqrMagnitude) return;
            _nudge = next;
            Transform p = readout.parent;
            Vector3 local = p != null ? p.InverseTransformVector(_nudge) : _nudge;
            readout.localPosition = _readoutRest + local;
            if (confirmKey != null) confirmKey.localPosition = _confirmRest + (confirmKey.parent == p ? local : (confirmKey.parent != null ? confirmKey.parent.InverseTransformVector(_nudge) : _nudge));
            if (cancelKey != null) cancelKey.localPosition = _cancelRest + (cancelKey.parent == p ? local : (cancelKey.parent != null ? cancelKey.parent.InverseTransformVector(_nudge) : _nudge));
        }

        /// <summary>Microphone level 0..1 while listening.</summary>
        public void SetLevel(float level) { _level = level; }

        public void SetMic(VoiceMicVisual mic)
        {
            if (_micSet && mic == _mic) return;
            _mic = mic;
            _micSet = true;
            if (mic != VoiceMicVisual.Listening) _level = 0f;
            string word = mic == VoiceMicVisual.Listening ? "Stop" : mic == VoiceMicVisual.Thinking ? "Wait" : "Mic";
            SetText(micLabel, ref _micWord, word);
            if (haloRenderer != null && _block != null)
            {
                Color c = mic == VoiceMicVisual.Listening ? OfficeTokens.Teal
                        : mic == VoiceMicVisual.Thinking ? OfficeTokens.Amber
                        : mic == VoiceMicVisual.Off ? OfficeTokens.Housing
                        : OfficeTokens.Lavender;
                OfficeTokens.Tint(haloRenderer, _block, c);
            }
        }

        /// <summary>Show the readout. Empty strings clear a line. The two keys show only when asked.</summary>
        public void Show(string title, string mode, string body, string foot, VoiceTone tone,
                         string confirmWord = null, string cancelWord = null)
        {
            SetText(titleText, ref _title, title ?? "");
            SetText(modeText, ref _mode, mode ?? "");
            SetText(bodyText, ref _body, body ?? "");
            SetText(footText, ref _foot, foot ?? "");
            if (toneStrip != null && _block != null && (!_toneSet || tone != _tone))
            {
                _tone = tone; _toneSet = true;
                OfficeTokens.Tint(toneStrip, _block, ToneColor(tone));
            }
            if (!_ready) return;
            if (readout != null)
            {
                if (_readoutEase.Shown) { /* already up: the text changed in place */ }
                else _readoutEase.Show();
            }
            SetKey(confirmKey, _confirmEase, confirmLabel, ref _confirmWord, confirmWord);
            SetKey(cancelKey, _cancelEase, cancelLabel, ref _cancelWord, cancelWord);
        }

        /// <summary>Only the body line changes (the words so far while listening).</summary>
        public void SetBody(string body) { SetText(bodyText, ref _body, body ?? ""); }

        public void Hide()
        {
            if (!_ready) return;
            if (readout != null) _readoutEase.Hide();
            if (confirmKey != null) _confirmEase.Hide();
            if (cancelKey != null) _cancelEase.Hide();
        }

        void SetKey(Transform key, ScaleEase ease, TMP_Text label, ref string shown, string word)
        {
            if (key == null) return;
            if (string.IsNullOrEmpty(word)) { ease.Hide(); return; }
            SetText(label, ref shown, word);
            ease.Show();
        }

        static Color ToneColor(VoiceTone tone)
        {
            switch (tone)
            {
                case VoiceTone.Listening: return OfficeTokens.Teal;
                case VoiceTone.Good: return OfficeTokens.Success;
                case VoiceTone.Warn: return OfficeTokens.Amber;
                case VoiceTone.Bad: return OfficeTokens.Coral;
                case VoiceTone.Agent: return OfficeTokens.InfoBlue;
                default: return OfficeTokens.Lavender;
            }
        }

        static void SetText(TMP_Text label, ref string shown, string text)
        {
            if (text == shown) return;
            shown = text;
            if (label != null) label.text = text;
        }
    }
}
