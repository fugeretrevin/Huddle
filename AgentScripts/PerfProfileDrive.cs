using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EDITOR-ONLY Play Mode perf sampler (Sprint A13). Numbers come from the Editor, NOT a Quest:
/// they are useful only to compare scenario against scenario / before against after.
///
/// Usage (Play Mode, via the usual driver route):
///   PerfProfileDrive.Begin("desk-idle", 5)        // samples for 5 s of real time
///   ... wait ...
///   PerfProfileDrive.Table()                      // prints all finished scenarios
///   PerfProfileDrive.Reset()                      // clear results
/// Helpers to set a scenario up first (all name based, nothing is destroyed):
///   PerfProfileDrive.SetActiveByName("WallHabitatRoot", true)   // Wall habitat, if its flag/root exists
/// </summary>
public static class PerfProfileDrive
{
    public class Result
    {
        public string Label; public int Frames; public float Seconds;
        public float AvgMs, P95Ms, MaxMs;
        public long GcBytesPerFrameAvg, GcBytesTotal; public int GcFramesWithAlloc;
        public long Batches, DrawCalls, SetPass, Tris;   // averages; -1 when the counter is unavailable
    }

    static readonly List<Result> Results = new List<Result>();
    static Sampler _live;

    public static string Begin(string label, float seconds)
    {
        if (!Application.isPlaying) return "ERROR: not in Play Mode";
        if (_live != null) return "ERROR: sampler already running (" + _live.Label + ")";
        _live = new Sampler();
        _live.Init(label, seconds, r => { Results.Add(r); _live = null; AppendRow(r); });
        return "Sampling '" + label + "' for " + seconds + " s (Editor-only numbers)";
    }

    public static string Idle() => Begin("desk-idle", 6f);   // A16 wrappers (drv.ps1 passes one arg)
    public static string Board() => Begin("board-open", 6f);
    public static string Reset() { Results.Clear(); return "cleared"; }

    public static string Status() => _live != null ? "running: " + _live.Label : "idle, " + Results.Count + " result(s)";

    public static string Table()
    {
        var sb = new StringBuilder();
        sb.AppendLine("PERF TABLE (Editor-only, not Quest; compare scenarios, do not quote as device metrics)");
        sb.AppendLine("scenario            frames  avg ms  p95 ms  max ms  GC B/frame  GC frames  batches  drawcalls  setpass  tris");
        foreach (var r in Results)
            sb.AppendLine(string.Format("{0,-19} {1,6}  {2,6:F2}  {3,6:F2}  {4,6:F2}  {5,10}  {6,9}  {7,7}  {8,9}  {9,7}  {10}",
                r.Label, r.Frames, r.AvgMs, r.P95Ms, r.MaxMs, r.GcBytesPerFrameAvg, r.GcFramesWithAlloc,
                F(r.Batches), F(r.DrawCalls), F(r.SetPass), F(r.Tris)));
        if (Results.Count == 0) sb.AppendLine("(no results yet; call Begin first)");
        return sb.ToString();
    }

    // A16: each driver run compiles a fresh assembly, so statics do not survive between runs; rows persist in a file.
    const string RowFile = "Library/PerfProfileRows.txt";
    static void AppendRow(Result r)
    {
        System.IO.File.AppendAllText(RowFile, string.Format("{0,-19} {1,6}  {2,6:F2}  {3,6:F2}  {4,6:F2}  {5,10}  {6,9}  {7,7}  {8,9}  {9,7}  {10}\n",
            r.Label, r.Frames, r.AvgMs, r.P95Ms, r.MaxMs, r.GcBytesPerFrameAvg, r.GcFramesWithAlloc, F(r.Batches), F(r.DrawCalls), F(r.SetPass), F(r.Tris)));
    }
    public static string TableFile() { return System.IO.File.Exists(RowFile) ? System.IO.File.ReadAllText(RowFile) : "(no rows)"; }

    static string F(long v) => v < 0 ? "n/a" : v.ToString();

    public static string SetActiveByName(string objectName, bool active)
    {
        if (!Application.isPlaying) return "ERROR: not in Play Mode";
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t.name != objectName || !t.gameObject.scene.IsValid()) continue;
            t.gameObject.SetActive(active);
            return objectName + " active=" + active;
        }
        return "not found: " + objectName;
    }

    // Plain class driven by EditorApplication.update (a MonoBehaviour here would need a matching file name).
    class Sampler
    {
        public string Label;
        float _dur, _elapsed;
        System.Action<Result> _done;
        readonly List<float> _ms = new List<float>(2048);
        ProfilerRecorder _gc, _batches, _draws, _setPass, _tris;
        long _gcSum; int _gcFrames; long _bSum, _dSum, _sSum, _tSum; int _n;

        public void Init(string label, float seconds, System.Action<Result> done)
        {
            Label = label; _dur = seconds; _done = done;
            _gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _lastFrame = -1;
            EditorApplication.update += Update;
        }

        int _lastFrame;

        void Update()
        {
            if (!Application.isPlaying) { Finish(); return; }
            if (Time.frameCount == _lastFrame) return;   // one sample per rendered frame
            _lastFrame = Time.frameCount;
            _elapsed += Time.unscaledDeltaTime;
            _ms.Add(Time.unscaledDeltaTime * 1000f);
            if (_gc.Valid) { long g = _gc.LastValue; _gcSum += g; if (g > 0) _gcFrames++; }
            if (_batches.Valid) _bSum += _batches.LastValue;
            if (_draws.Valid) _dSum += _draws.LastValue;
            if (_setPass.Valid) _sSum += _setPass.LastValue;
            if (_tris.Valid) _tSum += _tris.LastValue;
            _n++;
            if (_elapsed >= _dur) Finish();
        }

        void Finish()
        {
            var sorted = new List<float>(_ms); sorted.Sort();
            float sum = 0f; foreach (var m in _ms) sum += m;
            int n = Mathf.Max(1, _n);
            var r = new Result
            {
                Label = Label, Frames = _n, Seconds = _elapsed,
                AvgMs = sum / n,
                P95Ms = sorted.Count > 0 ? sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.95f))] : 0f,
                MaxMs = sorted.Count > 0 ? sorted[sorted.Count - 1] : 0f,
                GcBytesTotal = _gc.Valid ? _gcSum : -1, GcBytesPerFrameAvg = _gc.Valid ? _gcSum / n : -1,
                GcFramesWithAlloc = _gcFrames,
                Batches = _batches.Valid ? _bSum / n : -1, DrawCalls = _draws.Valid ? _dSum / n : -1,
                SetPass = _setPass.Valid ? _sSum / n : -1, Tris = _tris.Valid ? _tSum / n : -1,
            };
            _gc.Dispose(); _batches.Dispose(); _draws.Dispose(); _setPass.Dispose(); _tris.Dispose();
            EditorApplication.update -= Update;
            _done?.Invoke(r);
        }
    }
}



