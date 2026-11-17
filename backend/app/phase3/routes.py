"""Additive Phase 3 ``/v2`` routes: read-only side conversation and Manager plans."""
from __future__ import annotations

import asyncio
from typing import Any

from fastapi import Depends, FastAPI, Header, HTTPException, Request
from pydantic import BaseModel, ConfigDict, Field

from ..auth import verify_token
from ..phase2.store import Phase2Store
from .common import idempotent, require_flag
from .manager import ManagerPlanService
from .provider import get_provider
from .side import SideConversationService


class _Body(BaseModel):
    model_config = ConfigDict(extra="ignore")
    schema_version: int | None = None


class SideQuestionBody(_Body):
    request_id: str = Field(min_length=1, max_length=100)
    task_id: str | None = Field(default=None, max_length=100)
    visit_session_id: str = Field(min_length=1, max_length=100)
    question: str = Field(min_length=1, max_length=1000)
    context_revision: int | None = Field(default=None, ge=0)
    idempotency_key: str = Field(min_length=8, max_length=200)


class ProposeBody(_Body):
    request_id: str = Field(min_length=1, max_length=100)
    objective: str = Field(min_length=3, max_length=500)
    idempotency_key: str = Field(min_length=8, max_length=200)


class ReviseBody(_Body):
    revision: int = Field(ge=0)
    items: list[dict[str, Any]] | None = None
    title: str | None = Field(default=None, max_length=200)
    idempotency_key: str = Field(min_length=8, max_length=200)


class RejectBody(_Body):
    revision: int = Field(ge=0)
    reason: str = Field(default="", max_length=300)
    idempotency_key: str = Field(min_length=8, max_length=200)


class CommitBody(_Body):
    revision: int = Field(ge=0)
    approved_item_ids: list[str] = Field(default_factory=list, max_length=50)
    authorized_dangerous_item_ids: list[str] = Field(default_factory=list, max_length=50)
    idempotency_key: str = Field(min_length=8, max_length=200)


class RetryBody(_Body):
    idempotency_key: str = Field(min_length=8, max_length=200)


class AmendBody(_Body):
    """A change the user CONFIRMED (e.g. an agent's suggested change from a visit): one new step."""
    revision: int = Field(ge=0)
    summary: str = Field(min_length=3, max_length=300)
    agent_id: str = Field(min_length=1, max_length=40)
    after_task_id: str | None = Field(default=None, max_length=100)
    idempotency_key: str = Field(min_length=8, max_length=200)


