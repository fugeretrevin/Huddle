using UnityEngine;

namespace MuseDesk.Motion
{
    /// <summary>
    /// The one place MuseDesk's motion timing lives (polish spec section 4). Every value is a STARTING hypothesis to be
    /// tuned from headset video, not a law. Loaded from <c>Resources/MuseMotionProfile</c> when that asset exists, else the
    /// built-in defaults below (identical values), so code never depends on the asset being present.
    /// Visual transitions never gate backend execution.
    /// </summary>
    [CreateAssetMenu(fileName = "MuseMotionProfile", menuName = "MuseDesk/Motion Profile")]
    public sealed class MuseMotionProfile : ScriptableObject
    {
        [Header("UI and objects")]
        public MotionToken hover = new MotionToken(0.11f, EaseKind.OutQuad);
        public MotionToken pressDown = new MotionToken(0.09f, EaseKind.OutQuad);
        public MotionToken pressReturn = new MotionToken(0.13f, EaseKind.OutCubic);
        public MotionToken cardPickup = new MotionToken(0.16f, EaseKind.OutCubic);
        public MotionToken cardRelease = new MotionToken(0.28f, EaseKind.InOutCubic);
        public MotionToken panelAppear = new MotionToken(0.26f, EaseKind.OutCubic);
        public MotionToken panelHide = new MotionToken(0.18f, EaseKind.InOutCubic);
        public MotionToken paperUnfold = new MotionToken(0.40f, EaseKind.InOutCubic);
        public MotionToken detailsSheet = new MotionToken(0.33f, EaseKind.InOutCubic);

        [Header("Characters")]
        [Tooltip("Time for a body/head turn to settle (critically damped).")]
        public float turnSmoothTime = 0.16f;
        [Tooltip("Largest heading change (deg) taken while walking; larger turns stop and turn in place first.")]
        public float walkTurnLimit = 50f;
        [Tooltip("Seconds to reach walking speed from rest, and to stop from it.")]
        public float startSeconds = 0.30f;
        public float stopSeconds = 0.28f;
        public MotionToken acknowledge = new MotionToken(0.48f, EaseKind.SoftBack);
        public MotionToken gazeBlend = new MotionToken(0.35f, EaseKind.InOutSine);

        [Header("Elevator")]
        public MotionToken elevatorDoor = new MotionToken(0.45f, EaseKind.InOutCubic);
        [Tooltip("Seconds to reach cruise speed, and to brake from it.")]
        public float elevatorAccelSeconds = 0.6f;
        public float elevatorBrakeSeconds = 0.7f;

        [Header("Portal and workspaces")]
        public MotionToken portalSpawn = new MotionToken(0.65f, EaseKind.InOutSine);
        public MotionToken portalReturn = new MotionToken(0.50f, EaseKind.InOutSine);
        public MotionToken visitLand = new MotionToken(0.35f, EaseKind.OutCubic);
        public MotionToken fadeSwap = new MotionToken(0.30f, EaseKind.InOutSine);
        [Tooltip("Total length of a completion acknowledgement (staggered, one event).")]
        public float completionSeconds = 2.0f;

        static MuseMotionProfile _active;

        /// <summary>The profile in use: the Resources asset if present, else built-in defaults.</summary>
        public static MuseMotionProfile Active
        {
            get
            {
                if (_active != null) return _active;
                _active = Resources.Load<MuseMotionProfile>("MuseMotionProfile");
                if (_active == null)
                {
                    _active = CreateInstance<MuseMotionProfile>();
                    _active.name = "MuseMotionProfile (built-in defaults)";
                    _active.hideFlags = HideFlags.DontSave;
                }
                return _active;
            }
        }

        /// <summary>Test / tooling hook: use this profile (null restores the default lookup).</summary>
        public static void Override(MuseMotionProfile profile) { _active = profile; }

        /// <summary>Acceleration (units/s^2) that reaches <paramref name="speed"/> in <paramref name="seconds"/>. Not
        /// shortened by Reduced Motion: a harder start or stop is the opposite of what that setting is for.</summary>
        public static float AccelFor(float speed, float seconds)
        {
            return speed / Mathf.Max(0.02f, seconds);
        }
    }
}
