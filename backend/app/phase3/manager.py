"""Manager plan service: propose -> revise -> reject | commit.

Trust model: a plan is DATA. Nothing exists on the board until ``commit`` succeeds, and
commit creates cards only through the existing ``TaskGraphService.create_task`` (so
dependency gating is the existing ``blocked``/``queued`` logic, not a copy of it).
"""
from __future__ import annotations

import asyncio
import copy
import logging
import uuid
from typing import Any

from ..phase2.events import Phase2EventBus, utcnow_iso
from ..phase2.models import EventTypeV2
from ..phase2.sanitize import sanitize_text
from ..phase2.store import Phase2Store
from ..phase2.task_graph import TaskGraphService
from .common import PlanCommandError
from .planner import (DEMO_NOTE, MODEL_NOTE, PLANNER_SYSTEM, TEMPLATE_NOTE,
                      build_model_messages, parse_model_plan, template_plan)
from .plan_ops import PlanOps
from .plan_schema import validate_plan_document
from .provider import ModelProvider
from .store import Phase3Store
from .validation import ValidationResult, topological_order, validate_items

log = logging.getLogger("musedesk.phase3.manager")

OPEN_STATES = ("proposed", "approved")


def public_plan(plan: dict[str, Any]) -> dict[str, Any]:
    out = copy.deepcopy(plan)
    out.pop("session_id", None)
    return out


