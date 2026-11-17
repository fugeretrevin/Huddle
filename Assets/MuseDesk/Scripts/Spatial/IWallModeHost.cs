using System;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The Wall Habitat seen from <see cref="SpatialWorkspaceDirector"/> (Phase 3, additive). The director stays
    /// the only writer of <see cref="SpatialWorkspaceState"/>: it publishes Wall while <see cref="WallActive"/>
    /// and hands Wall requests to the host. With no host wired, or its flag off, nothing changes for
    /// Desk / Floor / Ceiling.
    ///
    /// The second half (UnifiedModeDockV1) is what the ONE Spaces dock (<see cref="ModeConsole"/>) needs to show the
    /// Wall's keys itself instead of a second floating dock: the dock's state, its words, the presses, and a
    /// change notification. Every press is a request the host may refuse (with words in <see cref="WallStatus"/>).
    /// </summary>
    public interface IWallModeHost
    {
        /// <summary>The wall flag is on in this build.</summary>
        bool WallEnabled { get; }
        /// <summary>The habitat is the presented office (placed and confirmed). Mode is then Wall.</summary>
        bool WallActive { get; }
        /// <summary>A wall placement preview is open (the office has not moved yet).</summary>
        bool WallPreviewing { get; }
        /// <summary>Open the wall preview (from Desk only, nothing moving). False with a reason shown to the user.</summary>
        bool RequestWall(bool deskIdle);
        /// <summary>Leave Wall mode (or its preview) safely: visits and portals return, agents re-home, the desk office shows again.</summary>
        void ExitWall(string why);

        // ── the unified Spaces dock ──

        /// <summary>Raised when anything the dock shows for the wall changed (state, the spot's fit, layout, words).</summary>
        event Action DockChanged;
        /// <summary>Set true by the director when the Spaces dock carries the Wall keys: the host then builds and shows no dock of its own.</summary>
        bool ExternalDock { get; set; }
        /// <summary>The previewed wall spot fits: Place is armed.</summary>
        bool WallCanPlace { get; }
        /// <summary>The preview shows the Full layout (the layout key offers Compact).</summary>
        bool WallLayoutFull { get; }
        /// <summary>Select Agents is armed (the select key reads Done).</summary>
        bool WallSelecting { get; }
        bool WallSelectEnabled { get; }
        bool WallManagerEnabled { get; }
        /// <summary>The wall's one-line status, in words; empty when there is nothing to say.</summary>
        string WallStatus { get; }
        /// <summary>Place the habitat at the previewed spot (refused unless it fits; nothing else moves).</summary>
        void WallPlace();
        void WallNextSpot();
        void WallToggleLayout();
        /// <summary>Leave the preview as it was (entering: back to Desk; moving the habitat: it stays where it was).</summary>
        void WallCancel();
        /// <summary>Open a preview to move the placed habitat (refused while an agent visits or rides).</summary>
        void WallMove();
        void WallSelect();
        void WallManager();

        // ── Meet (MeetKeyV1): one poke to the large-agent portal ──

        /// <summary>The large-agent portal flag is on: the dock offers Meet while the office is on the wall.</summary>
        bool WallMeetEnabled { get; }
        /// <summary>Meet: open (or close) the agent chooser; with an agent on the desk perch, that agent steps out. A request the
        /// host may refuse (words in <see cref="WallStatus"/>). Only ever opens a preview the user must confirm.</summary>
        void WallMeet();
    }
}
