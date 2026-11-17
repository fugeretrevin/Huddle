using MuseDesk.Spatial.Ui;

namespace MuseDesk.Spatial
{
    /// <summary>Everything the unified Spaces dock needs to know, as plain values.</summary>
    public struct SpacesDockInput
    {
        public bool floorEnabled, ceilingEnabled, wallEnabled;
        /// <summary>The director's mode (Desk, Floor or Ceiling). Wall comes from <see cref="wallActive"/>.</summary>
        public PlacementMode mode;
        /// <summary>A Floor or Ceiling preview is open (the director's phase).</summary>
        public bool previewing;
        public bool canCycle, transitioning;
        public CeilingPodState podState;
        public bool podWaiting;
        /// <summary>The open preview is a Floor preview, and its spot and way are clear (Place may be shown).</summary>
        public bool floorPreview, placeReady;
        /// <summary>The wall habitat is the presented office (placed and confirmed).</summary>
        public bool wallActive;
        /// <summary>A wall preview is open: entering Wall, or moving the habitat (then wallActive is true too).</summary>
        public bool wallPreviewing;
        /// <summary>The wall spot shown in the preview fits (Place is armed).</summary>
        public bool wallCanPlace;
        /// <summary>The preview shows the Full layout (the layout key then offers Compact).</summary>
        public bool wallLayoutFull;
        public bool wallSelectEnabled, wallManagerEnabled, wallSelecting;
        /// <summary>The large-agent portal flag is on: Meet is offered while the office is on the wall (MeetKeyV1).</summary>
        public bool wallMeetEnabled;
    }

    /// <summary>Which keys of the Spaces dock apply, how the four place keys look, and the words on the shared keys.</summary>
    public struct SpacesDockKeys
    {
        /// <summary>0 Desk, 1 Floor, 2 Ceiling, 3 Wall: the place key that is lit.</summary>
        public int active;
        public bool deskDisabled, floorDisabled, ceilingDisabled, wallDisabled;
        // Context row. The Next / Place / Cancel keys are shared by every preview (Floor, Ceiling, Wall).
        public bool next, confirm, cancel, stop, pod, bring, status, layout, move, select, manager, meet;
        /// <summary>Place is on show but not armed (a wall spot that does not fit): it looks disabled and cannot be poked.</summary>
        public bool confirmDisabled;
        /// <summary>The shared preview keys act on the wall preview, not on the director's.</summary>
        public bool wallContext;
        public string podWords, confirmWords, nextWords, layoutWords, selectWords;
        /// <summary>Why the greyed Wall key does nothing (Floor or Ceiling at rest); null when there is nothing to say.</summary>
        public string wallNote;
    }

    /// <summary>
    /// The Spaces dock's state table (ONE place selector: Desk | Floor | Ceiling | Wall), free of scene access.
    ///
    ///   always ................... Desk | Floor | Ceiling | Wall, the current place lit. A place whose flag is off is
    ///                              disabled; every place is disabled while the office moves. Wall is offered from the
    ///                              desk only (leaving Floor or Ceiling for the wall goes through Desk: fail-closed).
    ///   Floor / Ceiling preview .. Next spot (when there is a choice), Place (Floor: only while the spot and its way are
    ///                              clear), Cancel
    ///   Wall preview ............. Next spot, Place (shown; armed only while the spot fits), Full / Compact, Cancel
    ///   the office is moving ..... Stop
    ///   Floor, at rest ........... Bring, Status
    ///   Ceiling, at rest ......... Lower / Raise, Status
    ///   Wall, at rest ............ Move, Select (flag), Manager (flag), Meet (portal flag; in the Layout key's slot)
    /// Placement is armed ONLY while a preview is open: no Place key exists outside one.
    /// </summary>
    public static class SpacesDockLogic
    {
        public const string Desk = "Desk", Floor = "Floor", Ceiling = "Ceiling", Wall = "Wall", SendBack = "Send back";
        public const string NextSpot = "Next spot", Place = "Place", Cancel = "Cancel", Stop = "Stop";
        public const string Bring = "Bring", Status = "Status", Full = "Full", Compact = "Compact";
        public const string WallFromDeskNote = "Wall: go back to Desk first";
        /// <summary>From Floor / Ceiling: Wall is offered and says what it will do (the office comes home, then the wall preview).</summary>
        public const string WallViaDeskNote = "Wall: the office comes home first";
        public const string Move = "Move", Select = "Select", Done = "Done", Manager = "Manager", Meet = "Meet";

