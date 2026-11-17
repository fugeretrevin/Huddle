using System;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using MuseDesk.Documents;
using MuseDesk.Networking;
using MuseDesk.Phase2.Board;
using MuseDesk.Spatial;

namespace MuseDesk.Diagnostics
{
    /// <summary>
    /// Read-only performance bookkeeping for a worn session. About four times a second it works out which
    /// scenario the app is in and, when it changes, logs "[MuseDesk.Perf] scenario=NAME". Every five
    /// seconds it logs a heartbeat with CPU/GPU frame time, fps and memory. scripts/quest_perf_split.ps1
    /// joins these lines with the compositor's VrApi lines to give per-scenario numbers.
    /// It changes no other script's behaviour and allocates nothing per frame (strings are built only
    /// when a line is logged).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerfScenarioMarker : MonoBehaviour
    {
        public const string Tag = "[MuseDesk.Perf]";

        public enum Scenario
        {
            desk_idle, desk_task_running, paper_held, paper_expanded, board_open,
            floor_preview, floor_moving, floor_active,
            ceiling_scan, ceiling_descending, ceiling_deployed, ceiling_retracting, ceiling_docked,
            // Wall habitat (rc10 polish): the device log could not tell a Wall session from the desk before.
            wall_preview, wall_active, wall_visit, wall_portal,
        }

        [Tooltip("Seconds between scenario polls (0.25 = four per second).")]
        public float pollSeconds = 0.25f;
        [Tooltip("Seconds between heartbeat lines.")]
        public float heartbeatSeconds = 5f;

        SpatialWorkspaceDirector _director;
        CeilingScanReveal _ceilingScan;
        TaskDock _dock;
        SessionController _session;
        DocumentDeliveryController _delivery;
        HeldPaperController _held;
        MuseDesk.Wall.WallHabitatController _wall;
        float _nextBind, _nextPoll, _nextBeat;

        Scenario _current = (Scenario)(-1);

        // frame timing accumulators
        readonly FrameTiming[] _timings = new FrameTiming[1];
        double _cpuSum, _cpuMax, _gpuSum;
        int _cpuN, _gpuN, _frames;
        float _wallSum;
        readonly StringBuilder _sb = new StringBuilder(160);

        public Scenario Current => _current;

        void OnEnable()
        {
            _nextPoll = 0f;
            _nextBeat = Time.unscaledTime + heartbeatSeconds;
            ResetWindow();
        }

        void ResetWindow()
        {
            _cpuSum = _cpuMax = _gpuSum = 0; _cpuN = _gpuN = _frames = 0; _wallSum = 0f;
        }

        void Bind()
        {
            if (_director == null) _director = FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
            if (_ceilingScan == null) _ceilingScan = FindAnyObjectByType<CeilingScanReveal>(FindObjectsInactive.Include);
            if (_dock == null) _dock = FindAnyObjectByType<TaskDock>(FindObjectsInactive.Include);
            if (_session == null) _session = FindAnyObjectByType<SessionController>(FindObjectsInactive.Include);
            if (_delivery == null) _delivery = FindAnyObjectByType<DocumentDeliveryController>(FindObjectsInactive.Include);
            if (_wall == null) _wall = FindAnyObjectByType<MuseDesk.Wall.WallHabitatController>(FindObjectsInactive.Include);
            if (_held == null && _delivery != null && _delivery.paperRoot != null)
                _held = _delivery.paperRoot.GetComponent<HeldPaperController>();
        }

        void Update()
        {
            float now = Time.unscaledTime;

            // Frame accounting, every frame, no allocation.
            _frames++;
            _wallSum += Time.unscaledDeltaTime;
            FrameTimingManager.CaptureFrameTimings();
            uint got = FrameTimingManager.GetLatestTimings(1, _timings);
            if (got > 0)
            {
                double cpu = _timings[0].cpuMainThreadFrameTime;
                double gpu = _timings[0].gpuFrameTime;
                if (cpu > 0) { _cpuSum += cpu; _cpuN++; if (cpu > _cpuMax) _cpuMax = cpu; }
                if (gpu > 0) { _gpuSum += gpu; _gpuN++; }
            }

            if (now >= _nextPoll)
            {
                _nextPoll = now + pollSeconds;
                if (_director == null || _dock == null || _session == null || _delivery == null || _held == null || _wall == null)
                {
                    if (now >= _nextBind) { _nextBind = now + 1f; Bind(); }
                }
                Scenario s = Detect();
                if (s != _current)
                {
                    _current = s;
                    Debug.Log(_sb.Clear().Append(Tag).Append(" scenario=").Append(s.ToString()).ToString());
                }
            }

            if (now >= _nextBeat)
            {
                _nextBeat = now + heartbeatSeconds;
                LogHeartbeat();
                ResetWindow();
            }
        }

        void LogHeartbeat()
        {
            float avgDt = _frames > 0 ? _wallSum / _frames : 0f;
            double fps = avgDt > 0f ? 1.0 / avgDt : 0.0;
            double cpuAvg, cpuMax;
            if (_cpuN > 0) { cpuAvg = _cpuSum / _cpuN; cpuMax = _cpuMax; }
            else { cpuAvg = avgDt * 1000.0; cpuMax = cpuAvg; }      // fallback: whole frame time
            double gpuAvg = _gpuN > 0 ? _gpuSum / _gpuN : 0.0;       // 0 means the platform gave no GPU time
            double gcMb = GC.GetTotalMemory(false) / 1048576.0;
            double totalMb = Profiler.GetTotalAllocatedMemoryLong() / 1048576.0;
            _sb.Clear().Append(Tag).Append(" hb scenario=").Append(_current.ToString())
               .Append(" cpuMain=").Append(cpuAvg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
               .Append(" cpuMainMax=").Append(cpuMax.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
               .Append(" gpu=").Append(gpuAvg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
               .Append(" fps=").Append(fps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
               .Append(" gcMB=").Append(gcMb.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
               .Append(" totalMB=").Append(totalMb.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            Debug.Log(_sb.ToString());
        }

        /// <summary>The most specific scenario that applies right now. Read-only.</summary>
        public Scenario Detect()
        {
            var core = _director != null ? _director.Core : null;
            PlacementMode mode = core != null ? core.Mode : (PlacementMode)(int)SpatialWorkspaceState.Mode;
            bool transitioning = core != null ? core.IsTransitioning : SpatialWorkspaceState.IsTransitioning;

            // Wall habitat first: while it is the office the director sits in Desk, so Desk scenarios would mislabel it.
            if (_wall != null && _wall.isActiveAndEnabled)
            {
                if (_wall.Portal != null && _wall.Portal.State != MuseDesk.Spatial.Wall.PortalState.Hidden) return Scenario.wall_portal;
                if (_wall.Visit != null && _wall.Visit.Busy) return Scenario.wall_visit;
                if (_wall.WallPreviewing) return Scenario.wall_preview;
                if (_wall.WallActive) return Scenario.wall_active;
            }

            // A placement preview is open: the scan grid is up.
            if (core != null && core.HasPreview)
            {
                if (core.PendingMode == PlacementMode.Ceiling) return Scenario.ceiling_scan;
                if (core.PendingMode == PlacementMode.Floor) return Scenario.floor_preview;
            }
            if (_ceilingScan != null && _ceilingScan.Shown && mode != PlacementMode.Floor) return Scenario.ceiling_scan;

            if (mode == PlacementMode.Ceiling)
            {
                CeilingPodState pod = core != null && core.Pod != null ? core.Pod.State : CeilingPodState.Docked;
                switch (pod)
                {
                    case CeilingPodState.PreparingToDescend:
                    case CeilingPodState.Descending:
                    case CeilingPodState.Stabilizing:
                        return Scenario.ceiling_descending;
                    case CeilingPodState.Engaged:
                        return Scenario.ceiling_deployed;
                    case CeilingPodState.PreparingToRetract:
                    case CeilingPodState.Retracting:
                    case CeilingPodState.ReturningToDock:
                    case CeilingPodState.ErrorRecovery:
                        return Scenario.ceiling_retracting;
                    default:
                        return Scenario.ceiling_docked;
                }
            }
            if (mode == PlacementMode.Floor) return transitioning ? Scenario.floor_moving : Scenario.floor_active;

            // Desk, most specific first.
            if (_delivery != null && _delivery.paperRoot != null && _delivery.paperRoot.activeInHierarchy)
            {
                if (_delivery.panel != null && _delivery.panel.IsExpanded) return Scenario.paper_expanded;
                if (_held == null || _held.IsHeld || _held.IsTaken) return Scenario.paper_held;
            }
            if (_dock != null && _dock.board != null && _dock.board.IsExpanded) return Scenario.board_open;
            if (_session != null && _session.RunInProgress) return Scenario.desk_task_running;
            return Scenario.desk_idle;
        }
    }
}

