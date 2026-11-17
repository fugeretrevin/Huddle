using System;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Owns the room data source for the spatial system and a small cached summary of it (is there
    /// a usable floor, is there a ceiling, in words for the user). The summary is recomputed only
    /// when the room raises an event or on an explicit <see cref="Recompute"/>: never per frame.
    /// The real source is <see cref="MrukRoomGeometry"/>; a synthetic room can be swapped in for
    /// the Editor and tests. While the system is inactive (both flags off) the MRUK reader stays
    /// disabled, so nothing subscribes to room events.
    /// </summary>
    public class RoomPlacementService : MonoBehaviour
    {
        [Tooltip("The MRUK-backed room reader. Kept disabled until a spatial flag is on.")]
        public MrukRoomGeometry mrukRoom;

        IRoomGeometry _room;
        bool _active;
        bool _synthetic;
        // Wall Habitat (Phase 3, additive): the wall mode may need the room while Floor and Ceiling are off.
        bool _flagActive, _wallDemand;
        MuseDesk.Spatial.Wall.SyntheticWallRoom _syntheticWalls;

        /// <summary>Walls, doors and windows of the current room (MRUK, or the synthetic room's walls). Null when inactive.</summary>
        public MuseDesk.Spatial.Wall.IWallGeometry Walls
        {
            get
            {
                if (_synthetic) return _syntheticWalls;
                return _active ? mrukRoom : null;
            }
        }

        /// <summary>The wall mode asks for room data (its flag is on). Same effect as a Floor/Ceiling flag on the
        /// MRUK reader; with the wall flag off this changes nothing.</summary>
        public void SetWallDemand(bool on)
        {
            _wallDemand = on;
            ApplyActive();
        }

        /// <summary>Summary, in words the user can be shown.</summary>
        public string StatusText { get; private set; } = "";
        public RoomAvailability FloorAvailability { get; private set; } = RoomAvailability.NotLoadedYet;
        public RoomAvailability CeilingAvailability { get; private set; } = RoomAvailability.NotLoadedYet;
        /// <summary>Counts how often the summary was recomputed (tests: must not grow per frame).</summary>
        public int RecomputeCount { get; private set; }
        public bool UsingSyntheticRoom => _synthetic;

        /// <summary>The current room source (never null once <see cref="SetActive"/>(true) ran with a reader wired).</summary>
        public IRoomGeometry Room => _room;

        /// <summary>Raised when the room source or its data changed.</summary>
        public event Action RoomChanged;

        /// <summary>Called by the director when the flags change. Inactive = no MRUK subscription at all.</summary>
        public void SetActive(bool active)
        {
            _flagActive = active;
            ApplyActive();
        }

        void ApplyActive()
        {
            bool active = _flagActive || _wallDemand;
            if (_active == active) return;
            _active = active;
            if (!_synthetic)
            {
                if (mrukRoom != null) mrukRoom.enabled = active;
                Bind(active && mrukRoom != null ? (IRoomGeometry)mrukRoom : null);
            }
        }

        /// <summary>Replace the scanned room with a hand-built one (Editor runs, tests). Pass null to
        /// go back to the scanned room.</summary>
        public void UseSyntheticRoom(SyntheticRoomGeometry room)
        {
            if (_syntheticWalls != null && !ReferenceEquals(_syntheticWalls.room, room)) _syntheticWalls = null;
            _synthetic = room != null;
            if (_synthetic)
            {
                if (mrukRoom != null) mrukRoom.enabled = false;
                Bind(room);
            }
            else
            {
                if (mrukRoom != null) mrukRoom.enabled = _active;
                Bind(_active && mrukRoom != null ? (IRoomGeometry)mrukRoom : null);
            }
        }

        /// <summary>
        /// Editor / Play Mode convenience: a 4 x 4 m synthetic room around the given head position,
        /// floor 1.15 m below the eyes, ceiling 2.5 m above the floor, with a desk in front of the
        /// user. Clearly NOT real room data: the status text says "synthetic test room".
        /// </summary>
        public SyntheticRoomGeometry UseSyntheticTestRoom(Vector3 head, Vector3 forward)
        {
            forward.y = 0f;
            forward = forward.sqrMagnitude < 1e-6f ? Vector3.forward : forward.normalized;
            float floorY = head.y - 1.15f;
            // Built with its four walls (wall mode); Floor/Ceiling read only the floor, ceiling and furniture.
            var walls = MuseDesk.Spatial.Wall.SyntheticWallRoom.Rectangle(head.x, head.z, 4f, 4f, floorY, floorY + 2.5f);
            var room = walls.room;
            _syntheticWalls = walls;
            Vector3 desk = head + forward * 0.75f;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            room.AddFurniture("TABLE", new Vector3(desk.x, floorY + 0.375f, desk.z), new Vector3(1.4f, 0.75f, 0.7f), yaw);
            room.roomId = "synthetic-test-room";
            UseSyntheticRoom(room);
            return room;
        }

        /// <summary>Explicit request to (re)load the room scan from the device. This is the only way
        /// this system asks MRUK to load; allowSpaceSetup = true may open the system room setup.</summary>
        public void RequestRoomScan(bool allowSpaceSetup)
        {
            if (_synthetic || mrukRoom == null || !_active) return;
            mrukRoom.RequestSceneLoad(allowSpaceSetup);
        }

        /// <summary>Explicit request to re-read the room and rebuild the summary.</summary>
        public void Recompute()
        {
            if (!_synthetic && _active && mrukRoom != null) mrukRoom.Refresh();   // raises Changed -> OnRoomChanged
            else OnRoomChanged();
        }

        void Bind(IRoomGeometry room)
        {
            if (ReferenceEquals(_room, room)) return;
            if (_room != null) _room.Changed -= OnRoomChanged;
            _room = room;
            if (_room != null) _room.Changed += OnRoomChanged;
            OnRoomChanged();
        }

        void OnDestroy()
        {
            if (_room != null) _room.Changed -= OnRoomChanged;
            _room = null;
        }

        void OnRoomChanged()
        {
            RecomputeCount++;
            if (_room == null)
            {
                FloorAvailability = CeilingAvailability = RoomAvailability.NotLoadedYet;
                StatusText = "";
            }
            else
            {
                FloorAvailability = _room.Availability;
                CeilingAvailability = _room.CeilingAvailability;
                if (FloorAvailability != RoomAvailability.Available)
                    StatusText = "Room scan not available: place it by hand. " + SpatialGeometry.ReasonText(FloorAvailability) + ".";
                else if (CeilingAvailability != RoomAvailability.Available)
                    StatusText = (_synthetic ? "Synthetic test room: floor found, no ceiling." : "Room scan: floor found, no ceiling.");
                else
                    StatusText = _synthetic ? "Synthetic test room: floor and ceiling." : "Room scan: floor and ceiling found.";
            }
            var h = RoomChanged;
            if (h != null) h();
        }
    }
}
