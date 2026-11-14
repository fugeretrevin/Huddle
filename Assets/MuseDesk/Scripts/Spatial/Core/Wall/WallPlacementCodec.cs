using System;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>
    /// What is remembered about a confirmed wall habitat: the room, the wall's stable id, the spot on it,
    /// the layout, and the pose relative to the room's reference anchor (a second way to find the wall if
    /// its id changed). Never any task or backend state.
    /// </summary>
    [Serializable]
    public sealed class SavedWallPlacement
    {
        public int version = 1;
        public string roomId = "";
        public string wallId = "";
        public int layout;
        public float u;
        public float centerY;
        public Vector3 refLocalPosition;
        public Quaternion refLocalRotation = Quaternion.identity;
        public bool hasReference;
        public string buildGuid = "";
    }

    /// <summary>A string slot the saved placement lives in (PlayerPrefs on device, memory in tests).</summary>
    public interface IWallPlacementStore
    {
        string Load();
        void Save(string json);
        void Clear();
    }

    public sealed class MemoryWallPlacementStore : IWallPlacementStore
    {
        public string value;
        public string Load() { return value; }
        public void Save(string json) { value = json; }
        public void Clear() { value = null; }
    }

    public static class WallPlacementCodec
    {
        public const int Version = 1;
        /// <summary>A wall found by position must be within this distance of the saved spot (m) ...</summary>
        public const float RefindDistance = 0.12f;
        /// <summary>... and turned at most this much (deg).</summary>
        public const float RefindAngle = 12f;

        public static SavedWallPlacement Build(WallCandidate c, string roomId, bool hasReference, Pose reference, string buildGuid)
        {
            var s = new SavedWallPlacement
            {
                version = Version,
                roomId = roomId ?? "",
                wallId = c.wallId ?? "",
                layout = (int)c.layout,
                u = c.u,
                centerY = c.centerY,
                hasReference = hasReference,
                buildGuid = buildGuid ?? "",
            };
            if (hasReference)
            {
                Quaternion inv = Quaternion.Inverse(reference.rotation);
                s.refLocalPosition = inv * (c.position - reference.position);
                s.refLocalRotation = inv * c.rotation;
            }
            return s;
        }

        public static string Encode(SavedWallPlacement s) { return s == null ? null : JsonUtility.ToJson(s); }

        public static bool TryDecode(string json, out SavedWallPlacement s)
        {
            s = null;
            if (string.IsNullOrEmpty(json)) return false;
            try { s = JsonUtility.FromJson<SavedWallPlacement>(json); }
            catch (Exception) { s = null; }
            return s != null && s.version == Version;
        }

        /// <summary>
        /// Find the saved spot in the current room and re-run every check. The habitat is restored only when
        /// the room is the same, the wall is found (by id, else by its pose relative to the room anchor) and
        /// the spot still passes; otherwise the reason says why and the caller shows a preview instead.
        /// </summary>
        public static bool TryRestore(SavedWallPlacement saved, IRoomGeometry room, IWallGeometry walls, SeatedUser user,
                                      WallPlacementSettings settings, out WallCandidate result, out string reason)
        {
            result = default(WallCandidate);
            if (saved == null) { reason = "Nothing saved"; return false; }
            if (walls == null || walls.WallAvailability != RoomAvailability.Available)
            { reason = walls == null ? "No room scan" : SpatialGeometry.ReasonText(walls.WallAvailability); return false; }
            if (!string.IsNullOrEmpty(saved.roomId) && room != null && !string.IsNullOrEmpty(room.RoomId) && room.RoomId != saved.roomId)
            { reason = "A different room is loaded"; return false; }

            int index = -1;
            float u = saved.u, y = saved.centerY;
            for (int i = 0; i < walls.WallCount; i++)
                if (!string.IsNullOrEmpty(saved.wallId) && walls.GetWall(i).id == saved.wallId) { index = i; break; }

            if (index < 0 && saved.hasReference && room != null)
            {
                Pose reference;
                if (room.TryGetReferencePose(out reference))
                {
                    Vector3 p = reference.position + reference.rotation * saved.refLocalPosition;
                    Quaternion r = reference.rotation * saved.refLocalRotation;
                    Vector3 n = r * Vector3.forward; n.y = 0f;
                    float best = RefindDistance;
                    for (int i = 0; i < walls.WallCount; i++)
                    {
                        RoomWall w = walls.GetWall(i);
                        float dist = Mathf.Abs(w.SignedDistance(p));
                        if (dist > best) continue;
                        if (n.sqrMagnitude > 1e-6f && Vector3.Angle(w.normal, n.normalized) > RefindAngle) continue;
                        float wu = w.UOf(p);
                        if (Mathf.Abs(wu) > w.width * 0.5f) continue;
                        best = dist; index = i; u = wu; y = p.y;
                    }
                }
            }
            if (index < 0) { reason = "The saved wall was not found in this scan"; return false; }

            result = WallPlacementEvaluator.Evaluate(room, walls, index, u, y, (WallLayoutKind)saved.layout, user, settings);
            if (!result.valid) { reason = "The saved spot no longer fits: " + result.reason; return false; }
            reason = "Restored";
            return true;
        }
    }
}
