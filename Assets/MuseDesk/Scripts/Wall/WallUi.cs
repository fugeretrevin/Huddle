using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Oculus.Interaction;

namespace MuseDesk.Wall
{
    /// <summary>Something that animates per frame while visible changes are in flight (ticked by WallUi.Tick).</summary>
    interface IWallAnim { bool Step(float dt); }

    /// <summary>One poke key: a runtime clone of a ModeConsole key (the on-head-proven ISDK poke setup).</summary>
    public sealed class WallKey : IWallAnim
    {
        public Transform root;
        public PokeInteractable poke;
        public TMP_Text label;
        public Vector3 shownScale;
        public bool shown;
        public bool enabled = true;
        public Action action;
        public float armAt;

        // Shown/hidden by a timed eased scale (motion tokens), never to exactly zero: a zero scale breaks poke maths
        // (ScaleEase.HiddenFactor). The poke is enabled only once fully shown and disabled at once on hide.
        readonly MuseDesk.Motion.MotionClock _clock = new MuseDesk.Motion.MotionClock();
        float _from, _to = 0f, _k;
        Color _labelColor = Color.clear;

        public void Show(bool on, bool enabledToo = true)
        {
            if (root == null) return;
            if (shown != on)
            {
                shown = on;
                if (on) armAt = Time.unscaledTime + 0.25f;
                _from = _k; _to = on ? 1f : 0f;
                var p = MuseDesk.Motion.MuseMotionProfile.Active;
                _clock.Start(on ? p.panelAppear : p.panelHide);
                WallUi.Register(this);
            }
            if (enabled != enabledToo) { enabled = enabledToo; ApplyEnabledLook(); }
            bool wantPoke = on && enabledToo && !_clock.Running;
            if (poke != null && poke.enabled != wantPoke) poke.enabled = wantPoke;
        }

        bool IWallAnim.Step(float dt)
        {
            if (root == null) return false;
            bool done = _clock.Step(dt) || !_clock.Running;
            _k = Mathf.LerpUnclamped(_from, _to, _clock.Value);
            float f = Mathf.Max(Workspace.ScaleEase.HiddenFactor, _k);
            root.localScale = shownScale * f;
            if (done)
            {
                bool wantPoke = shown && enabled;
                if (poke != null && poke.enabled != wantPoke) poke.enabled = wantPoke;
            }
            return !done;
        }

        /// <summary>A disabled key must LOOK disabled (audit C #4): its label dims; the key stays in place.</summary>
        void ApplyEnabledLook()
        {
            if (label == null) return;
            if (_labelColor == Color.clear) _labelColor = label.color;
            Color c = _labelColor;
            if (!enabled) c.a *= 0.38f;
            label.color = c;
        }

        public void SetLabel(string s) { if (label != null && label.text != s) label.text = s; }
    }

    /// <summary>
    /// Builds Wall Habitat controls from a template key (ModeConsole's Status key). Keys are hidden by scale and
    /// their PokeInteractable switched off, never SetActive (project rule for ISDK interactables). A press only
    /// calls the action given here: the clone's copied persistent listeners are switched off.
    /// </summary>
    public static class WallUi
    {
        static readonly List<IWallAnim> _anims = new List<IWallAnim>();

        internal static void Register(IWallAnim a) { if (!_anims.Contains(a)) _anims.Add(a); }

        /// <summary>Advance every key/card transition in flight. Called once per frame by the Wall controller.</summary>
        public static void Tick(float dt)
        {
            for (int i = _anims.Count - 1; i >= 0; i--)
            {
                bool more;
                try { more = _anims[i].Step(dt); } catch (Exception e) { Debug.LogException(e); more = false; }
                if (!more) _anims.RemoveAt(i);
            }
        }

