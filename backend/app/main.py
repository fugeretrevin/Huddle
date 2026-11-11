"""MuseDesk backend: versioned REST + WebSocket event channel.

Run: python -m uvicorn app.main:app --host 127.0.0.1 --port 8765 --reload
"""
from __future__ import annotations

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
from .settings import Settings, get_settings
from .storage import Storage

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
        orchestrator = (LiveOrchestrator(storage, bus, settings)
                        if settings.producer == "live"
                        else Orchestrator(storage, bus, settings))
        app.state.settings = settings
        app.state.storage = storage
        app.state.bus = bus
        app.state.orchestrator = orchestrator
        try:
            yield
        finally:
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
        return request.app.state.settings.producer_source_mode

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
        tasks = []
        import json as _json
        for t in storage.list_tasks(session_id):
            tasks.append({"task_id": t["id"], "agent_id": t["agent_id"],
                          "parent_task_id": t["parent_task_id"], "status": t["status"],
                          "template": t["template"],
                          "dependencies": _json.loads(t["dependency_json"])})
        decisions = [{"decision_id": d["id"], "task_id": d["task_id"],
                      "agent_id": d["agent_id"], "state": d["status"],
                      "summary": d["summary"], "recommendation": d["recommendation"]}
                     for d in storage.list_decisions(session_id)]
        return Snapshot(session_id=session_id, mode=session["mode"],
                       last_seq=storage.session_last_seq(session_id),
                       agents=agents, tasks=tasks, decisions=decisions)

    # ─────────────────────── tasks ───────────────────────
    @app.post("/v1/sessions/{session_id}/tasks", status_code=201)
    async def create_task(session_id: str, body: TaskCreate, request: Request,
                         session=Depends(require_session)) -> dict[str, Any]:
        storage = _storage(request)
        prior = storage.get_idempotent(session_id, body.idempotency_key)
        if prior is not None:
            return prior
        now = utcnow_iso()
        parent_task_id = f"task-{uuid.uuid4().hex[:10]}"
        storage.upsert_task(parent_task_id, session_id, None, None,
                           TaskState.running.value, body.task_template, [], now, now)
        result = {"schema_version": 1, "task_id": parent_task_id,
                  "status": TaskState.running.value, "session_id": session_id,
                  "task_template": body.task_template}
        storage.save_idempotent(session_id, body.idempotency_key, "POST", "tasks", result)
        _orch(request).start_auth_demo(session_id, parent_task_id)
        return result

    @app.get("/v1/sessions/{session_id}/tasks")
    async def list_tasks(session_id: str, request: Request,
                        session=Depends(require_session)) -> dict[str, Any]:
        import json as _json
        tasks = [{"task_id": t["id"], "agent_id": t["agent_id"],
                  "parent_task_id": t["parent_task_id"], "status": t["status"],
                  "template": t["template"], "dependencies": _json.loads(t["dependency_json"])}
                 for t in _storage(request).list_tasks(session_id)]
        return {"schema_version": 1, "tasks": tasks}

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
        return QuestionAnswer(answer=answer, evidence=evidence,
                             source_mode=_mode(request))

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
            sent = after_seq
            for ev in bus.replay(session_id, after_seq):
                if ev["seq"] > sent:
                    await ws.send_json(ev)
                    sent = ev["seq"]
            while True:
                env = await q.get()
                if env.seq > sent:
                    await ws.send_json(env.model_dump(mode="json"))
                    sent = env.seq
        except WebSocketDisconnect:
            pass
        finally:
            bus.unsubscribe(session_id, q)

    @app.exception_handler(HTTPException)
    async def http_exc_handler(request: Request, exc: HTTPException) -> JSONResponse:
        detail = exc.detail
        if isinstance(detail, dict):
            return JSONResponse(status_code=exc.status_code, content=detail)
        return JSONResponse(status_code=exc.status_code,
                           content={"error_code": "INVALID_REQUEST", "user_message": str(detail)})

    return app


app = create_app()
