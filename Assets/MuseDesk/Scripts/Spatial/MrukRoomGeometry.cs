using System;
using System.Collections.Generic;
using UnityEngine;
using Meta.XR.MRUtilityKit;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The room as scanned by the headset, read through the MR Utility Kit. This is the ONLY file of
    /// the spatial system that touches MRUK. It never assumes data exists at start-up: it waits for
    /// MRUK's scene-loaded and room events, copies the geometry it needs into plain data, and
    /// raises <see cref="Changed"/>. It never starts room setup by itself; only
    /// <see cref="RequestSceneLoad"/> (an explicit call) asks MRUK to load, and only with
    /// allowSpaceSetup = true may that open the system's room setup. It does not touch MRUK's
    /// world lock.
    ///
    /// The component is saved disabled; RoomPlacementService enables it when a spatial flag is on,
    /// so with both flags off it does not even subscribe.
    /// </summary>
    public class MrukRoomGeometry : MonoBehaviour, IRoomGeometry
    {
        [Tooltip("Seconds between checks for the MRUK instance while it does not exist yet.")]
        public float subscribeRetrySeconds = 0.5f;

        readonly List<RoomPlane> _floors = new List<RoomPlane>();
        readonly List<RoomPlane> _ceilings = new List<RoomPlane>();
        readonly List<FurnitureBox> _furniture = new List<FurnitureBox>();

        MRUK _mruk;
        bool _subscribed;
        bool _dirty;
        float _nextSubscribeTry;
        RoomAvailability _availability = RoomAvailability.NotLoadedYet;
        RoomAvailability _loadFailure = RoomAvailability.NotLoadedYet;
        string _roomId = "";
        Bounds _bounds;
        Pose _reference = Pose.identity;
        bool _hasReference;
        bool _loading;

        public event Action Changed;

        public bool HasRoom => _availability == RoomAvailability.Available && _floors.Count > 0;
        public RoomAvailability Availability => _availability;
        public RoomAvailability CeilingAvailability
        {
            get
            {
                if (_availability != RoomAvailability.Available) return _availability;
                return _ceilings.Count > 0 ? RoomAvailability.Available : RoomAvailability.NoCeiling;
            }
        }
        public string RoomId => _roomId;
        public Bounds RoomBounds => _bounds;
        public int FloorCount => HasRoom ? _floors.Count : 0;
        public RoomPlane GetFloor(int index) { return _floors[index]; }
        public int CeilingCount => HasRoom ? _ceilings.Count : 0;
        public RoomPlane GetCeiling(int index) { return _ceilings[index]; }
        public int FurnitureCount => HasRoom ? _furniture.Count : 0;
        public FurnitureBox GetFurniture(int index) { return _furniture[index]; }

        public bool TryGetReferencePose(out Pose pose)
        {
            pose = _reference;
            return HasRoom && _hasReference;
        }

        void OnEnable()
        {
            _nextSubscribeTry = 0f;
            TrySubscribe();
            _dirty = true;
        }

        void OnDisable()
        {
            if (_subscribed && _mruk != null)
            {
                _mruk.SceneLoadedEvent.RemoveListener(OnSceneLoaded);
                _mruk.RoomCreatedEvent.RemoveListener(OnRoomEvent);
                _mruk.RoomUpdatedEvent.RemoveListener(OnRoomEvent);
                _mruk.RoomRemovedEvent.RemoveListener(OnRoomEvent);
            }
            _subscribed = false;
            _mruk = null;
        }

        void TrySubscribe()
        {
            if (_subscribed) return;
            var mruk = MRUK.Instance;
            if (mruk == null) return;
            _mruk = mruk;
            _subscribed = true;
            mruk.RoomCreatedEvent.AddListener(OnRoomEvent);
            mruk.RoomUpdatedEvent.AddListener(OnRoomEvent);
            mruk.RoomRemovedEvent.AddListener(OnRoomEvent);
            // Fires at once when the scene is already loaded.
            mruk.RegisterSceneLoadedCallback(OnSceneLoaded);
        }

        void OnSceneLoaded() { _dirty = true; }

        // Room removal is announced BEFORE the room is destroyed, so every event only marks the
        // data dirty and the rebuild happens on the next Update.
        void OnRoomEvent(MRUKRoom room) { _dirty = true; }

        void Update()
        {
            if (!_subscribed)
            {
                if (Time.unscaledTime < _nextSubscribeTry) return;
                _nextSubscribeTry = Time.unscaledTime + Mathf.Max(0.1f, subscribeRetrySeconds);
                TrySubscribe();
                if (!_subscribed) return;
            }
            if (!_dirty) return;
            _dirty = false;
            Rebuild();
        }

        /// <summary>Re-read the room now (explicit request; normally room events do this).</summary>
        public void Refresh()
        {
            TrySubscribe();
            Rebuild();
        }

        /// <summary>
        /// Explicitly ask MRUK to load the room from the device. With <paramref name="allowSpaceSetup"/>
        /// true and no room on the device, the system's room setup may open. Never called by this
        /// component on its own.
        /// </summary>
        public async void RequestSceneLoad(bool allowSpaceSetup)
        {
            TrySubscribe();
            if (_mruk == null || _loading) return;
            _loading = true;
            try
            {
                MRUK.LoadDeviceResult result = await _mruk.LoadSceneFromDevice(allowSpaceSetup);
                if (result == MRUK.LoadDeviceResult.NoScenePermission) _loadFailure = RoomAvailability.PermissionDenied;
                else if (result == MRUK.LoadDeviceResult.NoRoomsFound) _loadFailure = RoomAvailability.NoRoomSetup;
                else _loadFailure = RoomAvailability.NotLoadedYet;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MuseDesk] Room load request failed: " + e.Message);
            }
            _loading = false;
            _dirty = true;
        }

        static bool ScenePermissionMissing()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return !UnityEngine.Android.Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission);
