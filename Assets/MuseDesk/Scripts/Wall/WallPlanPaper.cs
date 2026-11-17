using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using MuseDesk.Spatial.Ui;

namespace MuseDesk.Wall
{
    /// <summary>What the framed stamp zone on the paper says.</summary>
    public enum StampZoneMode { Hidden, Awaiting, Committing, Settled }

    /// <summary>
    /// The Manager's plan-review paper: a near-user <see cref="WallCard"/> (plate, up to eight poke keys, provenance footer)
    /// whose body is drawn row by row from the A6 <see cref="PlanReviewLayout"/> (type scale, legibility floor for the
    /// reading distance, token colours), paged by <see cref="PlanPaperPager"/> so nothing is shrunk or silently dropped.
    ///
    /// Stamp zone: a framed "Stamp here" box (Teal frame, paper-toned plate, the desk decision paper's wording) sits between the
    /// text and the keys while the plan awaits the user. A held stamp presses at its centre (<see cref="StampSpotWorld"/>) and the
    /// APPROVED / REJECTED mark is drawn inside it, tilted like a real stamp. The mark is drawn ONLY when the caller says the
    /// backend confirmed (approved = committed, rejected). It shows what it is given; it decides nothing.
    /// </summary>
    public sealed class WallPlanPaper
    {
        public readonly WallCard card;
        readonly TMP_FontAsset _font;
        readonly Vector2 _size;
        readonly List<TMP_Text> _rows = new List<TMP_Text>();
        readonly TMP_Text _stamp, _pageMark, _zoneCaption;
        readonly Renderer _stampRing;
        readonly Transform _zone;                         // the zone's centre: the stamp spot
        readonly List<Renderer> _frame = new List<Renderer>();
        readonly WallHabitatView _art;
        readonly MuseDesk.Motion.MotionClock _stampClock = new MuseDesk.Motion.MotionClock();
        readonly PaperRect _zoneRect;
        string _stampWord = "";
        StampZoneMode _zoneMode = StampZoneMode.Hidden;
        int _shownRows;
        string _signature = "";

        /// <summary>Viewing distance used for the legibility floor (m). The paper stands about this far from the seated eye.</summary>
        public float readingDistance = 0.45f;

        const float RingW = 0.084f, RingH = 0.018f, RingD = 0.0006f, FrameBar = 0.0014f;

        public float TextWidthMm => PlanPaperGeometry.TextWidthMm(_size.x);
        /// <summary>Text height (mm) with no stamp zone (status and plain pages).</summary>
        public float PageHeightMm => PlanPaperGeometry.PageHeightMm(_size.y, false);
        /// <summary>Text height (mm) above the reserved stamp zone (plan pages): rows never run under the frame.</summary>
        public float PlanPageHeightMm => PlanPaperGeometry.PageHeightMm(_size.y, true);
        public int RowCount => _shownRows;
        public string StampWord => _stampWord;
        public bool Visible => card.Visible;
        public StampZoneMode ZoneMode => _zoneMode;
        public bool ZoneVisible => _zone.gameObject.activeSelf;
        /// <summary>Where a held stamp presses AND where the APPROVED / REJECTED mark is drawn: the centre of the framed zone (world space).</summary>
        public Vector3 StampSpotWorld => _zone.position;
        /// <summary>The zone on the paper plane, in the paper's local metres (tests and drivers).</summary>
        public PaperRect ZoneRect => _zoneRect;

        // ── Step ticks (owner, rc18 worn: "too many buttons ... a check box or an x next to each work item rather than keep 1,
        //    keep 2, so it's just a little check"). Plan pages keep a narrow gutter on the right; each work item's title row
        //    gets a small square poke key there: ✓ = the step is kept, ✗ = skipped (its title is struck through). ──
        public const float CheckGutter = 0.026f, CheckSize = 0.018f;
        readonly Transform _keyTemplate;
        readonly float _keyScale;
        readonly List<WallKey> _checks = new List<WallKey>();
        readonly List<int> _rowItem = new List<int>();          // per shown row: the work item it titles, or -1
        readonly List<float> _rowY = new List<float>();         // per shown row: the centre of its first line (local y)
        float _gutter;
        string _checkSig = "";
        public const string Kept = "✓", Skipped = "✗";

