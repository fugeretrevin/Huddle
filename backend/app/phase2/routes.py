"""Additive ``/v2/...`` API surface for Phase 2.

Reuses the SAME per-session bearer auth as v1 (tokens issued by ``POST /v1/sessions``).
All v2 state lives in the v2 store; v1 routes and the v1 event stream are untouched.
"""
from __future__ import annotations

from typing import Any

from fastapi import FastAPI, Header, HTTPException, Request, WebSocket, WebSocketDisconnect
from fastapi.responses import JSONResponse

from ..auth import verify_token
from .artifacts import ArtifactService
from .diagrams import DiagramService
from .errors import CommandError, NotFoundError
from .events import Phase2EventBus
from .focus import FocusService
from .handoffs import HandoffService
from .models import (
    ArtifactRefV2,
    AssignCommand,
    DiagramCommand,
    FocusCommand,
    HandoffV2,
    InboxItemV2,
    PauseCommand,
    PriorityCommand,
    ResumeCommand,
    SnapshotV2,
    TaskCardV2,
    TaskCreateV2,
)
from .store import Phase2Store, body_hash
from .task_graph import TaskGraphService


def _source_mode(request: Request) -> str:
    return request.app.state.settings.producer_source_mode


def _store(request: Request) -> Phase2Store:
    return request.app.state.phase2_store


def _bus(request: Request) -> Phase2EventBus:
    return request.app.state.phase2_bus


def _services(request: Request) -> dict[str, Any]:
    store = _store(request)
    bus = _bus(request)
    mode = _source_mode(request)
    ws_root = getattr(request.app.state.settings, "workspace_root", None)
    return {
        "graph": TaskGraphService(store, bus, mode),
        "handoffs": HandoffService(store, bus, mode),
        "artifacts": ArtifactService(store, bus, mode, ws_root),
        "focus": FocusService(store, bus, mode),
        "diagrams": DiagramService(store, bus, mode),
    }


def _build_snapshot(request: Request, session_row: dict) -> SnapshotV2:
    store = _store(request)
    sid = session_row["id"]
    focus = FocusService(store, _bus(request), _source_mode(request))
    import json as _json
    tasks = [
        TaskCardV2(task_id=t["id"], title=t["title"], task_state=t["task_state"],
                   agent_id=t["agent_id"], priority=t["priority"], revision=t["revision"],
                   dependencies=_json.loads(t["dependency_json"]), source_mode=t["source_mode"])
        for t in store.list_tasks_v2(sid)
    ]
    handoffs = [
        HandoffV2(handoff_id=h["id"], from_agent_id=h["from_agent_id"],
                  to_agent_id=h["to_agent_id"], task_id=h["task_id"], artifact_id=h["artifact_id"],
                  kind=h["kind"], state=h["state"], source_mode=h["source_mode"])
        for h in store.list_handoffs_v2(sid)
    ]
    artifacts = [
        ArtifactRefV2(artifact_id=a["id"], kind=a["kind"], source_mode=a["source_mode"])
        for a in store.list_artifacts_v2(sid)
    ]
    inbox = [
        InboxItemV2(item_id=i["item_id"], kind=i["kind"], summary=i["summary"],
                    priority=i["priority"], critical=bool(i["critical"]),
                    created_at=i["created_at"], source_mode=i["source_mode"])
        for i in store.list_inbox_v2(sid)
    ]
    return SnapshotV2(session_id=sid, last_seq=store.last_seq_v2(sid),
                      source_mode=_source_mode(request), tasks=tasks, handoffs=handoffs,
                      artifacts=artifacts, focus=focus.snapshot_projection(sid), inbox=inbox)


