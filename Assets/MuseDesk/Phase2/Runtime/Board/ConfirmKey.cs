using System;
using System.Threading.Tasks;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Wall;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// rc22: a poke key for a destructive action with a two-press confirm. A clone of an existing board key (the proven poke
    /// setup, made by WallUi.MakeKey). Idle it reads <c>idle</c>; the first press turns it into <c>armed</c> ("Sure?") for
    /// three seconds; a second press inside that window runs the action, and the key reads "..." while the request is in
    /// flight. Hidden by scale (poke off), never SetActive. A stray poke can never delete anything.
    /// </summary>
    public sealed class ConfirmKey
    {
        readonly WallKey _key;
        readonly ConfirmGate _gate;
        readonly string _idle, _armed;
        readonly Func<Task> _run;
        bool _busy, _visible, _applied;

        public bool Busy => _busy;
        public bool Visible => _visible;
        public bool Armed => _gate.IsArmed(Time.unscaledTime);
        public string Label => _key != null && _key.label != null ? _key.label.text : null;
        public Transform Root => _key != null ? _key.root : null;
        /// <summary>The template key's housing size at root scale 1 (so a caller can lay keys out side by side).</summary>
        public Vector2 Unit => _key != null ? _key.unit : Vector2.one;

        public ConfirmKey(Transform template, Transform parent, string name, Vector3 localPos, float scale, float widthScale,
                          string idle, string armed, Func<Task> run)
        {
            _idle = idle; _armed = armed; _run = run;
            _gate = new ConfirmGate(3f);
            _key = WallUi.MakeKey(template, parent, name, idle, localPos, scale, OnPoke, widthScale);
            if (_key == null || _key.root == null) { _key = null; return; }
            foreach (var b in _key.root.GetComponentsInChildren<Behaviour>(true))
            {
                if (b == null) continue;
                string n = b.GetType().Name;
                if (n == "FaceUser") b.enabled = false;           // the template billboards; this key must stay put
                else if (n == "ScaleReveal") UnityEngine.Object.Destroy(b);   // visibility is managed here
            }
            _key.root.localRotation = Quaternion.identity;
            _key.shown = true;                                     // the press handler checks it; visibility is by scale below
            SetVisible(false);
        }

        public bool Built => _key != null;

        /// <summary>Show or hide by scale (poke off while hidden). Hiding disarms the key.</summary>
        public void SetVisible(bool visible)
        {
            if (_key == null) return;
            if (_applied && visible == _visible) return;
            _applied = true;
            if (visible && !_visible) _key.armAt = Time.unscaledTime + 0.25f;
            _visible = visible;
            if (!visible) _gate.Reset();
            float f = visible ? 1f : MuseDesk.Workspace.ScaleEase.HiddenFactor;
            _key.root.localScale = _key.shownScale * f;
            if (_key.poke != null && _key.poke.enabled != visible) _key.poke.enabled = visible;
            Refresh();
        }

        /// <summary>Forget a pending first press (the target changed).</summary>
        public void Disarm() { _gate.Reset(); Refresh(); }

        /// <summary>Keep the label in step with the confirm window (call every frame; cheap).</summary>
        public void Tick() => Refresh();

        void Refresh()
        {
            if (_key == null) return;
            _key.SetLabel(_gate.Label(Time.unscaledTime, _busy, _idle, _armed));
        }

        /// <summary>The press, as a poke or a driver does it. Returns true when it started the action.</summary>
        public bool Press() => PressCore();

        void OnPoke() => PressCore();

        bool PressCore()
        {
            if (_key == null || !_visible || _busy) return false;
            if (!_gate.Press(Time.unscaledTime)) { Refresh(); return false; }
            _ = RunAsync();
            return true;
        }

        async Task RunAsync()
        {
            _busy = true;
            Refresh();
            try { await _run(); }
            catch (Exception ex) { Debug.LogWarning("[MuseDesk.P2] confirm key action failed: " + ex.Message); }
            finally
            {
                _busy = false;
                _gate.Reset();
                Refresh();
            }
        }
    }
}
