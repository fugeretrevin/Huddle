"""MuseDesk backend: versioned REST + WebSocket event channel.

Run: python -m uvicorn app.main:app --host 127.0.0.1 --port 8765 --reload
"""
from __future__ import annotations

import asyncio
import uuid
from contextlib import asynccontextmanager
from typing import Any

from fastapi import (
    Depends,
    FastAPI,
    Header,
    HTTPException,
    Request,
    WebSocket,
    WebSocketDisconnect,
)
from fastapi.responses import JSONResponse

from . import __version__
from .auth import hash_token, new_token, verify_token
from .events import EventBus, utcnow_iso
from .models import (
    AgentId,
    AgentState,
    DecisionAction,
    DecisionResolution,
    DecisionResponse,
    DecisionState,
    EventType,
    HealthResponse,
    QuestionAnswer,
    QuestionRequest,
    SessionCreate,
    SessionCreated,
    Snapshot,
    TaskCreate,
    TaskState,
)
from .orchestrator import Orchestrator
from .live_orchestrator import LiveOrchestrator
from .phase2.board_bridge import BoardRunBridge
from .phase2.events import Phase2EventBus
from .phase2.routes import install_phase2_exception_handlers, register_phase2_routes
from .phase2.store import Phase2Store
from .phase3.provider import build_provider, get_provider
from .phase3.routes import register_phase3_routes
from .phase3.runner import PlanRunner
from .phase3.store import Phase3Store
from .run_launcher import RunLauncher
from .run_recovery import reconcile_interrupted_runs
from .settings import Settings, get_settings
from .storage import Storage
from .ws_stream import pump_events

AGENT_DEFS = [
    (AgentId.researcher, "Researcher", "Rhea"),
    (AgentId.developer, "Developer", "Dev"),
    (AgentId.tester, "Tester", "Tess"),
]

QUESTION_ANSWERS = {
    "default": (
        "OAuth lets a managed identity provider handle credentials, so this prototype "
        "avoids building and securing password storage. The tradeoff is a dependency on "
        "an external identity provider.",
        ["research notes: oauth_vs_passwords", "risk assessment: medium"],
    ),
}


