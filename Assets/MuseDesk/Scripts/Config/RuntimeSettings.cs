using UnityEngine;

namespace MuseDesk.Config
{
    /// <summary>
    /// Non-secret runtime configuration. NEVER store provider API keys or durable
    /// admin tokens here — the APK ships only non-secret config and takes a
    /// short-lived per-session token from the backend at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "RuntimeSettings", menuName = "MuseDesk/Runtime Settings", order = 0)]
    public class RuntimeSettings : ScriptableObject
    {
        [Header("Backend endpoints (dev: USB + adb reverse)")]
        [Tooltip("Base REST URL. For USB tethering use http://127.0.0.1:8765 after 'adb reverse tcp:8765 tcp:8765'.")]
        public string apiBaseUrl = "http://127.0.0.1:8765";

        [Tooltip("WebSocket base. Dev USB: ws://127.0.0.1:8765")]
        public string webSocketBaseUrl = "ws://127.0.0.1:8765";

        [Header("Mode selection")]
        [Tooltip("Default to the offline demo when no reachable backend is configured/found.")]
        public bool preferOfflineDemo = true;

        [Tooltip("Seconds to wait for a live backend handshake before offering offline demo.")]
        [Range(2f, 12f)] public float connectTimeoutSeconds = 6f;

        [Header("Workspace placement")]
        [Tooltip("Distance in front of the user for the manual placement tray when no table is found.")]
        [Range(0.3f, 0.8f)] public float manualTrayDistance = 0.5f;

        [Tooltip("Max distance a decision paper may be offered from the user (comfort/reach).")]
        [Range(0.4f, 0.75f)] public float maxReachMeters = 0.65f;

        [Header("Diagnostics")]
        public bool showDiagnosticsOverlay = true;
    }
}