def register_phase3_routes(app: FastAPI) -> None:
    async def require_session(session_id: str, request: Request,
                              authorization: str | None = Header(default=None)) -> dict[str, Any]:
        # Same bearer-token contract as the Phase 2 routes.
        row = request.app.state.storage.get_session(session_id)
        if row is None:
            raise HTTPException(status_code=404, detail="unknown session")
        if not request.app.state.settings.require_pairing:
            return row
        if not authorization or not authorization.lower().startswith("bearer "):
            raise HTTPException(status_code=401, detail="missing bearer token")
        if not verify_token(authorization.split(" ", 1)[1].strip(), row["token_hash"]):
            raise HTTPException(status_code=401, detail="invalid token for session")
        return row

    async def side_gate(request: Request, session=Depends(require_session)) -> dict[str, Any]:
        require_flag(request, "p3_side_conversation_enabled")
        return session

    async def manager_gate(request: Request, session=Depends(require_session)) -> dict[str, Any]:
        require_flag(request, "p3_manager_enabled")
        return session

    def store2(request: Request) -> Phase2Store:
        return request.app.state.phase2_store

    def side_service(request: Request) -> SideConversationService:
        st = request.app.state
        if getattr(st, "p3_side", None) is None:
            st.p3_side = SideConversationService(st.phase2_store, st.phase3_store, st.settings,
                                                 lambda: get_provider(app))
        return st.p3_side

    def manager_service(request: Request) -> ManagerPlanService:
        st = request.app.state
        return ManagerPlanService(st.phase2_store, st.phase3_store, st.phase2_bus,
                                  st.settings.state_source_mode, get_provider(request.app),
                                  st.settings.p3_plan_timeout_seconds,
                                  execute_code=bool(st.settings.p3_execute_code))

    def plan_lock(request: Request, plan_id: str) -> asyncio.Lock:
        st = request.app.state
        if getattr(st, "p3_plan_locks", None) is None:
            st.p3_plan_locks = {}
        return st.p3_plan_locks.setdefault(plan_id, asyncio.Lock())

    # ─────────────────────── side conversation ───────────────────────
    @app.post("/v2/sessions/{session_id}/agents/{agent_id}/side-question")
    async def side_question(session_id: str, agent_id: str, body: SideQuestionBody,
                            request: Request, session=Depends(side_gate)) -> dict[str, Any]:
        svc = side_service(request)
        req = {**body.model_dump(mode="json"), "agent_id": agent_id}

        async def run() -> dict[str, Any]:
            return await svc.ask(session_id, req)

        return await idempotent(
            store2(request), session_id, f"side.{agent_id}", body.model_dump(mode="json"), run,
            cache=lambda r: r["status"] in ("answered", "amendment_suggested"))

    @app.delete("/v2/sessions/{session_id}/agents/{agent_id}/visits/{visit_session_id}")
    async def end_visit(session_id: str, agent_id: str, visit_session_id: str,
                        request: Request, session=Depends(side_gate)) -> dict[str, Any]:
        n = side_service(request).clear_visit(session_id, visit_session_id)
        return {"visit_session_id": visit_session_id, "agent_id": agent_id,
                "cleared_exchanges": n}

    # ─────────────────────── manager plans ───────────────────────
    @app.post("/v2/sessions/{session_id}/manager/plans", status_code=201)
    async def propose_plan(session_id: str, body: ProposeBody, request: Request,
                           session=Depends(manager_gate)) -> dict[str, Any]:
        svc = manager_service(request)
        return await idempotent(
            store2(request), session_id, "manager.propose", body.model_dump(mode="json"),
            lambda: svc.propose(session_id, body.request_id, body.objective.strip()))

    @app.get("/v2/sessions/{session_id}/manager/plans/{plan_id}")
    async def get_plan(session_id: str, plan_id: str, request: Request,
                       session=Depends(manager_gate)) -> dict[str, Any]:
        return manager_service(request).get(session_id, plan_id)

    @app.post("/v2/sessions/{session_id}/manager/plans/{plan_id}/revise")
    async def revise_plan(session_id: str, plan_id: str, body: ReviseBody, request: Request,
                          session=Depends(manager_gate)) -> dict[str, Any]:
        svc = manager_service(request)
        async with plan_lock(request, plan_id):
            return await idempotent(
                store2(request), session_id, f"manager.{plan_id}.revise",
                body.model_dump(mode="json"),
                lambda: svc.revise(session_id, plan_id, body.revision, body.items, body.title))

    @app.post("/v2/sessions/{session_id}/manager/plans/{plan_id}/reject")
    async def reject_plan(session_id: str, plan_id: str, body: RejectBody, request: Request,
                          session=Depends(manager_gate)) -> dict[str, Any]:
        svc = manager_service(request)
        async with plan_lock(request, plan_id):
            return await idempotent(
                store2(request), session_id, f"manager.{plan_id}.reject",
                body.model_dump(mode="json"),
                lambda: svc.reject(session_id, plan_id, body.revision, body.reason))

    @app.get("/v2/sessions/{session_id}/manager/status")
    async def manager_status(session_id: str, request: Request,
                             session=Depends(manager_gate)) -> dict[str, Any]:
        return manager_service(request).manager_status(session_id)

    @app.get("/v2/sessions/{session_id}/manager/plans/{plan_id}/status")
    async def plan_status(session_id: str, plan_id: str, request: Request,
                          session=Depends(manager_gate)) -> dict[str, Any]:
        return manager_service(request).plan_status(session_id, plan_id)

    @app.post("/v2/sessions/{session_id}/manager/plans/{plan_id}/tasks/{task_id}/retry")
    async def retry_plan_card(session_id: str, plan_id: str, task_id: str, body: RetryBody,
                              request: Request, session=Depends(manager_gate)) -> dict[str, Any]:
        # Explicit user command only; the service is itself idempotent (a repeat is a no-op).
        svc = manager_service(request)
        async with plan_lock(request, plan_id):
            return await idempotent(
                store2(request), session_id, f"manager.{plan_id}.retry.{task_id}",
                body.model_dump(mode="json"),
                lambda: svc.retry_card(session_id, plan_id, task_id))

    @app.post("/v2/sessions/{session_id}/manager/plans/{plan_id}/amend")
    async def amend_plan(session_id: str, plan_id: str, body: AmendBody, request: Request,
                         session=Depends(manager_gate)) -> dict[str, Any]:
        # Explicit, confirmed user command only (the visit card's Apply key): adds ONE step.
        svc = manager_service(request)
        async with plan_lock(request, plan_id):
            return await idempotent(
                store2(request), session_id, f"manager.{plan_id}.amend",
                body.model_dump(mode="json"),
                lambda: svc.amend(session_id, plan_id, body.revision, body.summary,
                                  body.agent_id, body.after_task_id))

    @app.post("/v2/sessions/{session_id}/manager/plans/{plan_id}/commit")
    async def commit_plan(session_id: str, plan_id: str, body: CommitBody, request: Request,
                          session=Depends(manager_gate)) -> dict[str, Any]:
        svc = manager_service(request)
        async with plan_lock(request, plan_id):
            return await idempotent(
                store2(request), session_id, f"manager.{plan_id}.commit",
                body.model_dump(mode="json"),
                lambda: svc.commit(session_id, plan_id, body.revision,
                                   body.approved_item_ids, body.authorized_dangerous_item_ids))