#else
            return false;
#endif
        }

        void Rebuild()
        {
            _floors.Clear();
            _ceilings.Clear();
            _furniture.Clear();
            _hasReference = false;
            _roomId = "";
            _bounds = new Bounds();

            RoomAvailability availability = RoomAvailability.NotLoadedYet;
            try
            {
                availability = Read();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MuseDesk] Reading the room scan failed: " + e.Message);
                _floors.Clear(); _ceilings.Clear(); _furniture.Clear();
                availability = RoomAvailability.NotLoadedYet;
            }
            _availability = availability;

            var h = Changed;
            if (h != null) h();
        }

        RoomAvailability Read()
        {
            if (_mruk == null) return ScenePermissionMissing() ? RoomAvailability.PermissionDenied : RoomAvailability.NotLoadedYet;
            if (!_mruk.IsInitialized)
            {
                if (ScenePermissionMissing()) return RoomAvailability.PermissionDenied;
                return _loadFailure;
            }

            MRUKRoom room = _mruk.GetCurrentRoom();
            if (room == null && _mruk.Rooms.Count > 0) room = _mruk.Rooms[0];
            if (room == null) return RoomAvailability.NoRoomSetup;

            _roomId = room.Anchor.Uuid.ToString();

            List<MRUKAnchor> floors = room.FloorAnchors;
            for (int i = 0; i < floors.Count; i++)
            {
                RoomPlane plane = ReadPlane(floors[i]);
                if (plane == null) continue;
                if (!_hasReference)
                {
                    Transform t = floors[i].transform;
                    _reference = new Pose(t.position, t.rotation);
                    _hasReference = true;
                }
                _floors.Add(plane);
            }
            if (_floors.Count == 0) return RoomAvailability.InvalidFloor;

            List<MRUKAnchor> ceilings = room.CeilingAnchors;
            for (int i = 0; i < ceilings.Count; i++)
            {
                RoomPlane plane = ReadPlane(ceilings[i]);
                if (plane != null) _ceilings.Add(plane);
            }

            List<MRUKAnchor> anchors = room.Anchors;
            for (int i = 0; i < anchors.Count; i++)
            {
                MRUKAnchor a = anchors[i];
                if (a == null || !a.VolumeBounds.HasValue) continue;
                if (a.HasAnyLabel(MRUKAnchor.SceneLabels.GLOBAL_MESH | MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.CEILING)) continue;
                _furniture.Add(ReadVolume(a, a.VolumeBounds.Value));
            }

            // Bounds from the outlines read above (MRUKRoom.GetRoomBounds warns when a room has no ceiling).
            bool any = false;
            Vector3 mn = Vector3.zero, mx = Vector3.zero;
            Encapsulate(_floors, ref any, ref mn, ref mx);
            Encapsulate(_ceilings, ref any, ref mn, ref mx);
            if (any) _bounds.SetMinMax(mn, mx);

            return RoomAvailability.Available;
        }

        static void Encapsulate(List<RoomPlane> planes, ref bool any, ref Vector3 mn, ref Vector3 mx)
        {
            for (int i = 0; i < planes.Count; i++)
            {
                Vector2[] poly = planes[i].polygon;
                for (int j = 0; j < poly.Length; j++)
                {
                    var p = new Vector3(poly[j].x, planes[i].height, poly[j].y);
                    if (!any) { mn = mx = p; any = true; }
                    else { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
                }
            }
        }

        /// <summary>Outline of a floor or ceiling anchor in world XZ. The boundary points are in the
        /// anchor's local XY plane (as MRUKRoom.CalculateRoomOutlineAndBounds reads them).</summary>
        static RoomPlane ReadPlane(MRUKAnchor anchor)
        {
            if (anchor == null) return null;
            List<Vector2> boundary = anchor.PlaneBoundary2D;
            if (boundary == null || boundary.Count < 3) return null;
            Transform t = anchor.transform;
            var poly = new Vector2[boundary.Count];
            float sumY = 0f;
            for (int i = 0; i < boundary.Count; i++)
            {
                Vector3 w = t.TransformPoint(new Vector3(boundary[i].x, boundary[i].y, 0f));
                poly[i] = new Vector2(w.x, w.z);
                sumY += w.y;
            }
            return new RoomPlane { polygon = poly, height = sumY / boundary.Count };
        }

        /// <summary>A scene volume as an upright box. Scene volumes are gravity-aligned; whichever
        /// local axis of the anchor is closest to vertical gives the height.</summary>
        static FurnitureBox ReadVolume(MRUKAnchor anchor, Bounds local)
        {
            Transform t = anchor.transform;
            Vector3 scale = t.lossyScale;
            Vector3 e = new Vector3(local.extents.x * Mathf.Abs(scale.x), local.extents.y * Mathf.Abs(scale.y), local.extents.z * Mathf.Abs(scale.z));
            Vector3 right = t.right, up = t.up, fwd = t.forward;
            float ax = Mathf.Abs(right.y), ay = Mathf.Abs(up.y), az = Mathf.Abs(fwd.y);

            float vertical;
            Vector3 along;          // the footprint's "forward" axis, before flattening
            Vector2 half;           // (across, along)
            if (az >= ax && az >= ay) { vertical = e.z; along = up; half = new Vector2(e.x, e.y); }
            else if (ay >= ax) { vertical = e.y; along = fwd; half = new Vector2(e.x, e.z); }
            else { vertical = e.x; along = fwd; half = new Vector2(e.y, e.z); }

            along.y = 0f;
            float yaw = along.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
            Vector3 c = t.TransformPoint(local.center);
            return new FurnitureBox
            {
                label = anchor.Label.ToString(),
                center = new Vector2(c.x, c.z),
                halfSize = half,
                yawDeg = yaw,
                minY = c.y - vertical,
                maxY = c.y + vertical,
            };
        }
    }
}
