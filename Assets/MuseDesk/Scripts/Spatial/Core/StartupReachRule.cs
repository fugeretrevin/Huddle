using UnityEngine;

namespace MuseDesk.Spatial
{
    public enum StartupReachAction
    {
        /// <summary>Not yet: no desk pose, no usable head, head not steady, or something is in hand / in a preview.</summary>
        Wait,
        /// <summary>The near-user panels are within reach: leave them.</summary>
        Keep,
        /// <summary>Put the near-user rig in front of the head now.</summary>
        Reseat,
        /// <summary>The start-up window is over (or the user took over): never act again this session.</summary>
        Done,
    }

    /// <summary>The start-up reach check, as plain values (NearUserKit fills it once per frame during the window).</summary>
    public struct StartupReachInput
    {
        /// <summary>Seconds since the near-user rig first took the desk pose (or was first seated from the head because the
        /// office left the desk before any desk pose: <see cref="StartupReachRule.DecideAwaySeat"/>); negative while neither happened.</summary>
        public float secondsSinceDeskPose;
        /// <summary>The head pose is usable (<see cref="StartupReachRule.HeadUsable"/>), and the headset is worn.</summary>
        public bool headUsable;
        /// <summary>How long the head has been steady (under 45 degrees per second), seconds.</summary>
        public float headSteadySeconds;
        /// <summary>At rest in any mode (<see cref="StartupReachRule.Placeable"/>): no move, no preview, nothing held.</summary>
        public bool placeable;
        /// <summary>The user dragged a panel this session: their layout wins from then on.</summary>
        public bool userMovedPanel;
        /// <summary>The rig took its first desk pose while the head pose was not usable (the pose it was placed from is stale).</summary>
        public bool firstPoseWithoutHead;
        public int reseats;
        public float secondsSinceLastReseat;
        /// <summary>Farthest and nearest panel pivot from the head, metres; worst negative = nothing measured.</summary>
        public float worstDistance, nearestDistance;
    }

    public enum AwaySeatAction
    {
        /// <summary>Nothing to do: at the desk, or the rig already stands at a placed desk pose, or it was seated already.</summary>
        None,
        /// <summary>The panels need a seat but not now: tracking loss, or something is in hand.</summary>
        Wait,
        /// <summary>Stand the near-user rig in front of the head now (panels only; the office never moves).</summary>
        Seat,
    }

    /// <summary>The "left the desk before the first desk pose" check, as plain values (NearUserKit fills it).</summary>
    public struct AwaySeatInput
    {
        /// <summary>The office is not resting at the desk (Floor, Ceiling or Wall, or on its way there): the rig does not follow the root.</summary>
        public bool away;
        /// <summary>The rig has stood at a PLACED desk pose this session (the desk placement had finished).</summary>
        public bool hasPlacedDeskPose;
        /// <summary>The rig was already seated from the head since the office left the desk.</summary>
        public bool seated;
        /// <summary>The head pose is usable (<see cref="StartupReachRule.HeadUsable"/>) and the headset is worn.</summary>
        public bool headUsable;
        /// <summary>Something is in hand, or a hand holds a transition.</summary>
        public bool held;
        /// <summary>The user dragged a panel this session: their layout wins.</summary>
        public bool userMovedPanel;
    }

    /// <summary>
    /// Start-up reach (owner, rc8: "the Start/board dock was multiple steps away when I started the app"). The office and
    /// every near-user panel are placed once from the head pose of the first seconds, and the panels are glued to that pose.
    /// This rule decides, during a short window after the first desk pose, whether the panel rig must be re-seated in front
    /// of the head: when the first pose was taken without a usable head, or when a panel is beyond arm's reach (or inside
    /// the head). It never acts on tracking loss (an unusable or moving head only waits), never after the user moved a
    /// panel, never during a preview or a move, and at most <see cref="MaxReseats"/> times.
    /// </summary>
    public static class StartupReachRule
    {
        /// <summary>No near-user panel is left farther than this from the head at start, metres.</summary>
        public const float MaxReach = 0.80f;
        /// <summary>Nor closer than this (inside the head), metres.</summary>
        public const float MinReach = 0.20f;
        public const float WindowSeconds = 45f;
        public const float SteadySeconds = 0.35f;
        public const float MinGapSeconds = 1.0f;
        public const int MaxReseats = 3;
        /// <summary>Steadiness threshold of the head's turn rate, degrees per second (as WorkspacePlacement).</summary>
        public const float SteadyDegPerSec = 45f;

