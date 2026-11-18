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

        [Header("rc21: the readout's own spot")]
        [Tooltip("When the readout appears, lift it off the dock and turn it to face the eyes (see PlaceForEye).")]
        public bool placeTowardEye = true;
        [Tooltip("How far above the microphone key the readout's centre sits (m, world up).")]
        public float liftMetres = 0.10f;
        [Tooltip("How far from the microphone key toward the eyes the readout's centre sits (m).")]
        public float towardEyeMetres = 0.08f;

        /// <summary>rc20 (owner, worn rc19: "the panel with what was heard was intersecting with the panel, bring that bit
        /// out"). Set by whoever shows cards near the user (the Wall habitat): given the readout's world bounds, the world
        /// offset that brings it out in front of anything it would cut into (zero when it is clear).</summary>
        public static Func<Bounds, Vector3> Clearance;
        Vector3 _readoutRest, _confirmRest, _cancelRest, _nudge;
        Quaternion _readoutRestRot = Quaternion.identity, _confirmRestRot = Quaternion.identity, _cancelRestRot = Quaternion.identity;
        // The pose the readout and its keys have while shown (local to their parents), without the clearance nudge.
        Vector3 _readoutBase, _confirmBase, _cancelBase;
        Quaternion _readoutBaseRot = Quaternion.identity, _confirmBaseRot = Quaternion.identity, _cancelBaseRot = Quaternion.identity;
        bool _restSet, _poseDirty = true;
        Transform _eye;
        /// <summary>For drivers: how far the readout is brought out right now (m).</summary>
        public float NudgeMetres => _nudge.magnitude;
        /// <summary>For drivers: true when the readout was lifted toward the eyes the last time it appeared.</summary>
        public bool PlacedForEye { get; private set; }
        /// <summary>For drivers: how many times the readout was given its spot.</summary>
        public int PlaceCount { get; private set; }

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

        void EnsureRest()
        {
            if (_restSet || readout == null) return;
            _readoutRest = readout.localPosition; _readoutRestRot = readout.localRotation;
            if (confirmKey != null) { _confirmRest = confirmKey.localPosition; _confirmRestRot = confirmKey.localRotation; }
            if (cancelKey != null) { _cancelRest = cancelKey.localPosition; _cancelRestRot = cancelKey.localRotation; }
            _readoutBase = _readoutRest; _readoutBaseRot = _readoutRestRot;
            _confirmBase = _confirmRest; _confirmBaseRot = _confirmRestRot;
            _cancelBase = _cancelRest; _cancelBaseRot = _cancelRestRot;
            _restSet = true;
        }

        bool KeysIdle()
        {
            return !(_confirmEase.Shown || _confirmEase.Animating || _cancelEase.Shown || _cancelEase.Animating);
        }

        Transform Eye()
        {
            if (_eye != null && _eye.gameObject.activeInHierarchy) return _eye;
            Camera c = Camera.main;
            if (c != null) { _eye = c.transform; return _eye; }
            var go = GameObject.Find("CenterEyeAnchor");
            _eye = go != null ? go.transform : null;
            return _eye;
        }

        /// <summary>
        /// rc21 (owner, worn rc20: "the panel for speech is still clipping"). The readout was built 8 cm up the dock's own
        /// face (MicSlot on ModeConsole, local y +0.08, z 0): coplanar with the dock body and its status lip, so it cut into
        /// the dock, and into whatever stood next to the dock, from any angle. When it appears it now gets its own spot:
        /// <see cref="liftMetres"/> above the microphone key and <see cref="towardEyeMetres"/> toward the eyes, turned to face
        /// them (below eye line, in reach). The Confirm / Cancel keys keep their place in front of the readout's lower edge.
        /// The spot is chosen only while the readout and both keys are hidden, so nothing moves under a pressing finger; it is
        /// stored relative to the dock, so it follows the dock if the dock is moved.
        /// </summary>
        public void PlaceForEye()
        {
            EnsureRest();
            if (readout == null) return;
            Transform p = readout.parent;
            Transform eye = Eye();
            PlacedForEye = false;
            _readoutBase = _readoutRest; _readoutBaseRot = _readoutRestRot;
            _confirmBase = _confirmRest; _confirmBaseRot = _confirmRestRot;
            _cancelBase = _cancelRest; _cancelBaseRot = _cancelRestRot;
            if (placeTowardEye && p != null && eye != null)
            {
                Vector3 anchor = transform.position;                 // the microphone key
                Vector3 toEye = eye.position - anchor;
                float d = toEye.magnitude;
                if (d > 0.12f)
                {
                    toEye /= d;
                    Vector3 pos = anchor + Vector3.up * liftMetres + toEye * Mathf.Min(towardEyeMetres, d * 0.4f);
                    Vector3 away = pos - eye.position;                // the text faces -Z: +Z points away from the eyes
                    if (away.sqrMagnitude > 1e-6f)
                    {
                        Quaternion rot = Quaternion.LookRotation(away.normalized, Vector3.up);
                        // Rest pose of the readout in world space (its rest rotation is the frame the keys' offsets live in).
                        Quaternion restWorld = p.rotation * _readoutRestRot;
                        Vector3 restWorldPos = p.TransformPoint(_readoutRest);
                        Quaternion delta = rot * Quaternion.Inverse(restWorld);
                        _readoutBase = p.InverseTransformPoint(pos);
                        _readoutBaseRot = Quaternion.Inverse(p.rotation) * rot;
                        if (confirmKey != null) PlaceKey(confirmKey, _confirmRest, _confirmRestRot, restWorldPos, pos, delta, out _confirmBase, out _confirmBaseRot);
                        if (cancelKey != null) PlaceKey(cancelKey, _cancelRest, _cancelRestRot, restWorldPos, pos, delta, out _cancelBase, out _cancelBaseRot);
                        PlacedForEye = true;
                    }
                }
            }
            PlaceCount++;
            _poseDirty = true;
            ApplyPose();
        }

        static void PlaceKey(Transform key, Vector3 restLocal, Quaternion restRot, Vector3 readoutRestWorld, Vector3 readoutWorld,
                             Quaternion delta, out Vector3 baseLocal, out Quaternion baseRot)
        {
            Transform kp = key.parent;
            Vector3 restWorld = kp != null ? kp.TransformPoint(restLocal) : restLocal;
            Quaternion restWorldRot = kp != null ? kp.rotation * restRot : restRot;
            // Same offset from the readout, turned with it.
            Vector3 world = readoutWorld + delta * (restWorld - readoutRestWorld);
            Quaternion worldRot = delta * restWorldRot;
            baseLocal = kp != null ? kp.InverseTransformPoint(world) : world;
            baseRot = kp != null ? Quaternion.Inverse(kp.rotation) * worldRot : worldRot;
        }

        void ApplyPose()
        {
            if (readout == null || !_restSet) return;
            _poseDirty = false;
            readout.localPosition = _readoutBase + ToLocal(readout.parent, _nudge);
            readout.localRotation = _readoutBaseRot;
            if (confirmKey != null) { confirmKey.localPosition = _confirmBase + ToLocal(confirmKey.parent, _nudge); confirmKey.localRotation = _confirmBaseRot; }
            if (cancelKey != null) { cancelKey.localPosition = _cancelBase + ToLocal(cancelKey.parent, _nudge); cancelKey.localRotation = _cancelBaseRot; }
        }

        static Vector3 ToLocal(Transform parent, Vector3 world) { return parent != null ? parent.InverseTransformVector(world) : world; }

        void KeepClear(float dt)
        {
            if (readout == null) return;
            EnsureRest();
            Vector3 want = Vector3.zero;
            bool keysUp = !KeysIdle();
            if (keysUp) want = _nudge;                 // a key may be under a finger: nothing moves while the keys are up
            else if (Clearance != null && _readoutEase.Shown)
            {
                // The readout's bounds at its base place (the nudge is taken off first so it does not chase itself).
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
            if ((next - want).sqrMagnitude < 1e-8f) next = want;
            if (next == _nudge && !_poseDirty) return;
            _nudge = next;
            ApplyPose();
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
                else
                {
                    // rc21: its own spot, chosen only while nothing of it can be under a finger.
                    if (KeysIdle() || PlaceCount == 0) PlaceForEye();
                    _readoutEase.Show();
                }
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