        /// <summary>Text width (mm) for a plan page: the step-tick gutter is left free on the right.</summary>
        public float PlanTextWidthMm => TextWidthMm - CheckGutter * 1000f;
        /// <summary>For drivers: how many step ticks are on show, and the item / label of tick <paramref name="i"/>.</summary>
        public int ChecksShown { get; private set; }
        public readonly List<int> CheckItems = new List<int>();
        public string CheckLabel(int i) { return i >= 0 && i < _checks.Count && _checks[i].label != null ? _checks[i].label.text : null; }
        public Vector3 CheckWorld(int i) { return i >= 0 && i < _checks.Count && _checks[i].root != null ? _checks[i].root.position : Vector3.zero; }

        public WallPlanPaper(Transform parent, string name, Transform keyTemplate, TMP_FontAsset font, WallHabitatView art, Vector2 size, float keyScale)
        {
            _font = font; _size = size; _art = art; _keyTemplate = keyTemplate; _keyScale = keyScale;
            card = new WallCard(parent, name, keyTemplate, font, art, size, keyScale, true);   // rc12: the cream paper sheet look
            _zoneRect = PlanPaperGeometry.StampZone(size.x, size.y);
            Color ink = Workspace.OfficeTokens.Ink, soft = Workspace.OfficeTokens.InkSoft, teal = Workspace.OfficeTokens.Teal;

            // The framed zone (a plate, four bars, a caption), drawn in front of the paper plate, behind the mark.
            _zone = new GameObject("StampZone").transform;
            _zone.SetParent(card.root, false);
            _zone.localPosition = new Vector3(_zoneRect.CenterX, _zoneRect.CenterY, -0.0006f);
            if (art != null)
            {
                Renderer plate = WallUi.Plate(_zone, "ZonePlate", new Vector3(0f, 0f, 0.0004f), _zoneRect.Width, _zoneRect.Height, 0.003f, Color.Lerp(WallCard.PaperSheet, teal, 0.08f), WallUi.PaperMaterial, art);
                _frame.Add(plate);
                float hw = _zoneRect.Width * 0.5f - FrameBar * 0.5f, hh = _zoneRect.Height * 0.5f - FrameBar * 0.5f;
                _frame.Add(art.Box(_zone, "ZoneTop", new Vector3(0f, hh, -0.0003f), new Vector3(_zoneRect.Width, FrameBar, 0.0006f), teal));
                _frame.Add(art.Box(_zone, "ZoneBottom", new Vector3(0f, -hh, -0.0003f), new Vector3(_zoneRect.Width, FrameBar, 0.0006f), teal));
                _frame.Add(art.Box(_zone, "ZoneLeft", new Vector3(-hw, 0f, -0.0003f), new Vector3(FrameBar, _zoneRect.Height, 0.0006f), teal));
                _frame.Add(art.Box(_zone, "ZoneRight", new Vector3(hw, 0f, -0.0003f), new Vector3(FrameBar, _zoneRect.Height, 0.0006f), teal));
            }
            _zoneCaption = WallUi.MakeText(_zone, "ZoneCaption", font, new Vector3(0f, 0f, -0.0008f), new Vector2(_zoneRect.Width * 100f - 0.8f, 1.2f),
                                           TypeScale.CaptionMm, soft, TextAlignmentOptions.Center);
            _zoneCaption.fontStyle = FontStyles.Bold;
            _zoneCaption.overflowMode = TextOverflowModes.Overflow;
            _zoneCaption.textWrappingMode = TextWrappingModes.NoWrap;
            _zone.gameObject.SetActive(false);

            // The mark: a ring and a word, tilted like a real stamp, inside the zone (-Z is the reading side).
            _stamp = WallUi.MakeText(_zone, "Stamp", font, new Vector3(0f, 0f, -0.0012f), new Vector2(8.2f, 1.6f), 9f, Workspace.OfficeTokens.Success, TextAlignmentOptions.Center);
            _stamp.fontStyle = FontStyles.Bold;
            _stamp.overflowMode = TextOverflowModes.Overflow;
            _stamp.textWrappingMode = TextWrappingModes.NoWrap;
            _stamp.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
            _stamp.gameObject.SetActive(false);
            if (art != null)
            {
                _stampRing = art.Box(_zone, "StampRing", new Vector3(0f, 0f, -0.0003f), new Vector3(RingW, RingH, RingD), Workspace.OfficeTokens.Success);
                _stampRing.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
                _stampRing.gameObject.SetActive(false);
            }

            // Page mark: in the gap between the stamp zone (or text) and the key rows, so it never sits on a text row.
            // rc12: beside the stamp zone on the right (the measured key rows reach up to where the old mark sat).
            _pageMark = WallUi.MakeText(card.root, "PageMark", font, new Vector3(size.x * 0.5f - 0.045f, _zoneRect.CenterY, -0.0005f), new Vector2(8f, 0.8f),
                                        TypeScale.MinMmAt(readingDistance), soft, TextAlignmentOptions.Right);
            _pageMark.overflowMode = TextOverflowModes.Overflow;
            _pageMark.textWrappingMode = TextWrappingModes.NoWrap;
        }