        /// <summary>Every word a dock key can show (the label-fit test walks this list).</summary>
        public static readonly string[] AllWords =
        {
            Desk, Floor, Ceiling, Wall, SendBack, NextSpot, Place, Cancel, Stop,
            ModeConsoleLogic.Lower, ModeConsoleLogic.Raise, ModeConsoleLogic.Waiting,
            Bring, Status, Full, Compact, Move, Select, Done, Manager, Meet,
        };

        /// <summary>Cap width of a dock key (the console's segment key, BuildModeConsoleV1.SegCapWidth), millimetres.</summary>
        public const float KeyCapWidthMm = 36f;
        /// <summary>Smallest label size the dock keys auto-size down to, millimetres (BuildModeConsoleV1.KeyLabel).</summary>
        public const float KeyLabelMinMm = 5.5f;
        /// <summary>Share of the cap width the label box uses (BuildModeConsoleV1.KeyLabel).</summary>
        public const float LabelBoxShare = 0.90f;

        /// <summary>True when <paramref name="word"/> fits one line of a key's label box at <paramref name="sizeMm"/>
        /// (bold Inter average widths, <see cref="TextFit"/>), so it never needs an ellipsis.</summary>
        public static bool FitsOnKey(string word, float capWidthMm = KeyCapWidthMm, float sizeMm = KeyLabelMinMm)
        {
            if (string.IsNullOrEmpty(word)) return true;
            return TextFit.CharsPerLine(capWidthMm * LabelBoxShare, sizeMm, FontWeightKind.Bold) >= word.Length;
        }

        public static SpacesDockKeys Compute(SpacesDockInput i)
        {
            ModeConsoleKeys b = ModeConsoleLogic.Compute(i.floorEnabled, i.ceilingEnabled, i.mode, i.previewing, i.canCycle,
                                                         i.transitioning, i.podState, i.podWaiting, i.floorPreview, i.placeReady);
            SpacesDockKeys k = new SpacesDockKeys
            {
                active = b.active,
                deskDisabled = b.deskDisabled,
                floorDisabled = b.floorDisabled,
                ceilingDisabled = b.ceilingDisabled,
                next = b.next, confirm = b.confirm, cancel = b.cancel, stop = b.stop,
                pod = b.pod, bring = b.bring, status = b.status,
                podWords = b.podWords,
                confirmWords = Place, nextWords = NextSpot,
                layoutWords = i.wallLayoutFull ? Compact : Full,
                selectWords = i.wallSelecting ? Done : Select,
            };
            bool moving = i.transitioning && !i.previewing;
            bool deskIdle = i.mode == PlacementMode.Desk && !i.previewing && !i.transitioning;

            if (i.wallPreviewing)
            {
                // Entering Wall, or moving the habitat: the shared preview keys act on the wall. Nothing has moved.
                k.active = i.wallActive ? 3 : b.active;
                k.wallContext = true;
                k.next = true;
                k.confirm = true;
                k.confirmDisabled = !i.wallCanPlace;
                k.cancel = true;
                k.layout = true;
                k.stop = k.pod = k.bring = k.status = k.move = k.select = k.manager = k.meet = false;
                k.wallDisabled = false;
                return k;
            }
            if (i.wallActive)
            {
                // The office is on the wall (the director is in Desk). Floor and Ceiling first leave Wall safely.
                k.active = 3;
                k.next = k.confirm = k.cancel = k.stop = k.pod = k.bring = k.status = false;
                k.move = true;
                k.select = i.wallSelectEnabled;
                k.manager = i.wallManagerEnabled;
                k.meet = i.wallMeetEnabled;
                k.deskDisabled = moving;
                k.floorDisabled = moving || !i.floorEnabled;
                k.ceilingDisabled = moving || !i.ceilingEnabled;
                k.wallDisabled = false;
                return k;
            }
            // Desk / Floor / Ceiling as before. Owner (worn rc17): "wall mode should be enabled from anywhere not just from desk".
            // From Floor or Ceiling at rest, Wall is offered whenever Desk is: the office first goes home (the normal, swept
            // Desk move), then the wall preview opens by itself. Nothing is placed until Place, as always.
            bool floorOrCeiling = i.mode == PlacementMode.Floor || i.mode == PlacementMode.Ceiling;
            bool awayAtRest = floorOrCeiling && !i.previewing && !i.transitioning && !b.deskDisabled;
            k.wallDisabled = !i.wallEnabled || !(deskIdle || awayAtRest);
            return k;
        }
    }
}
