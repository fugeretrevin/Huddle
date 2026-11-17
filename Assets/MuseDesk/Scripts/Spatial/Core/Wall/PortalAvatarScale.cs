using System;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    [Serializable]
    public sealed class PortalScaleSettings
    {
        [Tooltip("Smallest and largest uniform scale the large avatar may take.")]
        public float minScale = 4f;
        public float maxScale = 14f;
        [Tooltip("Head room kept under a known ceiling (m).")]
        public float headRoom = 0.12f;
        [Tooltip("Scale change per second when growing/shrinking (calm, never a pop).")]
        public float scaleRatePerSecond = 6f;
    }

    /// <summary>Result of <see cref="PortalAvatarScale.Compute"/>.</summary>
    public struct PortalAvatarFit
    {
        public float scale;
        /// <summary>World Y of the avatar ROOT so the feet rest on the floor plane.</summary>
        public float rootY;
        public float height;
        public float bodyRadius;
        /// <summary>The scale hit a clamp (ceiling or min/max) rather than the requested height.</summary>
        public bool clamped;
    }

    /// <summary>
    /// Controlled uniform scale of the large portal avatar presentation and where its feet land. The visit
    /// avatar and this one are presentations of the SAME logical agent, so only the presentation transform
    /// scales; nothing else is touched.
    /// </summary>
    public static class PortalAvatarScale
    {
        /// <summary>
        /// nativeHeight: height of the rig at scale 1 (m). nativeFootOffset: root-to-feet distance at scale 1,
        /// positive when the root is above the feet (e.g. hip pivot), 0 for a root at the feet.
        /// </summary>
        public static PortalAvatarFit Compute(float nativeHeight, float nativeFootOffset, float nativeRadius,
                                              float targetHeight, float floorY, float ceilingY, PortalScaleSettings s)
        {
            float nh = Mathf.Max(1e-4f, nativeHeight);
            float want = targetHeight / nh;
            float scale = Mathf.Clamp(want, s.minScale, s.maxScale);
            if (ceilingY > floorY)
            {
                float maxByCeiling = (ceilingY - floorY - s.headRoom) / nh;
                scale = Mathf.Min(scale, Mathf.Max(0f, maxByCeiling));
            }
            return new PortalAvatarFit
            {
                scale = scale,
                rootY = floorY + nativeFootOffset * scale,
                height = nh * scale,
                bodyRadius = nativeRadius * scale,
                clamped = Mathf.Abs(scale - want) > 1e-4f,
            };
        }

        /// <summary>Scale for a given scale step: moves toward target at the configured rate, no overshoot.</summary>
        public static float Step(float current, float target, float dt, PortalScaleSettings s)
        {
            float maxDelta = s.scaleRatePerSecond * Mathf.Max(0f, dt);
            return Mathf.MoveTowards(current, target, maxDelta);
        }

        /// <summary>Root Y that keeps the feet on the floor while the scale changes (no floating mid-grow).</summary>
        public static float RootYFor(float scale, float nativeFootOffset, float floorY) { return floorY + nativeFootOffset * scale; }

        /// <summary>Horizontal gap between the body circle and the user's head (m). Negative = intersecting.</summary>
        public static float GapToUser(Vector3 bodyCentre, float bodyRadius, SeatedUser user)
        {
            Vector3 d = bodyCentre - user.head; d.y = 0f;
            return d.magnitude - bodyRadius;
        }

        /// <summary>True when the body bounds stay outside the user's comfort envelope (a horizontal cylinder around the head).
        /// Wired: every swept sample of <see cref="PortalSpotPlanner.SweepClear"/> calls this with the seated head-and-torso
        /// envelope (<see cref="SeatedUserKeepOut.HeadTorsoEnvelope"/>).</summary>
        public static bool ClearsUserEnvelope(Vector3 bodyCentre, float bodyRadius, SeatedUser user, float envelopeRadius)
        {
            return GapToUser(bodyCentre, bodyRadius, user) >= envelopeRadius;
        }

        /// <summary>
        /// Comfort body radius of the large avatar for its height: a taller avatar (a scale change) is wider, so the planner
        /// and the guard re-check with this, never with a fixed footprint. Never below <paramref name="minRadius"/>.
        /// </summary>
        public static float ComfortRadius(float avatarHeight, float minRadius, float radiusPerHeight)
        {
            return Mathf.Max(Mathf.Max(0f, minRadius), Mathf.Max(0f, avatarHeight) * Mathf.Max(0f, radiusPerHeight));
        }
    }
}
