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
/// Typical run:  Open -> AimAtFootprint -> Pinch -> DragToWall -> Release (over the wall: not moved, words) -> PlaceHere
/// No room scan: OpenNoRoom -> Pinch -> DragTowardUser -> Release -> PlaceHere
///
/// rc6 deliberate placement: letting go NEVER moves the office; the Place here key (guide card or
/// console) does, through Confirm and its swept route. Each of these opens its own preview; call Desk
/// and wait for the office to be back between them. The move runs over the next frames: call Report
/// a few seconds later to see the root at the spot.
///   DragAndRelease          5 x 5 room, drag 0.3 m outward, let go: rootMoved=False, stage NeedsConfirmation,
///                           canPlaceHere=True, Place here key shown on the guide card
///   PlaceHere               press the card's Place here key (or the director's PlaceHere): mode Floor, stage Moving,
///                           route = the swept route; Report after 3 s shows the root at the spot
///   ReleaseBesideShoulder   5 x 5 room, a spot 1.35 m away beside and behind the right shoulder: released, not moved;
///                           then PlaceHere moves it along a side step
///   ReleaseWhereBlocked     5 x 5 room, a 2 m cabinet 0.2 m beyond a spot to the right: stage RouteBlocked while
///                           dragged, words "The way there is blocked by STORAGE", rootMoved=False, no Place here key
///   TwitchRelease / FlickRelease / HoldStill   nothing moves, ever
///   HeldPaperPinch          a paper's transition hold and a grab interactor reporting a hold: pinch on the
///                           footprint and drag: grabs=0, footprint and root unchanged
/// Every report ends with the office root's position: it must not change unless PlaceHere was pressed.
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
        sb.Append($" stage={d.Core.PlaceStage} canPlaceHere={d.Core.CanPlaceHere} words='{d.Core.FootprintWords}'");
        var guide = One<PlacementGuide>();
        if (guide != null) sb.Append($" guide={guide.State} placeKey={(guide.placeKey.root == null ? "MISSING" : guide.placeKey.shown ? "shown" : "hidden")}"
                                     + $" anotherKey={(guide.anotherKey.root == null ? "MISSING" : guide.anotherKey.shown ? "shown" : "hidden")} body='{guide.BodyText}'");
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

    /// <summary>Close the pinch and hold it 0.3 s (the arbiter arms a fresh, clear pinch after 0.15 s).</summary>
    public static string Pinch()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        _pinch = true;
        Push(d, aim, 0.3f);
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

    /// <summary>Aim at the footprint, pinch, drag it 0.5 m away from the user, let go. Since rc6 letting go
    /// never moves the office: the footprint stays there and the Place here key appears when it is clear.</summary>
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
        Push(d, aim, 0.3f);
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

    /// <summary>Aim at the footprint and keep a pinch still for 2 s. Since rc6 nothing is confirmed by a gesture:
    /// expect previewing=True, mode unchanged, officeRootMoved=False.</summary>
    public static string HoldStill()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!d.CanAim) return "no floor preview open | " + Report();
        var ws = Root();
        Vector3 rootBefore = ws != null ? ws.position : Vector3.zero;
        _pinch = false; _target = FootprintCentre(d);
        Push(d, aim, 0.7f);
        _pinch = true;
        Push(d, aim, 2f);
        FloorAimState during = aim.Session.State;
        _pinch = false;
        Push(d, aim, 0.7f);
        bool rootMoved = ws != null && (ws.position - rootBefore).sqrMagnitude > 1e-10f;
        return $"HoldStill: whileHeld={during} previewing={d.IsPreviewing} officeRootMoved={rootMoved} | " + Report();
    }

    /// <summary>Kept for old run sheets: the pinch-and-hold confirm is gone (see HoldStill).</summary>
    public static string HoldToConfirm() { return HoldStill(); }

    /// <summary>
    /// Press Place here: the guide card's key when it is shown (as its poke would), else the director's
    /// PlaceHere. Reports whether the office started to move, the route and its sweep, and the stage.
    /// </summary>
    public static string PlaceHere()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        var guide = One<PlacementGuide>();
        bool viaKey = guide != null && guide.placeKey.root != null && guide.placeKey.shown;
        bool canBefore = d.Core.CanPlaceHere;
        if (viaKey) guide.PressPlace(); else d.PlaceHere();
        bool started = d.Core.IsTransitioning || d.Mode == WorkspaceMode.Floor && !d.IsPreviewing;
        WorkspaceRoute r = d.Core.LastRoute;
        string route = $" route={r.count} poses";
        for (int i = 0; i < r.count; i++) route += " " + r[i].position.ToString("F2");
        route += $" swept clear={d.Core.LastRouteCheck.clear}";
        aim.ClearSimulation();
        return $"PlaceHere: viaGuideKey={viaKey} canPlaceHereBefore={canBefore} started={started} stage={d.Core.PlaceStage}{route} | " + Report();
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

        _pinch = false; _target = FootprintCentre(d);
        Push(d, aim, 0.7f);
        bool over = aim.Session.OverFootprint(_hand);
        _pinch = true;
        Push(d, aim, 0.3f);
        Vector3 cursor = _target + (root - from); cursor.y = FloorY(d);
        Sweep(d, aim, cursor, 0.8f);                 // ends with 0.5 s at rest, pinch held
        FloorAimState during = aim.Session.State;
        FloorPlaceStage stageDuring = d.Core.PlaceStage;
        bool readyShown = d.Core.ShownValid;
        string wordsBefore = d.Core.FootprintWords;
        Vector3 shown = d.Core.ShownPose.position;

        _pinch = false;
        Push(d, aim, 0.7f);
        Push(d, aim, 1.0f);                          // a second after letting go: still nothing may move
        bool rootMoved = ws != null && (ws.position - rootBefore).sqrMagnitude > 1e-10f;
        return $"{name}: over={over} whileDragging={during} stageWhileDragging={stageDuring} readyShown={readyShown} wordsBeforeRelease='{wordsBefore}' footprintAt={shown:F2} target={root:F2}"
               + $" | rootMoved={rootMoved} mode={d.Mode} previewing={d.IsPreviewing} mover={d.Core.Mover.Phase} stageAfter={d.Core.PlaceStage} canPlaceHere={d.Core.CanPlaceHere}"
               + $" wordsAfter='{d.Core.FootprintWords}' status='{d.StatusText}' drops={aim.Session.Drops} | " + Report();
    }

    /// <summary>5 x 5 room: drag the footprint from the first offered spot 0.3 m further from the user and let go.
    /// Expect stageWhileDragging=RouteValid, rootMoved=False, stageAfter=NeedsConfirmation, canPlaceHere=True.</summary>
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
        var ws = Root();
        Vector3 rootBefore = ws != null ? ws.position : Vector3.zero;
        _pinch = false; _target = FootprintCentre(d);
        Push(d, aim, 0.7f);
        int drops = aim.Session.Drops;
        _pinch = true;
        for (int i = 0; i < 14; i++) Push(d, aim, Dt, new Vector3(0.03f * (i % 2 == 0 ? 1f : -1f), 0f, 0.03f * (i % 3 == 0 ? 1f : -1f)));
        _pinch = false;
        Push(d, aim, 0.7f);
        bool rootMoved = ws != null && (ws.position - rootBefore).sqrMagnitude > 1e-10f;
        return $"TwitchRelease: rootMoved={rootMoved} dropsAdded={aim.Session.Drops - drops} footprintMoved={(d.Core.PreviewPose.position - before).magnitude:F3} m previewing={d.IsPreviewing} | " + Report();
    }

    /// <summary>Pinch, drag 0.5 m to the side within 0.14 s, let go at once. Expect grabsAdded=0 (it never armed) and rootMoved=False.</summary>
    public static string FlickRelease()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!d.CanAim) return "no floor preview open (call Open first) | " + Report();
        var ws = Root();
        Vector3 rootBefore = ws != null ? ws.position : Vector3.zero;
        _pinch = false; _target = FootprintCentre(d);
        Push(d, aim, 0.7f);
        int grabs = aim.Session.Grabs;
        _pinch = true;
        Push(d, aim, 0.04f);
        _target += User(d).Right * 0.5f;
        Push(d, aim, 0.1f);
        FloorAimState during = aim.Session.State;
        _pinch = false;
        Push(d, aim, 0.7f);
        bool rootMoved = ws != null && (ws.position - rootBefore).sqrMagnitude > 1e-10f;
        return $"FlickRelease: whileFlicking={during} grabsAdded={aim.Session.Grabs - grabs} rootMoved={rootMoved} previewing={d.IsPreviewing} | " + Report();
    }

    /// <summary>
    /// A pinch meant for a held paper / stamp / card. Opens the 5 x 5 room preview, then holds the paper's
    /// transition hold (what DecisionPaper sets while held) AND simulates a grab interactor holding something
    /// on the pinching hand, aims at the footprint, pinches, drags 0.5 m, lets go. Expect grabsAdded=0,
    /// footprintMoved=0, rootMoved=False, owner=Holding, block=Holding. Releases the hold afterwards.
    /// </summary>
    public static string HeldPaperPinch()
    {
        SpatialWorkspaceDirector d; FloorAimController aim; string why;
        if (!Ready(out d, out aim, out why)) return why;
        if (!OpenRoom5(d, aim, false)) return "no floor preview could be opened (is the office at the desk and at rest?) | " + Report();
        var ws = Root();
        Vector3 rootBefore = ws != null ? ws.position : Vector3.zero;
        Vector3 before = d.Core.PreviewPose.position;
        Push(d, aim, 0.2f);                                  // the session starts
        int grabs = aim.Session.Grabs;
        SpatialWorkspaceState.AcquireTransitionHold();
        string result;
        try
        {
            aim.SimulateInteraction(_hand, true, false, false);
            _pinch = false; _target = FootprintCentre(d);
            Push(d, aim, 0.7f);
            bool over = aim.Session.OverFootprint(_hand);
            _pinch = true;
            Push(d, aim, 0.4f);
            HandOwner owner = aim.Session.Arbiter.Owner(_hand);
            HandBlock block = aim.Session.Arbiter.LastBlock(_hand);
            Sweep(d, aim, _target + User(d).Right * 0.5f, 0.6f);
            _pinch = false;
            Push(d, aim, 0.7f);
            bool rootMoved = ws != null && (ws.position - rootBefore).sqrMagnitude > 1e-10f;
            result = $"HeldPaperPinch: overFootprint={over} owner={owner} block={block} grabsAdded={aim.Session.Grabs - grabs}"
                     + $" footprintMoved={(d.Core.PreviewPose.position - before).magnitude:F3} m rootMoved={rootMoved} previewing={d.IsPreviewing}";
        }
        finally
        {
            SpatialWorkspaceState.ReleaseTransitionHold();
            aim.SimulateInteraction(_hand, false, false, false);
        }
        return result + " | " + Report();
    }

    // ───────────────────────── keys ─────────────────────────

    public static string Next() { var d = One<SpatialWorkspaceDirector>(); if (d != null) d.NextCandidate(); return Report(); }
    public static string Confirm() { var d = One<SpatialWorkspaceDirector>(); if (d != null) d.ConfirmPlacement(); return Report(); }
    public static string Cancel() { var d = One<SpatialWorkspaceDirector>(); if (d != null) d.CancelPlacement(); return Report(); }
    public static string Desk() { var d = One<SpatialWorkspaceDirector>(); if (d != null) d.RequestDesk(); return Report(); }

    /// <summary>What HandInteractionProbe reads from the rig's interactors (side, kind, hold / hover per hand).</summary>
    public static string Probe() { return HandInteractionProbe.Describe() + " | anyHeld=" + HandInteractionProbe.AnyHeld; }

    /// <summary>Stop standing in for the hands (the real hands are read again).</summary>
    public static string Clear()
    {
        var aim = One<FloorAimController>();
        if (aim != null) aim.ClearSimulation();
        _pinch = false;
        return Report();
    }
}
