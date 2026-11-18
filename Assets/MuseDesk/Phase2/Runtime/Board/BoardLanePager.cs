using System;
using TMPro;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Wall;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// rc22: the board's per-column header furniture, built at runtime next to the existing header blocks:
    /// a small rounded counter badge at the right end of each header (so the header reads as the column name only),
    /// and up / down poke keys that scroll a column that holds more cards than fit. The keys are clones of the board's
    /// own "NewTaskButton" (the on-head-proven poke setup), hidden by scale and with the poke switched off when the
    /// column fits; never SetActive. All sizes are derived from the key the board was authored with, so they stay right
    /// while BoardExpander scales the board.
    /// </summary>
    public sealed class BoardLanePager
    {
        const float KeyWorldW = 0.030f, KeyWorldH = 0.022f, BadgeWorldW = 0.046f, GapWorld = 0.005f, MarginWorld = 0.008f;

        readonly BoardPresenter _presenter;
        readonly Transform _board;
        readonly Action<int, int> _scroll;

        class Lane
        {
            public Renderer badgePlate;
            public TMP_Text badgeText;
            public WallKey up, down;
            public string text;
            public bool upShown, downShown, overflow;
            public Color plateColor = Color.black, textColor = Color.white;
        }

        readonly Lane[] _lanes = { new Lane(), new Lane(), new Lane(), new Lane() };
        public bool Built { get; private set; }

        public BoardLanePager(BoardPresenter presenter, Action<int, int> scroll)
        {
            _presenter = presenter;
            _board = presenter != null ? presenter.transform : null;
            _scroll = scroll;
        }

        // ───────────── read side (probes, tests of the live scene) ─────────────
        public string BadgeText(int lane) => Ok(lane) ? _lanes[lane].text : null;
        public bool UpShown(int lane) => Ok(lane) && _lanes[lane].upShown;
        public bool DownShown(int lane) => Ok(lane) && _lanes[lane].downShown;
        static bool Ok(int lane) => lane >= 0 && lane < 4;

        /// <summary>Authored board scale: the board's own scale once the key's authored size is known (a key is 4 cm in the
        /// world at full size), else its current scale when that looks like a full-size board. 0 = not known yet.</summary>
        float FullScale()
        {
            Transform nk = Find(_board, "NewTaskButton");
            if (nk != null && nk.localScale.x > 1e-4f && nk.localScale.x < 10f)
            {
                float s = 0.040f / nk.localScale.x;
                if (s > 0.05f && s < 20f) return s;
            }
            float cur = Mathf.Abs(_board.lossyScale.x);
            return cur > 0.2f ? cur : 0f;
        }

        static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t != null && t.name == name) return t;
            return null;
        }

        /// <summary>Try to build the badges and keys. Returns true when finished (built, or nothing to build on).</summary>
        public bool TryBuild()
        {
            if (Built) return true;
            if (_board == null || _presenter.laneHeaders == null || _presenter.laneHeadings == null) return false;
            float sFull = FullScale();
            if (sFull <= 0f) return false;
            float u = 1f / sFull;                                  // world metres -> board units at full size
            Transform template = Find(_board, "NewTaskButton");

            for (int i = 0; i < 4; i++)
            {
                Renderer header = i < _presenter.laneHeaders.Length ? _presenter.laneHeaders[i] : null;
                TMP_Text heading = i < _presenter.laneHeadings.Length ? _presenter.laneHeadings[i] : null;
                if (header == null || heading == null) continue;
                if (!HeaderBounds(_board, header, out Vector3 mn, out Vector3 mx)) continue;
                var lane = _lanes[i];
                float hH = mx.y - mn.y;
                float cy = (mn.y + mx.y) * 0.5f;
                float labelZ = _board.InverseTransformPoint(heading.transform.position).z;

                // Colours: the opposite lightness of the header block, so the box reads on it.
                Color hc = HeaderColor(header);
                bool lightHeader = (0.299f * hc.r + 0.587f * hc.g + 0.114f * hc.b) > 0.55f;
                lane.plateColor = lightHeader ? new Color(0.122f, 0.145f, 0.188f) : new Color(0.969f, 0.949f, 0.890f);   // Ink / Paper
                lane.textColor = lightHeader ? new Color(0.969f, 0.949f, 0.890f) : new Color(0.122f, 0.145f, 0.188f);

                float badgeH = Mathf.Clamp(hH * 0.60f, 0.010f * u, 0.016f * u);
                float badgeW = BadgeWorldW * u;
                float right = mx.x - MarginWorld * u;
                float bx = right - badgeW * 0.5f;
                float plateZ = labelZ - 0.0004f * u;
                Material mat = WallUi.PlateMaterial != null ? WallUi.PlateMaterial : header.sharedMaterial;
                lane.badgePlate = WallUi.Plate(_board, "LaneBadge_" + i, new Vector3(bx, cy, plateZ), badgeW, badgeH, badgeH * 0.5f, lane.plateColor, mat, null);
                lane.badgeText = WallUi.MakeText(_board, "LaneBadgeText_" + i, heading.font, new Vector3(bx, cy, plateZ - 0.0005f * u),
                    new Vector2(BadgeWorldW * 100f, badgeH * sFull * 100f), 9f, lane.textColor, TextAlignmentOptions.Center);
                lane.badgeText.transform.localScale = Vector3.one * (0.01f * u);
                lane.badgeText.fontStyle = FontStyles.Bold;
                lane.badgeText.textWrappingMode = TextWrappingModes.NoWrap;
                lane.badgeText.overflowMode = TextOverflowModes.Truncate;
                lane.badgeText.enableAutoSizing = true;
                lane.badgeText.fontSizeMin = 6f;
                lane.badgeText.fontSizeMax = 9f;
                lane.badgeText.text = lane.text ?? "0";

                if (template != null)
                {
                    float keyH = Mathf.Clamp(hH * 0.80f, 0.016f * u, KeyWorldH * u);
                    float keyW = KeyWorldW * u;
                    float downX = bx - badgeW * 0.5f - GapWorld * u - keyW * 0.5f;
                    float upX = downX - keyW - GapWorld * u;
                    int laneIndex = i;
                    lane.down = MakePagerKey(template, "LaneKeyDown_" + i, "Down", new Vector3(downX, cy, labelZ), 0.040f * u, keyW, keyH, () => _scroll(laneIndex, +1));
                    lane.up = MakePagerKey(template, "LaneKeyUp_" + i, "Up", new Vector3(upX, cy, labelZ), 0.040f * u, keyW, keyH, () => _scroll(laneIndex, -1));
                }
                ApplyKeys(lane);
                ApplyBadgeColours(lane);
            }
            Built = true;
            return true;
        }

        WallKey MakePagerKey(Transform template, string name, string text, Vector3 pos, float scale, float w, float h, Action press)
        {
            // MakeKey instantiates the template under the board with the given scale; Resize then fits the housing to w x h.
            WallKey k = WallUi.MakeKey(template, _board, name, text, pos, scale, press);
            if (k == null || k.root == null) return k;
            foreach (var b in k.root.GetComponentsInChildren<Behaviour>(true))
                if (b != null && b.GetType().Name == "FaceUser") b.enabled = false;   // the template billboards; a header key must not turn
            k.root.localRotation = Quaternion.identity;
            k.shown = true;                       // the press handler checks it; visibility is by scale below
            k.Resize(w, h);
            return k;
        }

        static bool HeaderBounds(Transform board, Renderer header, out Vector3 mn, out Vector3 mx)
        {
            mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
            var f = header.GetComponent<MeshFilter>();
            if (f == null || f.sharedMesh == null) return false;
            var bnd = f.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = bnd.center + Vector3.Scale(bnd.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var l = board.InverseTransformPoint(header.transform.TransformPoint(c));
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
            }
            return true;
        }

        static Color HeaderColor(Renderer header)
        {
            Material m = header.sharedMaterial;
            if (m == null) return Color.gray;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.gray;
        }

        /// <summary>Set one lane's counter and arrow state. Cheap when nothing changed.</summary>
        public void Update(int lane, int offset, int total, int capacity)
        {
            if (!Ok(lane)) return;
            var l = _lanes[lane];
            l.text = BoardPaging.CounterText(offset, total, capacity);
            l.overflow = BoardPaging.Overflows(total, capacity);
            bool up = l.overflow && BoardPaging.CanScrollUp(offset);
            bool down = l.overflow && BoardPaging.CanScrollDown(offset, total, capacity);
            if (l.badgeText != null && l.badgeText.text != l.text) l.badgeText.text = l.text;
            bool changed = up != l.upShown || down != l.downShown;
            l.upShown = up; l.downShown = down;
            if (changed) ApplyKeys(l);
        }

        void ApplyBadgeColours(Lane l)
        {
            if (l.badgePlate != null) WallUi.TintPlate(l.badgePlate, l.plateColor);
        }

        // An arrow shows only when that direction can scroll; a column that fits shows neither.
        static void ApplyKeys(Lane l)
        {
            SetKey(l.up, l.upShown);
            SetKey(l.down, l.downShown);
        }

        static void SetKey(WallKey k, bool visible)
        {
            if (k == null || k.root == null) return;
            if (k.shown != visible && visible) k.armAt = Time.unscaledTime + 0.25f;
            k.shown = visible;
            float f = visible ? 1f : MuseDesk.Workspace.ScaleEase.HiddenFactor;
            k.root.localScale = k.shownScale * f;
            if (k.poke != null && k.poke.enabled != visible) k.poke.enabled = visible;
        }
    }
}