def create_app(settings: Settings | None = None) -> FastAPI:
    settings = settings or get_settings()

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        storage = Storage(settings.db_path)
        bus = EventBus(storage)
        phase2_store = Phase2Store(storage)
        phase2_bus = Phase2EventBus(phase2_store)
        phase3_store = Phase3Store(storage)  # additive manager_plans_v3 table
        # Projects a run's milestones onto the task card that launched it (v2 stream).
        board = BoardRunBridge(phase2_store, phase2_bus)
        orchestrator = (LiveOrchestrator(storage, bus, settings, phase2_store, phase2_bus,
                                         board=board,
                                         provider_getter=lambda: get_provider(app))
                        if settings.producer == "live"
                        else Orchestrator(storage, bus, settings, board=board))
        app.state.settings = settings
        app.state.storage = storage
        app.state.bus = bus
        app.state.orchestrator = orchestrator
        app.state.phase2_store = phase2_store
        app.state.phase2_bus = phase2_bus
        app.state.phase3_store = phase3_store
        app.state.p3_side = None  # side-conversation service (ephemeral memory), lazily built
        # Optional model provider (phase3.provider.ModelProvider). None = no model:
        # side answers are TASK_SNAPSHOT and Manager plans are TEMPLATE_PLAN.
        # Built from MUSEDESK_MODEL_PROVIDER + key (env only); None when not configured.
        app.state.p3_provider = build_provider(settings)
        # One launcher for both entry points (v1 task create, v2 card assign), so both
        # start the same run through the same producer.
        app.state.run_launcher = RunLauncher(storage, orchestrator)
        # Plan runner (flag-gated): works the cards a committed Manager plan created.
        # Independent of the auth_demo pipeline: a pending pipeline decision can wait for the
        # human indefinitely and must not hold the plan's text-only work.
        app.state.p3_runner = PlanRunner(
            phase2_store, phase3_store, phase2_bus, settings, lambda: get_provider(app))
        runner_task = None
        if settings.p3_plan_runner_enabled and settings.p3_manager_enabled:
            runner_task = asyncio.create_task(app.state.p3_runner.run_forever())
        # Runs are driven by in-memory asyncio tasks + decision waiters. Anything the
        # database still shows as in flight was cut off by the previous process; report
        # it as failed (and expire its pending decision) instead of leaving agents
        # "working"/"waiting" on a run nobody is driving.
        await reconcile_interrupted_runs(storage, bus, settings.state_source_mode,
                                         board=board)
        try:
            yield
        finally:
            if runner_task is not None:
                app.state.p3_runner.stop()
                runner_task.cancel()
                try:
                    await runner_task
                except (asyncio.CancelledError, Exception):  # noqa: BLE001
                    pass
            await orchestrator.shutdown()
            storage.close()

    app = FastAPI(title="MuseDesk backend", version=__version__, lifespan=lifespan)

    # ─────────────────────── helpers ───────────────────────
    def _storage(request: Request) -> Storage:
        return request.app.state.storage

    def _bus(request: Request) -> EventBus:
        return request.app.state.bus

    def _orch(request: Request) -> Orchestrator:
        return request.app.state.orchestrator

    def _mode(request: Request) -> str:
        # Label for state the API itself records (session, decision resolution).
        return request.app.state.settings.state_source_mode

    def _task_views(request: Request, session_id: str) -> list[dict[str, Any]]:
        """v1 task rows. ``board_task_id`` (additive) is the v2 task card that launched
        the run a task belongs to, or null for a card-less run."""
        import json as _json
        cards = {c["run_task_id"]: c["id"]
                 for c in request.app.state.phase2_store.list_tasks_v2(session_id)
                 if c.get("run_task_id")}
        return [{"task_id": t["id"], "agent_id": t["agent_id"],
                 "parent_task_id": t["parent_task_id"], "status": t["status"],
                 "template": t["template"],
                 "dependencies": _json.loads(t["dependency_json"]),
                 "board_task_id": cards.get(t["parent_task_id"] or t["id"])}
                for t in _storage(request).list_tasks(session_id)]

    async def require_session(
        session_id: str,
        request: Request,
        authorization: str | None = Header(default=None),
    ) -> dict[str, Any]:
        row = _storage(request).get_session(session_id)
        if row is None:
            raise HTTPException(status_code=404, detail="unknown session")
        if not request.app.state.settings.require_pairing:
            return row
        if not authorization or not authorization.lower().startswith("bearer "):
            raise HTTPException(status_code=401, detail="missing bearer token")
        token = authorization.split(" ", 1)[1].strip()
        if not verify_token(token, row["token_hash"]):
            raise HTTPException(status_code=401, detail="invalid token for session")
        return row

    # ─────────────────────── health ───────────────────────
    @app.get("/health", response_model=HealthResponse)
    async def health() -> HealthResponse:
        return HealthResponse(status="ok", version=__version__)

    # ─────────────────────── sessions ───────────────────────
    @app.post("/v1/sessions", response_model=SessionCreated, status_code=201)
    async def create_session(body: SessionCreate, request: Request) -> SessionCreated:
        if body.schema_version != 1:
            raise HTTPException(status_code=400, detail="unsupported schema_version")
        storage = _storage(request)
        bus = _bus(request)
        session_id = f"ses-{uuid.uuid4().hex[:12]}"
        token = new_token()
        created_at = utcnow_iso()
        storage.create_session(session_id, body.mode.value, created_at,
                               hash_token(token), expires_at=None)
        for agent_id, role, name in AGENT_DEFS:
            storage.upsert_agent(session_id, agent_id.value, role, name,
                                AgentState.idle.value, created_at)
        await bus.publish(session_id, EventType.session_ready,
                         {"mode": body.mode.value,
                          "agents": [{"agent_id": a.value, "role": r, "display_name": n,
                                      "state": "idle"} for a, r, n in AGENT_DEFS],
                          "source_mode": _mode(request)})
        return SessionCreated(session_id=session_id, token=token, mode=body.mode,
                             created_at=created_at, last_seq=storage.session_last_seq(session_id))

    @app.get("/v1/sessions/{session_id}/snapshot", response_model=Snapshot)
    async def snapshot(session_id: str, request: Request,
                      session=Depends(require_session)) -> Snapshot:
        storage = _storage(request)
        agents = [{"agent_id": a["agent_id"], "role": a["role"],
                   "display_name": a["display_name"], "state": a["state"]}
                  for a in storage.list_agents(session_id)]
        tasks = _task_views(request, session_id)
        decisions = [{"decision_id": d["id"], "task_id": d["task_id"],
                      "agent_id": d["agent_id"], "state": d["status"],
                      "summary": d["summary"], "recommendation": d["recommendation"]}
                     for d in storage.list_decisions(session_id)]
        return Snapshot(session_id=session_id, mode=session["mode"],
                       last_seq=storage.session_last_seq(session_id),
                       source_mode=_mode(request),
                       agents=agents, tasks=tasks, decisions=decisions)

    # ─────────────────────── tasks ───────────────────────
    @app.post("/v1/sessions/{session_id}/tasks", status_code=201)
    async def create_task(session_id: str, body: TaskCreate, request: Request,
                         session=Depends(require_session)) -> dict[str, Any]:
        storage = _storage(request)
        prior = storage.get_idempotent(session_id, body.idempotency_key)
        if prior is not None:
            # The key store is shared per session across routes: only a task-create
            # record is a replay of this request (mirrors the check in respond_decision).
            if "task_template" not in prior:
                raise HTTPException(status_code=409, detail={
                    "error_code": "IDEMPOTENCY_KEY_REUSE",
                    "user_message": "This idempotency key was already used for a "
                                    "different request."})
            return prior
        # One run per session at a time. A second concurrent run shared (and wiped) the
        # first run's sandbox workspace mid-execution and interleaved both runs' states
        # on the same three agents, so its outcome could not be trusted.
        launcher: RunLauncher = request.app.state.run_launcher
        if launcher.run_in_progress(session_id):
            raise HTTPException(status_code=409, detail={
                "error_code": "RUN_IN_PROGRESS",
                "user_message": "A task is already running in this session. Wait for it "
                                "to finish (or answer its pending decision) first."})
        parent_task_id = launcher.create_run(session_id, body.task_template)
        # ``template`` duplicates ``task_template`` under the key the client's TaskView
        # (and the snapshot/list rows) use, so the response deserializes fully.
        result = {"schema_version": 1, "task_id": parent_task_id,
                  "status": TaskState.running.value, "session_id": session_id,
                  "task_template": body.task_template, "template": body.task_template}
        storage.save_idempotent(session_id, body.idempotency_key, "POST", "tasks", result)
        launcher.start(session_id, parent_task_id, body.task_template)
        return result

    @app.get("/v1/sessions/{session_id}/tasks")
    async def list_tasks(session_id: str, request: Request,
                        session=Depends(require_session)) -> dict[str, Any]:
        return {"schema_version": 1, "source_mode": _mode(request),
                "tasks": _task_views(request, session_id)}

    # ─────────────────────── decisions ───────────────────────
    @app.post("/v1/sessions/{session_id}/decisions/{decision_id}/respond",
              response_model=DecisionResolution)
    async def respond_decision(session_id: str, decision_id: str, body: DecisionResponse,
                              request: Request,
                              session=Depends(require_session)) -> DecisionResolution:
        storage = _storage(request)
        decision = storage.get_decision(decision_id)
        if decision is None or decision["session_id"] != session_id:
            raise HTTPException(status_code=404, detail="unknown decision")

        # Exact replay of the same idempotency key → canonical prior result, no re-exec.
        prior = storage.get_idempotent(session_id, body.idempotency_key)
        if prior is not None:
            # The key store is shared per session across routes. Only a record for THIS
            # decision is a replay; the same key seen on another decision or another
            # route (e.g. a task create) must not be echoed back as this decision's
            # result (it previously returned the other decision's outcome, or crashed
            # with a 500 on a task-create record).
            if prior.get("decision_id") != decision_id:
                raise HTTPException(status_code=409, detail={
                    "error_code": "IDEMPOTENCY_KEY_REUSE",
                    "user_message": "This idempotency key was already used for a "
                                    "different request."})
            return DecisionResolution(**{**prior, "applied": False})

        state_map = {
            DecisionAction.approve: DecisionState.approved,
            DecisionAction.reject: DecisionState.rejected,
            DecisionAction.request_changes: DecisionState.changes_requested,
        }
        new_state = state_map[body.action]
        resolved_at = utcnow_iso()
        applied = storage.resolve_decision(decision_id, new_state.value, body.action.value,
                                           resolved_at, body.idempotency_key)
        if not applied:
            # Already resolved by a different key. Same action → idempotent; else 409.
            current = storage.get_decision(decision_id)
            canonical = {"schema_version": 1, "decision_id": decision_id,
                         "state": current["status"], "action": current["resolved_action"],
                         "resolved_at": current["resolved_at"]}
            if current["resolved_action"] == body.action.value:
                return DecisionResolution(**canonical, applied=False)
            raise HTTPException(status_code=409, detail={
                "error_code": "DECISION_ALREADY_RESOLVED",
                "user_message": "This decision was already resolved.",
                "canonical": canonical})

        canonical = {"schema_version": 1, "decision_id": decision_id,
                     "state": new_state.value, "action": body.action.value,
                     "resolved_at": resolved_at}
        storage.save_idempotent(session_id, body.idempotency_key, "POST", "respond", canonical)
        await _bus(request).publish(session_id, EventType.decision_resolved,
                                    {"state": new_state.value, "action": body.action.value,
                                     "resolved_at": resolved_at, "comment": body.comment,
                                     "source_mode": _mode(request)},
                                    decision_id=decision_id)
        _orch(request).notify_resolved(decision_id, body.action.value)
        return DecisionResolution(**canonical, applied=True)

    # ─────────────────────── questions ───────────────────────
    @app.post("/v1/sessions/{session_id}/questions", response_model=QuestionAnswer)
    async def ask_question(session_id: str, body: QuestionRequest, request: Request,
                          session=Depends(require_session)) -> QuestionAnswer:
        answer, evidence = QUESTION_ANSWERS["default"]
        # This answer is a fixed, canned string (no model, no task lookup), so it is
        # ALWAYS labelled "demo" — never "live", even when the producer is live.
        return QuestionAnswer(answer=answer, evidence=evidence, source_mode="demo")

    # ─────────────────────── artifacts ───────────────────────
    @app.get("/v1/sessions/{session_id}/artifacts/{artifact_id}")
    async def get_artifact(session_id: str, artifact_id: str, request: Request,
                          session=Depends(require_session)) -> dict[str, Any]:
        import json as _json
        art = _storage(request).get_artifact(artifact_id)
        if art is None or art["session_id"] != session_id:
            raise HTTPException(status_code=404, detail="unknown artifact")
        return {"schema_version": 1, "artifact_id": art["id"], "artifact_type": art["type"],
                "summary": art["summary"], "content": _json.loads(art["content_json"]),
                "source_mode": art["mode"], "created_at": art["created_at"]}

    # ─────────────────────── websocket ───────────────────────
    @app.websocket("/v1/sessions/{session_id}/events")
    async def events_ws(ws: WebSocket, session_id: str, after_seq: int = 0,
                       token: str | None = None) -> None:
        storage: Storage = ws.app.state.storage
        bus: EventBus = ws.app.state.bus
        row = storage.get_session(session_id)
        if row is None:
            await ws.close(code=4404)
            return
        if ws.app.state.settings.require_pairing:
            if not token or not verify_token(token, row["token_hash"]):
                await ws.close(code=4401)
                return
        await ws.accept()
        q = bus.subscribe(session_id)
        try:
            await pump_events(ws, q, bus.replay(session_id, after_seq), after_seq,
                              lambda env: env.model_dump(mode="json"))
        finally:
            bus.unsubscribe(session_id, q)

    @app.exception_handler(Exception)
    async def unhandled_exc_handler(request: Request, exc: Exception) -> JSONResponse:
        # Never leak a stack trace, exception text or host path to a client.
        import logging
        logging.getLogger("musedesk").error("unhandled %s on %s", type(exc).__name__,
                                            request.url.path)
        return JSONResponse(status_code=500, content={
            "error_code": "INTERNAL_ERROR",
            "user_message": "Something went wrong on the backend. Please try again."})

    @app.exception_handler(HTTPException)
    async def http_exc_handler(request: Request, exc: HTTPException) -> JSONResponse:
        detail = exc.detail
        if isinstance(detail, dict):
            return JSONResponse(status_code=exc.status_code, content=detail)
        return JSONResponse(status_code=exc.status_code,
                           content={"error_code": "INVALID_REQUEST", "user_message": str(detail)})

    # ─────────────────────── Phase 2 (additive /v2 surface) ───────────────────────
    register_phase2_routes(app)
    install_phase2_exception_handlers(app)
    # ─────────────────────── Phase 3 (additive; flag-gated server-side) ───────────────────────
    register_phase3_routes(app)  # its errors reuse the Phase 2 CommandError handler

    return app


app = create_app()
