using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Networking;
using MuseDesk.Workspace;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Makes the three characters aware of the person. One in the scene. It senses the user (head
    /// pose, the fingertips of the existing hand visuals through HandFingertips; no new rig, no
    /// camera, no physics query), decides who reacts, and tells that character's AgentPresence. It
    /// also owns the speech bubbles and the gaze chips: where they sit, how large they are for the
    /// viewing distance, and what they say.
    ///
    /// API for the voice side (exact names): <see cref="Instance"/>, <see cref="Address"/>,
    /// <see cref="Say"/>, <see cref="DescribeStatus"/>, <see cref="WhoNeedsUser"/>.
    ///
    /// Honesty: every sentence this component writes itself comes from AgentStatusSentence, which
    /// is a pure function of the reducer's agent state, the pending decision and the session's run
    /// flags, in scripted and live mode alike. The chip repeats the nameplate's words and says
    /// LIVE or SCRIPTED DEMO. <see cref="Say"/> shows the caller's text unchanged: the caller is
    /// responsible for it being true.
    ///
    /// What it senses, and who answers:
    ///   lean in (the head comes at least 10 cm closer than it has been lately) or a fingertip
    ///       within 12 cm: the nearest free character glances up, in its own manner. At most one
    ///       glance every 2 s overall and every 5 s per character. In Focus mode only a fingertip
    ///       within 6 cm counts.
    ///   index fingertip inside a character's poke volume: bounce, turn, wave or nod, and a
    ///       bubble with its status. Again within 2 s: a wobble or a laugh.
    ///   looking steadily at a character for 1.2 s (head-forward ray, a cone that is generous up
    ///       close and as narrow as the character from across the room): the chip and a soft
    ///       highlight, until the user looks away.
    ///   an open hand waving in view: the free idle characters wave back.
    ///   a hand moving fast right over a character: it ducks.
    ///   an index fingertip held still just in front of an idle character for 0.6 s: it reaches
    ///       up and taps it.
    ///   nothing at all: small idle gestures that follow the run state (look at the colleague who
    ///       is working, a stretch when there is nothing to do, the waiting Researcher presents
    ///       the paper).
    /// Characters the pipeline is using never react (AgentPresence.PipelineBusy).
    ///
    /// No allocation per frame. Runs in LateUpdate after the movement, the Animator and the bone
    /// layers, so the signs follow the heads without a frame of lag.
    /// </summary>
    [DefaultExecutionOrder(430)]
    [DisallowMultipleComponent]
    public class AgentAttention : MonoBehaviour
    {
        [Header("Wiring (assigned by AgentScripts/BuildAgentInteractionV1; found at start if left empty)")]
        public SessionController controller;
        public Transform workspaceRoot;
        [Tooltip("Parent of the bubbles and chips (WorkspaceRoot/AgentTalk).")]
        public Transform talkRoot;
        [Tooltip("The three characters, Researcher, Developer, Tester.")]
        public AgentPresence[] agents = new AgentPresence[0];

        [Header("Awareness")]
        [Tooltip("A fingertip closer than this to a character draws a glance, metres.")]
        public float handAwareRadius = 0.12f;
        [Tooltip("In Focus mode only a fingertip this close does.")]
        public float handAwareRadiusFocus = 0.06f;
        [Tooltip("The head must come this much closer than its recent distance to count as leaning in, metres.")]
        public float leanInDelta = 0.10f;
        [Tooltip("Leaning in only counts within this distance of the character, metres.")]
        public float leanInMaxDistance = 1.2f;
        [Tooltip("Seconds between two awareness glances, whoever gives them.")]
        public float glanceGlobalCooldown = 2f;

        [Header("Gaze dwell")]
        public float dwellSeconds = 1.2f;
        public float dwellLingerSeconds = 0.6f;
        [Tooltip("People look a little below where the head points. The gaze is tested along the head's forward and along a ray this many degrees below it.")]
        public float gazeDownBias = 8f;

        [Header("Signs")]
        [Tooltip("Distance the bubble and chip text is authored for (cap height about 4.7 mm). Beyond it they grow in proportion.")]
        public float referenceDistance = 0.5f;
        public float maxSignScale = 7f;
        [Tooltip("Gap between the top of the head and a sign, metres at scale 1.")]
        public float signGap = 0.008f;
        [Tooltip("Seconds a poke's status bubble stays.")]
        public float pokeBubbleSeconds = 3.5f;

        [Header("Hands")]
        [Tooltip("A hand faster than this over a character makes it duck, m/s.")]
        public float flinchSpeed = 0.9f;
        public float waveCooldown = 4f;

        [Header("Idle gestures")]
        public float idleIntervalMin = 7f;
        public float idleIntervalMax = 16f;
        [Tooltip("In Focus mode idle gestures come this many times less often.")]
        public float focusIdleSlowdown = 2.5f;

        // ───────────────────────── singleton ─────────────────────────

        static AgentAttention _instance;
        static bool _quitting;

        /// <summary>The one AgentAttention. If the scene has none (the builder was not run) one is
        /// created and wires itself at run time, so callers never get null in Play Mode.</summary>
        public static AgentAttention Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = FindAnyObjectByType<AgentAttention>();
                if (_instance == null && Application.isPlaying && !_quitting)
                {
                    var go = new GameObject("AgentAttention");
                    _instance = go.AddComponent<AgentAttention>();
                }
                return _instance;
            }
        }

        // ───────────────────────── state ─────────────────────────

        const int Hands = 3;            // two tracked hands and one synthetic (Play Mode driver)
        const int SyntheticHand = 2;

        Transform _cam;
        float _nextCamTry;
        Vector3 _headPos, _headFwd = Vector3.forward, _headRight = Vector3.right;
        bool _hasHead;

        CompletionSequence _completion;
        bool _wired;
        float _nextWireTry;
        readonly List<Renderer> _avoid = new List<Renderer>(16);
        readonly List<Material> _ownMaterials = new List<Material>(2);

        // per agent
        int _n;
        AgentStatusKind[] _kinds = new AgentStatusKind[0];
        string[] _roles = new string[0];
        PokeCombo[] _combo;
        Cooldown[] _pokeCd, _highFiveCd, _duckCd;
        float[] _baseline, _dist, _angles, _cones, _speakUntil, _signScale;
        bool[] _speaking, _anchorSet;
        Vector3[] _anchor;
        float[] _bubbleY;

        // per hand
        readonly bool[] _handValid = new bool[Hands];
        readonly bool[] _handHadPrev = new bool[Hands];
        readonly bool[] _handOpen = new bool[Hands];
        readonly Vector3[] _indexTip = new Vector3[Hands];
        readonly Transform[] _indexT = new Transform[Hands];
        readonly Vector3[] _centroid = new Vector3[Hands];
        readonly Vector3[] _prevCentroid = new Vector3[Hands];
        readonly float[] _handSpeed = new float[Hands];
        readonly WaveDetector[] _wave = { new WaveDetector(), new WaveDetector(), new WaveDetector() };

        // per hand x agent
        bool[] _inside, _near;
        HoldStillDetector[] _hold;

        readonly GazeDwellTimer _dwell = new GazeDwellTimer();
        int _shown = -1;
        Cooldown _glanceCd, _waveCd;
        string _pendingAgentId;
        float _nextPendingRefresh;

        // synthetic input (AgentScripts/AgentInteractionDrive)
        Vector3 _synthTip;
        float _synthTipUntil;
        int _synthGaze = -1;
        float _synthGazeUntil;

        readonly StringBuilder _sb = new StringBuilder(512);

        // ───────────────────────── lifecycle ─────────────────────────

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[MuseDesk] a second AgentAttention was found; it switches itself off.");
                enabled = false;
                return;
            }
            _instance = this;
        }

        void OnApplicationQuit() { _quitting = true; }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            for (int i = 0; i < _ownMaterials.Count; i++)
                if (_ownMaterials[i] != null) Destroy(_ownMaterials[i]);
            _ownMaterials.Clear();
        }

        void Start() { TryWire(); }

        // Find what the builder did not assign, and build what is missing. Runs once; retried a few
        // times a second until the characters exist.
        void TryWire()
        {
            if (_wired) return;
            _nextWireTry = Time.unscaledTime + 0.5f;

            if (controller == null) controller = FindAnyObjectByType<SessionController>();
            if (workspaceRoot == null) workspaceRoot = MuseDesk.Spatial.SpatialWorkspaceState.WorkspaceRoot;
            if (workspaceRoot == null)
            {
                GameObject ws = GameObject.Find("WorkspaceRoot");
                if (ws != null) workspaceRoot = ws.transform;
            }

            bool haveAgents = agents != null && agents.Length > 0;
            if (haveAgents)
                for (int i = 0; i < agents.Length; i++) if (agents[i] == null) haveAgents = false;
            if (!haveAgents)
            {
                AgentView[] views = FindObjectsByType<AgentView>(FindObjectsSortMode.None);
                if (views.Length == 0) return;
                var found = new List<AgentPresence>(views.Length);
                for (int step = 1; step <= 4; step++)       // Researcher, Developer, Tester, then anything else
                    for (int i = 0; i < views.Length; i++)
                    {
                        int s = AgentStatusSentence.StepOf(views[i].agentId);
                        if ((s == step) || (step == 4 && s == 0))
                        {
                            AgentPresence p = views[i].GetComponent<AgentPresence>();
                            if (p == null) p = views[i].gameObject.AddComponent<AgentPresence>();
                            found.Add(p);
                        }
                    }
                agents = found.ToArray();
            }

            _n = agents.Length;
            _kinds = new AgentStatusKind[_n];
            _roles = new string[_n];
            _combo = new PokeCombo[_n];
            _pokeCd = new Cooldown[_n];
            _highFiveCd = new Cooldown[_n];
            _duckCd = new Cooldown[_n];
            _baseline = new float[_n];
            _dist = new float[_n];
            _angles = new float[_n];
            _cones = new float[_n];
            _speakUntil = new float[_n];
            _signScale = new float[_n];
            _speaking = new bool[_n];
            _anchorSet = new bool[_n];
            _anchor = new Vector3[_n];
            _bubbleY = new float[_n];
            _liftSm = new float[_n * 2]; _liftVel = new float[_n * 2]; _liftInit = new bool[_n * 2];
            _inside = new bool[Hands * _n];
            _near = new bool[Hands * _n];
            _hold = new HoldStillDetector[Hands * _n];
            for (int i = 0; i < _hold.Length; i++) _hold[i] = new HoldStillDetector();

            // Signs and poke volumes the builder did not make.
            bool needSigns = false;
            for (int i = 0; i < _n; i++)
            {
                _roles[i] = agents[i].Role;
                _signScale[i] = 1f;
                _baseline[i] = -1f;
                if (agents[i].pokeVolume == null) AgentTalkRig.EnsurePokeVolume(agents[i]);
                if (agents[i].bubble == null || agents[i].chip == null) needSigns = true;
            }
            if (needSigns && workspaceRoot != null)
            {
                if (talkRoot == null) talkRoot = AgentTalkRig.EnsureRoot(workspaceRoot);
                TMP_FontAsset font = null;
                AgentNameplate plate = FindAnyObjectByType<AgentNameplate>();
                if (plate != null && plate.roleText != null) font = plate.roleText.font;
                Material paper = RuntimeMaterial(OfficeTokens.Paper);
                Material dark = RuntimeMaterial(OfficeTokens.Plate);
                for (int i = 0; i < _n; i++)
                {
                    if (agents[i].bubble == null) agents[i].bubble = AgentTalkRig.EnsureBubble(talkRoot, _roles[i], paper, font);
                    if (agents[i].chip == null) agents[i].chip = AgentTalkRig.EnsureChip(talkRoot, _roles[i], dark, font);
                }
            }
            if (talkRoot == null && workspaceRoot != null) talkRoot = workspaceRoot.Find(AgentTalkRig.RootName);

            // Things a sign must not sit on: the "!" above an agent, and the far-office beacon.
            _avoid.Clear();
            for (int i = 0; i < _n; i++)
            {
                AgentView v = agents[i].view;
                if (v != null && v.alert != null) v.alert.GetComponentsInChildren(true, _avoidScratch);
                for (int r = 0; r < _avoidScratch.Count; r++) _avoid.Add(_avoidScratch[r]);
                _avoidScratch.Clear();
            }
            var beacon = FindAnyObjectByType<MuseDesk.Spatial.AttentionBeacon>(FindObjectsInactive.Include);
            if (beacon != null && beacon.officeCue != null)
            {
                beacon.officeCue.GetComponentsInChildren(true, _avoidScratch);
                for (int r = 0; r < _avoidScratch.Count; r++) _avoid.Add(_avoidScratch[r]);
                _avoidScratch.Clear();
            }

            _completion = FindAnyObjectByType<CompletionSequence>();
            _dwell.dwellSeconds = dwellSeconds;
            _dwell.lingerSeconds = dwellLingerSeconds;
            _wired = true;
        }

        readonly List<Renderer> _avoidScratch = new List<Renderer>(8);

        Material RuntimeMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                // Not in the build by name: borrow the shader of a plate that is already in the scene.
                AgentNameplate plate = FindAnyObjectByType<AgentNameplate>();
                MeshRenderer mr = plate != null ? plate.GetComponentInChildren<MeshRenderer>() : null;
                if (mr != null && mr.sharedMaterial != null) shader = mr.sharedMaterial.shader;
            }
            if (shader == null) return null;
            var m = new Material(shader) { name = "AgentTalk (runtime)" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            _ownMaterials.Add(m);
            return m;
        }

        // ───────────────────────── public API (voice) ─────────────────────────

        /// <summary>
        /// The user spoke to <paramref name="role"/> ("Researcher" | "Developer" | "Tester"). The
        /// character stops fidgeting, turns head and upper body to the user, nods, holds for about
        /// four seconds and is then simply doing what the run state says (its animation state is
        /// never changed). If the pipeline is using the character right now (it walks, carries the
        /// paper, hands work over) the acknowledgement waits up to six seconds, then is dropped.
        /// </summary>
        public void Address(string role)
        {
            TryWire();
            int i = IndexOf(role);
            if (i < 0) return;
            agents[i].Acknowledge(4f);
        }

        /// <summary>
        /// A speech bubble above <paramref name="role"/> with <paramref name="text"/> for
        /// <paramref name="seconds"/>. One bubble per character; a new one replaces the old. The
        /// text is shown as given (up to three short lines, then an ellipsis): the caller must
        /// only pass text that is true to the run state.
        /// </summary>
        public void Say(string role, string text, float seconds)
        {
            TryWire();
            int i = IndexOf(role);
            if (i < 0 || agents[i].bubble == null) return;
            agents[i].bubble.Show(text, seconds);
            // The small speaking nod, unless the pipeline is using the character or someone else
            // (the voice side) already drives it.
            AgentGaze g = agents[i].gaze;
            if (g != null && g.isActiveAndEnabled && !g.IsSpeaking && agents[i].CanReact)
            {
                g.SetSpeaking(true);
                _speaking[i] = true;
            }
            if (_speaking[i]) _speakUntil[i] = Time.time + Mathf.Clamp(seconds, 0.5f, 20f);
        }

        /// <summary>A short true sentence about what <paramref name="role"/> is doing, built from
        /// the current run state: "Writing the code. Step 2 of 3." / "Waiting for your decision." /
        /// "Idle. Press Start to begin."</summary>
        public string DescribeStatus(string role)
        {
            TryWire();
            int i = IndexOf(role);
            if (i < 0) return "There is no agent with that role.";
            RefreshKinds(true);
            bool running = controller != null && controller.RunInProgress;
            bool started = controller != null && controller.HasStartedRun;
            string mate = null;
            AgentStatusKind mateKind = AgentStatusKind.Idle;
            for (int j = 0; j < _n; j++)
                if (j != i && AgentStatusSentence.IsWorking(_kinds[j])) { mate = _roles[j]; mateKind = _kinds[j]; break; }
            return AgentStatusSentence.Describe(_roles[i], _kinds[i], running, started, mate, mateKind);
        }

        /// <summary>A sentence naming the character that needs the user now (a decision is waiting,
        /// a paper is on its way, or something failed), or "Nobody needs you right now."</summary>
        public string WhoNeedsUser()
        {
            TryWire();
            RefreshKinds(true);
            return AgentStatusSentence.WhoNeedsUser(_roles, _kinds, _n);
        }

        /// <summary>The role that needs the user now ("Researcher" ...), or an empty string.</summary>
        public string RoleThatNeedsUser()
        {
            TryWire();
            RefreshKinds(true);
            return AgentStatusSentence.RoleThatNeedsUser(_roles, _kinds, _n) ?? "";
        }

        /// <summary>The character's presence component for a role, or null.</summary>
        public AgentPresence PresenceOf(string role)
        {
            TryWire();
            int i = IndexOf(role);
            return i < 0 ? null : agents[i];
        }

        int IndexOf(string role)
        {
            string r = AgentStatusSentence.NormalizeRole(role);
            if (r == null) return -1;
            for (int i = 0; i < _n; i++) if (_roles[i] == r) return i;
            return -1;
        }

        // ───────────────────────── run state ─────────────────────────

        void RefreshKinds(bool force)
        {
            float now = Time.unscaledTime;
            if (force || now >= _nextPendingRefresh)
            {
                _nextPendingRefresh = now + 0.25f;
                _pendingAgentId = null;
                if (controller != null)
                {
                    var pending = controller.Reducer.FirstPendingDecision();
                    if (pending != null) _pendingAgentId = pending.AgentId;
                }
            }
            for (int i = 0; i < _n; i++) _kinds[i] = KindOf(i);
        }

        bool PendingOn(int i)
        {
            AgentView v = agents[i].view;
            return v != null && _pendingAgentId != null && _pendingAgentId == v.agentId;
        }

        AgentStatusKind KindOf(int i)
        {
            AgentView v = agents[i].view;
            if (v == null) return AgentStatusKind.Idle;
            bool pending = PendingOn(i);
            switch (WorkstationStatusDisplay.Classify(v.State, pending))
            {
                case WorkstationStatusDisplay.Activity.Researching: return AgentStatusKind.Researching;
                case WorkstationStatusDisplay.Activity.Coding: return AgentStatusKind.Coding;
                case WorkstationStatusDisplay.Activity.Testing: return AgentStatusKind.Testing;
                case WorkstationStatusDisplay.Activity.Reporting: return AgentStatusKind.Reporting;
                case WorkstationStatusDisplay.Activity.Approaching: return AgentStatusKind.Approaching;
                case WorkstationStatusDisplay.Activity.Waiting: return AgentStatusKind.Waiting;
                case WorkstationStatusDisplay.Activity.WaitingForUser: return AgentStatusKind.WaitingForUser;
                case WorkstationStatusDisplay.Activity.Complete: return AgentStatusKind.Complete;
                case WorkstationStatusDisplay.Activity.Error: return AgentStatusKind.Error;
                // A decision from this agent is waiting but its own state has not said so yet.
                default: return pending ? AgentStatusKind.WaitingForUser : AgentStatusKind.Idle;
            }
        }

        static Color RoleColor(string role)
        {
            if (role == AgentStatusSentence.Developer) return OfficeTokens.Success;
            if (role == AgentStatusSentence.Tester) return OfficeTokens.Amber;
            return OfficeTokens.InfoBlue;
        }

        // ───────────────────────── the frame ─────────────────────────

        void LateUpdate()
        {
            if (!_wired)
            {
                if (Time.unscaledTime >= _nextWireTry) TryWire();
                if (!_wired) return;
            }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float now = Time.time;

            ResolveHead();
            RefreshKinds(false);

            bool globalBlock = MuseDesk.Spatial.SpatialWorkspaceState.IsTransitioning
                               || (_completion != null && _completion.IsPlaying);
            bool focus = FocusModeController.Active;
            for (int i = 0; i < _n; i++)
            {
                AgentPresence p = agents[i];
                p.GlobalBlock = globalBlock;
                if (_speaking[i] && (now >= _speakUntil[i] || p.PipelineBusy))
                {
                    if (p.gaze != null) p.gaze.SetSpeaking(false);
                    _speaking[i] = false;
                }
                if (_hasHead)
                {
                    Vector3 to = p.HeadPoint - _headPos;
                    _dist[i] = to.magnitude;
                }
            }

            if (_hasHead)
            {
                SenseHands(now, dt);
                HandReactions(now, dt, focus);
                LeanIn(now, dt, focus);
                GazeDwell(now, dt, focus, globalBlock);
            }
            IdleGestures(now, focus);
            PlaceSigns(now, dt);
        }

        void ResolveHead()
        {
            if (_cam == null && Time.unscaledTime >= _nextCamTry)
            {
                _nextCamTry = Time.unscaledTime + 0.5f;
                Camera cam = Camera.main;
                if (cam != null) _cam = cam.transform;
            }
            _hasHead = _cam != null;
            if (!_hasHead) return;
            _headPos = _cam.position;
            _headFwd = _cam.forward;
            _headRight = _cam.right;
        }

        // ── hands ──

        void SenseHands(float now, float dt)
        {
            IReadOnlyList<Transform> tips = HandFingertips.Get();
            int count = tips != null ? tips.Count : 0;
            int hands = count / 5;
            if (hands > 2) hands = 2;

            for (int h = 0; h < 2; h++)
            {
                bool valid = h < hands;
                Vector3 sum = Vector3.zero;
                if (valid)
                {
                    int b = h * 5;
                    for (int j = 0; j < 5; j++)
                    {
                        Transform t = tips[b + j];
                        if (t == null || !t.gameObject.activeInHierarchy) { valid = false; break; }
                        sum += t.position;
                    }
                    if (valid)
                    {
                        // Order from HandFingertips: thumb, index, middle, ring, pinky.
                        Transform thumb = tips[b], index = tips[b + 1], pinky = tips[b + 4];
                        _indexT[h] = index;
                        _indexTip[h] = index.position;
                        _centroid[h] = sum * 0.2f;
                        Vector3 pk = pinky.position;
                        _handOpen[h] = (_indexTip[h] - pk).sqrMagnitude > 0.055f * 0.055f
                                    && (thumb.position - pk).sqrMagnitude > 0.085f * 0.085f;
                    }
                }
                FinishHand(h, valid, dt);
            }

            bool synth = now < _synthTipUntil;
            if (synth)
            {
                _indexT[SyntheticHand] = null;
                _indexTip[SyntheticHand] = _synthTip;
                _centroid[SyntheticHand] = _synthTip;
                _handOpen[SyntheticHand] = false;
            }
            FinishHand(SyntheticHand, synth, dt);
        }

        void FinishHand(int h, bool valid, float dt)
        {
            _handValid[h] = valid;
            if (!valid)
            {
                _handHadPrev[h] = false;
                _handSpeed[h] = 0f;
                _indexT[h] = null;
                return;
            }
            if (_handHadPrev[h])
            {
                float inst = (_centroid[h] - _prevCentroid[h]).magnitude / dt;
                _handSpeed[h] = Mathf.Lerp(_handSpeed[h], inst, 0.5f);
            }
            _prevCentroid[h] = _centroid[h];
            _handHadPrev[h] = true;
        }

        void HandReactions(float now, float dt, bool focus)
        {
            Transform user = _cam;
            // Polish S9b (audit B C8): while the user holds a paper, stamp, card or panel, or a Select Agents carry
            // owns a hand, the characters stay out of it: no poke, glance, flinch, high five or wave. READ ONLY: the
            // probe and the claim registry are only read. The edge state (_inside, _near) still tracks, so nothing
            // fires the instant the hands become free.
            HandsBusy = MuseDesk.Agents.HandReactionGate.Busy(
                MuseDesk.Spatial.HandInteractionProbe.AnyHeld,
                MuseDesk.Spatial.HandClaimRegistry.Shared.Owner(0) == MuseDesk.Spatial.HandClaim.AgentSelect,
                MuseDesk.Spatial.HandClaimRegistry.Shared.Owner(1) == MuseDesk.Spatial.HandClaim.AgentSelect);
            bool busy = HandsBusy;
            for (int h = 0; h < Hands; h++)
            {
                bool valid = _handValid[h];
                Vector3 tip = _indexTip[h];

                for (int i = 0; i < _n; i++)
                {
                    int k = h * _n + i;
                    AgentPresence p = agents[i];
                    if (!valid)
                    {
                        _inside[k] = false; _near[k] = false; _hold[k].Reset();
                        continue;
                    }

                    // Poke: the index fingertip enters the trigger volume.
                    Collider vol = p.pokeVolume;
                    float surface = float.MaxValue;      // distance from the fingertip to the volume
                    if (vol != null && vol.enabled && vol.gameObject.activeInHierarchy)
                        surface = (vol.ClosestPoint(tip) - tip).magnitude;
                    bool inside = surface < (_inside[k] ? 0.008f : 0.002f);   // a little slack to stay in
                    if (inside && !_inside[k] && !busy && _pokeCd[i].TryFire(now, 0.3f)) OnPoke(i, now);
                    _inside[k] = inside;

                    // Awareness: a hand comes near.
                    float d = (tip - p.CenterPoint).magnitude;
                    float dHead = (tip - p.HeadPoint).magnitude;
                    if (dHead < d) d = dHead;
                    float radius = focus ? handAwareRadiusFocus : handAwareRadius;
                    bool near = d < (_near[k] ? radius * 1.25f : radius);
                    if (near && !_near[k] && !inside && !busy && _glanceCd.Ready(now))
                    {
                        if (p.Glance(_indexT[h] != null ? _indexT[h] : user)) _glanceCd.Start(now, glanceGlobalCooldown);
                    }
                    _near[k] = near;

                    // Flinch: a fast hand right over the head.
                    if (_handSpeed[h] > flinchSpeed && !inside && !busy && _duckCd[i].Ready(now))
                    {
                        Vector3 rel = _centroid[h] - p.HeadPoint;
                        float reach = Mathf.Max(0.08f, p.Height * 0.6f);
                        if (rel.y > -0.02f && rel.y < reach + 0.1f && rel.x * rel.x + rel.z * rel.z < reach * reach)
                        {
                            if (p.Duck(_indexT[h])) _duckCd[i].Start(now, 2f);
                        }
                    }

                    // High five: the fingertip held still just in front of an idle character.
                    bool zone = !inside && surface < 0.03f && !p.IsWorking && p.CanReact
                                && tip.y > p.CenterPoint.y - 0.01f
                                && Vector3.Dot(tip - p.CenterPoint, _headPos - p.CenterPoint) > 0f;
                    if (_hold[k].Tick(tip, zone && !busy, dt) && _highFiveCd[i].TryFire(now, 3f))
                    {
                        if (p.HighFive(tip)) FeedbackFor(i);
                    }
                }

                // Wave: an open hand, in view, moving side to side.
                if (h != SyntheticHand)
                {
                    bool ok = valid && _handOpen[h] && !busy;
                    float lateral = 0f;
                    if (ok)
                    {
                        Vector3 to = _centroid[h] - _headPos;
                        float dist = to.magnitude;
                        ok = dist > 0.15f && dist < 0.9f && Vector3.Angle(_headFwd, to) < 40f && OfficeInView();
                        lateral = Vector3.Dot(to, _headRight);
                    }
                    if (_wave[h].Tick(lateral, ok, now)) OnUserWave(now, focus);
                }
            }
        }

        /// <summary>True while the user's hands are busy (something held, or a Select Agents carry): the characters do not
        /// poke-react, glance at a hand, flinch, high-five or wave back. Set every frame in HandReactions.</summary>
        public bool HandsBusy { get; private set; }

        bool OfficeInView()
        {
            if (_n == 0) return false;
            Vector3 mid = Vector3.zero;
            for (int i = 0; i < _n; i++) mid += agents[i].CenterPoint;
            mid /= _n;
            return Vector3.Angle(_headFwd, mid - _headPos) < 60f;
        }

        void FeedbackFor(int i)
        {
            MuseDesk.Feedback.FeedbackController.PlayCue(MuseDesk.Feedback.FeedbackCue.KeyPress);
        }

        void OnPoke(int i, float now)
        {
            AgentPresence p = agents[i];
            int streak = _combo[i].Register(now);
            p.Poke(streak);
            // The status is shown even when the body cannot react (it is standing with the paper),
            // but not while it walks or the completion sequence plays.
            if (p.HardBusy || p.bubble == null) return;
            if (streak == 0) p.bubble.Show(DescribeStatus(_roles[i]), pokeBubbleSeconds);
            else p.bubble.Extend(2f);
        }

        void OnUserWave(float now, bool focus)
        {
            if (!_waveCd.TryFire(now, waveCooldown)) return;
            // The free characters with nothing in their hands wave back, a beat apart. If everyone
            // is working, the nearest one does and the others just glance up.
            int waved = 0;
            for (int i = 0; i < _n; i++)
            {
                AgentPresence p = agents[i];
                if (!p.CanReact || p.IsWorking) continue;
                if (focus && waved > 0) break;
                if (p.WaveBack(0.12f * waved)) waved++;
            }
            if (waved > 0) return;
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < _n; i++)
                if (agents[i].CanReact && _dist[i] < best) { best = _dist[i]; nearest = i; }
            if (nearest < 0) return;
            agents[nearest].WaveBack(0f);
            if (focus) return;
            for (int i = 0; i < _n; i++)
                if (i != nearest) agents[i].Glance(_cam);
        }

        // ── head ──

        void LeanIn(float now, float dt, bool focus)
        {
            int nearest = -1;
            float best = float.MaxValue;
            float k = 1f - Mathf.Exp(-dt / 3f);           // "lately" = about three seconds
            for (int i = 0; i < _n; i++)
            {
                float d = _dist[i];
                if (_baseline[i] < 0f) _baseline[i] = d;
                if (d < best) { best = d; nearest = i; }
            }
            if (nearest >= 0 && !focus && best < leanInMaxDistance
                && _baseline[nearest] - best > leanInDelta && _glanceCd.Ready(now))
            {
                if (agents[nearest].Glance(_cam)) _glanceCd.Start(now, glanceGlobalCooldown);
                for (int i = 0; i < _n; i++) _baseline[i] = _dist[i];   // one glance per lean
            }
            for (int i = 0; i < _n; i++) _baseline[i] = Mathf.Lerp(_baseline[i], _dist[i], k);
        }

        void GazeDwell(float now, float dt, bool focus, bool globalBlock)
        {
            int target = -1;
            if (now < _synthGazeUntil) target = _synthGaze;
            else if (!globalBlock)
            {
                Vector3 low = Quaternion.AngleAxis(gazeDownBias, _headRight) * _headFwd;
                for (int i = 0; i < _n; i++)
                {
                    AgentPresence p = agents[i];
                    Vector3 to = p.HeadPoint - _headPos;
                    float a = Vector3.Angle(_headFwd, to);
                    float b = Vector3.Angle(low, to);
                    _angles[i] = a < b ? a : b;
                    _cones[i] = AgentViewingMath.GazeConeDegrees(_dist[i], p.Height * 0.5f);
                }
                target = AgentViewingMath.PickGazeTarget(_angles, _cones, _n);
            }

            int shown = _dwell.Tick(target, dt);
            if (shown == _shown) return;
            if (_shown >= 0 && _shown < _n) agents[_shown].SetHighlight(false);
            _shown = shown;
            if (shown >= 0 && shown < _n)
            {
                agents[shown].SetHighlight(true);
                // It notices being looked at (not in Focus mode, and within the glance budget).
                if (!focus && _glanceCd.Ready(now) && agents[shown].Glance(_cam))
                    _glanceCd.Start(now, glanceGlobalCooldown);
            }
        }

        // ── idle gestures tied to the run state ──

        /// <summary>How many ambient idle gestures were skipped because Focus is on (evidence for a Play Mode driver).</summary>
        public int AmbientSuppressed { get; private set; }

        void IdleGestures(float now, bool focus)
        {
            for (int i = 0; i < _n; i++)
            {
                AgentPresence p = agents[i];
                if (now < p.NextIdleAt) continue;
                float slow = IdleGesturePolicy.StatusRepeatMultiplier(focus, focusIdleSlowdown);
                p.NextIdleAt = now + Random.Range(idleIntervalMin, idleIntervalMax) * slow;

                AgentStatusKind kind = _kinds[i];
                if (kind == AgentStatusKind.WaitingForUser)
                {
                    // Waiting on the user's decision: it presents the paper now and then. (Its look
                    // at the user is AgentGaze's own behaviour for a waiting agent.)
                    p.PresentPaper(p.gaze != null ? p.gaze.paper : null);
                    p.NextIdleAt = now + Random.Range(7f, 12f) * slow;
                    continue;
                }
                // Polish S9b (C5): the random ambient gestures below (look over at the worker, stretch, look at a
                // colleague) are not backed by any state: none of them under Focus. The status-backed paper
                // presentation above still shows, at the slower focus repeat.
                if (!IdleGesturePolicy.AmbientAllowed(focus)) { AmbientSuppressed++; continue; }
                if (!p.CanReact) continue;
                if (kind != AgentStatusKind.Idle && kind != AgentStatusKind.Complete) continue;

                // Nothing to do. Look over at whoever is working; otherwise stretch or look at a colleague.
                int worker = -1;
                for (int j = 0; j < _n; j++)
                    if (j != i && AgentStatusSentence.IsWorking(_kinds[j])) { worker = j; break; }
                float r = Random.value;
                if (worker >= 0 && r < 0.6f)
                {
                    p.LookAt(agents[worker].head, Random.Range(1.2f, 2f), 0.75f);
                }
                else if (r < 0.8f && !focus)
                {
                    p.Stretch();
                }
                else if (_n > 1)
                {
                    int other = (i + 1 + Random.Range(0, _n - 1)) % _n;
                    p.LookAt(agents[other].head, 1.2f, 0.6f);
                }
            }
        }

        // ── signs ──

        void PlaceSigns(float now, float dt)
        {
            float parentScale = talkRoot != null ? Mathf.Max(1e-4f, Mathf.Abs(talkRoot.lossyScale.x)) : 1f;
            bool live = controller != null && controller.IsLive;

            for (int i = 0; i < _n; i++)
            {
                AgentPresence p = agents[i];
                AgentSpeechBubble bubble = p.bubble;
                AgentStatusChip chip = p.chip;
                bool bubbleUp = false;
                if (bubble != null) { bubble.Tick(now, dt); bubbleUp = bubble.Visible; }

                if (chip != null)
                {
                    bool want = _shown == i && (bubble == null || !bubble.Wanted);
                    if (want)
                    {
                        AgentView v = p.view;
                        chip.SetContent(_roles[i], live ? "LIVE" : "SCRIPTED DEMO",
                            WorkstationStatusDisplay.ActivityWords(v != null ? v.State : null, PendingOn(i)),
                            RoleColor(_roles[i]));
                        if (!chip.Wanted) chip.Show();
                    }
                    else if (chip.Wanted) chip.Hide();
                    chip.Tick(dt);
                }
                bool chipUp = chip != null && chip.Visible;
                if (!bubbleUp && !chipUp) { _anchorSet[i] = false; _liftInit[i * 2] = false; _liftInit[i * 2 + 1] = false; continue; }
                if (!chipUp) _liftInit[i * 2] = false;
                if (!bubbleUp) _liftInit[i * 2 + 1] = false;

                // Above the head, eased a little so the sign does not shake with the animation.
                Vector3 top = p.TopPoint;
                if (!_anchorSet[i] || (top - _anchor[i]).sqrMagnitude > 0.05f * 0.05f)
                {
                    _anchor[i] = top; _anchorSet[i] = true;
                    _liftInit[i * 2] = false; _liftInit[i * 2 + 1] = false;   // the anchor jumped: re-seed the lift too
                }
                else _anchor[i] = Vector3.Lerp(_anchor[i], top, 1f - Mathf.Exp(-dt / 0.08f));

                float dist = _hasHead ? (_anchor[i] - _headPos).magnitude : referenceDistance;
                float s = AgentViewingMath.DistanceScale(dist, referenceDistance, maxSignScale);
                _signScale[i] = s;
                Vector3 basePos = _anchor[i] + Vector3.up * (signGap * s);

                if (chipUp)
                {
                    Vector3 pos = basePos;
                    pos.y = SmoothLift(i * 2, basePos.y, AvoidLift(pos, chip.Width * 0.5f * s, chip.Height * s), dt);
                    Place(chip.transform, pos, s * Mathf.Max(0.001f, chip.Ease), parentScale);
                }
                if (bubbleUp)
                {
                    Vector3 pos = basePos;
                    float halfW = bubble.Width * 0.5f * s, h = bubble.Height * s;
                    pos.y = AvoidLift(pos, halfW, h);
                    // Clear of the bubbles of the characters placed before this one.
                    for (int j = 0; j < i; j++)
                    {
                        AgentSpeechBubble other = agents[j].bubble;
                        if (other == null || !other.Visible) continue;
                        float sj = _signScale[j];
                        Vector3 oj = other.transform.position;
                        float lateral = Mathf.Abs(Vector3.Dot(pos - oj, _headRight));
                        if (lateral > halfW + other.Width * 0.5f * sj + 0.004f * s) continue;
                        float oTop = _bubbleY[j] + other.Height * sj;
                        if (pos.y > oTop || pos.y + h < _bubbleY[j]) continue;
                        pos.y = oTop + 0.004f * s;
                    }
                    pos.y = SmoothLift(i * 2 + 1, basePos.y, pos.y, dt);
                    _bubbleY[i] = pos.y;
                    Place(bubble.transform, pos, s * Mathf.Max(0.001f, bubble.Ease), parentScale);
                }
            }
        }

        // Polish S9a: the avoid-lift (and the clear-of-neighbour step) used to move a sign's y by up to a few
        // centimetres in one frame. Only that OFFSET above the head-following base is smoothed (critically damped),
        // so the sign still tracks the head at once. Slot = agent*2 (chip) or agent*2+1 (bubble). SafeStep inside Follow
        // bounds a hitch; Reduced Motion halves the smoothing time (shorter, never hidden).
        const float LiftSmoothSeconds = 0.12f;
        float[] _liftSm = new float[0], _liftVel = new float[0];
        bool[] _liftInit = new bool[0];

        float SmoothLift(int slot, float baseY, float targetY, float dt)
        {
            if (_liftInit.Length <= slot)
            {
                System.Array.Resize(ref _liftSm, slot + 1);
                System.Array.Resize(ref _liftVel, slot + 1);
                System.Array.Resize(ref _liftInit, slot + 1);
            }
            float want = targetY - baseY;
            if (!_liftInit[slot]) { _liftInit[slot] = true; _liftSm[slot] = want; _liftVel[slot] = 0f; return targetY; }
            float st = LiftSmoothSeconds * (MuseDesk.Motion.MotionSettings.ReducedMotion ? MuseDesk.Motion.MotionSettings.ReducedScale : 1f);
            _liftSm[slot] = MuseDesk.Motion.Damped.Follow(_liftSm[slot], want, ref _liftVel[slot], st, dt, 1e-4f);
            return baseY + _liftSm[slot];
        }

        // Raise a sign whose box (centre x/z at pos, from pos.y up by height) would sit on one of
        // the things to keep clear of.
        float AvoidLift(Vector3 pos, float halfWidth, float height)
        {
            float y = pos.y;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int r = 0; r < _avoid.Count; r++)
                {
                    Renderer rd = _avoid[r];
                    if (rd == null || !rd.enabled || !rd.gameObject.activeInHierarchy) continue;
                    Bounds b = rd.bounds;
                    if (b.size.y < 0.003f) continue;          // hidden by scale
                    float dx = b.center.x - pos.x, dz = b.center.z - pos.z;
                    float reach = halfWidth + Mathf.Max(b.extents.x, b.extents.z) + 0.004f;
                    if (dx * dx + dz * dz > reach * reach) continue;
                    if (y + height < b.min.y - 0.004f || y > b.max.y + 0.004f) continue;
                    y = b.max.y + 0.006f;
                }
            }
            return y;
        }

        // Upright, turned to the user by yaw only, readable side (local -Z) toward the head.
        void Place(Transform t, Vector3 pos, float worldScale, float parentScale)
        {
            Vector3 away = pos - _headPos;
            away.y = 0f;
            if (_hasHead && away.sqrMagnitude > 1e-6f)
                t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            t.position = pos;
            // The office root may be scaled in a spatial mode: the sign keeps its world size.
            Transform parent = t.parent;
            if (parent != null) parentScale = Mathf.Max(1e-4f, Mathf.Abs(parent.lossyScale.x));
            float ls = worldScale / parentScale;
            t.localScale = new Vector3(ls, ls, ls);
        }

        // ───────────────────────── Play Mode driver hooks ─────────────────────────

        /// <summary>Driver: put a synthetic index fingertip inside the character's poke volume for a
        /// moment, so the real detection path runs.</summary>
        public string DebugPoke(string role)
        {
            TryWire();
            int i = IndexOf(role);
            if (i < 0) return "no agent '" + role + "'";
            AgentPresence p = agents[i];
            if (p.pokeVolume == null) return _roles[i] + ": no poke volume";
            _inside[SyntheticHand * _n + i] = false;
            _synthTip = p.pokeVolume.bounds.center;
            _synthTipUntil = Time.time + 0.25f;
            return _roles[i] + ": synthetic fingertip in the poke volume for 0.25 s";
        }

        /// <summary>Driver: a synthetic fingertip 8 cm in front of the character for 1.5 s.</summary>
        public string DebugHandNear(string role)
        {
            TryWire();
            int i = IndexOf(role);
            if (i < 0) return "no agent '" + role + "'";
            AgentPresence p = agents[i];
            Vector3 toUser = _hasHead ? (_headPos - p.CenterPoint) : p.transform.forward;
            toUser.y = 0f;
            if (toUser.sqrMagnitude < 1e-6f) toUser = p.transform.forward;
            _near[SyntheticHand * _n + i] = false;
            _synthTip = p.CenterPoint + toUser.normalized * 0.08f;
            _synthTipUntil = Time.time + 1.5f;
            return _roles[i] + ": synthetic fingertip 8 cm in front for 1.5 s";
        }

        /// <summary>Driver: a synthetic fingertip at a world position for some seconds.</summary>
        public void DebugFingertip(Vector3 world, float seconds)
        {
            _synthTip = world;
            _synthTipUntil = Time.time + seconds;
        }

        /// <summary>Driver: behave as if the user looked at the character for this long.</summary>
        public string DebugGaze(string role, float seconds)
        {
            TryWire();
            int i = IndexOf(role);
            if (i < 0) return "no agent '" + role + "'";
            _synthGaze = i;
            _synthGazeUntil = Time.time + Mathf.Max(0f, seconds);
            return _roles[i] + ": synthetic gaze for " + seconds.ToString("F1") + " s";
        }

        /// <summary>Driver: as if an open hand had just waved.</summary>
        public string DebugWave()
        {
            TryWire();
            OnUserWave(Time.time, FocusModeController.Active);
            return "wave";
        }

        /// <summary>Driver: one line per character with everything this pass decides.</summary>
        public string DebugReport()
        {
            TryWire();
            RefreshKinds(true);
            _sb.Clear();
            _sb.Append("AgentAttention wired=").Append(_wired).Append(" agents=").Append(_n)
               .Append(" head=").Append(_hasHead).Append(" dwellShown=").Append(_shown)
               .Append(" focus=").Append(FocusModeController.Active)
               .Append(" live=").Append(controller != null && controller.IsLive)
               .Append(" run=").Append(controller != null && controller.RunInProgress)
               .Append(" avoid=").Append(_avoid.Count).Append('\n');
            _sb.Append("  whoNeedsUser='").Append(AgentStatusSentence.WhoNeedsUser(_roles, _kinds, _n)).Append("'\n");
            for (int i = 0; i < _n; i++)
            {
                AgentPresence p = agents[i];
                _sb.Append("  ").Append(_roles[i]).Append(": state=").Append(p.view != null ? p.view.State : "?")
                   .Append(" kind=").Append(_kinds[i].ToString())
                   .Append(" busy=").Append(p.PipelineBusy).Append(" hard=").Append(p.HardBusy)
                   .Append(" reacting=").Append(p.Reacting)
                   .Append(" dist=").Append(_dist[i].ToString("F2"))
                   .Append(" height=").Append(p.Height.ToString("F3"))
                   .Append(" signScale=").Append(_signScale[i].ToString("F2"))
                   .Append(" poke=").Append(p.pokeVolume != null)
                   .Append(" bubble=").Append(p.bubble != null && p.bubble.Wanted ? "'" + p.bubble.Text + "'" : "off")
                   .Append(" chip=").Append(p.chip != null && p.chip.Wanted)
                   .Append(" status='").Append(DescribeStatus(_roles[i])).Append("'\n");
            }
            return _sb.ToString();
        }
    }
}
