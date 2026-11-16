using System;

namespace MuseDesk.Spatial.Onboarding
{
    /// <summary>Every hint the coach can show. Order here is the priority order inside a group.</summary>
    public enum HintId
    {
        None = -1,
        // First-use flow.
        MeetAgents = 0,
        StartWork,
        PaperArrives,
        InspectPaper,
        StampDecision,
        WatchHandoff,
        ReceiptDelivered,
        // Optional follow-ups (after the receipt step is done).
        MoveWorkspace,
        FocusMode,
        CalmMotion,
        // Flag-gated (only when the build flag is on).
        WallPlace,
        ManagerPlan,
        PortalMeet,
        // Recovery hints for common mistakes (can repeat, capped).
        RecoverDroppedPaper,
        RecoverWorkspaceMoved,
        RecoverTrackingLost,
        RecoverNoUndo,
    }

    /// <summary>What a hint points at. The display layer maps this to a transform.</summary>
    public enum HintAnchorKind { Agent, Paper, Stamp, Board, Plaque }

    /// <summary>
    /// Real observed events the coach reacts to. Names map to the code events listed in
    /// docs/Engineering/sprint/A15_ONBOARDING.md; the ones marked "needs event" there have no source yet.
    /// </summary>
    public enum OnboardingEvent
    {
        // Source names live in docs/Engineering/sprint/A15_ONBOARDING.md (this assembly must not name scene-side types).
        SessionReady,          // snapshot loaded
        AgentTouched,          // needs event
        TaskStarted,           // session run-state change to running (scene side)
        DecisionRequested,     // decision requested
        PaperGrabbed,          // needs event
        PaperDetailsOpened,    // needs event
        DecisionSent,          // user pressed stamp/approve/reject (needs event; the session's respond call)
        DecisionResolvedApproved, // decision resolved as approved
        HandoffObserved,       // needs event (agent-changed is the source)
        TaskCompleted,         // task completed
        ReceiptViewed,         // needs event
        WorkspaceMoved,        // WorkspacePlacement.Placed / WorkspaceTransitionMachine.MoveFinished(true)
        FocusEntered,          // needs event (FocusModeController)
        CalmMotionToggled,     // needs event (MotionToggleKey)
        WallPlaced,            // needs event (WallEventBridge.HabitatEventRaised is the source)
        ManagerPlanShown,      // needs event
        ManagerPlanApproved,   // needs event
        AgentSelected,         // needs event
        PortalOpened,          // needs event
        // Mistake signals (recovery hints).
        PaperDropped,          // needs event
        PaperReturned,         // needs event
        WorkspaceMovedByAccident, // needs event
        WorkspaceReturnedToDesk,  // needs event
        TrackingLostWhileHolding, // GrabTrackingWatch (Core) is the source
        TrackingRegained,      // needs event
        UndoAttempted,         // needs event
    }

    /// <summary>Which build features are on. Hints needing a feature are never shown without it.</summary>
    [Flags]
    public enum OnboardingFlags
    {
        None = 0,
        Spatial = 1,     // floorMode or ceilingMode
        CalmToggle = 2,  // reducedMotionToggle
        Wall = 4,        // wallHabitat
        Manager = 8,     // wallManager (needs Wall)
        Portal = 16,     // largeAgentPortal (needs agentSelect and Wall)
    }

    public sealed class HintDef
    {
        public readonly HintId Id;
        public readonly string Text;
        public readonly HintAnchorKind Anchor;
        public readonly OnboardingEvent Trigger;
        public readonly OnboardingEvent[] Completes;
        public readonly OnboardingFlags Requires;
        public readonly HintId After;          // this step must be done first (None = no prerequisite)
        public readonly float DelaySeconds;    // wait after trigger before showing
        public readonly float ShowSeconds;     // auto-hide after this long
        public readonly int MaxShows;          // lifetime cap
        public readonly bool Recovery;
        public readonly bool ShowWhileHolding; // may show while the user's hands are busy with its own target

        public HintDef(HintId id, string text, HintAnchorKind anchor, OnboardingEvent trigger,
            OnboardingEvent[] completes, OnboardingFlags requires, HintId after,
            float delay, float show, int maxShows, bool recovery, bool showWhileHolding)
        {
            Id = id; Text = text; Anchor = anchor; Trigger = trigger; Completes = completes;
            Requires = requires; After = after; DelaySeconds = delay; ShowSeconds = show;
            MaxShows = maxShows; Recovery = recovery; ShowWhileHolding = showWhileHolding;
        }

        public bool CompletesOn(OnboardingEvent e)
        {
            for (int i = 0; i < Completes.Length; i++) if (Completes[i] == e) return true;
            return false;
        }

        public int WordCount()
        {
            return Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }

    /// <summary>Short plain-language copy (at most 9 words each). Copy never claims live AI or a backend result.</summary>
    public static class HintCatalog
    {
        public const int MaxWords = 9;

        static OnboardingEvent[] E(params OnboardingEvent[] e) { return e; }

