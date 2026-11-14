using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// Hands, Select Agents, the small desk visit with read-only /btw questions, and the large-agent portal.
    ///
    /// One pinch arbiter (the app's rc6 <see cref="HandOwnershipArbiter"/>, sharing <see cref="HandClaimRegistry.Shared"/>):
    /// anything held, or a grab/poke/UI hover, wins over these gestures; a lost hand cancels, never confirms.
    /// The carried agent is a PROJECTION (a visual clone): the home avatar stays at its desk with an "on visit"
    /// marker and the backend task is never touched. Questions go to the read-only side-question route only.
    /// </summary>
    public partial class WallHabitatController
    {
        AgentPresentationRegistry _registry;
        AgentVisitFlow _visit;
        PortalFlow _portal;
        SideConversationSession _side;
        readonly HandOwnershipArbiter _arb = new HandOwnershipArbiter(HandClaimRegistry.Shared);
        readonly AimHandSample[] _samples = new AimHandSample[2];
        readonly AimHandSample[] _sim = new AimHandSample[2];
        readonly bool[] _simOn = new bool[2];
        HandVisual _leftHand, _rightHand;
        float _nextHandSearch;
        int _hoverRole = -1;
        int _carryHand = -1;
        int _aimHand = -1;
        Transform _proxy;
        Vector3 _proxyFrom;
        float _proxyT;
        float _proxyScaleCarry;
        Transform _perchPad;
        Renderer _perchPadR;
        WallCard _visitCard;
        Transform _portalRoot, _portalAvatar;
        float _portalAvatarScale;
        int _visitCounter;
        string _lastQuestionLabel;

        public AgentVisitFlow Visit => _visit;
        public PortalFlow Portal => _portal;
        public AgentPresentationRegistry Presentations => _registry;
        public SideConversationSession Side => _side;
        public WallCard VisitCard => _visitCard;
        public Vector3 PerchWorld => nearUserRig != null ? nearUserRig.TransformPoint(perchLocal) : transform.position;

        // ───────────────────────── keys ─────────────────────────

        public void PressSelectAgents()
        {
            if (!flags.agentSelectEnabled || !WallActive) return;
            if (_visit.IsArmed) { _visit.Disarm(); Note("Select Agents off"); return; }
            if (_portal.State != PortalState.Hidden) { Note("Close the portal first"); return; }
            if (!_visit.Arm()) Note(_visit.Message); else Note(_visit.Message);
        }

        void ReleaseHands()
        {
            if (_carryHand >= 0) { _arb.Relinquish(_carryHand); _carryHand = -1; }
            if (_aimHand >= 0) { _arb.Relinquish(_aimHand); _aimHand = -1; }
        }

        // ───────────────────────── per frame ─────────────────────────

        void TickInteraction(float dt)
        {
            _visit.Tick(dt);
            _portal.comfortMode = comfortMode;
            _portal.Tick(dt);

            bool selectMode = WallActive && flags.agentSelectEnabled && (_visit.IsArmed || _visit.State == VisitState.Carrying);
            bool aimMode = WallPreviewing;
            if (!selectMode && !aimMode && _carryHand < 0 && _aimHand < 0)
            {
                _hoverRole = -1;
                TickProxy(dt);
                TickPortalVisual(dt);
                RefreshVisitCard();
                return;
            }

            _arb.externallyHeld = SpatialWorkspaceState.TransitionHeld;
            for (int i = 0; i < 2; i++)
            {
                AimHandSample s = _simOn[i] ? _sim[i] : ReadHand(i);
                if (!_simOn[i]) s = HandInteractionProbe.Apply(s, i);
                _samples[i] = s;
            }
            _arb.Tick(dt, _samples[0], _samples[1]);

            // Hover: which agent does a free hand's ray point at?
            _hoverRole = -1;
            int hoverHand = -1;
            if (selectMode && _visit.IsArmed && _view != null)
                for (int i = 0; i < 2 && _hoverRole < 0; i++)
                    if (_samples[i].tracked) { _hoverRole = RayPickAgent(_samples[i].origin, _samples[i].direction); if (_hoverRole >= 0) hoverHand = i; }

            for (int i = 0; i < 2; i++)
            {
                HandGateSignal sig = _arb.Signal(i);
                if (sig == HandGateSignal.Ready)
                {
                    if (selectMode && _visit.IsArmed && hoverHand == i && _hoverRole >= 0 && _carryHand < 0)
                    {
                        string agent = AgentId((HabitatRole)_hoverRole);
                        if (_visit.Grab(agent) && _arb.TryClaim(i, HandClaim.AgentSelect)) { _carryHand = i; BeginCarry((HabitatRole)_hoverRole); }
                        else { if (_visit.State == VisitState.Carrying) _visit.CancelCarry(VisitEndReason.Preempted); Note(_visit.Message); }
                    }
                    else if (aimMode && _aimHand < 0)
                    {
                        // Pinch while pointing at a wall: drag the ghost (placement still needs Place here).
                        int wi; float u, y, dist;
                        if (WallMath.Raycast(roomService != null ? roomService.Walls : null, _samples[i].origin, _samples[i].direction, 8f, out wi, out u, out y, out dist)
                            && _arb.TryClaim(i, HandClaim.Placement)) _aimHand = i;
                    }
                }
                if (i == _carryHand)
                {
                    if (sig == HandGateSignal.Released) { _carryHand = -1; EndCarry(true); }
                    else if (sig == HandGateSignal.Cancelled) { _carryHand = -1; EndCarry(false); }
                }
                if (i == _aimHand)
                {
                    if (sig == HandGateSignal.Holding && _samples[i].tracked) { _flow.Aim(_samples[i].origin, _samples[i].direction); ShowGhost(); }
                    else if (sig == HandGateSignal.Released || sig == HandGateSignal.Cancelled) _aimHand = -1;
                }
            }
            if (!aimMode && _aimHand >= 0) { _arb.Relinquish(_aimHand); _aimHand = -1; }

            TickProxy(dt);
            TickPortalVisual(dt);
            RefreshVisitCard();
        }

        int RayPickAgent(Vector3 origin, Vector3 dir)
        {
            float best = float.MaxValue; int pick = -1;
            float scale = _habitatRoot != null ? _habitatRoot.lossyScale.y : 1f;
            float r = Mathf.Max(0.045f, _view.AvatarHeight * 0.45f) * scale + 0.02f;      // a forgiving target at 1-2 m
            for (int i = 0; i < 4; i++)
            {
                Transform a = _view.Avatar((HabitatRole)i);
                if (a == null || _registry.Of(AgentId((HabitatRole)i)) != AgentPresentation.Home) continue;
                Vector3 c = a.position + _habitatRoot.up * (_view.AvatarHeight * 0.5f * scale);
                Vector3 oc = c - origin;
                float t = Vector3.Dot(oc, dir);
                if (t < 0.2f) continue;
                float d2 = (oc - dir * t).sqrMagnitude;
                if (d2 > r * r) continue;
                if (t < best) { best = t; pick = i; }
            }
            return pick;
        }

        // ───────────────────────── the carried projection ─────────────────────────

        void BeginCarry(HabitatRole role)
        {
            DestroyProxy();
            Transform home = _view.Avatar(role);
            float h = _view.AvatarHeight * (_habitatRoot != null ? _habitatRoot.lossyScale.y : 1f);
            GameObject src = SourceFor(role);
            _proxy = src != null ? WallHabitatView.CloneVisual(src, "VisitProjection_" + role, transform, h)
                                 : WallHabitatView.CloneVisual(home != null ? home.gameObject : null, "VisitProjection_" + role, transform, h);
            if (_proxy == null) { _visit.CancelCarry(VisitEndReason.Cancelled); return; }
            _proxy.position = home != null ? home.position : PerchWorld;
            _proxy.rotation = home != null ? home.rotation : Quaternion.identity;
            _proxyScaleCarry = _proxy.localScale.x;
            _proxyFrom = _proxy.position;
            _proxyT = 0f;
            // A soft ring marks the projection as a projection.
            ArtHelper().Box(_proxy, "ProjectionRing", Vector3.zero, Vector3.one, WallHabitatView.Teal).transform.localScale =
                new Vector3(0.09f, 0.002f, 0.09f) / Mathf.Max(1e-5f, _proxy.lossyScale.y / Mathf.Max(1e-5f, transform.lossyScale.y));
            EnsurePerch();
            _side = null;
            Note("Let go over the teal pad on your desk");
        }

        GameObject SourceFor(HabitatRole role)
        {
            // Prefer the habitat avatar: a clean visual clone whose renderers are on. (The desk agents' renderers
            // are switched off while Wall mode hides the desk office, and a clone would copy that.)
            if (_view != null && _view.Avatar(role) != null) return _view.Avatar(role).gameObject;
            if (agentSources == null || agentSources.Length == 0) return null;
            int i = role == HabitatRole.Manager ? 1 : Mathf.Min((int)role, agentSources.Length - 1);
            return agentSources[i];
        }

        void TintProxy(Transform t, Color c)
        {
            var mpb = new MaterialPropertyBlock();
            foreach (var r in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", Color.Lerp(Color.white, c, 0.45f));
                r.SetPropertyBlock(mpb);
            }
        }

        void EndCarry(bool released)
        {
            if (_visit.State != VisitState.Carrying) return;
            if (!released) { _visit.CancelCarry(VisitEndReason.TrackingLost); Note("Hand lost: the agent went home. Nothing changed."); return; }
            bool over = _proxy != null && (_proxy.position - PerchWorld).magnitude <= perchRadius;
            _visit.Release(over);
            if (over) BeginDeskVisit();
            else Note("Not over the desk pad: going home");
        }

        void TickProxy(float dt)
        {
            bool showPad = _visit.State == VisitState.Carrying || _visit.State == VisitState.Perched;
            if (_perchPad != null && _perchPad.gameObject.activeSelf != showPad) _perchPad.gameObject.SetActive(showPad);
            if (_proxy == null) return;
            switch (_visit.State)
            {
                case VisitState.Carrying:
                {
                    if (_carryHand < 0) return;
                    AimHandSample s = _samples[_carryHand];
                    if (!s.tracked) return;                                   // frozen: neither move nor release
                    if (!s.pinching) return;                                  // opening the fingers: hold still while the release debounces
                    _proxyT = Mathf.Min(1f, _proxyT + dt / 0.45f);
                    Vector3 target = s.origin + s.direction * 0.14f + Vector3.down * 0.02f;
                    float e = _proxyT * _proxyT * (3f - 2f * _proxyT);
                    _proxy.position = Vector3.Lerp(_proxyFrom, target, e);
                    Vector3 eye = ResolveHead() != null ? ResolveHead().position : _proxy.position + Vector3.back;
                    _proxy.rotation = WallUi.FacingEye(eye, _proxy.position) ;   // +Z toward the user
                    bool over = (_proxy.position - PerchWorld).magnitude <= perchRadius;
                    if (_perchPadR != null) ArtHelper().Tint(_perchPadR, over ? WallHabitatView.Teal : WallHabitatView.Teal * 0.55f);
                    return;
                }
                case VisitState.Perched:
                {
                    Vector3 eye = ResolveHead() != null ? ResolveHead().position : PerchWorld + Vector3.back;
                    _proxy.position = Vector3.Lerp(_proxy.position, PerchWorld, Mathf.Min(1f, dt * 10f));
                    _proxy.rotation = WallUi.FacingEye(eye, PerchWorld);
                    float s = _proxyScaleCarry * 1.35f;
                    _proxy.localScale = Vector3.Lerp(_proxy.localScale, Vector3.one * s, Mathf.Min(1f, dt * 6f));
                    return;
                }
                case VisitState.Returning:
                {
                    Transform home = _view != null && _visit.Agent != null ? _view.Avatar(RoleOf(_visit.Agent)) : null;
                    if (_proxyT >= 0f) { _proxyFrom = _proxy.position; _proxyT = -1f; }
                    float k = _visit.ReturnProgress;
                    Vector3 to = home != null ? home.position : _proxyFrom;
                    _proxy.position = Vector3.Lerp(_proxyFrom, to, k * k * (3f - 2f * k));
                    _proxy.localScale = Vector3.one * Mathf.Lerp(_proxyScaleCarry, _proxyScaleCarry * 0.2f, k);
                    return;
                }
                default:
                    DestroyProxy();
                    EndDeskVisit();
                    return;
            }
        }

        void DestroyProxy()
        {
            if (_proxy != null) Destroy(_proxy.gameObject);
            _proxy = null;
        }

        void EnsurePerch()
        {
            if (_perchPad != null || nearUserRig == null) return;
            _perchPadR = ArtHelper().Box(nearUserRig, "WallVisitPerch", perchLocal, new Vector3(0.11f, 0.004f, 0.11f), WallHabitatView.Teal * 0.55f);
            _perchPad = _perchPadR.transform;
            _perchPad.gameObject.SetActive(false);
        }

        static HabitatRole RoleOf(string agent)
        {
            HabitatRole r;
            return HabitatStage.TryRole(agent, out r) ? r : HabitatRole.Developer;
        }

        // ───────────────────────── desk visit + /btw ─────────────────────────

        void BeginDeskVisit()
        {
            _visitCounter++;
            _side = new SideConversationSession(_visit.Agent, "visit-" + DateTime.UtcNow.Ticks.ToString("x") + "-" + _visitCounter);
            EnsureVisitCard();
            PlaceCards();
            _visitCard.Show(true);
            Note("");
        }

        void EndDeskVisit()
        {
            if (_side != null && _backend != null) _backend.ClearVisit(_side.agentId, _side.visitSessionId);
            _side = null;
            if (_visitCard != null && _portal.State == PortalState.Hidden) _visitCard.Show(false);
        }

        void EnsureVisitCard()
        {
            if (_visitCard != null || nearUserRig == null) return;
            _visitCard = new WallCard(nearUserRig, "WallVisitCard", keyTemplate, font, ArtHelper(), new Vector2(0.16f, 0.13f), keyScale);
        }

        void PlaceCards()
        {
            Transform h = ResolveHead();
            Vector3 eye = h != null ? h.position : (nearUserRig != null ? nearUserRig.TransformPoint(new Vector3(0, 0.30f, 0.62f)) : Vector3.zero);
            if (_visitCard != null && nearUserRig != null) _visitCard.Place(nearUserRig.TransformPoint(visitCardLocal), eye, 12f);
            if (_planCard != null && nearUserRig != null) _planCard.Place(nearUserRig.TransformPoint(planCardLocal), eye, 12f);
        }

        static readonly string[] Questions = { "What are you working on?", "Is anything blocking you?", "What happens next?" };
        static readonly string[] QuestionKeys = { "Status?", "Blockers?", "Next?" };

        /// <summary>Ask the visiting (or portal) agent one of the preset questions. Read-only.</summary>
        public bool Ask(int questionIndex)
        {
            if (!flags.agentBtwEnabled) { Note("Questions are switched off in this build"); return false; }
            if (_side == null || _backend == null) return false;
            string q = Questions[Mathf.Clamp(questionIndex, 0, Questions.Length - 1)];
            return AskText(q);
        }

        /// <summary>Ask a free question (voice or a driver). Read-only: a change request comes back as a suggestion, never applied.</summary>
        public bool AskText(string question)
        {
            if (!flags.agentBtwEnabled || _side == null || _backend == null) return false;
            string taskId = null, title, state; int rev = -1;
            if (_tasks != null) _tasks.TryGetAgentTask(_side.agentId, out taskId, out title, out state, out rev);
            _side.taskId = taskId;
            string rid, key;
            if (!_side.TryBegin(question, Time.unscaledTime, out rid, out key)) return false;
            _lastQuestionLabel = question;
            SideConversationSession session = _side;
            _backend.AskSide(_side.agentId, taskId, _side.visitSessionId, question, _side.contextRevision >= 0 ? _side.contextRevision : rev, rid, key,
                reply => { if (session == _side) session.Complete(reply); });
            return true;
        }

        public void PressReturn()
        {
            if (_portal.State != PortalState.Hidden) { _portal.SendBack(); return; }
            _visit.Return(VisitEndReason.UserReturn);
        }

        void RefreshVisitCard()
        {
            if (_side != null) _side.CheckTimeout(Time.unscaledTime);
            bool visiting = _visit.State == VisitState.Perched || _portal.State == PortalState.Conversing || _portal.State == PortalState.AwaitConfirm || _portal.State == PortalState.Preview;
            if (_visitCard == null) return;
            if (!visiting) { if (_visitCard.Visible) _visitCard.Show(false); return; }
            if (!_visitCard.Visible) { PlaceCards(); _visitCard.Show(true); }

            string agent = _portal.State != PortalState.Hidden ? _portal.Agent : _visit.Agent;
            string name = AgentPresentationRegistry.Pretty(agent);
            var keys = new List<KeyValuePair<string, Action>>();
            string body;
            if (_portal.State == PortalState.Preview || _portal.State == PortalState.AwaitConfirm)
            {
                body = _portal.Message + (_portal.Spot.roomChecked ? "\nChecked against the room scan; things not in the scan were not checked." : "");
                keys.Add(new KeyValuePair<string, Action>(_portal.State == PortalState.AwaitConfirm ? "Open here" : null, () => _portal.Confirm()));
                keys.Add(new KeyValuePair<string, Action>("Other spot", NextPortalSpot));
                keys.Add(new KeyValuePair<string, Action>("Cancel", () => _portal.Interrupt("Cancelled")));
                _visitCard.SetText("Meet " + name + " larger?", body, "The agent's work continues either way.");
                _visitCard.SetKeys(keys);
                return;
            }

            string taskId, title, state; int rev;
            string task = _tasks != null && _tasks.TryGetAgentTask(agent, out taskId, out title, out state, out rev)
                ? "Task: " + title + " (" + state + ", rev " + rev + ")" : "No task right now";
            body = task + "\n";
            if (_side != null && _side.State == SideAskState.Asking) body += "\nYou: " + _lastQuestionLabel + "\n…asking";
            else if (_side != null && _side.History.Count > 0)
            {
                SideExchange x = _side.History[_side.History.Count - 1];
                body += "\nYou: " + x.question + "\n" + name + ": " + x.reply;
                if (x.staleContext) body += "\n(Task changed since; this is the current state.)";
                if (!string.IsNullOrEmpty(x.amendment)) body += "\nSuggested change NOT applied: needs your approval through the board.";
            }
            else body += flags.agentBtwEnabled ? "\nAsk a question. This never changes the task." : "";
            string foot = (_side != null && _side.History.Count > 0 ? ProvenanceWords(_side.History[_side.History.Count - 1].provenance) + " · " : "")
                        + (_tasks != null ? _tasks.SourceModeLabel : "") + " · projection: the real agent keeps working";
            _visitCard.SetText(name + (_portal.State != PortalState.Hidden ? " (portal)" : " visiting"), body, foot);
            if (flags.agentBtwEnabled) for (int i = 0; i < 3; i++) { int qi = i; keys.Add(new KeyValuePair<string, Action>(QuestionKeys[i], () => Ask(qi))); }
            if (_portal.State == PortalState.Hidden && flags.largeAgentPortalEnabled) keys.Add(new KeyValuePair<string, Action>("Meet larger", PressMeetLarger));
            else keys.Add(new KeyValuePair<string, Action>(null, null));
            keys.Add(new KeyValuePair<string, Action>(_portal.State != PortalState.Hidden ? "Send back" : "Return", PressReturn));
            _visitCard.SetKeys(keys);
        }

        static string ProvenanceWords(string p)
        {
            switch (p)
            {
                case "LIVE_MODEL": return "Answer from the AI model";
                case "TASK_SNAPSHOT": return "From the task record (no AI model)";
                case "SCRIPTED_DEMO": return "SCRIPTED DEMO";
                case "OFFLINE_SNAPSHOT": return "Offline: from local task state";
                default: return string.IsNullOrEmpty(p) ? "" : p;
            }
        }

        // ───────────────────────── large portal ─────────────────────────

        public PortalSettings portalSettings = new PortalSettings();
        List<PortalCandidate> _portalSpots = new List<PortalCandidate>();
        int _portalSpotIndex;

        public void PressMeetLarger()
        {
            if (!flags.largeAgentPortalEnabled) { Note("The portal is switched off in this build"); return; }
            string agent;
            if (!_visit.HandOverToPortal(out agent)) { Note("Bring an agent to your desk first"); return; }
            // The desk projection leaves without flying home: the portal takes the same presentation.
            DestroyProxy();
            SideConversationSession keep = _side;
            _portalSpots = PortalPlacementEvaluator.Find(roomService != null ? roomService.Room : null, ReadUser(), portalSettings);
            _portalSpotIndex = 0;
            PortalCandidate spot = _portalSpots.Count > 0 ? _portalSpots[0] : PortalPlacementEvaluator.ManualSmall(ReadUser(), 70f, portalSettings);
            if (!_portal.BeginPreview(agent, spot)) { Note(_portal.Message); return; }
            _side = keep;
            ShowPortalPreview();
        }

        void NextPortalSpot()
        {
            if (_portalSpots.Count == 0) { _portal.UpdateSpot(PortalPlacementEvaluator.ManualSmall(ReadUser(), _portalSpotIndex++ % 2 == 0 ? -70f : 70f, portalSettings)); }
            else { _portalSpotIndex = (_portalSpotIndex + 1) % _portalSpots.Count; _portal.UpdateSpot(_portalSpots[_portalSpotIndex]); }
            ShowPortalPreview();
        }

        void ShowPortalPreview()
        {
            if (_portalRoot == null)
            {
                _portalRoot = new GameObject("LargeAgentPortal").transform;
                _portalRoot.SetParent(transform, false);
                var art = ArtHelper();
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI * 2f / 12f;
                    var seg = art.Box(_portalRoot, "Ring" + i, new Vector3(Mathf.Cos(a), 0.003f, Mathf.Sin(a)), new Vector3(0.16f, 0.006f, 0.02f), WallHabitatView.Lavender).transform;
                    seg.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90f, 0);
                }
            }
            PortalCandidate c = _portal.Spot;
            float r = portalSettings.footprintRadius;
            _portalRoot.SetPositionAndRotation(c.position, c.rotation);
            for (int i = 0; i < 12; i++)
            {
                Transform seg = _portalRoot.GetChild(i);
                float a = i * Mathf.PI * 2f / 12f;
                seg.localPosition = new Vector3(Mathf.Cos(a) * r, 0.003f, Mathf.Sin(a) * r);
                seg.localScale = new Vector3(r * 0.55f, 0.006f, 0.02f);
                ArtHelper().Tint(seg.GetComponent<Renderer>(), c.valid ? (c.roomChecked ? WallHabitatView.Lavender : WallHabitatView.Amber) : WallHabitatView.Coral);
            }
            _portalRoot.gameObject.SetActive(true);
        }

        void TickPortalVisual(float dt)
        {
            PortalState st = _portal.State;
            if (st == PortalState.Hidden)
            {
                if (_portalRoot != null && _portalRoot.gameObject.activeSelf) _portalRoot.gameObject.SetActive(false);
                if (_portalAvatar != null)
                {
                    Destroy(_portalAvatar.gameObject);
                    _portalAvatar = null;
                    if (_visit.State != VisitState.Perched) EndDeskVisit();     // the conversation ends with the portal
                }
                return;
            }
            if (st == PortalState.Preview || st == PortalState.AwaitConfirm) { if (_portalRoot != null && (_portalRoot.position - _portal.Spot.position).sqrMagnitude > 1e-6f) ShowPortalPreview(); return; }
            if (_portalAvatar == null && st >= PortalState.Opening)
            {
                GameObject src = SourceFor(RoleOf(_portal.Agent));
                _portalAvatar = WallHabitatView.CloneVisual(src, "PortalProjection_" + _portal.Agent, _portalRoot, _portal.Spot.avatarHeight);
                if (_portalAvatar != null)
                {
                    _portalAvatarScale = _portalAvatar.localScale.x;
                    _portalAvatar.localPosition = Vector3.zero;
                    _portalAvatar.localRotation = Quaternion.identity;       // the root already faces the user
                    var an = _portalAvatar.GetComponentInChildren<Animator>();
                    if (an != null) { an.applyRootMotion = false; if (an.HasState(0, Animator.StringToHash("Talking"))) an.CrossFadeInFixedTime("Talking", 0.3f, 0); }
                }
            }
            if (_portalAvatar == null) return;
            float k = _portal.Progress;
            float grow;
            switch (st)
            {
                case PortalState.Opening: grow = 0.001f; break;
                case PortalState.Emerging: grow = Mathf.SmoothStep(0.05f, 1f, k); break;
                case PortalState.Conversing: grow = 1f; break;
                case PortalState.Returning: grow = Mathf.SmoothStep(1f, 0.05f, k); break;
                default: grow = 0.001f; break;
            }
            // Grows in place inside the cleared column (scale only; it never walks).
            _portalAvatar.localScale = Vector3.one * (_portalAvatarScale * grow);
        }

        // ───────────────────────── hands ─────────────────────────

        AimHandSample ReadHand(int index)
        {
            HandVisual visual = index == 0 ? _leftHand : _rightHand;
            if (visual == null)
            {
                if (Time.unscaledTime < _nextHandSearch) return AimHandSample.None;
                _nextHandSearch = Time.unscaledTime + 1f;
                foreach (var v in FindObjectsByType<HandVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (v == null || v.Hand == null) continue;
                    if (v.Hand.Handedness == Handedness.Left) { if (_leftHand == null) _leftHand = v; }
                    else if (_rightHand == null) _rightHand = v;
                }
                visual = index == 0 ? _leftHand : _rightHand;
                if (visual == null) return AimHandSample.None;
            }
            IHand hand = visual.Hand;
            if (hand == null || !hand.IsTrackedDataValid || !hand.IsPointerPoseValid) return AimHandSample.None;
            Pose pointer;
            if (!hand.GetPointerPose(out pointer)) return AimHandSample.None;
            return AimHandSample.Ray(pointer.position, pointer.rotation * Vector3.forward, hand.GetIndexFingerIsPinching());
        }

        /// <summary>Play Mode driver: stand in for a hand (ray + pinch). holding/nearHover mark it busy like a paper grab.</summary>
        public void SimulateHand(int hand, Vector3 origin, Vector3 direction, bool pinching, bool holding = false, bool nearHover = false)
        {
            if (hand < 0 || hand > 1) return;
            _simOn[hand] = true;
            _sim[hand] = AimHandSample.Ray(origin, direction.normalized, pinching).With(holding, nearHover, false);
        }

        /// <summary>Open the simulated pinch without moving the hand.</summary>
        public void SimulateRelease(int hand) { if (hand >= 0 && hand <= 1 && _simOn[hand]) _sim[hand].pinching = false; }

        public void SimulateHandLost(int hand) { if (hand >= 0 && hand <= 1) { _simOn[hand] = true; _sim[hand] = AimHandSample.None; } }
        public void StopSimulating() { _simOn[0] = _simOn[1] = false; }

        /// <summary>World centre of a habitat avatar (drivers aim at it).</summary>
        public bool TryGetAvatarCenter(HabitatRole r, out Vector3 c)
        {
            c = Vector3.zero;
            if (_view == null || _view.Avatar(r) == null) return false;
            c = _view.Avatar(r).position + _habitatRoot.up * (_view.AvatarHeight * 0.5f * _habitatRoot.lossyScale.y);
            return true;
        }

        public Vector3 ProxyPosition => _proxy != null ? _proxy.position : Vector3.zero;
        public bool HasProxy => _proxy != null;
        public bool HasPortalAvatar => _portalAvatar != null;
        public float PortalAvatarHeight => _portal.Spot.avatarHeight;
    }
}
