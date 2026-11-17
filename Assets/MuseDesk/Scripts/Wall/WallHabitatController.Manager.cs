using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial.Ui;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// The Manager and the Living Office loop:
    ///   ask (a key, a pinch on the Manager, or voice) -> the plan-review paper (objective, work items, assignments,
    ///   dependencies, expected outputs, assumptions, risks, provenance; proposed items are NOT tasks) -> tick steps,
    ///   confirm risky ones on a separate press, Revise (drop unticked steps), Reject, or stamp Approve -> only then the
    ///   backend creates real board cards -> each canonical card prints into the Manager's tray -> its owner rides up for
    ///   it and carries it down to its desk -> the status page shows every card's REAL state, the backend's blocked or
    ///   failure reason and a Retry key for failed cards -> when the backend's summary artifact exists, the Manager
    ///   brings it down and it arrives at the user's desk as a receipt.
    /// Every state shown comes from the backend (or, offline, from the demo board, labelled SCRIPTED DEMO). The stamp is
    /// drawn only after the backend confirmed. The Manager never approves anything itself.
    /// </summary>
    public partial class WallHabitatController
    {
        [Header("Manager paper and receipt (NearUserRig space)")]
        public Vector2 planPaperSize = new Vector2(0.22f, 0.30f);
        public Vector3 receiptCardLocal = new Vector3(-0.21f, 0.20f, 0.36f);
        [Range(0.3f, 4f)] public float receiptFlightSeconds = 1.6f;
        [Tooltip("A pinch while a free hand points at the Manager in the habitat opens the Manager paper.")]
        public bool managerRayPoke = true;

        ManagerPlanFlow _manager;
        WallPlanPaper _paper;
        WallCard _planCard;                       // the paper's card: placed with the other near-user cards
        WallCard _receiptCard;
        readonly PlanBoard _board = new PlanBoard();
        bool _planCardOpen, _riskStep, _askMode, _showCommittedPlan, _rejectBusy;
        int _page, _stepWindow, _statusFails, _boardVersionDrawn = -1;
        float _stampHoldUntil, _statusPollAt, _traySweepAt, _lastPrintAt, _paperRefreshAt;
        bool _statusBusy;
        string _managerWarning = "";

        // receipt
        string _receiptTitle = "", _receiptBody = "", _receiptLabel = "";
        int _receiptPage;
        Transform _receiptFlyer;
        Vector3 _receiptFrom;
        readonly MuseDesk.Motion.MotionClock _receiptClock = new MuseDesk.Motion.MotionClock();
        const int ReceiptChunk = 520;

        public ManagerPlanFlow Manager => _manager;
        public WallCard PlanCard => _planCard;
        public WallPlanPaper Paper => _paper;
        public PlanBoard Board => _board;
        public WallCard ReceiptCard => _receiptCard;
        public bool ReceiptInFlight => _receiptFlyer != null;
        public string ManagerWarning => _managerWarning;

        static readonly string[] Objectives =
        {
            "Build the login flow, use the existing repo, and have Tester verify it",
            "Find and fix the failing tests",
            "Write setup docs for the project",
        };
        static readonly string[] ObjectiveKeys = { "Login flow", "Fix tests", "Write docs" };

        public void PressManager()
        {
            if (!flags.wallManagerEnabled || !WallActive) return;
            EnsurePlanCard();
            if (_paper == null) return;
            _planCardOpen = !_planCardOpen;
            if (_planCardOpen) { _page = 0; _paperRefreshAt = 0f; PlaceCards(); }
            _planCard.Show(_planCardOpen);
        }

        void EnsurePlanCard()
        {
            if (_paper != null || nearUserRig == null) return;
            _paper = new WallPlanPaper(nearUserRig, "WallPlanCard", keyTemplate, font, ArtHelper(), planPaperSize, keyScale);
            _planCard = _paper.card;
        }

        /// <summary>Ask the Manager for a plan (a preset objective, voice, or a driver).</summary>
        public bool RequestPlan(string objective)
        {
            if (!flags.wallManagerEnabled || _backend == null) return false;
            string rid, key;
            if (!_manager.Request(objective, out rid, out key)) { Note(_manager.Message); return false; }
            _riskStep = false; _askMode = false; _showCommittedPlan = false; _page = 0; _stepWindow = 0; _stampHoldUntil = 0f;
            _backend.ProposePlan(rid, objective, key, r =>
            {
                if (_manager.RequestId != rid) return;            // a newer request replaced it
                if (r == null || !r.ok) { _manager.OnProposeFailed(r == null ? "no reply" : (r.error ?? r.errorCode)); return; }
                _manager.OnProposed(r.planId, r.revision, r.title, r.provenance, r.items);
                _manager.SetPlanNotes(r.objective, r.assumptions, r.risks);
                _workExecution = r.workExecution ?? "";
            });
            return true;
        }

        /// <summary>Voice: "Manager, build the login flow". Null when the Manager is not available (the voice layer then says
        /// it did not understand); otherwise the words to show.</summary>
        public string RequestPlanByVoice(string objective)
        {
            if (!flags.wallManagerEnabled || !WallActive || _backend == null) return null;
            EnsurePlanCard();
            if (_paper != null && !_planCardOpen) PressManager();
            return RequestPlan(objective) ? "The Manager is drafting a plan. Nothing starts until you approve it." : _manager.Message;
        }

        public void ToggleItem(int index)
        {
            if (index < 0 || index >= _manager.Items.Count) return;
            var it = _manager.Items[index];
            _manager.SetApproved(it.clientItemId, !it.approved);
        }

        /// <summary>The separate confirmation for every ticked risky item (shown only when one is ticked).</summary>
        public void ConfirmRisky()
        {
            foreach (var it in _manager.Items) if (it.dangerous && it.approved) _manager.Authorize(it.clientItemId);
            _riskStep = false;
        }

        public bool ApprovePlan()
        {
            List<string> approved, authorized; int rev; string key, problem;
            if (!_manager.TryBuildCommit(out approved, out authorized, out rev, out key, out problem))
            {
                Note(problem);
                foreach (var it in _manager.Items) if (it.dangerous && it.approved && !it.authorized) _riskStep = true;
                return false;
            }
            string planId = _manager.PlanId;
            _manager.BeginCommit();
            _backend.CommitPlan(planId, rev, approved, authorized, key, r =>
            {
                if (_manager.PlanId != planId) return;
                if (r != null && r.ok) _manager.OnCommitted(r.revision, r.tasks);
                else _manager.OnCommitFailed(r != null ? r.code : "NO_REPLY", r != null ? r.message : "No reply");
            });
            return true;
        }

        /// <summary>Send the plan back with the unticked steps removed: a new revision to review. Creates nothing.</summary>
        public bool RevisePlan()
        {
            List<ManagerPlanItemView> kept; int rev; string key, problem;
            if (!_manager.TryBuildRevision(out kept, out rev, out key, out problem)) { Note(problem); return false; }
            string planId = _manager.PlanId;
            _manager.BeginRevise();
            _backend.RevisePlan(planId, rev, kept, key, r =>
            {
                if (_manager.PlanId != planId) return;
                if (r != null && r.ok)
                {
                    _manager.OnProposed(r.planId, r.revision, r.title, r.provenance, r.items);
                    _manager.SetPlanNotes(r.objective, r.assumptions, r.risks);
                    _workExecution = r.workExecution ?? "";
                    _page = 0; _stepWindow = 0; _riskStep = false;
                }
                else _manager.OnReviseFailed(r != null ? r.errorCode : "NO_REPLY", r != null ? r.error : "No reply");
            });
            return true;
        }

        /// <summary>Reject the plan. The paper is stamped REJECTED only after the backend (or the offline board) confirmed.</summary>
        public void RejectPlan()
        {
            if (_manager.State != ManagerFlowState.AwaitingApproval || _rejectBusy) return;
            string planId = _manager.PlanId; int rev = _manager.Revision;
            if (_backend == null) return;
            _rejectBusy = true;
            _backend.RejectPlan(planId, rev, "Rejected on the plan paper", "mgr-reject-" + planId + "-r" + rev, (ok, msg) =>
            {
                _rejectBusy = false;
                if (_manager.PlanId != planId) return;
                if (ok) { _manager.Reject(); _askMode = false; }
                else Note("Not rejected: " + (string.IsNullOrEmpty(msg) ? "no reply" : msg));
            });
        }

        /// <summary>Retry a FAILED card of a committed plan (the user's explicit poke). One in flight per card; the same
        /// idempotency key is reused until the backend answers.</summary>
        public bool RetryCard(string taskId)
        {
            string planId, key, problem;
            if (_backend == null) return false;
            if (!_board.TryBeginRetry(taskId, out planId, out key, out problem)) { Note(problem); return false; }
            _backend.RetryPlanCard(planId, taskId, key, r =>
            {
                bool ok = r != null && r.ok;
                _board.EndRetry(taskId, r != null && r.definitive, ok, r != null ? r.message : "No reply");
                if (!ok) Note("Retry: " + (r != null && !string.IsNullOrEmpty(r.message) ? r.message : "no reply"));
                _statusPollAt = 0f;
            });
            return true;
        }

        // ───────────────────────── events (real ones only) ─────────────────────────

        void OnBoardTask(string taskId) { _manager.OnBoardTask(taskId); }

        void OnPlanCommitted(string planId, IList<ManagerPrintJob> tasks)
        {
            _board.AddCommitted(planId, tasks);
            _stampHoldUntil = Time.unscaledTime + 2.5f;       // the APPROVED stamp is seen before the status page takes over
            _showCommittedPlan = false;
            _statusPollAt = 0f; _statusFails = 0;
        }

        void OnPlanCardReported(PlanCardUpdate u)
        {
            if (_board.Apply(u)) _statusPollAt = Mathf.Min(_statusPollAt, Time.unscaledTime + 0.3f);
        }

        void OnPlanCommittedAnnounced(string planId, int revision, List<ManagerPrintJob> jobs)
        {
            _manager.OnCommittedEvent(planId, revision, jobs);
        }

        void OnSystemWarning(string code, string message)
        {
            if (code == "NO_MODEL_CONFIGURED") _managerWarning = string.IsNullOrEmpty(message) ? "No model is configured: plan cards are waiting." : message;
        }

        void OnCompletionArtifact(string artifactId, string anchorTaskId, string summary)
        {
            if (_board.OnSynthesis(artifactId, anchorTaskId, summary)) BeginReceipt();
        }

        // ───────────────────────── per frame ─────────────────────────

        void TickManager(float dt)
        {
            if (_paper != null) _paper.Tick(dt);
            float now = Time.unscaledTime;
            TickPlanStamp(dt, now);

            // Printer: one card per canonical task id that is both committed and on the board. It prints into the
            // Manager's tray; its owner is told (a real committed card) and comes up for it when the ride lane is free.
            ManagerPrintJob job;
            while (_view != null && WallActive && _manager.TryDequeuePrint(out job))
            {
                HabitatRole owner;
                bool known = HabitatStage.TryRole(job.agentId, out owner);
                if (!known) owner = HabitatRole.Manager;                      // no owner: it stays in the Manager's tray
                _view.PrintToTray(job.taskId, owner);
                _lastPrintAt = now;
                if (_audio != null) _audio.Play(WallCue.PrintTick);
                if (known && owner != HabitatRole.Manager && _stage != null)
                    _stage.Accept(HabitatEvent.Of("print-" + job.taskId, HabitatEventKind.CardPrinted, owner, job.taskId), now);
                Debug.Log("[MuseDesk.Wall] Printed card for task " + job.taskId + " -> " + job.agentId);
            }

            // A printed card nobody is coming for (its pick-up was dropped, or everything was reset) goes down by the chute.
            if (_view != null && _stage != null && WallActive && now >= _traySweepAt && now - _lastPrintAt > 1.2f)
            {
                _traySweepAt = now + 1.5f;
                for (int r = 0; r < 3; r++)
                {
                    var role = (HabitatRole)r;
                    if (_view.CardsInTray(role) == 0 || _stage.HasAct(role, HabitatActKind.CollectCard)) continue;
                    if (_view.CardsToDesk(role, _stage.Walker(role).Position) > 0) SetDelivery(role, CardDelivery.OnDesk, true);
                }
            }

            if (_board.Version != _boardVersionDrawn && _view != null)
            {
                _boardVersionDrawn = _board.Version;
                for (int i = 0; i < _board.Cards.Count; i++) _view.SetCardColor(_board.Cards[i].taskId, StateColor(_board.Cards[i].state));
            }

            TickStatusPoll(now);
            TickReceipt(dt);

            if (_planCard == null || !_planCard.Visible) return;
            // The paper is re-laid out ten times a second, not every frame (the layout allocates); a key press shows within 0.1 s.
            if (now < _paperRefreshAt) return;
            _paperRefreshAt = now + 0.1f;
            RefreshPlanPaper();
        }

        void SetDelivery(HabitatRole role, CardDelivery to, bool byChute)
        {
            string agent = AgentId(role);
            for (int i = 0; i < _board.Cards.Count; i++)
            {
                PlanCardView c = _board.Cards[i];
                if (c.agentId != agent || _view == null || !_view.HasCard(c.taskId) || c.delivery == CardDelivery.OnDesk) continue;
                c.delivery = to;
                if (to == CardDelivery.OnDesk) c.deliveredByChute = byChute;
            }
        }

        // The stage tells us where a real act is; the cards and the receipt follow it.
        void OnActStarted(HabitatAct a)
        {
            if (_view == null) return;
            _view.SetPropVisible(a.actor, a.kind != HabitatActKind.CollectCard);      // going UP for a card: empty hands
            if (a.kind == HabitatActKind.DeliverReceipt) _board.SetReceiptStage(ReceiptStage.ComingDown);
        }

        void OnActArrived(HabitatAct a)
        {
            if (_view == null || _stage == null) return;
            if (a.kind == HabitatActKind.CollectCard)
            {
                if (_view.CardsToAgent(a.actor, _stage.Walker(a.actor).Position) > 0) SetDelivery(a.actor, CardDelivery.Carried, false);
                _view.SetPropVisible(a.actor, true);
            }
            else if (a.kind == HabitatActKind.DeliverReceipt)
            {
                _view.SetPropVisible(a.actor, false);                                 // handed to the chute
                StartReceiptFlight(_view.SocketWorld("CourierDispatchSocket", new Vector3(0f, _view.AvatarHeight * 0.5f, 0.03f)));
            }
        }

        void OnActEnded(HabitatAct a)
        {
            if (_view == null || _stage == null) return;
            if (a.kind == HabitatActKind.CollectCard)
            {
                // Home (or sent home by a reset): carried cards go from the hands to the desk; any still in the tray take the chute.
                bool carried = false;
                string agent = AgentId(a.actor);
                for (int i = 0; i < _board.Cards.Count; i++) carried |= _board.Cards[i].agentId == agent && _board.Cards[i].delivery == CardDelivery.Carried;
                if (_view.CardsToDesk(a.actor, _stage.Walker(a.actor).Position) > 0) SetDelivery(a.actor, CardDelivery.OnDesk, !carried);
            }
            else if (a.kind == HabitatActKind.DeliverReceipt && _board.Receipt.stage == ReceiptStage.ComingDown && _receiptFlyer == null)
                StartReceiptFlight(_view.SocketWorld("TaskPrinterSocket", new Vector3(0f, 0.04f, 0.03f)));   // interrupted on the way: sent from the office
            _view.SetPropVisible(a.actor, true);
        }

        void OnActDropped(HabitatAct a, string why)
        {
            if (a.kind == HabitatActKind.DeliverReceipt && _board.Receipt.stage != ReceiptStage.None && _board.Receipt.stage != ReceiptStage.OnDesk && _receiptFlyer == null)
                StartReceiptFlight(_view != null ? _view.SocketWorld("TaskPrinterSocket", new Vector3(0f, 0.04f, 0.03f)) : transform.position);
            // A dropped CollectCard is picked up by the tray sweep (chute).
        }

        // ───────────────────────── plan status (the backend's own words) ─────────────────────────

        void TickStatusPoll(float now)
        {
            if (_backend == null || _statusBusy || _board.Count == 0 || now < _statusPollAt || _statusFails >= 4) return;
            string planId = _manager.CommittedPlanId;
            if (string.IsNullOrEmpty(planId)) return;
            // Poll while something can still change; a settled plan with its receipt needs no more reads.
            bool settled = _board.Settled(planId) && _board.Receipt.stage != ReceiptStage.None;
            _statusPollAt = now + (settled ? 15f : 2.5f);
            _statusBusy = true;
            _backend.GetPlanStatus(planId, s =>
            {
                _statusBusy = false;
                if (s == null) { _statusFails++; return; }
                _statusFails = 0;
                for (int i = 0; i < s.cards.Count; i++) _board.Apply(s.cards[i]);
                if (!string.IsNullOrEmpty(s.synthesisArtifactId) && _board.OnSynthesis(s.synthesisArtifactId, s.synthesisTaskId, "")) BeginReceipt();
            });
        }

        // ───────────────────────── receipt: the Manager's summary comes down to the desk ─────────────────────────

        void BeginReceipt()
        {
            PlanReceipt rc = _board.Receipt;
            _receiptTitle = rc.summary ?? ""; _receiptBody = ""; _receiptLabel = ""; _receiptPage = 0;
            string id = rc.artifactId;
            if (_tasks != null)
                _tasks.FetchArtifactText(id, (title, content) =>
                {
                    if (_board.Receipt.artifactId != id) return;
                    if (content == null) { _receiptBody = "The summary could not be read from the backend."; return; }
                    // The backend's first line is its own label (scripted, roll-up or model-written): it becomes the footer.
                    string text = content.Replace("\r", "");
                    int cut = text.IndexOf('\n');
                    _receiptLabel = cut > 0 ? text.Substring(0, cut).Trim() : "";
                    _receiptBody = cut > 0 ? text.Substring(cut).Trim() : text.Trim();
                    if (string.IsNullOrEmpty(_receiptTitle)) _receiptTitle = title ?? "";
                });
            bool walking = WallActive && _stage != null
                && _stage.Accept(HabitatEvent.Of("synth-" + id, HabitatEventKind.SynthesisReady, HabitatRole.Manager, rc.anchorTaskId), Time.unscaledTime);
            if (!walking) StartReceiptFlight(_view != null && WallActive ? _view.SocketWorld("TaskPrinterSocket", new Vector3(0f, 0.04f, 0.03f)) : ReceiptTarget());
        }

        Vector3 ReceiptTarget() { return nearUserRig != null ? nearUserRig.TransformPoint(receiptCardLocal) : transform.position; }

        void EnsureReceiptCard()
        {
            if (_receiptCard != null || nearUserRig == null) return;
            _receiptCard = new WallCard(nearUserRig, "WallReceiptCard", keyTemplate, font, ArtHelper(), new Vector2(0.18f, 0.20f), keyScale);
        }

        bool _receiptFlightDeferred;
        Vector3 _receiptDeferredFrom;

        void StartReceiptFlight(Vector3 fromWorld)
        {
            if (_receiptFlyer != null || _board.Receipt.stage == ReceiptStage.None || _board.Receipt.stage == ReceiptStage.OnDesk) return;
            // Acts are dropped while the habitat is being switched off (leaving Wall mode): no object may be parented under it
            // then (Unity: "Cannot set the parent ... while activating or deactivating"). Fly the receipt once it is active again.
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                _receiptFlightDeferred = true;
                _receiptDeferredFrom = fromWorld;
                return;
            }
            _receiptFlightDeferred = false;
            _board.SetReceiptStage(ReceiptStage.ComingDown);
            _receiptFrom = fromWorld;
            _receiptFlyer = ArtHelper().Box(transform, "ReceiptFlyer", Vector3.zero, Vector3.one, Color.Lerp(WallHabitatView.Lavender, WallHabitatView.Paper, 0.55f)).transform;
            _receiptFlyer.position = fromWorld;
            float s = transform.lossyScale.x > 1e-5f ? 1f / transform.lossyScale.x : 1f;
            _receiptFlyer.localScale = new Vector3(0.05f, 0.07f, 0.002f) * s;
            _receiptClock.Start(new MuseDesk.Motion.MotionToken(Comfort ? receiptFlightSeconds * 0.6f : receiptFlightSeconds, MuseDesk.Motion.EaseKind.InOutCubic));
        }

        void TickReceipt(float dt)
        {
            if (_receiptFlightDeferred && _receiptFlyer == null) StartReceiptFlight(_receiptDeferredFrom);
            if (_receiptFlyer != null)
            {
                bool done = _receiptClock.Step(dt) || !_receiptClock.Running;
                float u = _receiptClock.Value;
                Vector3 to = ReceiptTarget();
                Vector3 p = Vector3.LerpUnclamped(_receiptFrom, to, u);
                if (!Comfort) p.y += Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) * 0.08f;     // a small arc; none in comfort mode
                _receiptFlyer.position = p;
                Transform h = ResolveHead();
                if (h != null) _receiptFlyer.rotation = WallUi.FacingEye(p, h.position, 12f);
                if (done)
                {
                    Destroy(_receiptFlyer.gameObject);
                    _receiptFlyer = null;
                    _board.SetReceiptStage(ReceiptStage.OnDesk);
                    EnsureReceiptCard();
                    if (_receiptCard != null)
                    {
                        Vector3 eye = h != null ? h.position : ReceiptTarget() + Vector3.back;
                        _receiptCard.Place(ReceiptTarget(), eye, 12f);
                        _receiptCard.Show(true);
                    }
                    if (_audio != null) _audio.Play(WallCue.Land, true);
                    Debug.Log("[MuseDesk.Wall] Plan summary receipt delivered to the desk (artifact " + _board.Receipt.artifactId + ").");
                }
            }
            if (_receiptCard == null || !_receiptCard.Visible) return;
            if (_board.Receipt.stage != ReceiptStage.OnDesk) { _receiptCard.Show(false); return; }

            int pages = Mathf.Max(1, (_receiptBody.Length + ReceiptChunk - 1) / ReceiptChunk);
            _receiptPage = Mathf.Clamp(_receiptPage, 0, pages - 1);
            int start = _receiptPage * ReceiptChunk;
            string chunk = _receiptBody.Length == 0 ? "Reading the summary…" : _receiptBody.Substring(start, Mathf.Min(ReceiptChunk, _receiptBody.Length - start));
            if (start > 0) chunk = "…" + chunk;
            if (start + ReceiptChunk < _receiptBody.Length) chunk += "…";
            string body = (string.IsNullOrEmpty(_receiptTitle) ? "" : _receiptTitle + "\n\n") + chunk;
            string foot = (string.IsNullOrEmpty(_receiptLabel) ? "Source not stated" : WallOfflineLogic.Clip(_receiptLabel, 84))
                        + (pages > 1 ? " · " + (_receiptPage + 1) + "/" + pages : "");
            _receiptCard.SetText("Plan summary from the Manager", body, foot);
            var keys = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>(pages > 1 ? "More" : null, () => _receiptPage = (_receiptPage + 1) % pages),
                new KeyValuePair<string, Action>(null, null),
                new KeyValuePair<string, Action>(null, null),
                new KeyValuePair<string, Action>("Close", CloseReceipt),
            };
            _receiptCard.SetKeys(keys);
        }

        public void CloseReceipt()
        {
            if (_receiptCard != null) _receiptCard.Show(false);
            _board.DismissReceipt();
        }

        /// <summary>Play Mode driver hook: back to a clean Manager between back-to-back runs. Closes the paper and any visit, forgets the
        /// plan flow, the board and (offline) the demo gateway's plans. Unlike a session reset it does NOT rebuild from the backend.</summary>
        public void ResetForDriver()
        {
            _restoreToken++;                                     // a rebuild still in flight must not bring the old plan back
            if (_planCardOpen && _planCard != null) { _planCardOpen = false; _planCard.Show(false); }
            if (_visit != null && _visit.Busy) _visit.Return(VisitEndReason.Cancelled);
            if (_portal != null) _portal.Interrupt("Driver reset");
            if (_manager != null) _manager.Reset();
            ResetLivingOffice();
            _page = 0; _stepWindow = 0; _riskStep = false; _rejectBusy = false; _stampHoldUntil = 0f;
            if (_backend is WallBackendGateway g) g.ResetOfflineForDriver();
        }

        void ResetLivingOffice()
        {
            _board.Reset();
            if (_view != null) _view.ClearCards();
            if (_receiptFlyer != null) { Destroy(_receiptFlyer.gameObject); _receiptFlyer = null; }
            _receiptFlightDeferred = false;
            if (_receiptCard != null) _receiptCard.Show(false);
            _managerWarning = ""; _statusFails = 0; _statusBusy = false; _askMode = false; _showCommittedPlan = false;
        }

        // ───────────────────────── rebuild after a resync or reset ─────────────────────────

        /// <summary>Driver/test hook: do what the stream's session reset does (clear, then rebuild from the backend's status).</summary>
        public void SimulateSessionReset() { OnSessionReset(); }

        int _restoreToken;
        public int PlanRestores { get; private set; }

        /// <summary>After a stream resync or a session reset the wall's plan picture was cleared. The backend (or, offline, the demo
        /// board) still knows the committed plans: read them (GET .../manager/status) and rebuild the picture. Only into an idle
        /// Manager (a plan being drafted or reviewed is never replaced). Nothing is printed or carried again, and a summary the
        /// user may already have seen is not delivered a second time.</summary>
        void RestorePlanPicture()
        {
            if (_backend == null || !flags.wallManagerEnabled || !flags.wallHabitatEnabled) return;
            int token = ++_restoreToken;
            _backend.GetManagerStatus(plans =>
            {
                if (token != _restoreToken || plans == null || plans.Count == 0) return;
                if (_manager.State != ManagerFlowState.Idle) return;
                for (int p = 0; p < plans.Count; p++)
                {
                    WallParsedPlanStatus s = plans[p];
                    var jobs = new List<ManagerPrintJob>();
                    for (int i = 0; i < s.cards.Count; i++)
                        jobs.Add(new ManagerPrintJob { taskId = s.cards[i].taskId, clientItemId = s.cards[i].taskId, agentId = s.cards[i].agentId, title = s.cards[i].title });
                    if (jobs.Count == 0) continue;
                    _board.AddCommitted(s.planId, jobs);
                    for (int i = 0; i < s.cards.Count; i++) _board.Apply(s.cards[i]);
                    if (!string.IsNullOrEmpty(s.synthesisArtifactId) && _board.OnSynthesis(s.synthesisArtifactId, s.synthesisTaskId, "")) _board.DismissReceipt();
                    if (p == plans.Count - 1) { _manager.RestoreCommitted(s.planId, s.title, s.provenance, jobs, s.revision); _workExecution = s.workExecution ?? ""; }
                }
                _statusPollAt = 0f; _statusFails = 0; _showCommittedPlan = false;
                PlanRestores++;
                Debug.Log("[MuseDesk.Wall] Plan picture rebuilt from the backend: " + _board.Count + " card(s) in " + plans.Count + " plan(s).");
            });
        }

        // ───────────────────────── the paper ─────────────────────────

        string _workExecution = "";

        /// <summary>The live plan paper's footer for the backend's declared work mode.</summary>
        public static string PlanFooter(string workExecution)
        {
            return workExecution == "sandbox" ? "LIVE model · code + tests really run in a sandbox"
                                              : "LIVE model · text only, no code tools";
        }

        static void Key(List<KeyValuePair<string, Action>> keys, string label, Action a) { keys.Add(new KeyValuePair<string, Action>(label, a)); }

        void RefreshPlanPaper()
        {
            var keys = new List<KeyValuePair<string, Action>>();
            string mode = _tasks != null ? _tasks.SourceModeLabel : "";
            // A bare "LIVE" would read as live tool execution, so the footer says what the backend really does with the cards:
            // "sandbox" = Developer/Tester write real files and run real pytest on the PC; otherwise model text only.
            if (mode == "LIVE") mode = PlanFooter(_workExecution);
            ManagerFlowState st = _manager.State;
            bool committed = st == ManagerFlowState.Delegating || st == ManagerFlowState.Monitoring;

            if (st == ManagerFlowState.Proposing || st == ManagerFlowState.Revising)
            {
                _paper.SetStamp(null, Color.clear);
                _paper.ShowText(st == ManagerFlowState.Proposing ? "The Manager is drafting a plan" : "The Manager is revising the plan",
                                _manager.Objective + "\n\nNothing is created until you approve the plan.", mode);
                Key(keys, "Close", PressManager);
            }
            else if (st == ManagerFlowState.AwaitingApproval || st == ManagerFlowState.Committing
                     || (committed && (_showCommittedPlan || Time.unscaledTime < _stampHoldUntil))
                     || (st == ManagerFlowState.Rejected && !_askMode))
                ShowPlanPage(keys, st, committed);
            else if (committed && !_askMode && _board.Count > 0)
                ShowStatusPage(keys, mode);
            else
            {
                _paper.SetStamp(null, Color.clear);
                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(_manager.Message) && st != ManagerFlowState.Monitoring) sb.AppendLine(_manager.Message);
                sb.Append("Pick a goal. You will see the plan before anything is created.");
                _paper.ShowText("Manager: what should the team do?", sb.ToString(), mode);
                for (int i = 0; i < ObjectiveKeys.Length; i++) { int oi = i; Key(keys, ObjectiveKeys[i], () => RequestPlan(Objectives[oi])); }
                Key(keys, _board.Count > 0 && committed ? "Status" : null, () => _askMode = false);
                Key(keys, null, null); Key(keys, null, null); Key(keys, null, null);
                Key(keys, "Close", PressManager);
            }
            _planCard.SetKeys(keys);
        }

        void ShowPlanPage(List<KeyValuePair<string, Action>> keys, ManagerFlowState st, bool committed)
        {
            PlanModel model = PlanPaperMapper.ToModel(_manager);
            PlanLayoutResult layout = PlanReviewLayout.Layout(model, _paper.TextWidthMm, 0f, _paper.readingDistance);
            List<PlanPage> pages = PlanPaperPager.Paginate(layout, _paper.PlanPageHeightMm);      // the rows stop above the framed stamp zone
            if (pages.Count == 0) pages.Add(new PlanPage());
            _page = ((_page % pages.Count) + pages.Count) % pages.Count;

            string foot = layout.ProvenanceText;
            if (st == ManagerFlowState.Committing) foot += " · stamping: waiting for the board to confirm";
            else if (st == ManagerFlowState.AwaitingApproval)
                foot += " · " + (_riskStep ? "a risky step needs your separate OK" : "rev " + _manager.Revision + " · proposed steps are not tasks yet");
            else if (st == ManagerFlowState.Rejected) foot += " · rejected: nothing was created";
            else foot += " · committed: these are real board cards";
            _paper.ShowPage(pages[_page], _page, pages.Count, foot);

            // The stamp is a CONFIRMED outcome: approved once the backend created the cards, rejected once it said so.
            // The framed zone says where to stamp; the mark itself is drawn inside it, only after the backend confirmed.
            if (committed) _paper.SetStamp("APPROVED", Workspace.OfficeTokens.Success);
            else if (st == ManagerFlowState.Rejected) _paper.SetStamp("REJECTED", Workspace.OfficeTokens.Coral);
            else
            {
                _paper.SetStamp(null, Color.clear);
                _paper.SetStampZone(st == ManagerFlowState.AwaitingApproval ? StampZoneMode.Awaiting : st == ManagerFlowState.Committing ? StampZoneMode.Committing : StampZoneMode.Hidden);
            }

            int n = _manager.Items.Count;
            int windows = Mathf.Max(1, (n + 2) / 3);
            _stepWindow = ((_stepWindow % windows) + windows) % windows;
            bool more = pages.Count > 1 || windows > 1;
            Action turn = () => { _page++; _stepWindow++; };

            // Approve and Reject are the two stamps (guarded exactly as before: see Stamp.cs), so the paper has no keys for them.
            // What is left: the step ticks, More (page / next steps), Revise, Close, and the separate OK for a risky step.
            if (st == ManagerFlowState.AwaitingApproval)
            {
                for (int k = 0; k < 3; k++)
                {
                    int ii = _stepWindow * 3 + k;
                    if (ii < n) Key(keys, (_manager.Items[ii].approved ? "Keep " : "Skip ") + (ii + 1), () => ToggleItem(ii));
                    else Key(keys, null, null);
                }
                Key(keys, more ? "More" : null, turn);
                Key(keys, "Revise", () => RevisePlan());
                Key(keys, _riskStep ? "Confirm risk" : null, ConfirmRisky);
                Key(keys, null, null);
                Key(keys, "Close", PressManager);
            }
            else
            {
                Key(keys, committed && _board.Count > 0 ? "Status" : null, () => { _showCommittedPlan = false; _stampHoldUntil = 0f; _page = 0; });
                Key(keys, st == ManagerFlowState.Rejected ? "New plan" : null, () => _askMode = true);
                Key(keys, null, null);
                Key(keys, more ? "More" : null, turn);
                Key(keys, null, null); Key(keys, null, null); Key(keys, null, null);
                Key(keys, "Close", PressManager);
            }
        }

        static PlanTone StateTone(PlanCardView c)
        {
            if (c.Failed) return PlanTone.Coral;
            if (c.state == "completed") return PlanTone.Success;
            if (c.state == "running") return PlanTone.Teal;
            if (c.state == "awaiting_approval") return PlanTone.Amber;
            return PlanTone.InkSoft;                      // queued, blocked, paused: neutral (amber means "needs you")
        }

        /// <summary>The committed plan's cards with their real states and the backend's reasons, laid out by the same rules as the plan.</summary>
        public PlanLayoutResult BuildStatusLayout()
        {
            var res = new PlanLayoutResult();
            float w = _paper != null ? _paper.TextWidthMm : 180f, d = _paper != null ? _paper.readingDistance : 0.45f;
            string planId = _manager.CommittedPlanId;
            res.Rows.Add(PlanPaperPager.Row(PlanRowKind.Title, "Plan status", w, TypeScale.TitleMm, FontWeightKind.Bold, PlanTone.Ink, 1, d));
            int total = 0;
            for (int i = 0; i < _board.Cards.Count; i++) if (_board.Cards[i].planId == planId) total++;
            int done = _board.CountState(planId, "completed"), failed = _board.CountState(planId, "failed"), running = _board.CountState(planId, "running");
            int textOnly = 0;
            for (int i = 0; i < _board.Cards.Count; i++)
                if (_board.Cards[i].planId == planId && _board.Cards[i].state == "completed" && PlanBoard.IsTextOnlyResult(_board.Cards[i].resultSummary)) textOnly++;
            string head = PlanBoard.ProgressWords(done, total, textOnly, running, failed);
            res.Rows.Add(PlanPaperPager.Row(PlanRowKind.State, head, w, TypeScale.CaptionMm, FontWeightKind.Bold,
                                            failed > 0 ? PlanTone.Coral : done == total && total > 0 ? PlanTone.Success : PlanTone.InfoBlue, 1, d));
            if (!string.IsNullOrEmpty(_managerWarning) && _board.CountState(planId, "queued") > 0)
                res.Rows.Add(PlanPaperPager.Row(PlanRowKind.Risk, _managerWarning, w, TypeScale.CaptionMm, FontWeightKind.Medium, PlanTone.Amber, 2, d));
            int ord = 0;
            for (int i = 0; i < _board.Cards.Count; i++)
            {
                PlanCardView c = _board.Cards[i];
                if (c.planId != planId) continue;
                ord++;
                string who = PlanReviewLayout.AgentLabel(c.agentId);
                res.Rows.Add(PlanPaperPager.Row(PlanRowKind.ItemTitle, ord + ". " + c.title + (who != null ? " - " + who : ""), w, TypeScale.BodyMm, FontWeightKind.Bold, PlanTone.Ink, 2, d));
                res.Rows.Add(PlanPaperPager.Row(PlanRowKind.ItemDetail, PlanBoard.StatusWords(c), w, TypeScale.CaptionMm, FontWeightKind.Medium, StateTone(c), 3, d));
                if (c.retryInFlight)
                    res.Rows.Add(PlanPaperPager.Row(PlanRowKind.ItemOutput, "Retry sent: waiting for the backend", w, TypeScale.CaptionMm, FontWeightKind.Medium, PlanTone.InfoBlue, 1, d));
                else if (!string.IsNullOrEmpty(c.retryMessage))
                    res.Rows.Add(PlanPaperPager.Row(PlanRowKind.ItemOutput, "Retry: " + c.retryMessage, w, TypeScale.CaptionMm, FontWeightKind.Medium, PlanTone.Coral, 2, d));
            }
            return res;
        }

        void ShowStatusPage(List<KeyValuePair<string, Action>> keys, string mode)
        {
            _paper.SetStamp(null, Color.clear);
            _paper.SetStampZone(StampZoneMode.Hidden);
            List<PlanPage> pages = PlanPaperPager.Paginate(BuildStatusLayout(), _paper.PageHeightMm);
            if (pages.Count == 0) pages.Add(new PlanPage());
            _page = ((_page % pages.Count) + pages.Count) % pages.Count;
            bool live = _backend != null && _backend.IsLive;
            _paper.ShowPage(pages[_page], _page, pages.Count, mode + (live ? " · states and reasons are the backend's" : " · states and reasons are the demo board's"));

            // Retry: one key per failed card (the first three), labelled with the card's number on this page.
            string planId = _manager.CommittedPlanId;
            int shown = 0, ord = 0;
            for (int i = 0; i < _board.Cards.Count && shown < 3; i++)
            {
                PlanCardView c = _board.Cards[i];
                if (c.planId != planId) continue;
                ord++;
                if (!c.Failed) continue;
                string id = c.taskId;
                Key(keys, c.retryInFlight ? "Retrying " + ord : "Retry " + ord, c.retryInFlight ? (Action)(() => { }) : () => RetryCard(id));
                shown++;
            }
            while (keys.Count < 3) Key(keys, null, null);
            Key(keys, pages.Count > 1 ? "More" : null, () => _page++);
            Key(keys, "Plan", () => { _showCommittedPlan = true; _page = 0; });
            Key(keys, _board.Receipt.stage == ReceiptStage.OnDesk && _receiptCard != null && !_receiptCard.Visible ? "Receipt" : null, () => { if (_receiptCard != null) { PlaceReceipt(); _receiptCard.Show(true); } });
            Key(keys, "New plan", () => _askMode = true);
            Key(keys, "Close", PressManager);
        }

        void PlaceReceipt()
        {
            if (_receiptCard == null) return;
            Transform h = ResolveHead();
            _receiptCard.Place(ReceiptTarget(), h != null ? h.position : ReceiptTarget() + Vector3.back, 12f);
        }

        /// <summary>One line for drivers: what the paper shows right now.</summary>
        public string DescribeLivingOffice()
        {
            var sb = new StringBuilder("manager=" + _manager.State + " plan=" + _manager.PlanId + " committedPlan=" + _manager.CommittedPlanId + " rev=" + _manager.Revision);
            sb.Append(" cards=" + _board.Count);
            for (int i = 0; i < _board.Cards.Count; i++)
            {
                PlanCardView c = _board.Cards[i];
                sb.Append(" | " + c.clientItemId + ":" + c.agentId + "=" + c.state + "/" + c.delivery + (c.deliveredByChute ? "(chute)" : ""));
                if (c.Blocked) sb.Append(" blocked='" + c.blockedReason + "'");
                if (c.Failed) sb.Append(" failure='" + c.failureReason + "'" + (c.retryInFlight ? " retrying" : ""));
            }
            sb.Append(" | receipt=" + _board.Receipt.stage + (ReceiptInFlight ? "(flying)" : "") + (_receiptCard != null && _receiptCard.Visible ? " shown" : ""));
            if (_paper != null && _paper.Visible) sb.Append(" | paper rows=" + _paper.RowCount + " stamp='" + _paper.StampWord + "'");
            if (_stage != null && _stage.dispatch != null) sb.Append(" | lift " + _stage.dispatch + " lane=" + _stage.LaneHolder + " onDeck=" + _stage.OnDeck);
            if (!string.IsNullOrEmpty(_managerWarning)) sb.Append(" | warning='" + _managerWarning + "'");
            return sb.ToString();
        }
    }
}
