using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// Saves and restores the decorative/workstation layout (per-item visibility + local
    /// transform) to PlayerPrefs as JSON. Robust by design: a missing, corrupt, or
    /// version-incompatible payload falls back to the defaults captured at startup rather than
    /// throwing, so the workspace is always usable. Decorations never affect task/document logic;
    /// this is cosmetic persistence only.
    /// </summary>
    public class WorkspaceLayoutStore : MonoBehaviour
    {
        [Serializable] public class ItemState
        {
            public string id;
            public bool active;
            public Vector3 pos;
            public Vector3 euler;
        }
        [Serializable] class LayoutDoc
        {
            public int version = CurrentVersion;
            public string build;   // layouts are per-build: a new build's authored layout wins
            public List<ItemState> items = new List<ItemState>();
        }

        public const int CurrentVersion = 1;
        public string prefsKey = "musedesk.workspace.layout.v1";

        [Tooltip("Items under the workspace that save/restore (decorations + movable stations).")]
        public List<Transform> tracked = new List<Transform>();

        readonly Dictionary<string, Transform> _byId = new Dictionary<string, Transform>();
        readonly Dictionary<string, ItemState> _defaults = new Dictionary<string, ItemState>();

        void Awake()
        {
            Index();
            CaptureDefaults();
            Load();   // restore last session if present; otherwise defaults remain
        }

        void Index()
        {
            _byId.Clear();
            foreach (var t in tracked)
                if (t != null && !_byId.ContainsKey(t.name)) _byId[t.name] = t;
        }

        void CaptureDefaults()
        {
            _defaults.Clear();
            foreach (var kv in _byId)
                _defaults[kv.Key] = Snapshot(kv.Key, kv.Value);
        }

        static ItemState Snapshot(string id, Transform t) => new ItemState
        {
            id = id,
            active = t.gameObject.activeSelf,
            pos = t.localPosition,
            euler = t.localEulerAngles
        };

        public void Register(Transform t)
        {
            if (t == null || _byId.ContainsKey(t.name)) return;
            tracked.Add(t);
            _byId[t.name] = t;
            _defaults[t.name] = Snapshot(t.name, t);
        }

        public void SetVisible(string id, bool visible, bool save = true)
        {
            if (_byId.TryGetValue(id, out var t))
            {
                t.gameObject.SetActive(visible);
                if (save) Save();
            }
        }

        // Nothing else calls Save for a moved item, so persist when the app is backgrounded or the
        // headset comes off (the reliable "about to exit" signal on Quest).
        void OnApplicationPause(bool paused) { if (paused) Save(); }

        public void Save()
        {
            try
            {
                var doc = new LayoutDoc { build = Application.buildGUID };
                foreach (var kv in _byId) doc.items.Add(Snapshot(kv.Key, kv.Value));
                PlayerPrefs.SetString(prefsKey, JsonUtility.ToJson(doc));
                PlayerPrefs.Save();
            }
            catch (Exception ex) { Debug.LogWarning("[WorkspaceLayoutStore] save failed: " + ex.Message); }
        }

        public void Load()
        {
            string raw = null;
            try { raw = PlayerPrefs.GetString(prefsKey, null); } catch { }
            if (string.IsNullOrEmpty(raw)) return;   // nothing saved yet -> keep defaults

            LayoutDoc doc = null;
            try { doc = JsonUtility.FromJson<LayoutDoc>(raw); } catch { doc = null; }

            // A layout saved by a different build is ignored (not an error): the scene's authored
            // positions may have changed, and stale saved transforms would silently undo them.
            if (doc != null && doc.build != Application.buildGUID) return;

            if (doc == null || doc.version != CurrentVersion || doc.items == null)
            {
                Debug.LogWarning("[WorkspaceLayoutStore] incompatible/corrupt layout -> defaults.");
                ResetToDefault();
                return;
            }

            foreach (var it in doc.items)
            {
                if (it == null || string.IsNullOrEmpty(it.id)) continue;
                if (_byId.TryGetValue(it.id, out var t))
                {
                    t.gameObject.SetActive(it.active);
                    t.localPosition = it.pos;
                    t.localEulerAngles = it.euler;
                }
            }
        }

        public void ResetToDefault(bool save = true)
        {
            foreach (var kv in _defaults)
            {
                if (!_byId.TryGetValue(kv.Key, out var t) || t == null) continue;
                t.gameObject.SetActive(kv.Value.active);
                t.localPosition = kv.Value.pos;
                t.localEulerAngles = kv.Value.euler;
            }
            if (save) Save();
        }
    }
}
