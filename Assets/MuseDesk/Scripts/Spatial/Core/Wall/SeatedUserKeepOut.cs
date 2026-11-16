using System;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>What a keep-out volume protects. Each kind has its own margin and its own words for the user.</summary>
    public enum PortalKeepOutKind
    {
        Screen = 0,
        ScreenSightline = 1,
        DeskFront = 2,
        Door = 3,
        Controls = 4,
        UserHands = 5,
        WorkArea = 6,
    }

    /// <summary>
    /// An upright keep-out volume: an oriented rectangle in world XZ with a vertical range and its own margin.
    /// Infinite vertical bounds are allowed (an unknown monitor could be at any height).
    /// </summary>
    public struct PortalKeepOutVolume
    {
        public OrientedRect footprint;
        public float minY, maxY;
        public float margin;
        public PortalKeepOutKind kind;

        public static PortalKeepOutVolume Make(OrientedRect footprint, float minY, float maxY, float margin, PortalKeepOutKind kind)
        {
            return new PortalKeepOutVolume { footprint = footprint, minY = minY, maxY = maxY, margin = margin, kind = kind };
        }

        public string Reason
        {
            get
            {
                switch (kind)
                {
                    case PortalKeepOutKind.Screen: return "Too close to a screen";
                    case PortalKeepOutKind.ScreenSightline: return "Between you and a screen";
                    case PortalKeepOutKind.DeskFront: return "Over your desk's front zone";
                    case PortalKeepOutKind.Door: return "Blocks a door";
                    case PortalKeepOutKind.Controls: return "Blocks your controls";
                    case PortalKeepOutKind.UserHands: return "Too close to you: within reach of your hands";
                    case PortalKeepOutKind.WorkArea: return "In front of you: your desk and monitors";
                    default: return "Over a keep-out zone";
                }
            }
        }
    }

    /// <summary>
    /// The seated user's comfort keep-out and the swept-body model of the large portal avatar. All distances
    /// are in metres and are margins the avatar's BODY (not its centre) must keep. Conservative on purpose: a
    /// large figure that is merely close is uncomfortable, so the planner prefers "not enough space" to a squeeze.
    /// </summary>
    [Serializable]
    public sealed class SeatedComfortSettings
    {
        [Header("Head and torso (a column around the head's vertical axis)")]
        [Tooltip("Head, shoulders and a lean, horizontally from the head axis.")]
        public float headTorsoRadius = 0.45f;
        [Tooltip("Extra distance the avatar's body keeps from that column.")]
        public float headTorsoMargin = 0.20f;
        [Tooltip("The user's zones reach this far above the head (a raised hand, standing up a little).")]
        public float aboveHead = 0.30f;

        [Header("Hand comfort (a box in the user's seated frame)")]
        [Tooltip("Sideways reach of either hand from the head axis.")]
        public float handReachSide = 0.75f;
        [Tooltip("Forward reach of the hands from the head (to the desk).")]
        public float handReachForward = 0.75f;
        [Tooltip("Backward reach of the hands from the head (elbows, the chair arms).")]
        public float handReachBack = 0.15f;
        [Tooltip("The hand zone starts this far below the head (the lap).")]
        public float handBelowHead = 0.85f;
        public float handMargin = 0.10f;

        [Header("Default work area: always applied, monitors are never assumed to be known")]
        [Tooltip("How far in front of the head the desk, monitors and keyboard may be.")]
        public float workAreaForward = 1.30f;
        [Tooltip("Half width of that area.")]
        public float workAreaHalfWidth = 0.75f;
        public float workAreaMargin = 0.10f;

        [Header("Labelled room volumes (MRUK SCREEN, TABLE, DOOR_FRAME)")]
        [Tooltip("A TABLE whose footprint is within this distance of the head is the user's desk: its user-facing edge gets a front zone.")]
        public float deskNearUser = 1.2f;
        [Tooltip("Depth of the desk front zone, from the desk edge toward the user.")]
        public float deskFrontDepth = 0.60f;
        public float deskFrontMargin = 0.10f;
        [Tooltip("Half width added around the line of sight from the head to a scanned screen.")]
        public float sightlinePad = 0.10f;
        public float sightlineMargin = 0.10f;
        [Tooltip("Depth of the keep-out in front of a door (its swing and the way through).")]
        public float doorSwing = 0.90f;
        [Tooltip("Added to the door width on each side.")]
        public float doorSidePad = 0.10f;
        public float doorMargin = 0.10f;
        [Tooltip("Radius kept around the portal's controls (the visit card) and half width of the line of sight to them.")]
        public float controlsRadius = 0.25f;
        public float controlsSightlineHalfWidth = 0.12f;

        [Header("View")]
        [Tooltip("The avatar must be visible without turning further than this from the seated forward (deg).")]
        public float maxViewDeg = 110f;

        [Header("Avatar body (swept while it appears)")]
        [Tooltip("Body radius per metre of avatar height (scale changes widen the body). Never below the planner's bodyRadius.")]
        public float radiusPerHeight = 0.25f;
        [Tooltip("The avatar faces the user: arms and a lean reach this far toward the user per metre of height.")]
        public float reachPerHeight = 0.085f;
        [Tooltip("The appearance sweep starts at this fraction of full size (the view grows from 0.05).")]
        public float emergeGrow = 0.05f;
        [Tooltip("Largest boundary travel between two swept samples (m). Each sample is padded by half the actual spacing.")]
        public float sweepStep = 0.05f;
    }

    /// <summary>The user's comfort volumes, built from the seated frame (head position + seated forward). Pure, no allocation.</summary>
    public static class SeatedUserKeepOut
    {
        static Vector2 XZ(Vector3 v) { return new Vector2(v.x, v.z); }

        static float YawOf(Vector3 forward) { return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg; }

        /// <summary>Head-and-torso keep-out radius the avatar BODY must stay outside (horizontal, from the head axis).</summary>
        public static float HeadTorsoEnvelope(SeatedComfortSettings c) { return Mathf.Max(0f, c.headTorsoRadius) + Mathf.Max(0f, c.headTorsoMargin); }

        /// <summary>Where the seated user's hands work: a box in the user's frame, lap to above the head.</summary>
        public static PortalKeepOutVolume HandZone(SeatedUser u, SeatedComfortSettings c)
        {
            float fwd = Mathf.Max(0f, c.handReachForward), back = Mathf.Max(0f, c.handReachBack);
            Vector2 center = XZ(u.head) + XZ(u.forward) * ((fwd - back) * 0.5f);
            var rect = OrientedRect.FromYaw(center, new Vector2(Mathf.Max(0f, c.handReachSide), (fwd + back) * 0.5f), YawOf(u.forward));
            return PortalKeepOutVolume.Make(rect, u.head.y - Mathf.Max(0f, c.handBelowHead), u.head.y + Mathf.Max(0f, c.aboveHead), c.handMargin, PortalKeepOutKind.UserHands);
        }

        /// <summary>
        /// The default forward work area (desk, monitors, keyboard), applied whether or not the room scan knows a screen:
        /// passthrough does not know every monitor, so the planner never assumes the space in front is free.
        /// </summary>
        public static PortalKeepOutVolume WorkArea(SeatedUser u, SeatedComfortSettings c)
        {
            float f = Mathf.Max(0f, c.workAreaForward);
            Vector2 center = XZ(u.head) + XZ(u.forward) * (f * 0.5f);
            var rect = OrientedRect.FromYaw(center, new Vector2(Mathf.Max(0f, c.workAreaHalfWidth), f * 0.5f), YawOf(u.forward));
            return PortalKeepOutVolume.Make(rect, float.NegativeInfinity, float.PositiveInfinity, c.workAreaMargin, PortalKeepOutKind.WorkArea);
        }

        /// <summary>A rectangle along the segment a-b (XZ) with the given half width; a degenerate segment becomes a square.</summary>
        public static OrientedRect RectBetween(Vector2 a, Vector2 b, float halfWidth)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            Vector2 axisY = len < 1e-5f ? new Vector2(0f, 1f) : d / len;
            return new OrientedRect
            {
                center = (a + b) * 0.5f,
                half = new Vector2(Mathf.Max(0f, halfWidth), Mathf.Max(halfWidth, len * 0.5f)),
                axisY = axisY,
                axisX = new Vector2(axisY.y, -axisY.x),
            };
        }

        /// <summary>Body radius of the large avatar at full size: grows with its height, never below <paramref name="minRadius"/>.</summary>
        public static float BodyRadius(float avatarHeight, float minRadius, SeatedComfortSettings c)
        {
            return PortalAvatarScale.ComfortRadius(avatarHeight, minRadius, c.radiusPerHeight);
        }

        /// <summary>How far arms and lean reach toward the user at full size.</summary>
        public static float ReachTowardUser(float avatarHeight, SeatedComfortSettings c)
        {
            return Mathf.Max(0f, avatarHeight) * Mathf.Max(0f, c.reachPerHeight);
        }
    }
}
