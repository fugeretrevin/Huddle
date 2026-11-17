using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Remembers the spatial placement between sessions in PlayerPrefs as JSON (as
    /// WorkspaceLayoutStore does for decor): mode, office pose RELATIVE to the room's stable
    /// reference (the MRUK room's first floor anchor, keyed by the room anchor's UUID), scale and
    /// the ceiling dock. Records from another build, another version or another room are refused.
    /// The director validates a loaded record against the current room before using it and stays
    /// in Desk mode on any doubt. Cosmetic persistence only: no task, session or backend state.
    /// </summary>
    public class WorkspacePlacementPersistence : MonoBehaviour
    {
        public string prefsKey = "musedesk.spatial.placement.v1";

        IPlacementStore _store;

        /// <summary>Storage used; PlayerPrefs unless replaced (tests).</summary>
        public IPlacementStore Store
        {
            get { if (_store == null) _store = new PlayerPrefsPlacementStore(); return _store; }
            set { _store = value; }
        }

        /// <summary>Save a record, or delete the saved one when <paramref name="saved"/> is null.</summary>
        public void Save(SavedPlacement saved)
        {
            WorkspacePlacementCodec.Save(Store, prefsKey, saved);
        }

        public bool TryLoad(out SavedPlacement saved, out string reason)
        {
            return WorkspacePlacementCodec.TryLoad(Store, prefsKey, Application.buildGUID, out saved, out reason);
        }

        public void Clear()
        {
            Store.Delete(prefsKey);
        }
    }
}