class ManagerPlanService:
    def __init__(self, store2: Phase2Store, store3: Phase3Store, bus: Phase2EventBus,
                 source_mode: str, provider: ModelProvider | None = None,
                 plan_timeout: float = 20.0) -> None:
        self._s2 = store2
        self._s3 = store3
        self._bus = bus
        self._mode = source_mode
        self._provider = provider
        self._timeout = plan_timeout
        self._graph = TaskGraphService(store2, bus, source_mode)

    # ─────────────────────── helpers ───────────────────────
    def _require(self, session_id: str, plan_id: str) -> dict[str, Any]:
        plan = self._s3.get_plan(plan_id)
        if plan is None or plan["session_id"] != session_id:
            raise PlanCommandError("NOT_FOUND", f"unknown plan {plan_id}", status=404)
        return plan

    @staticmethod
    def _issues(res: ValidationResult) -> list[dict[str, Any]]:
        return [i.as_dict() for i in res.issues]

    def _invalid(self, code: str, res: ValidationResult) -> PlanCommandError:
        first = res.issues[0].message if res.issues else "invalid plan"
        return PlanCommandError(code, f"The plan failed validation: {first}", status=422,
                                details=self._issues(res))

    async def _emit(self, session_id: str, type_: EventTypeV2, plan: dict[str, Any],
                    extra: dict[str, Any] | None = None) -> None:
        payload = {"plan_id": plan["plan_id"], "revision": plan["revision"],
                   "status": plan["status"], "title": plan["title"],
                   "provenance": plan["provenance"], "agent_id": "manager",
                   "source_mode": self._mode, **(extra or {})}
        await self._bus.publish(session_id, type_, payload)

    def _bump(self, plan: dict[str, Any], expected_rev: int, status: str | None = None) -> None:
        plan["revision"] = expected_rev + 1
        if status:
            plan["status"] = status
        if not self._s3.save_plan(plan, expected_rev, utcnow_iso()):
            raise PlanCommandError("REVISION_CONFLICT", "plan changed concurrently", status=409,
                                   canonical_plan=public_plan(self._s3.get_plan(plan["plan_id"])))

    def _guard_revision(self, plan: dict[str, Any], revision: int) -> None:
        if plan["revision"] != revision:
            raise PlanCommandError(
                "REVISION_CONFLICT",
                f"plan revision is {plan['revision']}, command expected {revision}",
                status=409, canonical_plan=public_plan(plan))

    def _guard_open(self, plan: dict[str, Any]) -> None:
        if plan["status"] == "committed":
            raise PlanCommandError("PLAN_ALREADY_COMMITTED", "this plan is already committed",
                                   status=409, canonical_plan=public_plan(plan))
        if plan["status"] not in OPEN_STATES:
            raise PlanCommandError("PLAN_NOT_OPEN", f"plan is {plan['status']}", status=409,
                                   canonical_plan=public_plan(plan))

    # ─────────────────────── propose ───────────────────────
    async def propose(self, session_id: str, request_id: str, objective: str) -> dict[str, Any]:
        demo = self._mode == "demo"
        raw: dict[str, Any]
        if self._provider is not None and not demo:
            raw = await self._model_plan(session_id, objective)
            provenance, note = "LIVE_MODEL", MODEL_NOTE
        else:
            raw = template_plan(objective)
            provenance, note = ("SCRIPTED_DEMO", DEMO_NOTE) if demo else ("TEMPLATE_PLAN", TEMPLATE_NOTE)

        res = validate_plan_document(raw, objective)
        if not res.ok:
            code = "MODEL_PLAN_INVALID" if provenance == "LIVE_MODEL" else "PLAN_INVALID"
            raise self._invalid(code, res)
        now = utcnow_iso()
        plan = {
            "plan_id": f"mp-{uuid.uuid4().hex[:10]}", "session_id": session_id,
            "source_request_id": request_id,
            "title": self._text(raw.get("title"), 120) or "Plan",
            "objective": objective, "created_at": now, "provenance": provenance,
            "provenance_note": note,
            "assumptions": self._text_list(raw.get("assumptions")),
            "risks": self._text_list(raw.get("risks")),
            "items": res.items, "status": "proposed", "revision": 1,
            "reject_reason": None, "failure": None, "committed_tasks": [],
        }
        self._s3.insert_plan(plan, now)
        await self._emit(session_id, EventTypeV2.manager_plan_proposed, plan,
                         {"objective": objective, "items": plan["items"],
                          "assumptions": plan["assumptions"], "risks": plan["risks"],
                          "provenance_note": note})
        return public_plan(plan)

    async def _model_plan(self, session_id: str, objective: str) -> dict[str, Any]:
        tasks = self._s2.list_tasks_v2(session_id)
        board = "\n".join(f"- {sanitize_text(t['title'])[:80]} [{t['task_state']}]"
                          for t in tasks[:20]) or "(empty)"
        try:
            text = await asyncio.wait_for(
                self._provider.complete(system=PLANNER_SYSTEM,
                                        messages=build_model_messages(objective, board),
                                        max_tokens=1500, purpose="plan"),
                timeout=self._timeout)
        except asyncio.TimeoutError:
            raise PlanCommandError("PLANNER_TIMEOUT", "The planning model did not answer in time.",
                                   status=504)
        except Exception:  # noqa: BLE001 - provider failure must not leak internals
            log.warning("manager planner provider failed")
            raise PlanCommandError("PLANNER_UNAVAILABLE", "The planning model is unavailable.",
                                   status=503)
        obj = parse_model_plan(text)
        if obj is None:
            raise PlanCommandError(
                "MODEL_PLAN_INVALID", "The planning model did not return a JSON plan.", status=422,
                details=[{"code": "NOT_JSON", "message": "model output was not a JSON object"}])
        return obj

    @staticmethod
    def _text(v: Any, limit: int) -> str:
        return sanitize_text(v.strip())[:limit] if isinstance(v, str) else ""

    def _text_list(self, v: Any) -> list[str]:
        if not isinstance(v, list):
            return []
        return [t for t in (self._text(x, 200) for x in v[:8]) if t]

    # ─────────────────────── revise / reject ───────────────────────
    async def revise(self, session_id: str, plan_id: str, revision: int,
                     items: list[dict[str, Any]] | None, title: str | None) -> dict[str, Any]:
        plan = self._require(session_id, plan_id)
        self._guard_revision(plan, revision)
        self._guard_open(plan)
        if items is None and title is None:
            raise PlanCommandError("NOTHING_TO_REVISE", "send items and/or title", status=422)
        if items is not None:
            res = validate_items(items)
            if not res.ok:
                raise self._invalid("PLAN_INVALID", res)
            plan["items"] = res.items
        if title is not None:
            t = self._text(title, 120)
            if not t:
                raise PlanCommandError("BAD_TITLE", "title must not be empty", status=422)
            plan["title"] = t
        self._bump(plan, revision, "proposed")
        await self._emit(session_id, EventTypeV2.manager_plan_revised, plan,
                         {"items": plan["items"]})
        return public_plan(plan)

    async def reject(self, session_id: str, plan_id: str, revision: int,
                     reason: str) -> dict[str, Any]:
        plan = self._require(session_id, plan_id)
        self._guard_revision(plan, revision)
        self._guard_open(plan)
        plan["reject_reason"] = self._text(reason, 300) or None
        self._bump(plan, revision, "rejected")
        await self._emit(session_id, EventTypeV2.manager_plan_rejected, plan,
                         {"reason": plan["reject_reason"]})
        return public_plan(plan)

    def get(self, session_id: str, plan_id: str) -> dict[str, Any]:
        return public_plan(self._require(session_id, plan_id))

    # ─────────────────────── status / retry (domain; see plan_ops.py) ───────────────────────
    def _ops(self) -> PlanOps:
        return PlanOps(self._s2, self._s3, self._bus, self._mode, lambda: self._provider,
                       timeout=self._timeout)

    def plan_status(self, session_id: str, plan_id: str) -> dict[str, Any]:
        self._require(session_id, plan_id)
        return self._ops().plan_status(plan_id)

    def manager_status(self, session_id: str) -> dict[str, Any]:
        return self._ops().manager_status(session_id)

    async def retry_card(self, session_id: str, plan_id: str, task_id: str) -> dict[str, Any]:
        self._require(session_id, plan_id)
        return await self._ops().retry_card(session_id, plan_id, task_id)

    # ─────────────────────── commit ───────────────────────
    async def commit(self, session_id: str, plan_id: str, revision: int,
                     approved_ids: list[str], authorized_ids: list[str]) -> dict[str, Any]:
        plan = self._require(session_id, plan_id)
        self._guard_revision(plan, revision)
        self._guard_open(plan)

        res = validate_items(plan["items"])          # re-validate at the trust boundary
        if not res.ok:
            raise self._invalid("PLAN_INVALID", res)
        by_id = {it["client_item_id"]: it for it in res.items}

        approved = list(dict.fromkeys(approved_ids))
        if not approved:
            raise PlanCommandError("NO_ITEMS_APPROVED", "approve at least one item", status=422)
        unknown = [i for i in approved + list(authorized_ids) if i not in by_id]
        if unknown:
            raise PlanCommandError("UNKNOWN_ITEM", "unknown item id(s): " + ", ".join(sorted(set(unknown))),
                                   status=422)
        aset = set(approved)
        missing = [{"item_id": i, "code": "DEPENDENCY_NOT_APPROVED",
                    "message": f"'{i}' depends on '{d}', which is not approved"}
                   for i in approved for d in by_id[i]["depends_on"] if d not in aset]
        if missing:
            raise PlanCommandError("DEPENDENCY_NOT_APPROVED", missing[0]["message"], status=409,
                                   details=missing)
        authorized = set(authorized_ids)
        need = [{"item_id": i, "code": "AUTHORIZATION_REQUIRED",
                 "message": f"'{i}' is dangerous ({', '.join(by_id[i]['danger_reasons'])}) and "
                            "needs explicit authorization"}
                for i in approved if by_id[i]["dangerous"] and i not in authorized]
        if need:
            raise PlanCommandError("AUTHORIZATION_REQUIRED", need[0]["message"], status=409,
                                   details=need)

        graph_deps = {i: by_id[i]["depends_on"] for i in by_id if i in aset}
        order = topological_order(graph_deps)

        # ── atomic section: synchronous creates, no await, so nothing can interleave ──
        canonical: dict[str, str] = {}
        cards = []
        try:
            for cid in order:
                it = by_id[cid]
                card = self._graph.create_task(
                    session_id, it["title"], it["proposed_agent"], 1,
                    [canonical[d] for d in it["depends_on"]], None)
                canonical[cid] = card.task_id
                cards.append((cid, card))
        except Exception as exc:  # noqa: BLE001 - any failure rolls everything back
            self._s3.delete_tasks_v2([c.task_id for _, c in cards])
            code = getattr(exc, "code", "COMMIT_FAILED")
            plan["failure"] = {"code": code, "message": str(exc)[:200]}
            self._bump(plan, revision, "failed")
            await self._emit(session_id, EventTypeV2.manager_plan_failed, plan,
                             {"reason": plan["failure"]})
            log.warning("manager commit failed and was rolled back (%s)", code)
            raise PlanCommandError("COMMIT_FAILED",
                                   "Creating the tasks failed; nothing was created.",
                                   status=500, canonical_plan=public_plan(plan))

        plan["committed_tasks"] = [
            {"client_item_id": cid, "task_id": c.task_id, "agent_id": c.agent_id,
             "state": c.task_state.value, "revision": c.revision,
             "depends_on": list(c.dependencies)} for cid, c in cards]
        edges = [{"from_task_id": d, "to_task_id": t["task_id"]}
                 for t in plan["committed_tasks"] for d in t["depends_on"]]
        try:
            self._bump(plan, revision, "committed")
        except PlanCommandError:
            self._s3.delete_tasks_v2([c.task_id for _, c in cards])
            raise

        # Cards become visible to the headset only now, after the commit is durable.
        for _, card in cards:
            await self._graph.announce_created(session_id, card)
        await self._emit(session_id, EventTypeV2.manager_plan_committed, plan,
                         {"tasks": plan["committed_tasks"], "edges": edges,
                          "task_ids": {t["client_item_id"]: t["task_id"]
                                       for t in plan["committed_tasks"]}})
        return {"plan_id": plan["plan_id"], "status": "committed",
                "revision": plan["revision"], "tasks": plan["committed_tasks"]}
