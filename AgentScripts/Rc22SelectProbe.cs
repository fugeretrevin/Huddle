using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;
using MuseDesk.Wall;

/// <summary>
/// rc22 Select Agents probes (owner, worn rc21: "if we interact with something else it should get rid of that mode";
/// "the grabbing should keep hold of them and they should drop where you place them"). Play Mode, offline, SIMULATED hands
/// through WallHabitatController.SimulateHand (the same per-frame path as a real hand). Evidence: PLAY only. Real hands, the
/// ISDK grab state (HandInteractionProbe.AnyHeld) and the real desk are WORN-only.
///
///   Setup                 flags on (driver only), synthetic room, enter Wall, place. Call until it says "ready".
///   ArmThenPressManager   arm Select, press Manager as its poke would (Spaces dock), report armed=false. Synchronous.
///   ArmThenPressMove      arm Select, press Move as its poke would: Select goes off and the relocation is NOT refused. Synchronous.
///   CarryBlip             start a carry, 0.1 s pinch drop-out, then a 0.5 s tracking loss: report still carrying; then land.
///   DropLeft / DropRight  carry, let go 15 cm left / right of the pad, 8 cm above the desk: report where it stands vs PerchWorld.
///   DropChest             let go in front of the chest: report it was kept out of the body keep-out.
///   DropFar               let go out of reach: report the fallback (home).
///   Status                the running / last scenario's report.   Stop   abandon it.
/// Async ones return at once: call  scripts\drv.ps1 Rc22SelectProbe Status  after ~6 s.
/// </summary>
public static class Rc22SelectProbe
{
    static T One<T>() where T : UnityEngine.Object => UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static WallHabitatController C => One<WallHabitatController>();
    static float Now => Time.realtimeSinceStartup;
    static Vector3 Head() { var cam = Camera.main; return cam != null ? cam.transform.position : new Vector3(0, 1.2f, 0); }

    // ───────────────────────── setup ─────────────────────────

    public static string Setup()
    {
        if (!Application.isPlaying) return "not playing: enter Play Mode first";
        var c = C;
        if (c == null) return "no WallHabitatController";
        if (c.WallActive && !c.WallPreviewing) return "ready: " + Brief(c);
        if (!c.WallEnabled)
        {
            c.SetFlags(new WallFeatureFlags { wallHabitatEnabled = true, wallManagerEnabled = true, agentSelectEnabled = true, agentBtwEnabled = true, largeAgentPortalEnabled = true, wallElevatorVfxEnabled = false });
            var d = One<SpatialWorkspaceDirector>();
            if (d != null) d.UseSyntheticTestRoom();
            return "flags on (driver only), synthetic room; call Setup again";
        }
        if (!c.WallPreviewing) { c.PressWall(); return "entering Wall; call Setup again"; }
        if (c.Flow != null && c.Flow.HasShown && c.Flow.Shown.valid) { c.PressPlaceHere(); return "placing; call Setup again"; }
        return "previewing, no valid spot yet; call Setup again";
    }

    static string Brief(WallHabitatController c)
    {
        string eye = c.nearUserRig != null ? c.nearUserRig.InverseTransformPoint(Head()).ToString("F2") : "?";
        return "visit=" + c.Visit.State + " armed=" + c.Visit.IsArmed + " eye(rig)=" + eye + " status='" + c.StatusLine + "'";
    }

    // ───────────────────────── Select off when doing something else (synchronous) ─────────────────────────

    public static string ArmThenPressManager() { return ArmThenPress("Manager"); }
    public static string ArmThenPressMove() { return ArmThenPress("Move"); }

