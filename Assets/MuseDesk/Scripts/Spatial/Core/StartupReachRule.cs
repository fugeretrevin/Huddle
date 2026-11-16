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
        /// <summary>Seconds since the near-user rig first took the desk pose; negative while it has none.</summary>
        public float secondsSinceDeskPose;
        /// <summary>The head pose is usable (<see cref="StartupReachRule.HeadUsable"/>), and the headset is worn.</summary>
        public bool headUsable;
        /// <summary>How long the head has been steady (under 45 degrees per second), seconds.</summary>
        public float headSteadySeconds;
        /// <summary>Desk (or Wall, whose desk office stays put) at rest, no preview, nothing held.</summary>
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
