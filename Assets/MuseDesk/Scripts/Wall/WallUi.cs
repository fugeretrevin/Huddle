using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Oculus.Interaction;
using MuseDesk.Spatial.Ui;

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

        /// <summary>Label font size at authored size (before any shrink to fit) and the key's width factor.</summary>
        public float baseFontSize;
        public float widthScale = 1f;

        /// <summary>rc12: the template key's real housing size at root scale 1 (measured from its meshes when it is made). The
        /// old layout assumed one unit wide; the console key is about 1.8 x 1.35, so wide keys ran into each other.</summary>
        public Vector2 unit = Vector2.one;
        internal Vector3 labelScale0;
        internal Vector2 labelSize0;
        internal float depthScale;

        /// <summary>Size the key to exactly <paramref name="width"/> x <paramref name="height"/> metres (housing), keeping the
        /// label's glyph shape and giving it the whole key width. The label font then shrinks to fit if needed.</summary>
        public void Resize(float width, float height)
        {
            if (root == null) return;
            float sx = width / Mathf.Max(1e-4f, unit.x), sy = height / Mathf.Max(1e-4f, unit.y);
            shownScale = new Vector3(sx, sy, depthScale > 0f ? depthScale : sy);
            widthScale = sx / Mathf.Max(1e-5f, sy);
            if (label != null)
            {
                label.transform.localScale = new Vector3(labelScale0.x / widthScale, labelScale0.y, labelScale0.z);
                label.rectTransform.sizeDelta = new Vector2(labelSize0.x * widthScale * 0.92f, labelSize0.y);
                FitLabel(label.text);
            }
            float f = Mathf.Max(Workspace.ScaleEase.HiddenFactor, shown ? 1f : 0f);
            if (shown) root.localScale = shownScale * f;
        }

        /// <summary>Set the label and shrink its font (never below <see cref="KeyLabelFit.DefaultMinScale"/>) until the whole text
        /// fits the key: no ellipsis. A change only; nothing runs when the text is the same.</summary>
        public void SetLabel(string s)
        {
            if (label == null || label.text == s) return;
            label.text = s;
            FitLabel(s);
        }

        /// <summary>Fit the label's current text (used once, when the key is made with its first text).</summary>
        public void SetLabelFitOnly() { if (label != null) FitLabel(label.text); }

        void FitLabel(string s)
        {
            if (baseFontSize <= 0f || string.IsNullOrEmpty(s)) return;
            label.fontSize = baseFontSize;
            float avail = label.rectTransform.rect.width;
            float pw = label.GetPreferredValues(s, 0f, 0f).x;
            float k = KeyLabelFit.FontScale(pw, avail);
            if (k < 1f) label.fontSize = baseFontSize * k;
        }
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
            return MakeKey(template, parent, name, text, localPos, scale, onPress, 1f);
        }

        /// <summary>
        /// A key <paramref name="widthScale"/> times wider than the template (1 = the old size). Only the key (its housing and poke face)
        /// widens: the label is counter-scaled so its glyphs keep their shape, and its box grows with the key. The label never
        /// wraps or ellipses; <see cref="WallKey.SetLabel"/> shrinks the font to fit instead.
        /// </summary>
        public static WallKey MakeKey(Transform template, Transform parent, string name, string text, Vector3 localPos, float scale, Action onPress, float widthScale)
        {
            widthScale = Mathf.Max(1f, widthScale);
            var k = new WallKey { shownScale = new Vector3(scale * widthScale, scale, scale), action = onPress, widthScale = widthScale };
            if (template == null) return k;
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, parent);
            go.name = name;
            go.SetActive(true);
            k.root = go.transform;
            k.root.localPosition = localPos;
            k.root.localRotation = Quaternion.identity;
            k.unit = MeasureUnit(go.transform);
            k.depthScale = scale;
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
                    // rc22: another key pressed switches armed Select Agents off (before the key acts; Select ignores itself).
                    MuseDesk.Spatial.Wall.SelectModeExit.Raise(k.root != null ? k.root.name : "key");
                    try { if (k.action != null) k.action(); }
                    catch (Exception e) { Debug.LogException(e); }
                });
            }
            // The same soft click every other poke key in the app makes (the clone was made after start-up).
            MuseDesk.Feedback.FeedbackController.RegisterLateKeys(go);
            k.label = go.GetComponentInChildren<TMP_Text>(true);
            if (k.label != null)
            {
                k.label.enableAutoSizing = false;
                k.label.textWrappingMode = TextWrappingModes.NoWrap;
                k.label.overflowMode = TextOverflowModes.Overflow;      // never "..."; the font shrinks to fit instead
                k.labelScale0 = k.label.transform.localScale;
                k.labelSize0 = k.label.rectTransform.sizeDelta;
                if (widthScale > 1.001f)
                {
                    Vector3 ls = k.label.transform.localScale;
                    k.label.transform.localScale = new Vector3(ls.x / widthScale, ls.y, ls.z);   // undo the key's stretch on the glyphs
                    Vector2 sd = k.label.rectTransform.sizeDelta;
                    k.label.rectTransform.sizeDelta = new Vector2(sd.x * widthScale, sd.y);       // and give the text the wider box
                }
                k.baseFontSize = k.label.fontSize;
                k.label.text = text;
                if (!string.IsNullOrEmpty(text)) k.SetLabelFitOnly();
            }
            k.shown = false;
            k.enabled = true;
            if (k.poke != null) k.poke.enabled = false;
            return k;
        }

        /// <summary>The key's housing size in its root's units (meshes only, text excluded), whatever the root's scale now.</summary>
        static Vector2 MeasureUnit(Transform root)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<TMP_Text>() != null) continue;
                Bounds b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 w = mf.transform.TransformPoint(c);
                    Vector3 l = root.InverseTransformPoint(w);
                    minX = Mathf.Min(minX, l.x); maxX = Mathf.Max(maxX, l.x); minY = Mathf.Min(minY, l.y); maxY = Mathf.Max(maxY, l.y);
                }
            }
            if (minX > maxX) return Vector2.one;
            return new Vector2(Mathf.Max(0.05f, maxX - minX), Mathf.Max(0.05f, maxY - minY));
        }

        // ───────────── rc12 card look (unlit UI materials, the task dock's style) ─────────────

        /// <summary>Unlit materials the cards use (the controller sets them from its serialized fields): the task dock's dark
        /// plate, the paper sheet, and a plain unlit accent. Null: the cards fall back to the habitat's lit boxes.</summary>
        public static Material PlateMaterial, PaperMaterial, AccentMaterial;
        static readonly MaterialPropertyBlock s_mpb = new MaterialPropertyBlock();
        static readonly int s_base = Shader.PropertyToID("_BaseColor"), s_col = Shader.PropertyToID("_Color");

        public static Color Hex(int rgb) { return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f); }

        /// <summary>A flat rounded rectangle facing -Z (the reading side), centred on the origin.</summary>
        public static Mesh RoundedRect(float w, float h, float r, int cornerSegments = 6)
        {
            r = Mathf.Clamp(r, 0.0005f, Mathf.Min(w, h) * 0.5f);
            int n = cornerSegments + 1;
            var v = new Vector3[n * 4 + 1];
            v[0] = Vector3.zero;
            float hw = w * 0.5f - r, hh = h * 0.5f - r;
            Vector2[] centers = { new Vector2(hw, hh), new Vector2(-hw, hh), new Vector2(-hw, -hh), new Vector2(hw, -hh) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i < n; i++)
                {
                    float a = (c * 90f + i * 90f / cornerSegments) * Mathf.Deg2Rad;
                    v[1 + c * n + i] = new Vector3(centers[c].x + Mathf.Cos(a) * r, centers[c].y + Mathf.Sin(a) * r, 0f);
                }
            int count = n * 4;
            var tris = new int[count * 3];
            for (int i = 0; i < count; i++)
            {
                // Counter-clockwise outline seen from -Z: wind so the face points to -Z.
                tris[i * 3] = 0; tris[i * 3 + 1] = 1 + (i + 1) % count; tris[i * 3 + 2] = 1 + i;
            }
            var m = new Mesh { name = "RoundedRect", vertices = v, triangles = tris };
            var nrm = new Vector3[v.Length];
            for (int i = 0; i < nrm.Length; i++) nrm[i] = Vector3.back;
            m.normals = nrm;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A rounded plate in the unlit UI look (or the lit box fallback), tinted.</summary>
        public static Renderer Plate(Transform parent, string name, Vector3 localPos, float w, float h, float r, Color c, Material mat, WallHabitatView fallback)
        {
            if (mat == null)
                return fallback != null ? fallback.Box(parent, name, localPos, new Vector3(w, h, 0.002f), c) : null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = RoundedRect(w, h, r);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            TintPlate(mr, c);
            return mr;
        }

        public static void TintPlate(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(s_mpb);
            s_mpb.SetColor(s_base, c);
            s_mpb.SetColor(s_col, c);
            r.SetPropertyBlock(s_mpb);
        }

        static TMP_FontAsset s_lastGood, s_inter;
        static bool s_warned;

        /// <summary>Pure choice of the font a label gets: the one asked for, else the last Inter that was passed in, else any
        /// loaded Inter asset, else null (TextMeshPro's default). <paramref name="usedFallback"/> says the asked-for font was
        /// missing.</summary>
        public static TMP_FontAsset ChooseFont(TMP_FontAsset asked, TMP_FontAsset lastGood, TMP_FontAsset loadedInter, out bool usedFallback)
        {
            usedFallback = asked == null;
            if (asked != null) return asked;
            return lastGood != null ? lastGood : loadedInter;
        }

        /// <summary>A null font used to fall back to TextMeshPro's default (Liberation) without a word. It now falls back to
        /// Inter (the last Inter a Wall label was given, else a loaded Inter Medium asset) and warns once.</summary>
        static TMP_FontAsset ResolveFont(TMP_FontAsset font, string labelName)
        {
            if (font != null) { s_lastGood = font; return font; }
            if (s_inter == null)
                foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                    if (f != null && f.name.StartsWith("Inter-Medium")) { s_inter = f; break; }
            var chosen = ChooseFont(null, s_lastGood, s_inter, out _);
            if (!s_warned)
            {
                s_warned = true;
                Debug.LogWarning("[MuseDesk.Wall] WallUi.MakeText got no font for \"" + labelName + "\"; using "
                    + (chosen != null ? chosen.name : "the TextMeshPro default (no Inter asset is loaded)") + ". Assign the Inter font on the controller.");
            }
            return chosen;
        }

        /// <summary>A TextMeshPro label at the project's convention: scale 0.01 (sizes in cm, font size about mm).</summary>
        public static TMP_Text MakeText(Transform parent, string name, TMP_FontAsset font, Vector3 localPos, Vector2 sizeCm, float fontMm, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * 0.01f;
            var t = go.AddComponent<TextMeshPro>();
            font = ResolveFont(font, name);
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
    /// A near-user card: a rounded plate, a title, a body, a provenance footer and up to eight poke keys. Used for the visit
    /// conversation, the Meet chooser, the receipt and (paper style) the Manager plan. The card is placed once (on show,
    /// recenter); it is never tethered to the head.
    ///
    /// rc12 look (owner: "fix all the buttons after you open the papers to look nicer"): the task dock's style. A dark unlit
    /// plate with a thin rim and a teal accent bar, white title, soft body, muted footer; or, for the plan, the cream paper
    /// sheet with ink text (the desk decision paper's look). Keys are measured from the real template key and laid out
    /// evenly with gaps: up to four in one row, five to eight in two balanced rows. Null entries are skipped.
    /// </summary>
    public sealed class WallCard : IWallAnim
    {
        public readonly Transform root;
        readonly TMP_Text _title, _body, _foot;
        readonly List<WallKey> _keys = new List<WallKey>();
        readonly Transform _template;
        readonly Vector2 _size;
        readonly float _keyScale;
        readonly bool _paper;
        bool _visible;
        // Reveal: a short eased scale from 0.94 (panelAppear); hide: back to 0.94 (panelHide), then inactive.
        readonly MuseDesk.Motion.MotionClock _clock = new MuseDesk.Motion.MotionClock();
        float _from = 1f, _to = 1f, _s = 1f;
        const float Tucked = 0.94f;

        // Layout (metres, card space).
        public const float Side = 0.009f, KeyGap = 0.005f, KeyH = 0.024f, KeyRowGap = 0.005f, FootH = 0.012f, MaxKeyW = 0.072f;

        public static readonly Color DarkPlate = WallUi.Hex(0x1B1F27), DarkRim = WallUi.Hex(0x343B4A),
            DarkTitle = WallUi.Hex(0xF4F6FA), DarkBody = WallUi.Hex(0xC9D1DC), DarkFoot = WallUi.Hex(0x8E99A8),
            PaperSheet = WallUi.Hex(0xF7F2E3), PaperRim = WallUi.Hex(0xD9D0BA);

        public bool Visible => _visible;
        /// <summary>The card's size in metres (card space).</summary>
        public Vector2 Size => _size;
        public TMP_Text Title => _title;
        public TMP_Text Body => _body;
        public bool PaperStyle => _paper;

        public WallCard(Transform parent, string name, Transform keyTemplate, TMP_FontAsset font, WallHabitatView art, Vector2 size, float keyScale)
            : this(parent, name, keyTemplate, font, art, size, keyScale, false) { }

        public WallCard(Transform parent, string name, Transform keyTemplate, TMP_FontAsset font, WallHabitatView art, Vector2 size, float keyScale, bool paper)
        {
            _template = keyTemplate;
            _size = size;
            _keyScale = keyScale;
            _paper = paper;
            root = new GameObject(name).transform;
            root.SetParent(parent, false);
            // Plate behind everything (+Z side), text 0.5 mm in front (-Z), keys proud of the plate.
            Material mat = paper ? (WallUi.PaperMaterial != null ? WallUi.PaperMaterial : WallUi.PlateMaterial) : WallUi.PlateMaterial;
            Material accent = WallUi.AccentMaterial != null ? WallUi.AccentMaterial : mat;
            const float rim = 0.0014f, radius = 0.009f;
            WallUi.Plate(root, "Rim", new Vector3(0, 0, 0.0026f), size.x + rim * 2f, size.y + rim * 2f, radius + rim, paper ? PaperRim : DarkRim, mat, art);
            WallUi.Plate(root, "Plate", new Vector3(0, 0, 0.002f), size.x, size.y, radius, paper ? PaperSheet : DarkPlate, mat, art);
            WallUi.Plate(root, "Edge", new Vector3(0, size.y * 0.5f - 0.0042f, 0.0016f), size.x - 0.024f, 0.0022f, 0.0011f, WallHabitatView.Teal, accent, art);

            float w = size.x * 100f - Side * 200f;
            Color title = paper ? Workspace.OfficeTokens.Ink : DarkTitle, body = paper ? Workspace.OfficeTokens.Ink : DarkBody, foot = paper ? Workspace.OfficeTokens.InkSoft : DarkFoot;
            _title = WallUi.MakeText(root, "Title", font, new Vector3(0, size.y * 0.5f - 0.0125f, -0.0005f), new Vector2(w, 1.4f), 6.6f, title, TextAlignmentOptions.Left);
            _title.fontStyle = FontStyles.Bold;
            _body = WallUi.MakeText(root, "Body", font, Vector3.zero, new Vector2(w, 1f), 4.8f, body, TextAlignmentOptions.TopLeft);
            _body.lineSpacing = 6f;
            // Provenance is a trust signal, not fine print (audit C #6): body-adjacent size, soft ink, not grey.
            _foot = WallUi.MakeText(root, "Provenance", font, new Vector3(0, -size.y * 0.5f + 0.0062f, -0.0005f), new Vector2(w, 0.8f), 3.6f, foot, TextAlignmentOptions.Left);
            _foot.textWrappingMode = TextWrappingModes.NoWrap;      // one line under the keys, never into them
            LayoutBody(0);
            root.gameObject.SetActive(false);
        }

        /// <summary>The body fills the space between the title and the top of the key rows.</summary>
        void LayoutBody(int keyRows)
        {
            float top = _size.y * 0.5f - 0.020f;
            float bottom = -_size.y * 0.5f + FootH + (keyRows > 0 ? keyRows * KeyH + (keyRows - 1) * KeyRowGap + 0.006f : 0.004f);
            float h = Mathf.Max(0.01f, top - bottom);
            _body.transform.localPosition = new Vector3(0f, (top + bottom) * 0.5f, -0.0005f);
            _body.rectTransform.sizeDelta = new Vector2(_size.x * 100f - Side * 200f, h * 100f);
        }

        int _rowsLaidOut = -1;

        /// <summary>Keys: up to 8. One row up to four, two balanced rows from five. Null entries are skipped (no holes).</summary>
        public void SetKeys(IList<KeyValuePair<string, Action>> keys)
        {
            int n = keys == null ? 0 : keys.Count;
            while (_keys.Count < n)
            {
                int i = _keys.Count;
                _keys.Add(WallUi.MakeKey(_template, root, "Key" + i, "", Vector3.zero, _keyScale, null, 1f));
            }
            int used = 0;
            for (int i = 0; i < n; i++) if (keys[i].Key != null) used++;
            int rows = used == 0 ? 0 : used <= 4 ? 1 : 2;
            int top = rows == 2 ? (used + 1) / 2 : used;
            int slot = 0;
            for (int i = 0; i < _keys.Count; i++)
            {
                WallKey k = _keys[i];
                bool use = i < n && keys[i].Key != null;
                if (use)
                {
                    int row = rows == 2 && slot >= top ? 1 : 0;
                    int inRow = row == 0 ? top : used - top;
                    int col = row == 0 ? slot : slot - top;
                    PlaceKey(k, row, rows, col, inRow);
                    k.SetLabel(keys[i].Key);
                    k.action = keys[i].Value;
                    slot++;
                }
                k.Show(use && _visible);
            }
            if (rows != _rowsLaidOut) { _rowsLaidOut = rows; LayoutBody(rows); }
        }

        void PlaceKey(WallKey k, int row, int rows, int col, int inRow)
        {
            if (k.root == null) return;
            float avail = _size.x - 2f * Side;
            float kw = Mathf.Min(MaxKeyW, (avail - KeyGap * (inRow - 1)) / Mathf.Max(1, inRow));
            float span = kw * inRow + KeyGap * (inRow - 1);
            float x = -span * 0.5f + kw * 0.5f + col * (kw + KeyGap);
            // Rows from the bottom: the lower row sits just above the footer.
            float lowerY = -_size.y * 0.5f + FootH + KeyH * 0.5f + 0.002f;
            float y = rows == 2 && row == 0 ? lowerY + KeyH + KeyRowGap : lowerY;
            k.root.localPosition = new Vector3(x, y, -0.006f);
            k.Resize(kw, KeyH);
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
            MuseDesk.Spatial.Wall.SelectModeExit.Raise(_keys[i].root != null ? _keys[i].root.name : "key");   // as the poke does (rc22)
            _keys[i].action();
            return true;
        }

        public string KeyLabel(int i) { return i >= 0 && i < _keys.Count && _keys[i].label != null && _keys[i].shown ? _keys[i].label.text : null; }
        public int KeyCount => _keys.Count;
        /// <summary>For drivers and tests: the key's centre and size on the card (metres).</summary>
        public bool KeyRect(int i, out Vector2 centre, out Vector2 size)
        {
            centre = size = Vector2.zero;
            if (i < 0 || i >= _keys.Count || _keys[i].root == null || !_keys[i].shown) return false;
            centre = _keys[i].root.localPosition;
            size = new Vector2(_keys[i].shownScale.x * _keys[i].unit.x, _keys[i].shownScale.y * _keys[i].unit.y);
            return true;
        }
    }
}