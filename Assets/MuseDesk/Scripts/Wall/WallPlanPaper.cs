using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using MuseDesk.Spatial.Ui;

namespace MuseDesk.Wall
{
    /// <summary>
    /// The Manager's plan-review paper: a near-user <see cref="WallCard"/> (plate, eight poke keys, provenance footer)
    /// whose body is drawn row by row from the A6 <see cref="PlanReviewLayout"/> (type scale, legibility floor for the
    /// reading distance, token colours), paged by <see cref="PlanPaperPager"/> so nothing is shrunk or silently dropped.
    /// The stamp mark is drawn ONLY when the caller says the backend confirmed (approved = committed, rejected).
    /// It shows what it is given; it decides nothing.
    /// </summary>
    public sealed class WallPlanPaper
    {
        public readonly WallCard card;
        readonly TMP_FontAsset _font;
        readonly Vector2 _size;
        readonly List<TMP_Text> _rows = new List<TMP_Text>();
        readonly TMP_Text _stamp, _pageMark;
        readonly Renderer _stampRing;
        readonly WallHabitatView _art;
        readonly MuseDesk.Motion.MotionClock _stampClock = new MuseDesk.Motion.MotionClock();
        string _stampWord = "";
        int _shownRows;
        string _signature = "";

        /// <summary>Viewing distance used for the legibility floor (m). The paper stands about this far from the seated eye.</summary>
        public float readingDistance = 0.45f;

        // Layout of the plate (m): rows start under the top edge and stop above the two key rows and the footer.
        const float TopMargin = 0.009f, SideMargin = 0.008f, KeysAndFoot = 0.074f;

        public float TextWidthMm => (_size.x - 2f * SideMargin) * 1000f;
        public float PageHeightMm => (_size.y - TopMargin - KeysAndFoot) * 1000f;
        public int RowCount => _shownRows;
        public string StampWord => _stampWord;
        public bool Visible => card.Visible;
        /// <summary>Where a held stamp presses: the spot the APPROVED / REJECTED mark is later drawn at (world space).</summary>
        public Vector3 StampSpotWorld => _stamp.transform.position;

        public WallPlanPaper(Transform parent, string name, Transform keyTemplate, TMP_FontAsset font, WallHabitatView art, Vector2 size, float keyScale)
        {
            _font = font; _size = size; _art = art;
            card = new WallCard(parent, name, keyTemplate, font, art, size, keyScale);
            // The stamp: a ring and a word, tilted like a real stamp, in front of the text (-Z is the reading side).
            Vector3 at = new Vector3(size.x * 0.22f, -size.y * 0.5f + KeysAndFoot + 0.03f, -0.0012f);
            _stamp = WallUi.MakeText(card.root, "Stamp", font, at, new Vector2(8.5f, 1.6f), 9f, Workspace.OfficeTokens.Success, TextAlignmentOptions.Center);
            _stamp.fontStyle = FontStyles.Bold;
            _stamp.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
            _stamp.gameObject.SetActive(false);
            if (art != null)
            {
                _stampRing = art.Box(card.root, "StampRing", at + new Vector3(0f, 0f, 0.0006f), new Vector3(0.088f, 0.02f, 0.0006f), Workspace.OfficeTokens.Success);
                _stampRing.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
                _stampRing.gameObject.SetActive(false);
            }
            _pageMark = WallUi.MakeText(card.root, "PageMark", font, new Vector3(size.x * 0.5f - 0.03f, size.y * 0.5f - 0.0065f, -0.0005f), new Vector2(5f, 0.8f),
                                        TypeScale.MinMmAt(readingDistance), Workspace.OfficeTokens.InkSoft, TextAlignmentOptions.Right);
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
                default: return Workspace.OfficeTokens.Ink;
            }
        }

        /// <summary>Plain card mode (a title and a paragraph): the row area is cleared.</summary>
        public void ShowText(string title, string body, string footer)
        {
            SetRows(null, 0, 0);
            card.SetText(title, body, footer);
        }

        /// <summary>Row mode: one page of laid-out rows. The card's own title/body are blank; the footer is the provenance.</summary>
        public void ShowPage(PlanPage page, int pageIndex, int pageCount, string footer)
        {
            card.SetText("", "", footer);
            SetRows(page, pageIndex, pageCount);
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
            float top = _size.y * 0.5f - TopMargin;
            float wCm = TextWidthMm / 10f;
            for (int i = 0; i < _rows.Count; i++)
            {
                TMP_Text t = _rows[i];
                bool use = i < n;
                if (t.gameObject.activeSelf != use) t.gameObject.SetActive(use);
                if (!use) continue;
                PlanRow r = page.Rows[i];
                float hMm = Mathf.Max(r.HeightMm - PlanReviewLayout.RowGapMm, r.SizeMm * TextFit.LineHeightEm);
                t.rectTransform.sizeDelta = new Vector2(wCm, hMm / 10f);
                t.transform.localPosition = new Vector3(0f, top - (page.TopMm[i] + hMm * 0.5f) / 1000f, -0.0005f);
                t.fontSize = r.SizeMm;
                t.fontStyle = r.Bold ? FontStyles.Bold : FontStyles.Normal;
                t.color = ToneColor(r.Tone);
                t.text = r.Text ?? "";
            }
            _shownRows = n;
            string mark = pageCount > 1 ? "Page " + (pageIndex + 1) + " of " + pageCount : "";
            if (_pageMark.text != mark) _pageMark.text = mark;
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
            _stamp.text = word;
            _stamp.color = color;
            if (_stampRing != null && _art != null) _art.Tint(_stampRing, Color.Lerp(Workspace.OfficeTokens.Paper, color, 0.28f));
            var p = MuseDesk.Motion.MuseMotionProfile.Active;
            _stampClock.Start(p.panelAppear);
            ApplyStampScale(MuseDesk.Motion.MotionSettings.ReducedMotion ? 1f : 1.35f);
        }

        /// <summary>The stamp lands: a short eased settle from slightly large to its size (none under Reduced Motion).</summary>
        public void Tick(float dt)
        {
            if (!_stampClock.Running) return;
            _stampClock.Step(dt);
            ApplyStampScale(MuseDesk.Motion.MotionSettings.ReducedMotion ? 1f : Mathf.LerpUnclamped(1.35f, 1f, _stampClock.Value));
        }

        void ApplyStampScale(float k)
        {
            _stamp.transform.localScale = Vector3.one * 0.01f * k;
            if (_stampRing != null) _stampRing.transform.localScale = new Vector3(0.088f, 0.02f, 0.0006f) * k;
        }

        public string RowText(int i) { return i >= 0 && i < _shownRows ? _rows[i].text : null; }
        public float RowSizeMm(int i) { return i >= 0 && i < _shownRows ? _rows[i].fontSize : 0f; }
    }
}
