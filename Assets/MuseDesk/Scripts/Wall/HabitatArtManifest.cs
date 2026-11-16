using System;
using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// Swap-in of real Wall Habitat art (Antigravity's modules) without touching code: each entry places a
    /// prefab at a layout socket and hides the named proxy objects it replaces. Empty manifest = proxy art only.
    /// The collision/nav boxes never come from art: a module must FIT the layout's boxes (checked by
    /// AgentScripts/WallArtFitCheck before an entry is enabled), so art cannot make a character clip.
    /// </summary>
    [CreateAssetMenu(fileName = "HabitatArtManifest", menuName = "MuseDesk/Wall Habitat Art Manifest")]
    public class HabitatArtManifest : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string socketId;
            public GameObject prefab;
            public Vector3 localOffset;
            public Vector3 localEuler;
            public float uniformScale = 1f;
            [Tooltip("Proxy objects (by name, under the habitat root) this module replaces.")]
            public string[] hideProxies = new string[0];
            [Tooltip("Proxy objects whose RENDERERS (not the object) are turned off, for objects that carry live children such as the elevator cabin the riders attach to.")]
            public string[] hideRenderersOf = new string[0];
            [Tooltip("Off until the fit check and an on-head look passed.")]
            public bool enabled;
            [Tooltip("Which layouts this entry applies to.")]
            public bool full = true, compact = true;
        }

        [Tooltip("Rollback switch: false puts every proxy box back and instantiates no art.")]
        public bool useHabitatArt = true;
        public string artVersion = "proxy-0";
        public List<Entry> entries = new List<Entry>();

        public List<GameObject> ApplyTo(WallHabitatView view)
        {
            var placed = new List<GameObject>();
            LastHiddenCharacterRenderers = 0;
            if (view == null || entries == null || !useHabitatArt) return placed;
            foreach (var e in entries)
            {
                if (e == null || !e.enabled || e.prefab == null) continue;
                if (view.layout.kind == WallLayoutKind.Full && !e.full) continue;
                if (view.layout.kind == WallLayoutKind.Compact && !e.compact) continue;
                HabitatSocket s;
                if (!view.layout.TryGetSocket(e.socketId, out s)) { Debug.LogWarning("[MuseDesk.Wall] Art manifest: no socket " + e.socketId); continue; }
                // Hide the proxies first, so a name the art also uses (TaskPrinter) can only match the proxy.
                foreach (string n in e.hideRenderersOf)
                {
                    Transform t = Find(view.root, n);
                    if (t != null) foreach (var rr in t.GetComponentsInChildren<Renderer>(true)) rr.enabled = false;
                }
                foreach (string n in e.hideProxies)
                {
                    Transform t = Find(view.root, n);
                    if (t != null) t.gameObject.SetActive(false);
                }
                GameObject g = Instantiate(e.prefab, view.root);
                g.transform.localPosition = s.position + e.localOffset;
                g.transform.localRotation = s.Rotation * Quaternion.Euler(e.localEuler);
                g.transform.localScale = Vector3.one * e.uniformScale;
                placed.Add(g);
                foreach (var c in g.GetComponentsInChildren<Collider>(true)) c.enabled = false;   // art never collides
                LastHiddenCharacterRenderers += HideBakedCharacters(g.transform);
            }
            return placed;
        }

        /// <summary>Renderers switched off by the last <see cref="ApplyTo"/> because they belonged to a baked character.</summary>
        public static int LastHiddenCharacterRenderers;

        /// <summary>
        /// The complete habitat prefabs embed the four agent prefabs (Agent_Manager, Bay_*_Agent). The walkers are code-driven
        /// avatar clones, so the baked copies would be a second, differently scaled agent in the same place. Every renderer and
        /// Animator under a baked-character transform is switched off (kept in the hierarchy, so the art is otherwise unchanged).
        /// Returns the number of renderers hidden.
        /// </summary>
        public static int HideBakedCharacters(Transform artRoot)
        {
            if (artRoot == null) return 0;
            int n = 0;
            foreach (var t in artRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!HabitatArtRules.IsBakedCharacterName(t.name)) continue;
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled) { r.enabled = false; n++; }
                foreach (var a in t.GetComponentsInChildren<Animator>(true)) a.enabled = false;
                foreach (var mb in t.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) mb.enabled = false;   // no second agent brain either
            }
            return n;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }
    }
}