    static string ArmThenPress(string key)
    {
        if (!Application.isPlaying) return "not playing";
        var c = C;
        if (c == null) return "no controller";
        if (!c.WallActive || c.WallPreviewing) return "Wall is not active: run Setup first";
        c.StopSimulating();
        if (c.Visit.State != VisitState.Off && !c.Visit.IsArmed) return "a visit is in progress (" + c.Visit.State + "): press Return first";
        if (!c.Visit.IsArmed) c.PressSelectAgents();
        bool armedBefore = c.Visit.IsArmed;
        if (!armedBefore) return "Select refused: " + c.StatusLine;
        int offsBefore = c.SelectAutoOffs, raisedBefore = SelectModeExit.Raised;
        bool planBefore = c.PlanCard != null && c.PlanCard.Visible;

        string path;
        var dock = One<ModeConsole>();
        bool pressed = dock != null && dock.PressByName(key);
        if (pressed && SelectModeExit.Raised > raisedBefore) path = "Spaces dock (ModeConsole) poke path";
        else
        {
            // The Spaces dock refused the key (not shown in this layout): the signal path the Wall keys take, then the key's action.
            path = "signal path only (dock key " + (dock == null ? "missing" : "refused/hidden") + "); key action called directly";
            SelectModeExit.Raise(key);
            if (key == "Manager") c.PressManager(); else c.PressMove();
        }
        bool armedAfter = c.Visit.IsArmed;
        var sb = new StringBuilder();
        sb.Append(armedAfter ? "FAIL " : "OK ").Append("arm Select, press ").Append(key).Append(": armed before=").Append(armedBefore)
          .Append(" after=").Append(armedAfter).Append(" autoOffs ").Append(offsBefore).Append("->").Append(c.SelectAutoOffs)
          .Append(" cause='").Append(c.LastSelectAutoOff).Append("' via ").Append(path).Append(" | status='").Append(c.StatusLine).Append("'");
        if (key == "Manager")
        {
            bool planAfter = c.PlanCard != null && c.PlanCard.Visible;
            sb.Append(" | plan paper open before=").Append(planBefore).Append(" after=").Append(planAfter);
            if (planAfter && !planBefore) { c.PressManager(); sb.Append(" (closed again)"); }
        }
        else
        {
            bool relocating = c.WallPreviewing;
            sb.Append(" | Move ").Append(relocating ? "started the relocation preview (not refused)" : "did NOT start a relocation (status above says why)");
            if (relocating) { c.PressCancel(); sb.Append("; cancelled, Wall active=").Append(c.WallActive && !c.WallPreviewing); }
        }
        return sb.ToString();
    }

    // ───────────────────────── async scenarios ─────────────────────────

    sealed class Phase { public string name; public Func<float, bool> tick; public float timeout; }

    static readonly List<Phase> _ph = new List<Phase>();
    static readonly List<string> _log = new List<string>();
    static int _i;
    static float _t0, _runT0;
    static bool _running;
    static string _scenario = "";

    // CarryBlip measurements
    static bool _keptPinchBlip, _keptLostBlip, _gripSeen;
    static int _blipFrames, _lostFrames;
    // Drop measurements
    static Vector3 _targetWorld;

    public static string Status()
    {
        if (_ph.Count == 0) return "no scenario run yet";
        var sb = new StringBuilder();
        sb.AppendLine("Rc22SelectProbe " + _scenario + " (PLAY, simulated hands) " + (_running ? "RUNNING phase " + (_i + 1) + "/" + _ph.Count : "FINISHED"));
        foreach (string l in _log) sb.AppendLine(l);
        var c = Application.isPlaying ? C : null;
        if (c != null) sb.AppendLine("now: " + c.DescribeSelectGrab());
        return sb.ToString();
    }

    public static string Stop() { End("stopped by the driver"); return Status(); }

    static string Begin(string name, List<Phase> phases)
    {
        if (!Application.isPlaying) return "not playing";
        var c = C;
        if (c == null) return "no controller";
        if (!c.WallActive || c.WallPreviewing) return "Wall is not active: run Setup first";
        if (_running) End("replaced by " + name);
        _ph.Clear(); _log.Clear();
        _ph.AddRange(phases);
        _scenario = name; _i = 0; _t0 = _runT0 = Now; _running = true;
        _keptPinchBlip = _keptLostBlip = true; _gripSeen = false; _blipFrames = _lostFrames = 0;
        UnityEditor.EditorApplication.update -= Tick;
        UnityEditor.EditorApplication.update += Tick;
        return name + " started (" + phases.Count + " phases). Call Rc22SelectProbe Status in ~6 s.";
    }

    static void End(string why)
    {
        if (!_running) return;
        _running = false;
        UnityEditor.EditorApplication.update -= Tick;
        var c = Application.isPlaying ? C : null;
        if (c != null) c.StopSimulating();
        _log.Add("== ended: " + why + " after " + (Now - _runT0).ToString("F1") + " s");
    }

    static void Tick()
    {
        if (!_running) return;
        if (!Application.isPlaying) { End("Play Mode ended"); return; }
        var c = C;
        if (c == null) { End("controller gone"); return; }
        if (_i >= _ph.Count) { End("all phases done"); return; }
        Phase p = _ph[_i];
        float t = Now - _t0;
        bool done;
        try { done = p.tick(t); }
        catch (Exception e) { _log.Add("FAIL " + p.name + ": exception " + e.Message); End("exception"); return; }
        if (done)
        {
            _log.Add("ok   " + p.name + " [" + t.ToString("F2") + " s] " + c.DescribeSelectGrab());
            _i++; _t0 = Now;
            if (_i >= _ph.Count) End("all phases done");
            return;
        }
        if (t > p.timeout) { _log.Add("FAIL " + p.name + " timed out after " + p.timeout.ToString("F1") + " s | " + c.DescribeSelectGrab()); End("timed out"); }
    }