        public static Color ToneColor(PlanTone t)
        {
            switch (t)
            {
                case PlanTone.InkSoft: return Workspace.OfficeTokens.InkSoft;
                case PlanTone.Teal: return Workspace.OfficeTokens.Teal;
                case PlanTone.Amber: return Workspace.OfficeTokens.Amber;
                case PlanTone.Success: return Workspace.OfficeTokens.Success;
                case PlanTone.Coral: return Workspace.OfficeTokens.Coral;
                case PlanTone.Lavender: return Workspace.OfficeTokens.Lavender;
                case PlanTone.InfoBlue: return Workspace.OfficeTokens.InfoBlue;
                case PlanTone.Neutral: return Workspace.OfficeTokens.Neutral;
                case PlanTone.RoleResearcher: return Workspace.OfficeTokens.RoleResearcher;
                case PlanTone.RoleDeveloper: return Workspace.OfficeTokens.RoleDeveloper;
                case PlanTone.RoleTester: return Workspace.OfficeTokens.RoleTester;
                case PlanTone.RoleManager: return Workspace.OfficeTokens.RoleManager;
                default: return Workspace.OfficeTokens.Ink;
            }
        }

        /// <summary>Plain card mode (a title and a paragraph): the row area is cleared and there is no stamp zone.</summary>
        public void ShowText(string title, string body, string footer)
        {
            _gutter = 0f;
            SetRows(null, 0, 0);
            SetChecks(null, null);
            SetStampZone(StampZoneMode.Hidden);
            card.SetText(title, body, footer);
        }

        /// <summary>Row mode: one page of laid-out rows. The card's own title/body are blank; the footer is the provenance.
        /// <paramref name="checkGutter"/>: the page was laid out at <see cref="PlanTextWidthMm"/> (plan pages), so the rows sit
        /// left of the step-tick gutter.</summary>
        public void ShowPage(PlanPage page, int pageIndex, int pageCount, string footer, bool checkGutter = false)
        {
            card.SetText("", "", footer);
            float g = checkGutter ? CheckGutter : 0f;
            if (!Mathf.Approximately(g, _gutter)) { _gutter = g; _signature = ""; }
            SetRows(page, pageIndex, pageCount);
            if (!checkGutter) SetChecks(null, null);
        }