        public static StartupReachAction Decide(StartupReachInput i)
        {
            if (i.userMovedPanel || i.reseats >= MaxReseats) return StartupReachAction.Done;
            if (i.secondsSinceDeskPose < 0f) return StartupReachAction.Wait;
            if (i.secondsSinceDeskPose > WindowSeconds) return StartupReachAction.Done;
            // Fail-closed: nothing moves on tracking loss, a moving head, a preview, a move in flight or a held object.
            if (!i.placeable || !i.headUsable || i.headSteadySeconds < SteadySeconds) return StartupReachAction.Wait;
            if (i.reseats > 0 && i.secondsSinceLastReseat < MinGapSeconds) return StartupReachAction.Wait;
            if (i.firstPoseWithoutHead && i.reseats == 0) return StartupReachAction.Reseat;
            if (i.worstDistance < 0f) return StartupReachAction.Keep;
            if (i.worstDistance > MaxReach || i.nearestDistance < MinReach) return StartupReachAction.Reseat;
            return StartupReachAction.Keep;
        }

        /// <summary>
        /// Stranded panels (owner, rc9: "the panel for placing the wall or choosing floor or ceiling were entirely gone").
        /// The near-user rig only learns the desk pose while the office rests at a PLACED desk. When the office leaves
        /// the desk before that ever happened (a restore at launch, a voice request, any mode), the rig would be left at
        /// the office root's pre-placement pose, out of sight. This decides when to seat the rig in front of the head
        /// instead (<see cref="ReseatPose"/>). Panels only: the office is never moved. Fail-closed: it waits on tracking
        /// loss and while something is in hand, never acts twice for one departure, and never after the user moved a panel.
        /// </summary>
        public static AwaySeatAction DecideAwaySeat(AwaySeatInput i)
        {
            if (!i.away || i.hasPlacedDeskPose || i.seated || i.userMovedPanel) return AwaySeatAction.None;
            if (!i.headUsable || i.held) return AwaySeatAction.Wait;
            return AwaySeatAction.Seat;
        }

        /// <summary>
        /// May a start-up re-seat happen in this state? Any mode (Desk, Floor, Ceiling, Wall: the rig is either on the desk
        /// pose or standing still while the office is away), but never during a move, a preview (the director's or the
        /// wall's), or with something in hand.
        /// </summary>
        public static bool Placeable(bool transitioning, bool held, bool directorPreviewing, bool wallPreviewing)
        {
            return !transitioning && !held && !directorPreviewing && !wallPreviewing;
        }

        /// <summary>
        /// Keep a re-seat as a correction relative to the office root (so the rig follows the desk afterwards)? Only when
        /// the root stands at its desk pose: at the desk, or in Wall mode (the desk office stays put, hidden) once a placed
        /// desk pose exists. In Floor / Ceiling the root is elsewhere, so the rig is only stood in front of the head.
        /// </summary>
        public static bool KeepAsDeskCorrection(bool atDesk, bool inWall, bool hasPlacedDeskPose)
        {
            return atDesk || (inWall && hasPlacedDeskPose);
        }

        /// <summary>A head pose that can be placed from: finite, not at the tracking origin (no pose yet), looking
        /// within 40 degrees of level (not at the floor or the ceiling). Same test as WorkspacePlacement.</summary>
        public static bool HeadUsable(Vector3 position, Vector3 forward)
        {
            if (!Finite(position) || !Finite(forward)) return false;
            if (position.sqrMagnitude <= 0.0004f) return false;
            Vector3 flat = forward; flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) return false;
            return Vector3.Angle(forward, flat) < 40f;
        }

        static bool Finite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }

        /// <summary>
        /// Where the desk pose would be if it were placed from this head now (WorkspacePlacement.OffDeskPosition): to the
        /// side (+ = the user's right), ahead and below the eyes, upright, +Z pointing at the user (the scene's facing
        /// convention). The near-user panels are laid out around that pose, so this brings them to their designed reach.
        /// </summary>
        public static void ReseatPose(Vector3 head, Vector3 headForward, float side, float depth, float drop,
                                      out Vector3 position, out Quaternion rotation)
        {
            Vector3 fwd = headForward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            position = head + right * side + fwd * depth + Vector3.down * drop;
            Vector3 toUser = head - position; toUser.y = 0f;
            rotation = toUser.sqrMagnitude < 1e-6f ? Quaternion.LookRotation(-fwd, Vector3.up) : Quaternion.LookRotation(toUser.normalized, Vector3.up);
        }

        /// <summary>A world pose expressed relative to a root pose (rigid, no scale).</summary>
        public static void ToRootLocal(Vector3 rootPosition, Quaternion rootRotation, Vector3 position, Quaternion rotation,
                                       out Vector3 localPosition, out Quaternion localRotation)
        {
            Quaternion inv = Quaternion.Inverse(rootRotation);
            localPosition = inv * (position - rootPosition);
            localRotation = inv * rotation;
        }

        /// <summary>The inverse of <see cref="ToRootLocal"/>.</summary>
        public static void FromRootLocal(Vector3 rootPosition, Quaternion rootRotation, Vector3 localPosition, Quaternion localRotation,
                                         out Vector3 position, out Quaternion rotation)
        {
            position = rootPosition + rootRotation * localPosition;
            rotation = rootRotation * localRotation;
        }
    }
}