    // ── simulated hand ──

    static void Aim(HabitatRole r, bool pinch)
    {
        Vector3 at;
        if (!C.TryGetAvatarCenter(r, out at)) return;
        Vector3 o = Head() + Vector3.down * 0.25f + Vector3.right * 0.15f;
        C.SimulateHand(1, o, at - o, pinch);
    }

    /// <summary>Hold the pinch so the carried projection's feet come to <paramref name="p"/> (the carry point is carryDistance
    /// along the ray and 2 cm below it).</summary>
    static void HoldAt(Vector3 p)
    {
        float d = C.selectGrab != null ? C.selectGrab.carryDistance : 0.14f;
        Vector3 pt = p + Vector3.up * 0.02f;
        Vector3 dir = (pt - Head()).normalized;
        Vector3 o = pt - dir * d;
        C.SimulateHand(1, o, dir, true);
    }

    static Vector3 RigRight() { var r = C.nearUserRig; return r != null ? r.rotation * Vector3.right : Vector3.right; }
    static Vector3 RigUp() { var r = C.nearUserRig; return r != null ? r.rotation * Vector3.up : Vector3.up; }
    static Vector3 Local(Vector3 w) { var r = C.nearUserRig; return r != null ? r.InverseTransformPoint(w) : w; }

    /// <summary>Clear any visit, arm, point open at the Developer, pinch until the carry starts.</summary>
    static List<Phase> StartCarry()
    {
        return new List<Phase>
        {
            new Phase { name = "no visit in progress", timeout = 4f, tick = t =>
            {
                var c = C;
                c.StopSimulating();
                if (c.Visit.State == VisitState.Off || c.Visit.IsArmed) return true;
                if (c.Visit.State == VisitState.Perched || c.Visit.State == VisitState.Carrying) c.PressReturn();
                return false;
            } },
            new Phase { name = "arm Select", timeout = 2f, tick = t => { if (!C.Visit.IsArmed) C.PressSelectAgents(); return C.Visit.IsArmed; } },
            new Phase { name = "point open at the Developer", timeout = 2f, tick = t => { Aim(HabitatRole.Developer, false); return t > 0.5f; } },
            new Phase { name = "pinch: the carry starts", timeout = 3f, tick = t => { Aim(HabitatRole.Developer, true); return C.Visit.State == VisitState.Carrying; } },
        };
    }

    static Phase ReturnHome()
    {
        return new Phase { name = "clean up: Return, the agent is home", timeout = 4f, tick = t =>
        {
            var c = C;
            c.StopSimulating();
            if (c.Visit.State == VisitState.Perched || c.Visit.State == VisitState.Carrying) c.PressReturn();
            return c.Visit.State == VisitState.Off && c.Presentations.AwayCount == 0;
        } };
    }

    public static string CarryBlip()
    {
        var ph = StartCarry();
        ph.Add(new Phase { name = "carry over the pad for 1 s", timeout = 3f, tick = t =>
        {
            HoldAt(C.PerchPadWorld);
            if (C.CarryGripApplied) _gripSeen = true;
            return t > 1.0f && C.Visit.State == VisitState.Carrying;
        } });
        ph.Add(new Phase { name = "0.1 s pinch drop-out (hand tracked, fingers read open)", timeout = 1f, tick = t =>
        {
            C.SimulateRelease(1);
            _blipFrames++;
            if (C.Visit.State != VisitState.Carrying) _keptPinchBlip = false;
            return t >= 0.10f;
        } });
        ph.Add(new Phase { name = "pinch back for 0.4 s: still carrying", timeout = 2f, tick = t =>
        {
            HoldAt(C.PerchPadWorld);
            if (C.Visit.State != VisitState.Carrying) _keptPinchBlip = false;
            return t > 0.4f;
        } });
        ph.Add(new Phase { name = "0.5 s tracking loss", timeout = 2f, tick = t =>
        {
            C.SimulateHandLost(1);
            _lostFrames++;
            if (C.Visit.State != VisitState.Carrying) _keptLostBlip = false;
            return t >= 0.5f;
        } });
        ph.Add(new Phase { name = "hand back, still pinched, 0.4 s: still carrying", timeout = 2f, tick = t =>
        {
            HoldAt(C.PerchPadWorld);
            if (C.Visit.State != VisitState.Carrying) _keptLostBlip = false;
            return t > 0.4f;
        } });
        ph.Add(new Phase { name = "open for real: it lands on the pad", timeout = 4f, tick = t =>
        {
            if (C.Visit.State == VisitState.Carrying) { C.SimulateRelease(1); return false; }
            if (C.Visit.State != VisitState.Perched) return false;
            _log.Add((_keptPinchBlip && _keptLostBlip ? "RESULT OK " : "RESULT FAIL ") + "pinch drop-out 0.1 s (" + _blipFrames + " frames) kept=" + _keptPinchBlip
                     + "; tracking loss 0.5 s (" + _lostFrames + " frames) kept=" + _keptLostBlip + "; carry grip seen on the arbiter=" + _gripSeen
                     + "; released -> " + C.Visit.State + " lastDrop=" + C.LastDrop.kind);
            return true;
        } });
        ph.Add(ReturnHome());
        return Begin("CarryBlip", ph);
    }

