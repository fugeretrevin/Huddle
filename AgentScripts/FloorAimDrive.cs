using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using MuseDesk.Spatial;

/// <summary>
/// Play Mode driver for placing the floor office by pointing (FloorAimController), without hands:
/// it stands in for a hand with a ray and a pinch flag and steps the controller with a fixed time
/// step, so every method finishes within one call and reports what happened.
///
/// Typical run:  Open -> AimAtFootprint -> Pinch -> DragToWall -> Release (over the wall: not placed, words) -> HoldToConfirm
/// No room scan: OpenNoRoom -> Pinch -> DragTowardUser -> Release -> HoldToConfirm
/// (FullDrag now ends with a release on a ready spot, which places the office.)
///
/// Drag and let go places it (headset feedback 1). Each of these opens its own preview; call
/// Desk and wait for the office to be back between them. The move itself runs over the next
/// frames: call Report a few seconds later to see the root at the spot.
///   DragAndRelease          5 x 5 room, drag 0.3 m outward, let go: placed=True, mode Floor, mover running
///   ReleaseBesideShoulder   5 x 5 room, a spot 1.35 m away beside and behind the right shoulder (refused
///                           before this fix): placed=True, route shown as a side step
///   ReleaseWhereBlocked     5 x 5 room, a 2 m cabinet 0.2 m beyond a spot to the right: footprint not ready
///                           while dragged, words "The way there is blocked by STORAGE", placed=False, root unchanged
///   TwitchRelease           pinch with a 3 cm shake for 0.2 s, let go: placed=False, drops=0
///   FlickRelease            a 0.15 s drag, let go: placed=False (too short to be a placement)
/// Every report ends with the office root's position: it must not change unless a report says placed=True.
/// </summary>
public static class FloorAimDrive
{
    const float Dt = 1f / 72f;
    static int _hand = 1;                 // 0 = left, 1 = right
    static Vector3 _target;               // the floor point the simulated hand points at
    static bool _pinch;

    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static Transform Root() => Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
        .SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "WorkspaceRoot");

    static bool Ready(out SpatialWorkspaceDirector d, out FloorAimController aim, out string why)
    {
        d = null; aim = null; why = null;
        if (!Application.isPlaying) { why = "not playing"; return false; }
        d = One<SpatialWorkspaceDirector>();
        aim = One<FloorAimController>();
        if (d == null || d.Core == null) { why = "no SpatialWorkspaceDirector (or its core is not up)"; return false; }
        if (aim == null) { why = "no FloorAimController in the scene: run BuildFloorAimV1.Apply first"; return false; }
        // The statics above do not survive a recompile between two driver calls: take the hand,
        // the pinch and the aim point back from what the controller is being fed.
        for (int h = 0; h < 2; h++)
        {
            if (!aim.Simulating(h)) continue;
            AimHandSample s = aim.SimulatedSample(h);
            if (!s.tracked) continue;
            _hand = h;
            _pinch = s.pinching;
            Vector3 point;
            if (FloorAimMath.RayToFloor(s.origin, s.direction, FloorY(d), out point)) _target = point;
        }
        return true;
    }

    public static string Report()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        var ws = Root();
        var sb = new StringBuilder();
        sb.Append($"mode={d.Mode} previewing={d.IsPreviewing} canAim={d.CanAim} status='{d.StatusText}' candidates={d.Core.CandidateCount} index={d.Core.CandidateIndex}");
        sb.Append($" detected={d.Core.PreviewIsDetected} previewValid={d.Core.PreviewValid} placedByAim={d.Core.PlacedByAim} canCycle={d.Core.CanCycle}");
        if (d.Core.HasPreview) sb.Append($" preview={d.Core.PreviewPose.position:F2}");
        sb.Append(" | " + aim.Describe());
        sb.Append($" | simHand={(_hand == 0 ? "left" : "right")} target={_target:F2} pinch={_pinch}");
        sb.Append($" | ghostShown={(d.ghost != null && d.ghost.Shown)} OFFICE ROOT pos={(ws != null ? ws.position.ToString("F3") : "?")} mover={d.Core.Mover.Phase}");
        return sb.ToString();
    }

    // ───────────────────────── opening the preview ─────────────────────────

    /// <summary>Flags on, the synthetic 4 x 4 m room, floor preview open.</summary>
    public static string Open()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        d.SetFlags(true, true);
        d.UseSyntheticTestRoom();
        if (d.IsPreviewing) d.Cancel();
        d.RequestFloor();
        aim.ClearSimulation();
        _pinch = false;
        return Report();
    }

    /// <summary>Flags on, NO synthetic room (in the Editor there is no scan): hand placement, floor preview open.</summary>
    public static string OpenNoRoom()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        d.SetFlags(true, true);
        d.UseSyntheticRoom(null);
        if (d.IsPreviewing) d.Cancel();
        d.RequestFloor();
        aim.ClearSimulation();
        _pinch = false;
        return Report();
    }

    static string UseHand(int hand)
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!d.CanAim) return "no floor preview open (call Open first) | " + Report();
        _hand = hand;
        _pinch = false;
        _target = FootprintCentre(d);
        Push(d, aim, 0.6f);             // the other hand is fed as not tracked
        return Report();
    }

    /// <summary>Stand in for the left / right hand from now on (it starts pointing at the footprint, not pinching).</summary>
    public static string UseLeftHand() { return UseHand(0); }
    public static string UseRightHand() { return UseHand(1); }

    // ───────────────────────── the simulated hand ─────────────────────────

    static SeatedUser User(SpatialWorkspaceDirector d) { return d.CurrentUser; }

    static Vector3 HandOrigin(SpatialWorkspaceDirector d)
    {
        SeatedUser u = User(d);
        return u.head + u.Right * (_hand == 0 ? -0.25f : 0.25f) + Vector3.down * 0.35f + u.forward * 0.25f;
    }

    static float FloorY(SpatialWorkspaceDirector d)
    {
        float y;
        return d.Core.TryGetAimFloorHeight(out y) ? y : d.Core.PreviewPose.position.y;
    }

    static Vector3 FootprintCentre(SpatialWorkspaceDirector d)
    {
        RootPose p = d.Core.ShownPose;
        Vector2 off = d.Core.floorSettings.footprintCenterOffset;
        return p.position + p.rotation * new Vector3(off.x * p.scale, 0f, off.y * p.scale);
    }

    static void Push(SpatialWorkspaceDirector d, FloorAimController aim, float seconds, Vector3 jitter = default(Vector3))
    {
        int n = Mathf.Max(1, Mathf.CeilToInt(seconds / Dt));
        for (int i = 0; i < n; i++)
        {
            Vector3 origin = HandOrigin(d);
            Vector3 t = _target;
            t += jitter;
            t.y = FloorY(d);
            aim.SimulateHand(_hand, origin, t - origin, _pinch);
            aim.SimulateHandLost(1 - _hand);
            aim.Step(Dt);
        }
    }

    /// <summary>Slide the simulated aim point to a new floor point over the given time (a hand does not teleport).</summary>
    static void Sweep(SpatialWorkspaceDirector d, FloorAimController aim, Vector3 to, float seconds)
    {
        Vector3 from = _target;
        int n = Mathf.Max(1, Mathf.CeilToInt(seconds / Dt));
        for (int i = 1; i <= n; i++)
        {
            _target = Vector3.Lerp(from, to, i / (float)n);
            Push(d, aim, Dt);
        }
        Push(d, aim, 0.5f);             // let the smoothing settle
    }

    /// <summary>Point the simulated hand at the middle of the footprint, not pinching.</summary>
    public static string AimAtFootprint()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!d.CanAim) return "no floor preview open (call Open first) | " + Report();
        _pinch = false;
        _target = FootprintCentre(d);
        Push(d, aim, 0.6f);
        return Report();
    }

    /// <summary>Point 1.2 m to the side of the footprint (off it), not pinching.</summary>
    public static string AimOffFootprint()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!d.CanAim) return "no floor preview open | " + Report();
        _pinch = false;
        RootPose p = d.Core.ShownPose;
        _target = FootprintCentre(d) + p.rotation * Vector3.right * 1.2f;
        Push(d, aim, 0.6f);
        return Report();
    }

    public static string Pinch()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        _pinch = true;
        Push(d, aim, 0.1f);
        return Report();
    }

    public static string Release()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        _pinch = false;
        Push(d, aim, 0.7f);             // past the release debounce and the re-grab cooldown
        return Report();
    }

    /// <summary>Hold the hand where it is for one more second (pinch state unchanged).</summary>
    public static string Wait1s()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        Push(d, aim, 1f);
        return Report();
    }

    static string DragBy(float right, float forward)
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        SeatedUser u = User(d);
        Sweep(d, aim, _target + u.Right * right + u.forward * forward, 0.6f);
        return Report();
    }

    /// <summary>Move the aim point 0.3 m (relative to the user's view). With the pinch closed on the footprint this drags it.</summary>
    public static string MoveRight() { return DragBy(0.3f, 0f); }
    public static string MoveLeft() { return DragBy(-0.3f, 0f); }
    public static string MoveAway() { return DragBy(0f, 0.3f); }
    public static string MoveCloser() { return DragBy(0f, -0.3f); }

    /// <summary>Sweep the aim point far out to the user's right: past the wall of the synthetic room
    /// ("Too close to the wall" / "Off the scanned floor"), or to the 3.5 m limit with no room.</summary>
    public static string DragToWall()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        SeatedUser u = User(d);
        Vector3 to = u.head + u.Right * 6f; to.y = FloorY(d);
        Sweep(d, aim, to, 1.0f);
        return Report();
    }

    /// <summary>Sweep the aim point onto the user's desk (synthetic room: "Something is in the way").</summary>
    public static string DragOntoDesk()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        SeatedUser u = User(d);
        Vector3 to = u.head + u.forward * 1.3f + u.Right * 0.2f; to.y = FloorY(d);
        Sweep(d, aim, to, 1.0f);
        return Report();
    }

    /// <summary>Sweep the aim point to just behind the user's right shoulder, 0.85 m away ("Too close to you").</summary>
    public static string DragTowardUser()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        SeatedUser u = User(d);
        Vector3 to = u.head + (u.Right - u.forward).normalized * 0.85f; to.y = FloorY(d);
        Sweep(d, aim, to, 1.0f);
        return Report();
    }

    // ───────────────────────── whole gestures ─────────────────────────

    /// <summary>Aim at the footprint, pinch, drag it 0.5 m away from the user, let go. Since the headset
    /// feedback fix letting go on a ready spot PLACES the office (see DragAndRelease, which reports it);
    /// the root has not moved yet when this returns (the move runs over the next frames).</summary>
    public static string FullDrag()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!d.CanAim) return "no floor preview open (call Open first) | " + Report();
        var ws = Root();
        Vector3 rootBefore = ws != null ? ws.position : Vector3.zero;
        Vector3 before = d.Core.PreviewPose.position;

        _pinch = false; _target = FootprintCentre(d);
        Push(d, aim, 0.7f);
        bool over = aim.Session.OverFootprint(_hand);
        _pinch = true;
        Push(d, aim, 0.1f);
        SeatedUser u = User(d);
        Vector3 outward = before - u.head; outward.y = 0f;
        outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : u.Right;
        Sweep(d, aim, _target + outward * 0.5f, 0.8f);
        FloorAimState during = aim.Session.State;
        Vector3 shownDuring = d.Core.ShownPose.position;
        _pinch = false;
        Push(d, aim, 0.7f);

        Vector3 after = d.Core.PreviewPose.position;
        bool rootMoved = ws != null && (ws.position - rootBefore).sqrMagnitude > 1e-10f;
        return $"FullDrag: overBeforePinch={over} stateWhileDragging={during} footprint {before:F2} -> (held {shownDuring:F2}) -> {after:F2} moved={(after - before).magnitude:F2} m; officeRootMoved={rootMoved} | " + Report();
    }

    /// <summary>Aim at the footprint and keep a pinch still for 1.4 s: the ring fills and the spot is confirmed
    /// (the office then starts to move, through the unchanged Confirm).</summary>
    public static string HoldToConfirm()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!d.CanAim) return "no floor preview open | " + Report();
        _pinch = false; _target = FootprintCentre(d);
        Push(d, aim, 0.7f);
        _pinch = true;
        Push(d, aim, 0.5f);
        float half = aim.Session.HoldProgress;
        Push(d, aim, 0.9f);
        _pinch = false;
        aim.ClearSimulation();
        return $"HoldToConfirm: ringAt0.5s={half:F2} confirms={aim.Session.HoldConfirms}/{aim.Session.HoldConfirmAttempts} | " + Report();
    }

    /// <summary>A short pinch (0.15 s) on the footprint: nothing may happen.</summary>
    public static string Tap()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!d.CanAim) return "no floor preview open | " + Report();
        Vector3 before = d.Core.PreviewPose.position;
        _pinch = false; _target = FootprintCentre(d);
        Push(d, aim, 0.7f);
        _pinch = true; Push(d, aim, 0.15f);
        _pinch = false; Push(d, aim, 0.7f);
        return $"Tap: footprintMoved={(d.Core.PreviewPose.position - before).magnitude:F3} m stillPreviewing={d.IsPreviewing} | " + Report();
    }

    /// <summary>Pinch while pointing away from the footprint, then sweep across it: it must not be grabbed.</summary>
    public static string PinchOffFootprint()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!d.CanAim) return "no floor preview open | " + Report();
        Vector3 before = d.Core.PreviewPose.position;
        Vector3 centre = FootprintCentre(d);
        RootPose p = d.Core.ShownPose;
        _pinch = false; _target = centre + p.rotation * Vector3.right * 1.2f;
        Push(d, aim, 0.7f);
        _pinch = true; Push(d, aim, 0.2f);
        Sweep(d, aim, centre - p.rotation * Vector3.right * 1.2f, 1.0f);
        _pinch = false; Push(d, aim, 0.7f);
        return $"PinchOffFootprint: footprintMoved={(d.Core.PreviewPose.position - before).magnitude:F3} m (must be 0) | " + Report();
    }

    /// <summary>Two seconds of a hand that shakes by 3 cm on the floor, no pinch: how far the smoothed aim point wanders.</summary>
    public static string Jitter()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!d.CanAim) return "no floor preview open | " + Report();
        _pinch = false; _target = FootprintCentre(d);
        Push(d, aim, 0.7f);
        Vector3 mn = aim.Session.Cursor(_hand), mx = mn;
        for (int i = 0; i < 144; i++)
        {
            Push(d, aim, Dt, new Vector3(0.03f * (i % 2 == 0 ? 1f : -1f), 0f, 0.03f * (i % 3 == 0 ? 1f : -1f)));
            Vector3 c = aim.Session.Cursor(_hand);
            mn = Vector3.Min(mn, c); mx = Vector3.Max(mx, c);
        }
        return $"Jitter: raw shake +-0.03 m, smoothed aim point wandered {(mx - mn).magnitude:F3} m | " + Report();
    }

    // ───────────────────────── drag and let go places it ─────────────────────────

    /// <summary>A 5 x 5 m synthetic room around the head (floor 1.15 m below the eyes), the user's desk in
    /// front, and optionally a 2 m cabinet 2.31 m to the right; flags on; floor preview open.</summary>
    static bool OpenRoom5(SpatialWorkspaceDirector d, FloorAimController aim, bool cabinet)
    {
        d.SetFlags(true, true);
        if (d.IsPreviewing) d.Cancel();
        SeatedUser u = User(d);
        float floorY = u.head.y - 1.15f;
        var room = SyntheticRoomGeometry.Rectangle(u.head.x, u.head.z, 5f, 5f, floorY, floorY + 2.5f);
        float yaw = Mathf.Atan2(u.forward.x, u.forward.z) * Mathf.Rad2Deg;
        Vector3 desk = u.head + u.forward * 0.75f;
        room.AddFurniture("TABLE", new Vector3(desk.x, floorY + 0.375f, desk.z), new Vector3(1.4f, 0.75f, 0.7f), yaw);
        if (cabinet)
        {
            Vector3 c = u.head + u.Right * 2.31f - u.forward * 0.1f;
            room.AddFurniture("STORAGE", new Vector3(c.x, floorY + 1.0f, c.z), new Vector3(0.3f, 2.0f, 0.4f), yaw);
        }
        room.roomId = "floor-aim-drive-room";
        d.UseSyntheticRoom(room);
        d.RequestFloor();
        aim.ClearSimulation();
        _pinch = false;
        return d.CanAim;
    }

    /// <summary>A floor spot (root position) relative to the user: metres to their right and forward.</summary>
    static Vector3 Spot(SpatialWorkspaceDirector d, float right, float forward)
    {
        SeatedUser u = User(d);
        Vector3 p = u.head + u.Right * right + u.forward * forward;
        p.y = FloorY(d);
        return p;
    }

    /// <summary>
    /// Aim at the footprint, pinch, drag so the office root ends at <paramref name="root"/>, rest there
    /// for 0.4 s, let go. Reports what the footprint showed and said just before letting go, and after.
    /// </summary>
    static string DragRootAndRelease(string name, SpatialWorkspaceDirector d, FloorAimController aim, Vector3 root)
    {
        var ws = Root();
        Vector3 rootBefore = ws != null ? ws.position : Vector3.zero;
        Vector3 from = d.Core.PreviewPose.position;
        int places = aim.Session != null ? aim.Session.ReleasePlaces : 0;

        _pinch = false; _target = FootprintCentre(d);
        Push(d, aim, 0.7f);
        bool over = aim.Session.OverFootprint(_hand);
        _pinch = true;
        Push(d, aim, 0.1f);
        Vector3 cursor = _target + (root - from); cursor.y = FloorY(d);
        Sweep(d, aim, cursor, 0.8f);                 // ends with 0.5 s at rest, pinch held
        FloorAimState during = aim.Session.State;
        bool readyShown = d.Core.ShownValid;
        string wordsBefore = d.Core.FootprintWords;
        Vector3 shown = d.Core.ShownPose.position;

        _pinch = false;
        Push(d, aim, 0.7f);
        bool placed = aim.Session.ReleasePlaces > places;
        bool rootMovedYet = ws != null && (ws.position - rootBefore).sqrMagnitude > 1e-10f;
        string route = "";
        if (placed)
        {
            WorkspaceRoute r = d.Core.LastRoute;
            route = $" route={r.count} poses";
            for (int i = 0; i < r.count; i++) route += " " + r[i].position.ToString("F2");
            route += $" swept clear={d.Core.LastRouteCheck.clear}";
            aim.ClearSimulation();
        }
        return $"{name}: over={over} whileDragging={during} readyShown={readyShown} wordsBeforeRelease='{wordsBefore}' footprintAt={shown:F2} target={root:F2}"
               + $" | placed={placed} mode={d.Mode} previewing={d.IsPreviewing} mover={d.Core.Mover.Phase} wordsAfter='{d.Core.FootprintWords}' status='{d.StatusText}'"
               + $" rootMovedYet={rootMovedYet}{route} | " + Report();
    }

    /// <summary>5 x 5 room: drag the footprint from the first offered spot 0.3 m further from the user and let go.
    /// Expect readyShown=True, wordsBeforeRelease='Let go to place it here', placed=True.</summary>
    public static string DragAndRelease()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!OpenRoom5(d, aim, false)) return "no floor preview could be opened (is the office at the desk and at rest?) | " + Report();
        SeatedUser u = User(d);
        Vector3 from = d.Core.PreviewPose.position;
        Vector3 outward = from - u.head; outward.y = 0f;
        outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : u.Right;
        return DragRootAndRelease("DragAndRelease", d, aim, from + outward * 0.3f);
    }

    /// <summary>5 x 5 room: a spot beside and a little behind the right shoulder, 1.35 m away (the usual
    /// up-and-over route turns over the head there and was refused). Expect placed=True and a route whose
    /// second pose is stepped sideways from the desk.</summary>
    public static string ReleaseBesideShoulder()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!OpenRoom5(d, aim, false)) return "no floor preview could be opened (is the office at the desk and at rest?) | " + Report();
        return DragRootAndRelease("ReleaseBesideShoulder", d, aim, Spot(d, 1.25f, -0.5f));
    }

    /// <summary>5 x 5 room with a 2 m cabinet 0.2 m beyond a spot 1.7 m to the right: the office could stand
    /// there but cannot come down there. Expect readyShown=False, words "The way there is blocked by STORAGE",
    /// placed=False and the office root unchanged.</summary>
    public static string ReleaseWhereBlocked()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!OpenRoom5(d, aim, true)) return "no floor preview could be opened (is the office at the desk and at rest?) | " + Report();
        return DragRootAndRelease("ReleaseWhereBlocked", d, aim, Spot(d, 1.7f, -0.1f));
    }

    /// <summary>Pinch on the footprint while the hand shakes by 3 cm, let go after 0.2 s. Expect placed=False, drops unchanged.</summary>
    public static string TwitchRelease()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!d.CanAim) return "no floor preview open (call Open first) | " + Report();
        Vector3 before = d.Core.PreviewPose.position;
        _pinch = false; _target = FootprintCentre(d);
        Push(d, aim, 0.7f);
        int drops = aim.Session.Drops, places = aim.Session.ReleasePlaces;
        _pinch = true;
        for (int i = 0; i < 14; i++) Push(d, aim, Dt, new Vector3(0.03f * (i % 2 == 0 ? 1f : -1f), 0f, 0.03f * (i % 3 == 0 ? 1f : -1f)));
        _pinch = false;
        Push(d, aim, 0.7f);
        return $"TwitchRelease: placed={aim.Session.ReleasePlaces > places} dropsAdded={aim.Session.Drops - drops} footprintMoved={(d.Core.PreviewPose.position - before).magnitude:F3} m previewing={d.IsPreviewing} | " + Report();
    }

    /// <summary>Pinch, drag 0.5 m to the side in 0.1 s, let go at once. Expect placed=False (held under 0.25 s).</summary>
    public static string FlickRelease()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!d.CanAim) return "no floor preview open (call Open first) | " + Report();
        _pinch = false; _target = FootprintCentre(d);
        Push(d, aim, 0.7f);
        int places = aim.Session.ReleasePlaces;
        _pinch = true;
        Push(d, aim, 0.04f);
        _target += User(d).Right * 0.5f;
        Push(d, aim, 0.1f);
        FloorAimState during = aim.Session.State;
        float held = aim.Session.HeldSeconds;
        _pinch = false;
        Push(d, aim, 0.7f);
        return $"FlickRelease: whileDragging={during} held={held:F2}s placed={aim.Session.ReleasePlaces > places} previewing={d.IsPreviewing} | " + Report();
    }

    // ───────────────────────── keys ─────────────────────────

    public static string Next() { var d = One<SpatialWorkspaceDirector>(); if (d != null) d.NextCandidate(); return Report(); }
    public static string Confirm() { var d = One<SpatialWorkspaceDirector>(); if (d != null) d.ConfirmPlacement(); return Report(); }
    public static string Cancel() { var d = One<SpatialWorkspaceDirector>(); if (d != null) d.CancelPlacement(); return Report(); }
    public static string Desk() { var d = One<SpatialWorkspaceDirector>(); if (d != null) d.RequestDesk(); return Report(); }

    /// <summary>Stop standing in for the hands (the real hands are read again).</summary>
    public static string Clear()
    {
        var aim = One<FloorAimController>();
        if (aim != null) aim.ClearSimulation();
        _pinch = false;
        return Report();
    }
}
