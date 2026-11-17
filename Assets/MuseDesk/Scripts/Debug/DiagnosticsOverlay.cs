using System.Text;
using UnityEngine;
using TMPro;
using MuseDesk.Workspace;

namespace MuseDesk.DebugTools
{
    /// <summary>
    /// Developer diagnostics: device, mode, scene/placement status, endpoint, FPS, last applied
    /// event seq/id. It also remains the place SessionController reports the mode label and the
    /// current status note to (<see cref="SetMode"/>, <see cref="note"/>).
    ///
    /// In the redesigned office the user-facing mode display is the small desk plaque (ModePlaque),
    /// which listens to <see cref="ModeLabelChanged"/>. The large floating sign this class used to
    /// draw is therefore off by default (<see cref="showInWorld"/>); switch it on in the Inspector
    /// (or call <see cref="ToggleInWorld"/>) when debugging on the headset.
    /// </summary>
    public class DiagnosticsOverlay : MonoBehaviour
    {
        [Header("Wiring")]
        public TMP_Text text;
        public WorkspacePlacement placement;

        [Header("State (set by AppBootstrap / gateway)")]
        public string modeLabel = "DEMO";
        public string endpoint = "(offline)";
        public long lastSeq;
        public string lastEventId = "-";
        public string sceneStatus = "init";
        [Tooltip("Free-form status line set by SessionController (pending decision, result, etc.)")]
        public string note = "";

        [Header("Display")]
        [Tooltip("Set by AppBootstrap from RuntimeSettings.showDiagnosticsOverlay.")]
        public bool visible = true;
        [Tooltip("Draw the floating world-space sign. Off by default: the desk plaque shows the mode. " +
                 "Both this and 'visible' must be on for the sign to be drawn.")]
        public bool showInWorld = false;
        [Tooltip("Off: a two-line status sign (mode + what is happening). On: the full developer HUD.")]
        public bool verbose = false;
        [Range(0.1f, 1f)] public float refreshInterval = 0.5f;

        /// <summary>Raised (main thread) whenever <see cref="SetMode"/> is called, after
        /// <see cref="modeLabel"/> and <see cref="endpoint"/> were updated.</summary>
        public event System.Action ModeLabelChanged;

        float _fps;
        float _accum;
        int _frames;
        float _timer;
        readonly StringBuilder _sb = new StringBuilder(256);

        void OnEnable() { ApplyVisibility(); }

        bool ApplyVisibility()
        {
            bool show = visible && showInWorld;
            if (text != null && text.enabled != show) text.enabled = show;
            return show;
        }

        void Update()
        {
            _accum += Time.unscaledDeltaTime;
            _frames++;
            _timer += Time.unscaledDeltaTime;
            if (_timer >= refreshInterval)
            {
                _fps = _frames / Mathf.Max(_accum, 0.0001f);
                _accum = 0f; _frames = 0; _timer = 0f;
                Render();
            }
        }

        public void SetEvent(long seq, string eventId)
        {
            lastSeq = seq;
            lastEventId = eventId;
        }

        public void SetMode(string mode, string endpointUrl)
        {
            modeLabel = mode;
            endpoint = string.IsNullOrEmpty(endpointUrl) ? "(offline)" : endpointUrl;
            var handler = ModeLabelChanged;
            if (handler == null) return;
            try { handler(); }
            catch (System.Exception ex) { Debug.LogError($"[MuseDesk] mode-label listener error: {ex.Message}"); }
        }

        void Render()
        {
            if (text == null) return;
            // Hidden: build no text at all (this used to allocate a string twice a second).
            if (!ApplyVisibility()) return;
            if (placement != null) sceneStatus = placement.State.ToString();

            _sb.Clear();
            if (!verbose)
            {
                // What a user needs: is this live or a scripted demo, and what is going on right now.
                _sb.Append("<b>").Append(modeLabel).Append("</b>");
                if (!string.IsNullOrEmpty(note)) _sb.Append('\n').Append(note);
                text.text = _sb.ToString();
                return;
            }
            _sb.Append("MuseDesk diagnostics\n");
            _sb.Append("device : ").Append(SystemInfo.deviceModel).Append('\n');
            _sb.Append("mode   : ").Append(modeLabel).Append('\n');
            _sb.Append("scene  : ").Append(sceneStatus).Append('\n');
            _sb.Append("api    : ").Append(endpoint).Append('\n');
            _sb.Append("fps    : ").Append(Mathf.RoundToInt(_fps)).Append('\n');
            _sb.Append("seq    : ").Append(lastSeq).Append("  (").Append(lastEventId).Append(")\n");
            _sb.Append("status : ").Append(string.IsNullOrEmpty(note) ? "-" : note);
            text.text = _sb.ToString();
        }

        public void ToggleVisible() => visible = !visible;

        /// <summary>Developer toggle: show or hide the floating diagnostics sign.</summary>
        public void ToggleInWorld() { showInWorld = !showInWorld; ApplyVisibility(); }
    }
}