    public static string DropLeft() { return DropAt("DropLeft", () => C.PerchPadWorld + RigRight() * 0.15f + RigUp() * 0.08f, true); }
    public static string DropRight() { return DropAt("DropRight", () => C.PerchPadWorld - RigRight() * 0.15f + RigUp() * 0.08f, true); }
    public static string DropFar() { return DropAt("DropFar", () => C.PerchPadWorld + RigRight() * 0.60f + RigUp() * 0.05f, false); }
    public static string DropChest()
    {
        return DropAt("DropChest", () => C.nearUserRig != null ? C.nearUserRig.TransformPoint(new Vector3(0f, 0.12f, 0.56f)) : C.PerchPadWorld, true);
    }

    static string DropAt(string name, Func<Vector3> target, bool expectDesk)
    {
        var ph = StartCarry();
        ph.Add(new Phase { name = "carry to the release point for 1.2 s", timeout = 3f, tick = t =>
        {
            _targetWorld = target();
            HoldAt(_targetWorld);
            return t > 1.2f && C.Visit.State == VisitState.Carrying;
        } });
        ph.Add(new Phase { name = "let go", timeout = 2f, tick = t =>
        {
            if (C.Visit.State == VisitState.Carrying) { C.SimulateRelease(1); return false; }
            return true;
        } });
        ph.Add(new Phase { name = "landed (or went home)", timeout = 4f, tick = t =>
        {
            var c = C;
            if (c.Visit.State == VisitState.Perched && t < 1.4f) return false;   // let the landing ease finish
            if (c.Visit.State == VisitState.Returning) return false;
            DeskDropResult r = c.LastDrop;
            Vector3 rel = Local(_targetWorld), pad = Local(c.PerchPadWorld), eye = Local(Head());
            var sb = new StringBuilder();
            if (c.Visit.State == VisitState.Perched)
            {
                Vector3 stand = Local(c.ProxyPosition), perch = Local(c.PerchWorld);
                float onPerch = (c.ProxyPosition - c.PerchWorld).magnitude;
                float lateral = stand.x - pad.x;
                bool deskOk = !expectDesk || (r.kind == DeskDropKind.Spot || r.kind == DeskDropKind.Pad);
                sb.Append(onPerch < 0.01f && deskOk ? "RESULT OK " : "RESULT CHECK ");
                sb.Append("perched at rig ").Append(stand.ToString("F3")).Append(" PerchWorld(rig) ").Append(perch.ToString("F3"))
                  .Append(" |stand-PerchWorld|=").Append(onPerch.ToString("F3")).Append(" pad(rig) ").Append(pad.ToString("F3"))
                  .Append(" offset from pad x=").Append(lateral.ToString("F3")).Append(" z=").Append((stand.z - pad.z).ToString("F3"))
                  .Append(" | let go at rig ").Append(rel.ToString("F3"))
                  .Append(" | decision ").Append(r.kind).Append(r.adjusted ? " (pulled onto the desk)" : "").Append(r.why != null ? " why='" + r.why + "'" : "")
                  .Append(" | onUserSpot=").Append(c.PerchOnUserSpot);
                if (c.VisitCard != null && c.VisitCard.Visible) sb.Append(" | visit card at rig ").Append(Local(c.VisitCard.root.position).ToString("F3"));
            }
            else
            {
                sb.Append(expectDesk ? "RESULT CHECK " : "RESULT OK ").Append("not placed: visit=").Append(c.Visit.State).Append(" lastEnd=").Append(c.Visit.LastEnd)
                  .Append(" decision ").Append(r.kind).Append(r.why != null ? " why='" + r.why + "'" : "")
                  .Append(" | let go at rig ").Append(rel.ToString("F3")).Append(" pad(rig) ").Append(pad.ToString("F3"));
            }
            sb.Append(" | eye(rig) ").Append(eye.ToString("F2")).Append(" (desk placement needs the release within ").Append(c.deskDrop.reach.ToString("F2")).Append(" m of the eye)");
            _log.Add(sb.ToString());
            return true;
        } });
        ph.Add(ReturnHome());
        return Begin(name, ph);
    }
}