        public static WallKey MakeKey(Transform template, Transform parent, string name, string text, Vector3 localPos, float scale, Action onPress)
        {
            var k = new WallKey { shownScale = Vector3.one * scale, action = onPress };
            if (template == null) return k;
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, parent);
            go.name = name;
            go.SetActive(true);
            k.root = go.transform;
            k.root.localPosition = localPos;
            k.root.localRotation = Quaternion.identity;
            k.root.localScale = k.shownScale * Workspace.ScaleEase.HiddenFactor;
            k.poke = go.GetComponentInChildren<PokeInteractable>(true);
            var wrapper = go.GetComponentInChildren<InteractableUnityEventWrapper>(true);
            if (wrapper != null)
            {
                UnityEvent sel = wrapper.WhenSelect;
                for (int i = 0; i < sel.GetPersistentEventCount(); i++) sel.SetPersistentListenerState(i, UnityEventCallState.Off);
                sel.AddListener(() =>
                {
                    if (!k.shown || !k.enabled || Time.unscaledTime < k.armAt) return;
                    try { if (k.action != null) k.action(); }
                    catch (Exception e) { Debug.LogException(e); }
                });
            }
            // The same soft click every other poke key in the app makes (the clone was made after start-up).
            MuseDesk.Feedback.FeedbackController.RegisterLateKeys(go);
            k.label = go.GetComponentInChildren<TMP_Text>(true);
            if (k.label != null) { k.label.text = text; k.label.enableAutoSizing = false; }
            k.shown = false;
            k.enabled = true;
            if (k.poke != null) k.poke.enabled = false;
            return k;
        }

        /// <summary>A TextMeshPro label at the project's convention: scale 0.01 (sizes in cm, font size about mm).</summary>
        public static TMP_Text MakeText(Transform parent, string name, TMP_FontAsset font, Vector3 localPos, Vector2 sizeCm, float fontMm, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * 0.01f;
            var t = go.AddComponent<TextMeshPro>();
            if (font != null) t.font = font;
            t.fontSize = fontMm;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.rectTransform.sizeDelta = sizeCm;
            t.text = "";
            return t;
        }

