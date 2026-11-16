using System;

namespace MuseDesk.Spatial.Ui
{
    /// <summary>A rectangle in the paper's local plane (metres): centre and size. Y is up, the paper's centre is the origin.</summary>
    public struct PaperRect
    {
        public float CenterX, CenterY, Width, Height;
        public float Top => CenterY + Height * 0.5f;
        public float Bottom => CenterY - Height * 0.5f;
        public float Left => CenterX - Width * 0.5f;
        public float Right => CenterX + Width * 0.5f;
    }

    /// <summary>
    /// Pure layout of the Manager plan paper (A6 follow-up): where the text column ends, where the framed "Stamp here"
    /// zone sits, where the key rows are, and how wide a key can be so its label fits. The runtime paper
    /// (Wall/WallPlanPaper, Wall/WallUi) reads these numbers, so they can be tested without a scene.
    ///
    /// Top to bottom: top margin, text rows, [stamp zone], page mark, two key rows, provenance footer.
    /// </summary>
    public static class PlanPaperGeometry
    {
        public const float TopMargin = 0.009f, SideMargin = 0.008f;
        /// <summary>Everything under the text column except the stamp zone: page mark gap, two key rows, footer.</summary>
        public const float KeysAndFoot = 0.074f;
        public const float StampZoneHeight = 0.034f, StampZoneGap = 0.004f, StampZoneMaxWidth = 0.104f;
        /// <summary>Height of a key row pair: first row centre above the second.</summary>
        public const float KeyRowPitch = 0.03f, KeyRowBottomOffset = 0.022f, KeyHalfHeight = 0.0115f;
        public const int KeyColumns = 4;
        /// <summary>Widest a key may grow (a multiple of the template key), so the poke face stays a sensible target.</summary>
        public const float MaxKeyWidthScale = 2.4f;

        /// <summary>The framed stamp zone. Its centre is where a held stamp presses and where the APPROVED / REJECTED mark is drawn.</summary>
        public static PaperRect StampZone(float paperW, float paperH)
        {
            float w = Math.Min(StampZoneMaxWidth, Math.Max(0.02f, paperW - 2f * SideMargin));
            float bottom = -paperH * 0.5f + KeysAndFoot;
            return new PaperRect { CenterX = 0f, CenterY = bottom + StampZoneHeight * 0.5f, Width = w, Height = StampZoneHeight };
        }

        /// <summary>Height (mm) the text rows may use. With the stamp zone reserved the rows stop above the frame.</summary>
        public static float PageHeightMm(float paperH, bool reserveStampZone)
        {
            float h = paperH - TopMargin - KeysAndFoot - (reserveStampZone ? StampZoneHeight + StampZoneGap : 0f);
            return Math.Max(0f, h) * 1000f;
        }

        /// <summary>Text column width (mm).</summary>
        public static float TextWidthMm(float paperW) { return (paperW - 2f * SideMargin) * 1000f; }

        /// <summary>Y of the page mark ("Page 1 of 2"): in the gap between the stamp zone (or text) and the key rows.</summary>
        public static float PageMarkY(float paperH)
        {
            float keysTop = KeyRowCenterY(paperH, 0) + KeyHalfHeight;
            float zoneBottom = -paperH * 0.5f + KeysAndFoot;
            return (keysTop + zoneBottom) * 0.5f;
        }

        /// <summary>Local Y of a key row (0 = upper row, 1 = lower row).</summary>
        public static float KeyRowCenterY(float paperH, int row)
        {
            return -paperH * 0.5f + KeyRowBottomOffset + (1 - row) * KeyRowPitch;
        }

        /// <summary>Local X of a key column.</summary>
        public static float KeyColumnX(float paperW, int col)
        {
            float pitch = paperW / KeyColumns;
            return -paperW * 0.5f + pitch * (col + 0.5f);
        }

        /// <summary>
        /// How much wider than the template key a key should be: it fills most of its column but leaves a gap, never narrower
        /// than the template and never wider than <see cref="MaxKeyWidthScale"/>.
        /// </summary>
        public static float KeyWidthScale(float paperW, float keyScale)
        {
            if (keyScale <= 1e-5f) return 1f;
            float s = paperW / KeyColumns * 0.92f / keyScale;
            return Math.Max(1f, Math.Min(MaxKeyWidthScale, s));
        }

        /// <summary>Width (m) of a key of this scale and width factor, assuming a template key one unit wide.</summary>
        public static float KeyWidth(float keyScale, float widthScale) { return keyScale * widthScale; }
    }

    /// <summary>
    /// Fits a key label to its key without an ellipsis: the font shrinks (never below <paramref name="minScale"/>) until the
    /// text fits. Pure. The runtime measures the real text width with TextMeshPro and asks this for the factor.
    /// </summary>
    public static class KeyLabelFit
    {
        public const float DefaultMinScale = 0.62f;
        public const float Margin = 0.94f;

        /// <summary>Font scale in [minScale, 1] so that text of width <paramref name="textWidth"/> fits <paramref name="availableWidth"/>.</summary>
        public static float FontScale(float textWidth, float availableWidth, float minScale = DefaultMinScale)
        {
            if (textWidth <= 1e-6f || availableWidth <= 1e-6f) return 1f;
            float room = availableWidth * Margin;
            if (textWidth <= room) return 1f;
            return Math.Max(minScale, room / textWidth);
        }

        /// <summary>True when the text fits at <paramref name="scale"/> (so no ellipsis or overflow is needed).</summary>
        public static bool Fits(float textWidth, float availableWidth, float scale)
        {
            return textWidth * scale <= availableWidth + 1e-5f;
        }

        /// <summary>Rough estimate when no font is at hand (editor tests, plans): characters x average Inter width x size.</summary>
        public static float EstimateWidthMm(string text, float fontMm, FontWeightKind weight = FontWeightKind.Medium)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            return text.Length * fontMm * TextFit.AvgCharWidthEm(weight);
        }
    }
}
