using System.Text;
using UnityEngine;
using TMPro;
using MuseDesk.Workspace;

namespace MuseDesk.DebugTools
{
    /// <summary>
    /// World-space debug HUD: device, mode, scene/placement status, endpoint, FPS,
    /// last applied event seq/id. Toggleable; hidden in release unless explicitly enabled.
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

        [Header("Display")]
        public bool visible = true;
        [Range(0.1f, 1f)] public float refreshInterval = 0.5f;

        float _fps;
        float _accum;
        int _frames;
        float _timer;
        readonly StringBuilder _sb = new StringBuilder(256);

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
        }

        void Render()
        {
            if (text == null) return;
            text.enabled = visible;
            if (!visible) return;
            if (placement != null) sceneStatus = placement.State.ToString();

            _sb.Clear();
            _sb.Append("MuseDesk diagnostics\n");
            _sb.Append("device : ").Append(SystemInfo.deviceModel).Append('\n');
            _sb.Append("mode   : ").Append(modeLabel).Append('\n');
            _sb.Append("scene  : ").Append(sceneStatus).Append('\n');
            _sb.Append("api    : ").Append(endpoint).Append('\n');
            _sb.Append("fps    : ").Append(Mathf.RoundToInt(_fps)).Append('\n');
            _sb.Append("seq    : ").Append(lastSeq).Append("  (").Append(lastEventId).Append(')');
            text.text = _sb.ToString();
        }

        public void ToggleVisible() => visible = !visible;
    }
}