        /// <summary>
        /// The step ticks for the work items titled on this page. <paramref name="approved"/>[i] is item i's kept state (null:
        /// no ticks). <paramref name="toggle"/> is called with the item index when its tick is poked. Cheap when nothing changed
        /// (runs every frame while the paper is open).
        /// </summary>
        public void SetChecks(IList<bool> approved, Action<int> toggle)
        {
            bool on = approved != null && toggle != null && card.Visible;
            var sb = new System.Text.StringBuilder(on ? "1" : "0");
            if (on) { sb.Append('|').Append(_signature.GetHashCode()); for (int i = 0; i < approved.Count; i++) sb.Append(approved[i] ? '1' : '0'); }
            string sig = sb.ToString();
            if (sig == _checkSig) return;
            _checkSig = sig;

            int used = 0;
            CheckItems.Clear();
            if (on)
                for (int r = 0; r < _shownRows && r < _rowItem.Count; r++)
                {
                    int item = _rowItem[r];
                    if (item < 0 || item >= approved.Count) continue;
                    if (_checks.Count <= used) _checks.Add(WallUi.MakeKey(_keyTemplate, card.root, "StepTick" + used, Kept, Vector3.zero, _keyScale, null, 1f));
                    WallKey k = _checks[used];
                    if (k.root != null) k.root.localPosition = new Vector3(_size.x * 0.5f - PlanPaperGeometry.SideMargin - CheckSize * 0.5f, _rowY[r], -0.006f);
                    k.Resize(CheckSize, CheckSize);
                    bool kept = approved[item];
                    k.SetLabel(kept ? Kept : Skipped);
                    if (k.label != null)
                    {
                        k.label.color = kept ? Workspace.OfficeTokens.Success : Workspace.OfficeTokens.Coral;
                        k.label.fontSize = k.baseFontSize * 2.3f;              // one glyph: big and clear, it never needs to fit words
                        k.label.fontStyle = FontStyles.Bold;
                    }
                    int idx = item;
                    k.action = () => toggle(idx);
                    k.Show(true);
                    CheckItems.Add(item);
                    // A skipped step reads as skipped on the paper too: struck through, soft ink.
                    TMP_Text t = _rows[r];
                    t.fontStyle = kept ? (t.fontStyle & ~FontStyles.Strikethrough) : (t.fontStyle | FontStyles.Strikethrough);
                    t.color = kept ? Workspace.OfficeTokens.Ink : Workspace.OfficeTokens.InkSoft;
                    used++;
                }
            for (int i = used; i < _checks.Count; i++) _checks[i].Show(false);
            ChecksShown = used;
        }

        void SetRows(PlanPage page, int pageIndex, int pageCount)
        {
            int n = page == null ? 0 : page.Rows.Count;
            // Rebuild only when the content changed (this runs every frame while the paper is open).
            var sb = new System.Text.StringBuilder();
            sb.Append(pageIndex).Append('/').Append(pageCount).Append('|');
            for (int i = 0; i < n; i++) sb.Append((int)page.Rows[i].Kind).Append(':').Append((int)page.Rows[i].Tone).Append(':').Append(page.Rows[i].Text).Append('\n');
            string sig = sb.ToString();
            if (sig == _signature) return;
            _signature = sig;

            while (_rows.Count < n)
            {
                TMP_Text t = WallUi.MakeText(card.root, "Row" + _rows.Count, _font, Vector3.zero, new Vector2(1f, 1f), TypeScale.BodyMm, Workspace.OfficeTokens.Ink, TextAlignmentOptions.TopLeft);
                _rows.Add(t);
            }
            float top = _size.y * 0.5f - PlanPaperGeometry.TopMargin;
            float wCm = (TextWidthMm - _gutter * 1000f) / 10f;
            _rowItem.Clear(); _rowY.Clear();
            for (int i = 0; i < _rows.Count; i++)
            {
                TMP_Text t = _rows[i];
                bool use = i < n;
                if (t.gameObject.activeSelf != use) t.gameObject.SetActive(use);
                if (!use) continue;
                PlanRow r = page.Rows[i];
                float hMm = Mathf.Max(r.HeightMm - PlanReviewLayout.RowGapMm, r.SizeMm * TextFit.LineHeightEm);
                t.rectTransform.sizeDelta = new Vector2(wCm, hMm / 10f);
                t.transform.localPosition = new Vector3(-_gutter * 0.5f, top - (page.TopMm[i] + hMm * 0.5f) / 1000f, -0.0005f);
                _rowItem.Add(r.Kind == PlanRowKind.ItemTitle ? ItemOrdinal(r.Text) - 1 : -1);
                _rowY.Add(top - (page.TopMm[i] + r.SizeMm * TextFit.LineHeightEm * 0.5f) / 1000f);
                t.fontSize = r.SizeMm;
                t.fontStyle = r.Bold ? FontStyles.Bold : FontStyles.Normal;
                t.color = ToneColor(r.Tone);
                t.text = r.Text ?? "";
            }
            _shownRows = n;
            string mark = pageCount > 1 ? "Page " + (pageIndex + 1) + " of " + pageCount : "";
            if (_pageMark.text != mark) _pageMark.text = mark;
        }

