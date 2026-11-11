using UnityEngine;
using MuseDesk.Config;
using MuseDesk.Workspace;
using MuseDesk.DebugTools;

namespace MuseDesk.Bootstrap
{
    /// <summary>
    /// Entry point. Loads config, resolves the mode badge, and wires the scene's
    /// foundation systems. In TASK-01 it establishes mode display + placement; the
    /// live/demo gateway selection (TASK-07) plugs in through SetMode() later.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class AppBootstrap : MonoBehaviour
    {
        [Header("Config")]
        public RuntimeSettings settings;

        [Header("Scene systems")]
        public WorkspacePlacement placement;
        public DiagnosticsOverlay diagnostics;

        [Header("Mode (foundation default; gateway overrides at runtime)")]
        public bool startInOfflineDemo = true;

        void Awake()
        {
            if (settings == null)
                settings = Resources.Load<RuntimeSettings>("RuntimeSettings");

            if (diagnostics != null)
            {
                diagnostics.visible = settings == null || settings.showDiagnosticsOverlay;
                if (startInOfflineDemo)
                    diagnostics.SetMode("DEMO · Simulated work", "(offline)");
                else
                    diagnostics.SetMode("LIVE · Connecting…",
                        settings != null ? settings.apiBaseUrl : "");
            }

            if (placement != null)
                placement.Placed += OnWorkspacePlaced;
        }

        void OnDestroy()
        {
            if (placement != null)
                placement.Placed -= OnWorkspacePlaced;
        }

        void OnWorkspacePlaced(WorkspacePlacement.PlacementState state)
        {
            Debug.Log($"[MuseDesk] Workspace ready ({state}). Foundation online.");
        }

        /// <summary>Called by a hand-driven world-space button (TASK-01 acceptance smoke test).</summary>
        public void OnConfirmPlacementPressed()
        {
            if (placement != null) placement.ConfirmManualPlacement();
        }

        public void OnResetWorkspacePressed()
        {
            if (placement != null) placement.ResetWorkspaceInFront();
        }
    }
}
