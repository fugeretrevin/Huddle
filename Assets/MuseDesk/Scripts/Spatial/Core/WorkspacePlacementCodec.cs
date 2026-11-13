using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// What is remembered between sessions: the mode and the office pose RELATIVE to the room's
    /// stable reference (so it survives a recenter and a restart), never a raw world pose.
    /// Presentation only: no task, session or backend state is stored here.
    /// </summary>
    [Serializable]
    public sealed class SavedPlacement
    {
        public int version = WorkspacePlacementCodec.CurrentVersion;
        public string build;
        public int mode;
        public string roomId;
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public float scale = 1f;
        public bool hasDock;
        public Vector3 dockLocalPosition;
        public Quaternion dockLocalRotation = Quaternion.identity;
    }

    /// <summary>Where the saved placement text is kept (PlayerPrefs on the device, memory in tests).</summary>
    public interface IPlacementStore
    {
        string Read(string key);
        void Write(string key, string value);
        void Delete(string key);
    }

    public sealed class MemoryPlacementStore : IPlacementStore
    {
        readonly Dictionary<string, string> _values = new Dictionary<string, string>();
        public string Read(string key) { string v; return _values.TryGetValue(key, out v) ? v : null; }
        public void Write(string key, string value) { _values[key] = value; }
        public void Delete(string key) { _values.Remove(key); }
    }

    public sealed class PlayerPrefsPlacementStore : IPlacementStore
    {
        public string Read(string key)
        {
            try { return PlayerPrefs.GetString(key, null); } catch { return null; }
        }
        public void Write(string key, string value)
        {
            try { PlayerPrefs.SetString(key, value); PlayerPrefs.Save(); }
            catch (Exception ex) { Debug.LogWarning("[SpatialPlacement] save failed: " + ex.Message); }
        }
        public void Delete(string key)
        {
            try { PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); } catch { }
        }
    }

    /// <summary>Reads, writes and resolves saved placements. Robust like WorkspaceLayoutStore: anything
    /// missing, corrupt, from another build or from another room is refused with a reason.</summary>
    public static class WorkspacePlacementCodec
    {
        public const int CurrentVersion = 1;

        public static Vector3 ToLocal(Pose reference, Vector3 world)
        {
            return Quaternion.Inverse(reference.rotation) * (world - reference.position);
        }
        public static Quaternion ToLocal(Pose reference, Quaternion world)
        {
            return Quaternion.Inverse(reference.rotation) * world;
        }
        public static RootPose ToWorld(Pose reference, Vector3 localPosition, Quaternion localRotation, float scale)
        {
            return new RootPose(reference.position + reference.rotation * localPosition, reference.rotation * localRotation, scale);
        }

        /// <summary>Build the record for the current placement, or null when it cannot be stored
        /// relative to the room (Desk mode needs no record; without a room reference nothing is saved).</summary>
        public static SavedPlacement Capture(PlacementMode mode, IRoomGeometry room, RootPose rootPose,
                                             bool hasDock, RootPose dock, string build)
        {
            if (mode == PlacementMode.Desk || room == null) return null;
            Pose reference;
            if (!room.TryGetReferencePose(out reference)) return null;
            var s = new SavedPlacement
            {
                build = build,
                mode = (int)mode,
                roomId = room.RoomId ?? "",
                localPosition = ToLocal(reference, rootPose.position),
                localRotation = ToLocal(reference, rootPose.rotation),
                scale = rootPose.scale,
                hasDock = hasDock,
            };
            if (hasDock)
            {
                s.dockLocalPosition = ToLocal(reference, dock.position);
                s.dockLocalRotation = ToLocal(reference, dock.rotation);
            }
            return s;
        }

        public static void Save(IPlacementStore store, string key, SavedPlacement saved)
        {
            if (store == null) return;
            if (saved == null) { store.Delete(key); return; }
            store.Write(key, JsonUtility.ToJson(saved));
        }

        public static bool TryLoad(IPlacementStore store, string key, string build, out SavedPlacement saved, out string reason)
        {
            saved = null;
            reason = null;
            string raw = store != null ? store.Read(key) : null;
            if (string.IsNullOrEmpty(raw)) { reason = "No saved placement"; return false; }
            try { saved = JsonUtility.FromJson<SavedPlacement>(raw); } catch { saved = null; }
            if (saved == null) { reason = "Saved placement is unreadable"; return false; }
            if (saved.version != CurrentVersion) { saved = null; reason = "Saved placement is from another version"; return false; }
            if ((saved.build ?? "") != (build ?? "")) { saved = null; reason = "Saved placement is from another build"; return false; }
            if (saved.mode != (int)PlacementMode.Floor && saved.mode != (int)PlacementMode.Ceiling)
            { saved = null; reason = "Saved placement has no spatial mode"; return false; }
            if (float.IsNaN(saved.scale) || saved.scale <= 0f) { saved = null; reason = "Saved placement has an invalid scale"; return false; }
            return true;
        }

        /// <summary>Turn the room-relative record back into world poses for the CURRENT room. Fails
        /// when there is no room, no reference, or the record belongs to a different room.</summary>
        public static bool TryResolve(SavedPlacement saved, IRoomGeometry room, out RootPose pose, out RootPose dock, out string reason)
        {
            pose = new RootPose(Vector3.zero, Quaternion.identity, 1f);
            dock = pose;
            reason = null;
            if (saved == null) { reason = "No saved placement"; return false; }
            if (room == null || !room.HasRoom) { reason = SpatialGeometry.ReasonText(room == null ? RoomAvailability.NotLoadedYet : room.Availability); return false; }
            string id = room.RoomId ?? "";
            if (!string.IsNullOrEmpty(saved.roomId) && !string.IsNullOrEmpty(id) && saved.roomId != id)
            { reason = "Saved placement belongs to a different room"; return false; }
            Pose reference;
            if (!room.TryGetReferencePose(out reference)) { reason = "The room has no stable reference"; return false; }
            pose = ToWorld(reference, saved.localPosition, Normalized(saved.localRotation), saved.scale);
            if (saved.hasDock) dock = ToWorld(reference, saved.dockLocalPosition, Normalized(saved.dockLocalRotation), 1f);
            return true;
        }

        static Quaternion Normalized(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m < 1e-6f ? Quaternion.identity : new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }
    }
}