        static readonly HintDef[] Defs =
        {
            // First-use flow.
            new HintDef(HintId.MeetAgents, "Three agents work here. Poke one to say hi.", HintAnchorKind.Agent,
                OnboardingEvent.SessionReady, E(OnboardingEvent.AgentTouched, OnboardingEvent.TaskStarted),
                OnboardingFlags.None, HintId.None, 2f, 10f, 1, false, false),
            new HintDef(HintId.StartWork, "Poke Start on the dock to begin.", HintAnchorKind.Board,
                OnboardingEvent.SessionReady, E(OnboardingEvent.TaskStarted),
                OnboardingFlags.None, HintId.MeetAgents, 3f, 12f, 2, false, false),
            new HintDef(HintId.PaperArrives, "A paper is ready. Take it by the edge.", HintAnchorKind.Paper,
                OnboardingEvent.DecisionRequested, E(OnboardingEvent.PaperGrabbed),
                OnboardingFlags.None, HintId.None, 1f, 14f, 2, false, false),
            new HintDef(HintId.InspectPaper, "Read the question. Tap More for details.", HintAnchorKind.Paper,
                OnboardingEvent.PaperGrabbed, E(OnboardingEvent.PaperDetailsOpened, OnboardingEvent.DecisionSent),
                OnboardingFlags.None, HintId.PaperArrives, 1f, 8f, 1, false, true),
            new HintDef(HintId.StampDecision, "Press the stamp to approve, or reject.", HintAnchorKind.Stamp,
                OnboardingEvent.PaperGrabbed, E(OnboardingEvent.DecisionSent),
                OnboardingFlags.None, HintId.PaperArrives, 6f, 12f, 2, false, true),
            new HintDef(HintId.WatchHandoff, "Watch the work pass to the next agent.", HintAnchorKind.Agent,
                OnboardingEvent.DecisionResolvedApproved, E(OnboardingEvent.HandoffObserved),
                OnboardingFlags.None, HintId.None, 1f, 8f, 1, false, false),
            new HintDef(HintId.ReceiptDelivered, "Delivered. Your receipt is front right.", HintAnchorKind.Plaque,
                OnboardingEvent.TaskCompleted, E(OnboardingEvent.ReceiptViewed),
                OnboardingFlags.None, HintId.None, 2f, 12f, 1, false, false),
            // Optional follow-ups.
            new HintDef(HintId.MoveWorkspace, "Optional: try Floor or Ceiling on the Spaces dock.", HintAnchorKind.Plaque,
                OnboardingEvent.TaskCompleted, E(OnboardingEvent.WorkspaceMoved),
                OnboardingFlags.Spatial, HintId.ReceiptDelivered, 8f, 10f, 1, false, false),
            new HintDef(HintId.FocusMode, "Optional: Focus quiets everything but decisions.", HintAnchorKind.Board,
                OnboardingEvent.TaskCompleted, E(OnboardingEvent.FocusEntered),
                OnboardingFlags.None, HintId.ReceiptDelivered, 8f, 10f, 1, false, false),
            new HintDef(HintId.CalmMotion, "Optional: Calm motion shortens animations.", HintAnchorKind.Plaque,
                OnboardingEvent.TaskCompleted, E(OnboardingEvent.CalmMotionToggled),
                OnboardingFlags.CalmToggle, HintId.ReceiptDelivered, 8f, 10f, 1, false, false),
            // Flag-gated.
            new HintDef(HintId.WallPlace, "Optional: press Wall on the Spaces dock.", HintAnchorKind.Plaque,
                OnboardingEvent.TaskCompleted, E(OnboardingEvent.WallPlaced),
                OnboardingFlags.Wall, HintId.ReceiptDelivered, 10f, 12f, 1, false, false),
            new HintDef(HintId.ManagerPlan, "Review the plan card, then approve it.", HintAnchorKind.Board,
                OnboardingEvent.ManagerPlanShown, E(OnboardingEvent.ManagerPlanApproved),
                OnboardingFlags.Wall | OnboardingFlags.Manager, HintId.None, 1f, 12f, 1, false, false),
            new HintDef(HintId.PortalMeet, "Press Meet on the dock, then pick an agent.", HintAnchorKind.Agent,
                OnboardingEvent.AgentSelected, E(OnboardingEvent.PortalOpened),
                OnboardingFlags.Wall | OnboardingFlags.Portal, HintId.None, 1f, 10f, 1, false, false),
            // Recovery.
            new HintDef(HintId.RecoverDroppedPaper, "Put it back on the desk tray.", HintAnchorKind.Paper,
                OnboardingEvent.PaperDropped, E(OnboardingEvent.PaperReturned, OnboardingEvent.PaperGrabbed),
                OnboardingFlags.None, HintId.None, 0.5f, 8f, 5, true, false),
            new HintDef(HintId.RecoverWorkspaceMoved, "Moved by accident? Press Desk to return.", HintAnchorKind.Plaque,
                OnboardingEvent.WorkspaceMovedByAccident, E(OnboardingEvent.WorkspaceReturnedToDesk),
                OnboardingFlags.None, HintId.None, 0.5f, 8f, 5, true, false),
            new HintDef(HintId.RecoverTrackingLost, "Hands lost. Show them to continue.", HintAnchorKind.Paper,
                OnboardingEvent.TrackingLostWhileHolding, E(OnboardingEvent.TrackingRegained),
                OnboardingFlags.None, HintId.None, 0.5f, 8f, 5, true, true),
            new HintDef(HintId.RecoverNoUndo, "Undo isn't available. Reject to stop work.", HintAnchorKind.Stamp,
                OnboardingEvent.UndoAttempted, E(OnboardingEvent.DecisionSent),
                OnboardingFlags.None, HintId.None, 0.5f, 8f, 3, true, true),
        };

        public static int Count { get { return Defs.Length; } }

        public static HintDef At(int index) { return Defs[index]; }

        public static HintDef Get(HintId id)
        {
            for (int i = 0; i < Defs.Length; i++) if (Defs[i].Id == id) return Defs[i];
            return null;
        }

        public static int IndexOf(HintId id)
        {
            for (int i = 0; i < Defs.Length; i++) if (Defs[i].Id == id) return i;
            return -1;
        }
    }
}