def register_phase2_routes(app: FastAPI) -> None:
    async def require_session(session_id: str, request: Request,
                              authorization: str | None = Header(default=None)) -> dict[str, Any]:
        row = request.app.state.storage.get_session(session_id)
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

    # Local import to avoid a module-level Depends on a closure.
    from fastapi import Depends

    async def _idempotent_command(request: Request, session_id: str, route: str,
                                  body_dict: dict, coro):
        """Run a command with (session, route, key, body-hash) idempotency. ``coro`` is
        a zero-arg async callable returning the canonical result dict."""
        store = _store(request)
        key = body_dict["idempotency_key"]
        bhash = body_hash({"route": route, **body_dict})
        prior = store.get_idempotent_v2(session_id, key)
        if prior is not None:
            if prior["body_hash"] == bhash:
                return prior["result"]  # canonical replay; no re-exec, no new event
            raise CommandError("IDEMPOTENCY_KEY_REUSE",
                               "idempotency key reused with a different body", status=409)
        result = await coro()
        store.save_idempotent_v2(session_id, key, route, bhash, result)
        return result

    # ─────────────────────── snapshot ───────────────────────
    @app.get("/v2/sessions/{session_id}/snapshot", response_model=SnapshotV2)
    async def snapshot_v2(session_id: str, request: Request,
                          session=Depends(require_session)) -> SnapshotV2:
        return _build_snapshot(request, session)

    # ─────────────────────── task create (foundation) ───────────────────────
    @app.post("/v2/sessions/{session_id}/tasks", status_code=201)
    async def create_task_v2(session_id: str, body: TaskCreateV2, request: Request,
                             session=Depends(require_session)) -> dict[str, Any]:
        svc = _services(request)["graph"]

        async def run() -> dict[str, Any]:
            card = svc.create_task(
                session_id, body.title,
                body.agent_id.value if body.agent_id else None,
                body.priority, body.dependencies,
                body.role_lock.value if body.role_lock else None,
            )
            return card.model_dump(mode="json")

        return await _idempotent_command(request, session_id, "tasks.create",
                                         body.model_dump(mode="json"), run)

    # ─────────────────────── task commands ───────────────────────
    @app.post("/v2/sessions/{session_id}/tasks/{task_id}/assign")
    async def assign_task(session_id: str, task_id: str, body: AssignCommand, request: Request,
                          session=Depends(require_session)) -> dict[str, Any]:
        graph = _services(request)["graph"]

        async def run() -> dict[str, Any]:
            card = await graph.assign(session_id, task_id, body.target_agent_id.value,
                                      body.expected_revision)
            return card.model_dump(mode="json")

        return await _idempotent_command(request, session_id, f"tasks.{task_id}.assign",
                                         body.model_dump(mode="json"), run)

    @app.post("/v2/sessions/{session_id}/tasks/{task_id}/priority")
    async def set_priority(session_id: str, task_id: str, body: PriorityCommand, request: Request,
                           session=Depends(require_session)) -> dict[str, Any]:
        graph = _services(request)["graph"]

        async def run() -> dict[str, Any]:
            card = await graph.set_priority(session_id, task_id, body.priority,
                                            body.expected_revision)
            return card.model_dump(mode="json")

        return await _idempotent_command(request, session_id, f"tasks.{task_id}.priority",
                                         body.model_dump(mode="json"), run)

    @app.post("/v2/sessions/{session_id}/tasks/{task_id}/pause")
    async def pause_task(session_id: str, task_id: str, body: PauseCommand, request: Request,
                         session=Depends(require_session)) -> dict[str, Any]:
        graph = _services(request)["graph"]

        async def run() -> dict[str, Any]:
            card = await graph.pause(session_id, task_id, body.expected_revision)
            return card.model_dump(mode="json")

        return await _idempotent_command(request, session_id, f"tasks.{task_id}.pause",
                                         body.model_dump(mode="json"), run)

    @app.post("/v2/sessions/{session_id}/tasks/{task_id}/resume")
    async def resume_task(session_id: str, task_id: str, body: ResumeCommand, request: Request,
                          session=Depends(require_session)) -> dict[str, Any]:
        graph = _services(request)["graph"]

        async def run() -> dict[str, Any]:
            card = await graph.resume(session_id, task_id, body.expected_revision)
            return card.model_dump(mode="json")

        return await _idempotent_command(request, session_id, f"tasks.{task_id}.resume",
                                         body.model_dump(mode="json"), run)

    # ─────────────────────── focus ───────────────────────
    @app.post("/v2/sessions/{session_id}/focus")
    async def set_focus(session_id: str, body: FocusCommand, request: Request,
                        session=Depends(require_session)) -> dict[str, Any]:
        focus = _services(request)["focus"]

        async def run() -> dict[str, Any]:
            policy = await focus.set_focus(session_id, body.mode.value, body.until,
                                           body.quiet_priorities)
            return policy.model_dump(mode="json")

        return await _idempotent_command(request, session_id, "focus.set",
                                         body.model_dump(mode="json"), run)

    # ─────────────────────── artifacts (paginated) ───────────────────────
    @app.get("/v2/sessions/{session_id}/artifacts/{artifact_id}")
    async def get_artifact_v2(session_id: str, artifact_id: str, request: Request,
                              page: int = 0, page_size: int = 5,
                              session=Depends(require_session)) -> dict[str, Any]:
        artifacts = _services(request)["artifacts"]
        if page < 0:
            raise HTTPException(status_code=400, detail="page must be >= 0")
        resp = artifacts.get_page(session_id, artifact_id, page, page_size)
        return resp.model_dump(mode="json")

    # ─────────────────────── diagrams ───────────────────────
    @app.post("/v2/sessions/{session_id}/diagrams", status_code=201)
    async def create_diagram(session_id: str, body: DiagramCommand, request: Request,
                             session=Depends(require_session)) -> dict[str, Any]:
        diagrams = _services(request)["diagrams"]

        async def run() -> dict[str, Any]:
            dia = await diagrams.create_diagram(session_id, body.template, body.task_id,
                                                body.artifact_id)
            return dia.model_dump(mode="json")

        return await _idempotent_command(request, session_id, "diagrams.create",
                                         body.model_dump(mode="json"), run)

    # ─────────────────────── websocket ───────────────────────
    @app.websocket("/v2/sessions/{session_id}/events")
    async def events_ws_v2(ws: WebSocket, session_id: str, after_seq: int = 0,
                           token: str | None = None) -> None:
        storage = ws.app.state.storage
        bus: Phase2EventBus = ws.app.state.phase2_bus
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
                    await ws.send_json(env.wire())
                    sent = env.seq
        except WebSocketDisconnect:
            pass
        finally:
            bus.unsubscribe(session_id, q)


def install_phase2_exception_handlers(app: FastAPI) -> None:
    @app.exception_handler(CommandError)
    async def _command_error(request: Request, exc: CommandError) -> JSONResponse:
        return JSONResponse(status_code=exc.status, content=exc.body())

    @app.exception_handler(NotFoundError)
    async def _not_found(request: Request, exc: NotFoundError) -> JSONResponse:
        return JSONResponse(status_code=404,
                            content={"code": "NOT_FOUND", "message": exc.message})
