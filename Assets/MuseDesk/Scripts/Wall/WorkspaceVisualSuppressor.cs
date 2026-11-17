using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Wall
{
    /// <summary>
    /// Hides the desk office VISUALLY while the Wall Habitat is the presented office: renderers under the office
    /// root are switched off (only those that were on); colliders and scripts are untouched, nothing is deactivated, so every agent script, the
    /// decision flow and the remote paper delivery keep running exactly as in Floor mode. The decision paper
    /// (and anything under an excluded root) is never hidden. A rescan once a second catches renderers that
    /// appear later. Restore turns back on exactly what it turned off.
    /// </summary>
    public sealed class WorkspaceVisualSuppressor
    {
        readonly List<Renderer> _hidden = new List<Renderer>();
        readonly List<Renderer> _scratch = new List<Renderer>(256);
        Transform _root;
        Transform[] _exclude;
        float _nextScan;
        public bool Active { get; private set; }
        public int HiddenCount => _hidden.Count;

        public void Suppress(Transform officeRoot, Transform[] exclude)
        {
            _root = officeRoot;
            _exclude = exclude;
            Active = officeRoot != null;
            Scan();
        }

        public void Tick()
        {
            if (!Active || Time.unscaledTime < _nextScan) return;
            Scan();
        }

        void Scan()
        {
            _nextScan = Time.unscaledTime + 1f;
            if (_root == null) return;
            _root.GetComponentsInChildren(true, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                Renderer r = _scratch[i];
                if (r == null || !r.enabled || Excluded(r.transform)) continue;
                r.enabled = false;
                _hidden.Add(r);
            }
            // Something that was hidden but has since moved under an excluded root (the paper) comes back.
            for (int i = _hidden.Count - 1; i >= 0; i--)
            {
                Renderer r = _hidden[i];
                if (r == null) { _hidden.RemoveAt(i); continue; }
                if (Excluded(r.transform) || !r.transform.IsChildOf(_root)) { r.enabled = true; _hidden.RemoveAt(i); }
            }
        }

        bool Excluded(Transform t)
        {
            if (_exclude == null) return false;
            for (int i = 0; i < _exclude.Length; i++) if (_exclude[i] != null && t.IsChildOf(_exclude[i])) return true;
            return false;
        }

        public void Restore()
        {
            for (int i = 0; i < _hidden.Count; i++) if (_hidden[i] != null) _hidden[i].enabled = true;
            _hidden.Clear();
            Active = false;
        }
    }
}