        /// <summary>Rotation whose +Z points from the eye toward <paramref name="at"/> (TMP and poke faces show local -Z), tilted back.</summary>
        public static Quaternion FacingEye(Vector3 at, Vector3 eye, float tiltBackDeg = 0f)
        {
            Vector3 d = at - eye;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
            return Quaternion.LookRotation(d.normalized, Vector3.up) * Quaternion.Euler(tiltBackDeg, 0f, 0f);
        }
    }

    /// <summary>
    /// A near-user card: a paper plate, a title, a body and up to eight keys in two rows. Used for the visit
    /// conversation, the Manager plan and the portal controls. The card is placed once (on show, recenter);
    /// it is never tethered to the head.
    /// </summary>
    public sealed class WallCard : IWallAnim
    {
        public readonly Transform root;
        readonly TMP_Text _title, _body, _foot;
        readonly List<WallKey> _keys = new List<WallKey>();
        readonly Transform _template;
        readonly Vector2 _size;
        readonly float _keyScale;
        bool _visible;
        // Reveal: a short eased scale from 0.94 (panelAppear); hide: back to 0.94 (panelHide), then inactive.
        readonly MuseDesk.Motion.MotionClock _clock = new MuseDesk.Motion.MotionClock();
        float _from = 1f, _to = 1f, _s = 1f;
        const float Tucked = 0.94f;

        public bool Visible => _visible;
        public TMP_Text Title => _title;
        public TMP_Text Body => _body;

        public WallCard(Transform parent, string name, Transform keyTemplate, TMP_FontAsset font, WallHabitatView art, Vector2 size, float keyScale)
        {
            _template = keyTemplate;
            _size = size;
            _keyScale = keyScale;
            root = new GameObject(name).transform;
            root.SetParent(parent, false);
            // Plate behind everything (+Z side), text 0.5 mm in front (-Z), keys proud of the plate.
            if (art != null)
            {
                art.Box(root, "Plate", new Vector3(0, 0, 0.002f), new Vector3(size.x, size.y, 0.003f), WallHabitatView.Paper);
                art.Box(root, "Edge", new Vector3(0, size.y * 0.5f - 0.002f, 0.0005f), new Vector3(size.x, 0.004f, 0.002f), WallHabitatView.Teal);
            }
            float w = size.x * 100f - 1.2f;
            _title = WallUi.MakeText(root, "Title", font, new Vector3(0, size.y * 0.5f - 0.011f, -0.0005f), new Vector2(w, 1.4f), 6.5f, WallHabitatView.Ink, TextAlignmentOptions.Left);
            _body = WallUi.MakeText(root, "Body", font, new Vector3(0, size.y * 0.5f - 0.019f - size.y * 0.21f, -0.0005f), new Vector2(w, size.y * 100f * 0.42f), 4.6f, WallHabitatView.Ink, TextAlignmentOptions.TopLeft);
            // Provenance is a trust signal, not fine print (audit C #6): body-adjacent size, soft ink, not grey.
            _foot = WallUi.MakeText(root, "Provenance", font, new Vector3(0, -size.y * 0.5f + 0.0065f, -0.0005f), new Vector2(w, 1.1f), 4.2f, Workspace.OfficeTokens.InkSoft, TextAlignmentOptions.Left);
            root.gameObject.SetActive(false);
        }

        /// <summary>Keys: up to 8, laid out in two rows of four from the bottom. Null entries leave a gap.</summary>
        public void SetKeys(IList<KeyValuePair<string, Action>> keys)
        {
            int n = keys == null ? 0 : keys.Count;
            while (_keys.Count < n)
            {
                int i = _keys.Count;
                _keys.Add(WallUi.MakeKey(_template, root, "Key" + i, "", KeyPos(i), _keyScale, null));
            }
            for (int i = 0; i < _keys.Count; i++)
            {
                WallKey k = _keys[i];
                bool use = i < n && keys[i].Key != null;
                if (use) { k.SetLabel(keys[i].Key); k.action = keys[i].Value; }
                k.Show(use && _visible);
            }
        }

        Vector3 KeyPos(int i)
        {
            int row = i / 4, col = i % 4;
            float pitch = _size.x / 4f;
            float x = -_size.x * 0.5f + pitch * (col + 0.5f);
            float y = -_size.y * 0.5f + 0.022f + (1 - row) * 0.03f;
            return new Vector3(x, y, -0.006f);
        }

        public void SetText(string title, string body, string footer)
        {
            if (_title.text != title) _title.text = title ?? "";
            if (_body.text != body) _body.text = body ?? "";
            if (_foot.text != footer) _foot.text = footer ?? "";
        }

        public void Show(bool on)
        {
            if (_visible == on) return;
            _visible = on;
            if (on) root.gameObject.SetActive(true);
            _from = _s; _to = on ? 1f : Tucked;
            if (on && !_clock.Running) _from = Tucked;
            var p = MuseDesk.Motion.MuseMotionProfile.Active;
            _clock.Start(on ? p.panelAppear : p.panelHide);
            WallUi.Register(this);
            for (int i = 0; i < _keys.Count; i++) _keys[i].Show(on && _keys[i].action != null && _keys[i].label != null && !string.IsNullOrEmpty(_keys[i].label.text));
        }

        bool IWallAnim.Step(float dt)
        {
            if (root == null) return false;
            bool done = _clock.Step(dt) || !_clock.Running;
            _s = Mathf.LerpUnclamped(_from, _to, _clock.Value);
            root.localScale = Vector3.one * _s;
            if (done && !_visible) { root.gameObject.SetActive(false); _s = Tucked; }
            return !done;
        }

        public void Place(Vector3 world, Vector3 eye, float tiltBackDeg)
        {
            root.SetPositionAndRotation(world, WallUi.FacingEye(world, eye, tiltBackDeg));
        }

        /// <summary>For drivers: press key i as a finger would (only if shown and enabled).</summary>
        public bool Press(int i)
        {
            if (i < 0 || i >= _keys.Count || !_keys[i].shown || !_keys[i].enabled || _keys[i].action == null) return false;
            _keys[i].action();
            return true;
        }

        public string KeyLabel(int i) { return i >= 0 && i < _keys.Count && _keys[i].label != null && _keys[i].shown ? _keys[i].label.text : null; }
        public int KeyCount => _keys.Count;
    }
}
