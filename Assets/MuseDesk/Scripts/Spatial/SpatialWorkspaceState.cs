using System;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>Where the miniature office is presented. Desk is the original, always-available mode.</summary>
    public enum WorkspaceMode { Desk = 0, Floor = 1, Ceiling = 2 }

    /// <summary>
    /// The one shared, read-mostly description of the office's spatial presentation. It is the
    /// contract between the placement side (which decides and moves where the office is, and is the
    /// only writer) and the reach side (attention cues, bringing a paper to the user), so neither
    /// needs a reference to the other's components. Presentation only: nothing here starts, stops or
    /// changes backend work.
    /// </summary>
    public static class SpatialWorkspaceState
    {
        /// <summary>Current mode. Desk until a spatial mode is confirmed by the user.</summary>
        public static WorkspaceMode Mode { get; private set; } = WorkspaceMode.Desk;

        /// <summary>True when the office is beyond arm's reach of the seated user (Floor mode, or
        /// Ceiling mode while docked). Papers and controls must then be brought to the user.</summary>
        public static bool IsRemote { get; private set; }

        /// <summary>True while the office's root transform is being moved by a transition
        /// (mode change, ceiling pod descending or retracting). Nothing else may move the root.</summary>
        public static bool IsTransitioning { get; private set; }

        /// <summary>The office root (the existing WorkspaceRoot). Null before the director starts.</summary>
        public static Transform WorkspaceRoot { get; private set; }

        /// <summary>A comfortable seated interaction pose in world space (about 0.45 m in front of
        /// and slightly below the eyes, facing the user): where a remotely delivered paper should
        /// come to rest. Valid only when <see cref="HasNearUserPose"/>.</summary>
        public static Pose NearUserPose { get; private set; } = Pose.identity;
        public static bool HasNearUserPose { get; private set; }

        /// <summary>Raised after any of the values above change.</summary>
        public static event Action Changed;

        // ── transition holds: lets the reach side delay a move while the user holds something ──

        static int _holds;
        /// <summary>True while something asked that the office not be moved (for example the user is
        /// holding the decision paper). Transitions wait or refuse while this is set.</summary>
        public static bool TransitionHeld => _holds > 0;
        public static void AcquireTransitionHold() { _holds++; }
        public static void ReleaseTransitionHold() { if (_holds > 0) _holds--; }

        // ── writer API: only the spatial director / transition controller call these ──

        public static void Publish(WorkspaceMode mode, bool isRemote, bool isTransitioning)
        {
            bool changed = mode != Mode || isRemote != IsRemote || isTransitioning != IsTransitioning;
            Mode = mode; IsRemote = isRemote; IsTransitioning = isTransitioning;
            if (changed) Raise();
        }

        public static void PublishRoot(Transform workspaceRoot)
        {
            if (WorkspaceRoot == workspaceRoot) return;
            WorkspaceRoot = workspaceRoot;
            Raise();
        }

        public static void PublishNearUserPose(Pose pose, bool valid)
        {
            NearUserPose = pose; HasNearUserPose = valid;
            Raise();
        }

        /// <summary>Back to Desk defaults (domain reload off, tests, scene reload).</summary>
        public static void ResetForTests()
        {
            Mode = WorkspaceMode.Desk; IsRemote = false; IsTransitioning = false;
            WorkspaceRoot = null; NearUserPose = Pose.identity; HasNearUserPose = false; _holds = 0;
            Changed = null;
        }

        static void Raise()
        {
            var h = Changed;
            if (h == null) return;
            try { h(); } catch (Exception e) { Debug.LogException(e); }
        }
    }
}