        /// <summary>Show or hide the framed "Stamp here" zone and say what it asks. Settled = a mark is (or is about to be) in it.</summary>
        public void SetStampZone(StampZoneMode mode)
        {
            if (mode == _zoneMode) return;
            _zoneMode = mode;
            bool on = mode != StampZoneMode.Hidden;
            if (_zone.gameObject.activeSelf != on) _zone.gameObject.SetActive(on);
            if (!on) return;
            string cap = mode == StampZoneMode.Awaiting ? "Stamp here" : mode == StampZoneMode.Committing ? "Waiting for the board" : "";
            if (_zoneCaption.text != cap) _zoneCaption.text = cap;
            if (_art != null && _frame.Count > 1)
            {
                Color frame = mode == StampZoneMode.Awaiting ? Workspace.OfficeTokens.Teal : Workspace.OfficeTokens.InkSoft;
                for (int i = 1; i < _frame.Count; i++) _art.Tint(_frame[i], frame);
            }
        }

        /// <summary>Show (or clear, with null) the stamp. Call ONLY for a confirmed outcome.</summary>
        public void SetStamp(string word, Color color)
        {
            word = word ?? "";
            if (word == _stampWord) return;
            _stampWord = word;
            bool on = word.Length > 0;
            _stamp.gameObject.SetActive(on);
            if (_stampRing != null) _stampRing.gameObject.SetActive(on);
            if (!on) return;
            SetStampZone(StampZoneMode.Settled);              // the mark lands inside its frame
            _stamp.text = word;
            _stamp.color = color;
            if (_art != null && _frame.Count > 1) for (int i = 1; i < _frame.Count; i++) _art.Tint(_frame[i], Color.Lerp(Workspace.OfficeTokens.Paper, color, 0.7f));
            if (_stampRing != null && _art != null) _art.Tint(_stampRing, Color.Lerp(Workspace.OfficeTokens.Paper, color, 0.28f));
            var p = MuseDesk.Motion.MuseMotionProfile.Active;
            _stampClock.Start(p.panelAppear);
            ApplyStampScale(MuseDesk.Motion.MotionSettings.ReducedMotion ? 1f : 1.25f);
        }

        /// <summary>The stamp lands: a short eased settle from slightly large to its size (none under Reduced Motion).</summary>
        public void Tick(float dt)
        {
            if (!_stampClock.Running) return;
            _stampClock.Step(dt);
            ApplyStampScale(MuseDesk.Motion.MotionSettings.ReducedMotion ? 1f : Mathf.LerpUnclamped(1.25f, 1f, _stampClock.Value));
        }

        void ApplyStampScale(float k)
        {
            _stamp.transform.localScale = Vector3.one * 0.01f * k;
            if (_stampRing != null) _stampRing.transform.localScale = new Vector3(RingW, RingH, RingD) * k;
        }

        /// <summary>"3. Write the tests" -> 3 (the layout numbers item titles from 1); 0 when there is no number.</summary>
        public static int ItemOrdinal(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int n = 0, i = 0;
            while (i < text.Length && char.IsDigit(text[i]) && i < 4) { n = n * 10 + (text[i] - '0'); i++; }
            return i > 0 && i < text.Length && text[i] == '.' ? n : 0;
        }

        public string RowText(int i) { return i >= 0 && i < _shownRows ? _rows[i].text : null; }
        public float RowSizeMm(int i) { return i >= 0 && i < _shownRows ? _rows[i].fontSize : 0f; }
    }
}
