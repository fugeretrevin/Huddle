namespace MuseDesk.Spatial
{
    /// <summary>
    /// The Wall Habitat seen from <see cref="SpatialWorkspaceDirector"/> (Phase 3, additive). The director stays
    /// the only writer of <see cref="SpatialWorkspaceState"/>: it publishes Wall while <see cref="WallActive"/>
    /// and hands Wall requests to the host. With no host wired, or its flag off, nothing changes for
    /// Desk / Floor / Ceiling.
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
    }
}
