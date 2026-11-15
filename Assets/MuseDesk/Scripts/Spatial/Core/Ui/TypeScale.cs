using System;

namespace MuseDesk.Spatial.Ui
{
    /// <summary>
    /// Physical type scale for world-space TMP text (scale 0.01, so fontSize is roughly millimetres).
    /// Pure: no scene access.
    ///
    /// Legibility rule: the x-height of the text must subtend at least <see cref="MinXHeightDegrees"/>
    /// at the viewing distance. x-height (mm) = 2 * d * tan(theta / 2); font size (mm) = x-height / XHeightRatio.
    /// Inter's x-height is about 0.546 em. 0.30 deg is the hard floor (about 7-8 px on Quest 3 at ~25 PPD);
    /// 0.40 deg is the comfortable target. Provenance text is additionally never below
    /// <see cref="ProvenanceFloorMm"/> (4.2 mm), the project's existing floor.
    /// </summary>
    public static class TypeScale
    {
        public const float TitleMm = 7.0f;
        public const float HeadingMm = 6.0f;
        public const float BodyMm = 4.8f;
        public const float CaptionMm = 4.4f;
        public const float ProvenanceFloorMm = 4.2f;

        /// <summary>Hard-floor x-height angle in degrees.</summary>
        public const float MinXHeightDegrees = 0.30f;
        /// <summary>Comfortable x-height angle in degrees.</summary>
        public const float ComfortXHeightDegrees = 0.40f;
        /// <summary>Inter x-height as a fraction of the em (font size).</summary>
        public const float XHeightRatio = 0.546f;

        /// <summary>The ladder, largest first. Fit logic only picks from these.</summary>
        public static readonly float[] Ladder = { TitleMm, HeadingMm, BodyMm, CaptionMm, ProvenanceFloorMm };

        /// <summary>Font size (mm) whose x-height subtends <paramref name="degrees"/> at the distance.</summary>
        public static float MmForAngle(float distanceMeters, float degrees)
        {
            if (distanceMeters <= 0f || degrees <= 0f) return 0f;
            double rad = degrees * Math.PI / 180.0;
            double xHeightMm = 2.0 * distanceMeters * 1000.0 * Math.Tan(rad * 0.5);
            return (float)(xHeightMm / XHeightRatio);
        }

        /// <summary>Minimum legible font size (mm) at the viewing distance; never below the provenance floor.</summary>
        public static float MinMmAt(float distanceMeters)
        {
            return Math.Max(ProvenanceFloorMm, MmForAngle(distanceMeters, MinXHeightDegrees));
        }

        /// <summary>Comfortable font size (mm) at the viewing distance; never below the provenance floor.</summary>
        public static float ComfortMmAt(float distanceMeters)
        {
            return Math.Max(ProvenanceFloorMm, MmForAngle(distanceMeters, ComfortXHeightDegrees));
        }

        /// <summary>Largest viewing distance (m) at which <paramref name="fontMm"/> still meets the hard floor.</summary>
        public static float MaxDistanceFor(float fontMm)
        {
            if (fontMm <= 0f) return 0f;
            double rad = MinXHeightDegrees * Math.PI / 180.0;
            return (float)(fontMm * XHeightRatio / (2000.0 * Math.Tan(rad * 0.5)));
        }

        /// <summary>True when the size is legible at the distance.</summary>
        public static bool IsLegible(float fontMm, float distanceMeters)
        {
            return fontMm + 1e-4f >= MinMmAt(distanceMeters);
        }
    }
}
