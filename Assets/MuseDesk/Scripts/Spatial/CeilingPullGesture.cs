using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using MuseDesk.Workspace;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// "Reach up and pull it down". In Ceiling mode, with the pod docked, a pinch that STARTS while a
    /// hand ray or the head's gaze passes near the pod or its mast, pulled down by about 12 cm within
    /// 1.2 s and released, asks the director to bring the pod down. With the pod beside the user, a pinch
    /// started on the pod/mast and pushed up the same way sends it back up. The decisions are in
    /// <see cref="CeilingPullMachine"/> and <see cref="CeilingPullMath"/> (pure, tested).
    ///
    /// This only READS the hands (the pointer pose and the index pinch of the rig's Interaction SDK
    /// hands, exactly as <see cref="FloorAimController"/> does; no rig, no interactor, no collider) and
    /// DRAWS a small cue: a ring under the pod that fills while pulling and follows the hand, an arrow, and
    /// a short label. It never touches the pod machine or controller: it calls
    /// <see cref="SpatialWorkspaceDirector.BringPodDown"/> / <see cref="SpatialWorkspaceDirector.SendPodUp"/>,
    /// which sweep the route again and may refuse; a refusal shows its reason on the cue (the director also
    /// puts it on the mode console's status line).
    ///
    /// Hand ownership (rc6): a pinch arms only through <see cref="HandOwnershipArbiter"/> (claim Summon):
    /// fresh, held minPinchSeconds, nothing held in either hand (or a paper's transition hold), and this
    /// hand's grab, poke and UI ray clear of everything for nearClearSeconds (read by
    /// <see cref="HandInteractionProbe"/>). The pod must be the identified target (under the hand ray or
    /// the gaze) when the pinch begins AND when it arms, and must stay under the hand ray or the gaze
    /// (with a wider radius and a short gap) until the pull fires, or the pull is cancelled. A lost hand
    /// cancels and is never a release; anything picked up mid-pull cancels; after any cancel the hand
    /// must open and pinch again. Outside Ceiling mode (or with the pod mid-move) it does one property
    /// read per frame. The cue has no colliders or interactables, so SetActive on it is safe.
    /// </summary>
    public class CeilingPullGesture : MonoBehaviour
    {
        [Header("Wiring")]
        public SpatialWorkspaceDirector director;
        public HandVisual leftHandVisual;
        public HandVisual rightHandVisual;
        [Tooltip("The user's head. The director's head, then Camera.main, when empty.")]
        public Transform head;

        [Header("Behaviour")]
        public CeilingPullSettings settings = new CeilingPullSettings();
        [Tooltip("Pinch timing and hand ownership (fresh pinch, debounce, near-object clear window, tracking grace).")]
        public HandOwnershipSettings ownership = new HandOwnershipSettings();
        [Tooltip("When the pod is beside the user their gaze is on it all the time, so only the hand ray counts for pushing it up.")]
        public bool gazeCountsWhenEngaged = false;
        [Tooltip("How long a refusal stays on the cue (seconds).")]
        public float reasonSeconds = 3f;

        [Header("Cue (built by BuildCeilingPullV1; nothing interactable inside)")]
        public GameObject visuals;
        [Tooltip("Positioned under the pod and turned so the label reads from the head.")]
        public Transform cueRoot;
        public MeshFilter ringTrack;
        public MeshFilter ringFill;
        public MeshFilter arrow;
        public TMP_Text label;

        [Header("Cue shape (metres)")]
        public float ringRadius = 0.09f;
        public float ringWidth = 0.016f;
        public float arrowSize = 0.035f;
        [Tooltip("Where the ring sits below the shelf (docked) at office scale 1.")]
        public float ringDrop = 0.07f;
        public float labelDrop = 0.15f;

        const int RingSegments = 40;
        const string DownWords = "Pinch and pull down";
        const string UpWords = "Pinch and push up";

        readonly CeilingPullSession _session = new CeilingPullSession(HandClaimRegistry.Shared);
        readonly AimHandSample[] _sim = new AimHandSample[2];
        readonly AimHandSample[] _samples = new AimHandSample[2];
        readonly bool[] _simOn = new bool[2];
        readonly bool[] _simBusy = new bool[2], _simHolding = new bool[2], _simUi = new bool[2];
        readonly bool[] _targeted = new bool[2], _keep = new bool[2];
        float _nextHandSearch;
        bool _wasRelevant;
        bool _gazeSimulated;
        Vector3 _gazeForward = Vector3.forward;

        /// <summary>True when the gaze was on the pod on the last step.</summary>
        public bool LastGazeOnPod { get; private set; }

        MaterialPropertyBlock _block;
        Mesh _trackMesh, _fillMesh, _arrowMesh;
        bool _built, _visible;
        int _colorKey = -1, _segmentsShown = -1;
        int _wordsKey = -1;
        float _reasonUntil;
        string _reason = "";
        string _lastText;

        // Cached geometry of the last relevant frame (a driver reads it).
        Vector3 _top, _bottom, _centre;
        bool _haveGeometry;

        public CeilingPullMachine Machine(int hand) { return _session.Machine(hand); }
        /// <summary>Who owns which hand for this gesture (signals, last block).</summary>
        public HandOwnershipArbiter Arbiter => _session.Arbiter;
        public bool CueShown => _visible;
        /// <summary>The last refusal or cancel words shown on the cue (empty when none).</summary>
        public string Reason => Time.unscaledTime < _reasonUntil ? _reason : "";
        public int FiredCount { get; private set; }
        public int RefusedCount { get; private set; }
        public bool LastTargeted { get; private set; }

        void Awake()
        {
            if (visuals != null) visuals.SetActive(false);
        }

        void OnDisable()
        {
            _session.Reset();
            Show(false);
        }

        void OnDestroy()
        {
            if (_trackMesh != null) Destroy(_trackMesh);
            if (_fillMesh != null) Destroy(_fillMesh);
            if (_arrowMesh != null) Destroy(_arrowMesh);
        }

        void Update()
        {
            if (director == null) return;
            if (!_visible && director.Mode != WorkspaceMode.Ceiling) return;
            Step(Time.unscaledDeltaTime);
        }

        /// <summary>One frame. Update calls it; a Play Mode driver calls it with its own time step.</summary>
        public void Step(float dt)
        {
            if (director == null) return;
            CeilingPodController pod = director.pod;
            SpatialDirectorCore core = director.Core;
            Transform root = pod != null && pod.transition != null ? pod.transition.workspaceRoot : null;
            CeilingPodState podState = pod != null ? pod.State : CeilingPodState.Docked;
            bool relevant = core != null && root != null && director.Mode == WorkspaceMode.Ceiling && !director.IsPreviewing
                            && (podState == CeilingPodState.Docked || podState == CeilingPodState.Engaged);

            if (!relevant)
            {
                if (_wasRelevant)
                {
                    _session.Reset();
                    _wasRelevant = false;
                }
                _haveGeometry = false;
                if (_visible) Show(false);
                return;
            }
            if (!_wasRelevant)
            {
                // Just became pullable: a pinch already closed now is not fresh.
                _session.Reset();
                _wasRelevant = true;
            }

            CeilingPullDirection dir = podState == CeilingPodState.Docked ? CeilingPullDirection.Down : CeilingPullDirection.Up;
            bool allowed = !SpatialWorkspaceState.TransitionHeld && !core.IsTransitioning;
            ComputeGeometry(pod, root);
            float podR = settings.podRadius * root.localScale.x;
            float keep = Mathf.Max(1f, settings.targetKeepFactor);

            // Gaze: the head ray.
            Transform h = ResolveHead();
            bool gazeHit = false, gazeKeep = false;
            if (h != null && (dir == CeilingPullDirection.Down || gazeCountsWhenEngaged))
            {
                Vector3 gaze = _gazeSimulated ? _gazeForward : h.forward;
                gazeHit = CeilingPullMath.RayTargetsPod(h.position, gaze, _top, _bottom, _centre, settings.targetRadius, podR);
                gazeKeep = gazeHit || CeilingPullMath.RayTargetsPod(h.position, gaze, _top, _bottom, _centre, settings.targetRadius * keep, podR * keep);
            }
            LastGazeOnPod = gazeHit;

            // Hand ownership: one session for both hands, sharing the app's claim registry with the floor aim.
            HandOwnershipArbiter arb = _session.Arbiter;
            arb.settings = ownership;
            arb.externallyHeld = SpatialWorkspaceState.TransitionHeld;
            _session.settings = settings;
            bool anyTargeted = gazeHit;
            for (int i = 0; i < 2; i++)
            {
                AimHandSample s = _simOn[i] ? _sim[i] : ReadHand(i);
                s = s.With(_simHolding[i], _simBusy[i], _simUi[i]);
                s = HandInteractionProbe.Apply(s, i);
                _samples[i] = s;
                bool handHit = s.tracked && CeilingPullMath.RayTargetsPod(s.origin, s.direction, _top, _bottom, _centre, settings.targetRadius, podR);
                bool handKeep = handHit || (s.tracked && CeilingPullMath.RayTargetsPod(s.origin, s.direction, _top, _bottom, _centre, settings.targetRadius * keep, podR * keep));
                if (handHit) anyTargeted = true;
                _targeted[i] = handHit || gazeHit;
                _keep[i] = handKeep || gazeKeep;
            }
            _session.Tick(dt, _samples[0], _samples[1], _targeted[0], _targeted[1], _keep[0], _keep[1], allowed, dir);

            int activeHand = _session.ActiveHand;
            for (int i = 0; i < 2; i++)
            {
                CeilingPullMachine m = _session.Machine(i);
                if (_session.PinchedAtTargetWhileHeld(i)) Say("Put down what you are holding first.");
                CeilingPullState before = _session.StateBefore(i);
                if (m.State == CeilingPullState.Fired && before != CeilingPullState.Fired) Fire(pod, core, dir);
                else if (m.State == CeilingPullState.Cancelled && before != CeilingPullState.Cancelled) OnCancelled(m.LastCancel);
            }
            LastTargeted = anyTargeted;

            bool showReason = Time.unscaledTime < _reasonUntil;
            bool show = allowed && (anyTargeted || activeHand >= 0) || showReason;
            if (!show) { if (_visible) Show(false); return; }
            Render(root, dir, activeHand, showReason);
        }

        // ───────────────────────── firing ─────────────────────────

        void Fire(CeilingPodController pod, SpatialDirectorCore core, CeilingPullDirection dir)
        {
            // Same calls the director's BringPodDown / SendPodUp make (they are pass-throughs), but
            // keeping the answer: the core sweeps the route and returns false only when it refuses.
            // (A retract can be accepted without the pod state changing in this frame.)
            bool accepted = dir == CeilingPullDirection.Down ? core.RequestPodDescend() : core.RequestPodRetract();
            if (accepted)
            {
                FiredCount++;
                _reasonUntil = 0f;
                return;
            }
            // Refused. The director has put its reason in StatusText (and on the console); show it here too.
            RefusedCount++;
            string why = director.StatusText;
            Say(string.IsNullOrEmpty(why) ? "Not now. Try again in a moment." : why);
        }

        void OnCancelled(CeilingPullCancel why)
        {
            // Quiet for a plain release or drifting; say it only when something stopped a pull the user meant.
            if (why == CeilingPullCancel.Blocked) Say("Put the paper down first.");
            else if (why == CeilingPullCancel.Held) Say("Put down what you are holding first.");
            else if (why == CeilingPullCancel.Timeout) Say("A little faster: pull down in one motion.");
            else if (why == CeilingPullCancel.TargetLost) Say("Keep pointing at the office while you pull.");
            else if (why == CeilingPullCancel.Lost) Say("Hand lost. Nothing moved: pinch and pull again.");
        }

        void Say(string words)
        {
            _reason = Shorten(words, 54);
            _reasonUntil = Time.unscaledTime + reasonSeconds;
            _colorKey = -1;
        }

        static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max - 3) + "...";
        }

        // ───────────────────────── geometry ─────────────────────────

        void ComputeGeometry(CeilingPodController pod, Transform root)
        {
            float scale = root.localScale.x;
            Vector3 up = Vector3.up;
            _centre = root.position + up * (0.20f * scale);
            _bottom = pod.mastBottom != null ? pod.mastBottom.position : root.position + root.rotation * new Vector3(pod.shelfCenterOffset.x * scale, pod.mastAttachHeight * scale, pod.shelfCenterOffset.y * scale);
            _top = pod.mastTop != null ? pod.mastTop.position : _bottom + up * 0.6f;
            _haveGeometry = true;
        }

        /// <summary>Last computed mast top, mast bottom and pod centre (valid after a step in Ceiling mode).</summary>
        public bool TryGetGeometry(out Vector3 top, out Vector3 bottom, out Vector3 centre)
        {
            top = _top; bottom = _bottom; centre = _centre;
            return _haveGeometry;
        }

        Transform ResolveHead()
        {
            if (head != null) return head;
            if (director != null && director.head != null) return director.head;
            if (Time.unscaledTime < _nextHandSearch) return null;       // at most once a second
            _nextHandSearch = Time.unscaledTime + 1f;
            Camera cam = Camera.main;
            if (cam != null) head = cam.transform;
            return head;
        }

        // ───────────────────────── hands ─────────────────────────

        AimHandSample ReadHand(int index)
        {
            HandVisual visual = index == 0 ? leftHandVisual : rightHandVisual;
            if (visual == null)
            {
                if (Time.unscaledTime < _nextHandSearch) return AimHandSample.None;
                _nextHandSearch = Time.unscaledTime + 1f;
                ResolveHands();
                visual = index == 0 ? leftHandVisual : rightHandVisual;
                if (visual == null) return AimHandSample.None;
            }
            IHand hand = visual.Hand;
            if (hand == null || !hand.IsTrackedDataValid || !hand.IsPointerPoseValid) return AimHandSample.None;
            Pose pointer;
            if (!hand.GetPointerPose(out pointer)) return AimHandSample.None;
            return AimHandSample.Ray(pointer.position, pointer.rotation * Vector3.forward, hand.GetIndexFingerIsPinching());
        }

        /// <summary>Find the rig's two hand visuals by handedness (adds nothing).</summary>
        public void ResolveHands()
        {
            HandVisual[] all = FindObjectsByType<HandVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                HandVisual v = all[i];
                if (v == null || v.Hand == null) continue;
                bool isLeft = v.Hand.Handedness == Handedness.Left;
                if (isLeft && leftHandVisual == null) leftHandVisual = v;
                else if (!isLeft && rightHandVisual == null) rightHandVisual = v;
            }
        }

        // What each hand's interactors are doing comes from HandInteractionProbe (shared with the floor aim).

        // ───────────────────────── simulation (Play Mode driver, no hands needed) ─────────────────────────

        public void SimulateHand(int hand, Vector3 origin, Vector3 direction, bool pinching)
        {
            if (hand < 0 || hand > 1) return;
            _simOn[hand] = true;
            _sim[hand] = AimHandSample.Ray(origin, direction, pinching);
        }

        /// <summary>Stand in for a hand that is not tracked.</summary>
        public void SimulateHandLost(int hand)
        {
            if (hand < 0 || hand > 1) return;
            _simOn[hand] = true;
            _sim[hand] = AimHandSample.None;
        }

        /// <summary>The simulated hand is (or is not) hovering a near interactable (a paper, a stamp).</summary>
        public void SimulateBusy(int hand, bool busy) { if (hand >= 0 && hand <= 1) _simBusy[hand] = busy; }

        /// <summary>The simulated hand holds something / hovers a near object / is over a UI control.</summary>
        public void SimulateInteraction(int hand, bool holding, bool nearHover, bool uiHover)
        {
            if (hand < 0 || hand > 1) return;
            _simHolding[hand] = holding; _simBusy[hand] = nearHover; _simUi[hand] = uiHover;
        }

        /// <summary>Use this gaze direction instead of the head's forward (null: the head again).</summary>
        public void SimulateGaze(Vector3? forward)
        {
            _gazeSimulated = forward.HasValue;
            if (forward.HasValue && forward.Value.sqrMagnitude > 1e-8f) _gazeForward = forward.Value.normalized;
        }

        public void ClearSimulation()
        {
            _simOn[0] = _simOn[1] = false;
            for (int i = 0; i < 2; i++) _simBusy[i] = _simHolding[i] = _simUi[i] = false;
            _gazeSimulated = false;
        }

        // ───────────────────────── drawing ─────────────────────────

        void Show(bool on)
        {
            if (on == _visible) return;
            _visible = on;
            _colorKey = -1; _segmentsShown = -1; _wordsKey = -1; _lastText = null;
            if (visuals != null) visuals.SetActive(on);
        }

        void Render(Transform root, CeilingPullDirection dir, int activeHand, bool showReason)
        {
            if (!_built) BuildMeshes();
            Show(true);
            float scale = root.localScale.x;
            float progress = activeHand >= 0 ? _session.Machine(activeHand).Progress : 0f;
            bool pulling = activeHand >= 0;
            bool up = dir == CeilingPullDirection.Up;

            if (cueRoot != null)
            {
                // The ring hangs under the shelf and follows the pull (down) or rises into it (up).
                float drop = ringDrop * scale + (up ? (1f - progress) : progress) * settings.distance;
                Vector3 p = root.position + Vector3.down * drop;
                Quaternion face = cueRoot.rotation;
                Transform h = head != null ? head : director.head;
                if (h != null)
                {
                    Vector3 away = p - h.position; away.y = 0f;
                    if (away.sqrMagnitude > 1e-4f) face = Quaternion.LookRotation(away.normalized, Vector3.up);   // +Z away from the viewer: TMP reads correctly
                }
                cueRoot.SetPositionAndRotation(p, face);
                cueRoot.localScale = Vector3.one * Mathf.Max(0.5f, scale);
            }
            if (arrow != null)
            {
                // Points the way to move: down to pull, up to push.
                Quaternion r = up ? Quaternion.identity : Quaternion.Euler(0f, 0f, 180f);
                arrow.transform.localRotation = r;
                arrow.transform.localPosition = new Vector3(0f, -0.075f, 0f);
            }

            int segments = progress <= 0f ? 0 : Mathf.Clamp(Mathf.CeilToInt(progress * RingSegments), 1, RingSegments);
            if (segments != _segmentsShown)
            {
                _segmentsShown = segments;
                bool any = segments > 0;
                if (ringFill != null && ringFill.gameObject.activeSelf != any) ringFill.gameObject.SetActive(any);
                if (any && _fillMesh != null) _fillMesh.SetSubMesh(0, new SubMeshDescriptor(0, segments * 6, MeshTopology.Triangles));
            }

            int key = (showReason ? 1 : 0) | (pulling ? 2 : 0) | (up ? 4 : 0);
            if (key != _colorKey)
            {
                _colorKey = key;
                if (_block == null) _block = new MaterialPropertyBlock();
                Color c = showReason ? OfficeTokens.Coral : OfficeTokens.Teal;
                Tint(ringTrack, c);
                Tint(arrow, c);
                Tint(ringFill, OfficeTokens.OnPlate);
                if (label != null) label.color = showReason ? OfficeTokens.Coral : OfficeTokens.OnPlate;
            }
            if (label != null)
            {
                string text = showReason ? _reason : (up ? UpWords : DownWords);
                if (text != _lastText) { _lastText = text; label.text = text; }
                Transform lt = label.transform;
                if (lt.localPosition.y != -labelDrop) lt.localPosition = new Vector3(0f, -labelDrop, 0f);
            }
        }

        void Tint(MeshFilter f, Color c)
        {
            if (f == null) return;
            Renderer r = f.GetComponent<Renderer>();
            if (r != null) OfficeTokens.Tint(r, _block, c);
        }

        void BuildMeshes()
        {
            _built = true;
            float ro = Mathf.Max(0.02f, ringRadius), ri = Mathf.Max(0.005f, ringRadius - ringWidth);
            var verts = new Vector3[(RingSegments + 1) * 2];
            var normals = new Vector3[verts.Length];
            for (int i = 0; i <= RingSegments; i++)
            {
                float a = i / (float)RingSegments * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                verts[i * 2] = d * ro; verts[i * 2 + 1] = d * ri;
                normals[i * 2] = normals[i * 2 + 1] = Vector3.down;
            }
            var tris = new int[RingSegments * 6];
            for (int i = 0; i < RingSegments; i++)
            {
                int o0 = i * 2, i0 = o0 + 1, o1 = o0 + 2, i1 = o0 + 3, t = i * 6;
                tris[t] = o0; tris[t + 1] = o1; tris[t + 2] = i0;
                tris[t + 3] = i0; tris[t + 4] = o1; tris[t + 5] = i1;
            }
            _trackMesh = MakeMesh("CeilingPullTrack", verts, normals, tris, false);
            _fillMesh = MakeMesh("CeilingPullFill", verts, normals, tris, true);

            float s = Mathf.Max(0.01f, arrowSize);
            var av = new[] { new Vector3(0f, s, 0f), new Vector3(-s * 0.8f, -s * 0.3f, 0f), new Vector3(-s * 0.3f, -s * 0.3f, 0f),
                             new Vector3(-s * 0.3f, -s, 0f), new Vector3(s * 0.3f, -s, 0f), new Vector3(s * 0.3f, -s * 0.3f, 0f), new Vector3(s * 0.8f, -s * 0.3f, 0f) };
            var an = new Vector3[av.Length];
            for (int i = 0; i < an.Length; i++) an[i] = Vector3.back;
            // Head triangle (0,1,6) plus the stem quad (2,3,4,5); culling is off in the material.
            var at = new[] { 0, 6, 1, 2, 5, 3, 3, 5, 4 };
            _arrowMesh = MakeMesh("CeilingPullArrow", av, an, at, false);

            if (ringTrack != null) ringTrack.sharedMesh = _trackMesh;
            if (ringFill != null)
            {
                ringFill.sharedMesh = _fillMesh;
                ringFill.transform.localPosition = new Vector3(0f, 0.001f, 0f);
            }
            if (arrow != null) arrow.sharedMesh = _arrowMesh;
        }

        static Mesh MakeMesh(string name, Vector3[] verts, Vector3[] normals, int[] tris, bool dynamic)
        {
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            m.vertices = verts; m.normals = normals; m.triangles = tris;
            if (dynamic) m.MarkDynamic();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>One line for a Play Mode driver.</summary>
        public string Describe()
        {
            string s = "pull: cue=" + _visible + " targeted=" + LastTargeted + " fired=" + FiredCount + " refused=" + RefusedCount
                       + " reason='" + Reason + "'";
            for (int i = 0; i < 2; i++)
            {
                CeilingPullMachine m = _session.Machine(i);
                s += " " + (i == 0 ? "L" : "R") + "(" + m.State + " p=" + m.Progress.ToString("F2") + " last=" + m.LastCancel + " fires=" + m.Fires + " cancels=" + m.Cancels
                     + " owner=" + _session.Arbiter.Owner(i) + " block=" + _session.Arbiter.LastBlock(i) + ")";
            }
            return s;
        }
    }
}
