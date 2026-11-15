"""Operations on COMMITTED plans: status, blocked reasons, retry and final synthesis.

Everything here reads the real card rows, events and artifacts. Nothing is invented: the
synthesis is either a deterministic roll-up of recorded results or, when a model is
available, a labelled model rewrite of exactly those recorded facts.
"""
from __future__ import annotations

import asyncio
import json
import logging
from typing import Any, Callable

from ..phase2.artifacts import ArtifactService
from ..phase2.events import Phase2EventBus, utcnow_iso
from ..phase2.models import ArtifactKind, EventTypeV2, TaskStateV2
from ..phase2.sanitize import sanitize_text
from ..phase2.store import Phase2Store
from .common import PlanCommandError
from .provider import ModelProvider
from .store import Phase3Store

log = logging.getLogger("musedesk.phase3.plan_ops")

DONE = TaskStateV2.completed.value
FAILED = TaskStateV2.failed.value
CANCELLED = TaskStateV2.cancelled.value
BLOCKED = TaskStateV2.blocked.value
TERMINAL = (DONE, FAILED, CANCELLED)

SYNTH_SYSTEM = (
    "You write the closing summary of a small team's plan. You are given the plan and the "
    "recorded result of every step. Use ONLY those facts: do not add results, test outcomes, "
    "files or deployments that are not listed. State clearly which steps failed or were "
    "blocked. Plain text, under 250 words, short headings.")

LABEL_SCRIPTED = "[SCRIPTED DEMO] No model was used; this is a roll-up of the scripted steps."
LABEL_ROLLUP = "Deterministic roll-up of the recorded step results. No model was used."
LABEL_MODEL = "Model-written from the recorded step results, no tools: nothing was run or applied."


class PlanOps:
    def __init__(self, store2: Phase2Store, store3: Phase3Store, bus: Phase2EventBus,
                 source_mode: str, provider_getter: Callable[[], ModelProvider | None] | None = None,
                 timeout: float = 60.0) -> None:
        self._s2 = store2
        self._s3 = store3
        self._bus = bus
        self._mode = source_mode
        self._provider = provider_getter or (lambda: None)
        self._timeout = timeout
        self._synth_inflight: set[str] = set()

    # ─────────────────────── reading the cards ───────────────────────
    def _rows(self, plan: dict[str, Any]) -> list[tuple[dict, dict]]:
        out = []
        for entry in plan.get("committed_tasks", []):
            row = self._s2.get_task_v2(entry["task_id"])
            if row is not None:
                out.append((entry, row))
        return out

    @staticmethod
    def _deps(row: dict) -> list[str]:
        return json.loads(row["dependency_json"])

    def root_failures(self, row: dict, _seen: set[str] | None = None) -> list[dict]:
        """Failed/cancelled ancestors that keep ``row`` from ever running (transitive)."""
        seen = _seen if _seen is not None else set()
        roots: list[dict] = []
        for dep_id in self._deps(row):
            if dep_id in seen:
                continue
            seen.add(dep_id)
            dep = self._s2.get_task_v2(dep_id)
            if dep is None:
                continue
            if dep["task_state"] in (FAILED, CANCELLED):
                roots.append(dep)
            elif dep["task_state"] != DONE:
                roots.extend(self.root_failures(dep, seen))
        return roots

    def blocked_info(self, row: dict) -> dict[str, Any] | None:
        if row["task_state"] != BLOCKED:
            return None
        roots = self.root_failures(row)
        if roots:
            names = ", ".join(f"'{r['title']}' ({r['task_state']})" for r in roots)
            return {"reason": f"Blocked: it depends on {names}. Retry the failed step to continue.",
                    "blocked_by": [r["id"] for r in roots], "cause": "dependency_failed"}
        waiting = []
        for d in self._deps(row):
            dep = self._s2.get_task_v2(d)
            if dep is None or dep["task_state"] != DONE:
                waiting.append(dep["title"] if dep else d)
        reason = ("Waiting for " + ", ".join(f"'{w}'" for w in waiting) + " to finish."
                  if waiting else "Waiting to be released.")
        return {"reason": reason, "blocked_by": [], "cause": "waiting"}

    def _failure_text(self, sid: str, task_id: str) -> str:
        text = "Failed."
        for ev in self._s2.events_after_v2(sid, 0):
            if ev.get("task_id") == task_id and ev["type"] == EventTypeV2.task_failed.value:
                text = str((ev.get("payload") or {}).get("result_summary") or text)
        return text

    def _result_text(self, sid: str, row: dict) -> str:
        state = row["task_state"]
        if state == DONE:
            arts = [a for a in self._s2.list_artifacts_v2(sid)
                    if a["task_id"] == row["id"] and a["kind"] != ArtifactKind.completion.value]
            return arts[-1]["summary"] if arts else "Completed (no artifact recorded)."
        if state == FAILED:
            return self._failure_text(sid, row["id"])
        if state == CANCELLED:
            return "Cancelled before it finished."
        info = self.blocked_info(row)
        if info:
            return info["reason"]
        return f"Not finished ({state})."

    # ─────────────────────── status ───────────────────────
    def plan_status(self, plan_id: str) -> dict[str, Any]:
        plan = self._s3.get_plan(plan_id)
        if plan is None:
            raise PlanCommandError("NOT_FOUND", f"unknown plan {plan_id}", status=404)
        counts: dict[str, int] = {}
        cards, failed, blocked = [], [], []
        for entry, row in self._rows(plan):
            st = row["task_state"]
            counts[st] = counts.get(st, 0) + 1
            info = self.blocked_info(row)
            card = {"task_id": row["id"], "client_item_id": entry["client_item_id"],
                    "title": row["title"], "agent_id": row["agent_id"], "state": st,
                    "revision": row["revision"], "depends_on": self._deps(row)}
            if info:
                card["blocked_reason"] = info["reason"]
                card["blocked_by"] = info["blocked_by"]
                blocked.append({"task_id": row["id"], "reason": info["reason"],
                                "blocked_by": info["blocked_by"]})
            if st == FAILED:
                card["failure"] = self._failure_text(plan["session_id"], row["id"])
                failed.append({"task_id": row["id"], "title": row["title"],
                               "reason": card["failure"]})
            cards.append(card)
        return {"plan_id": plan_id, "title": plan["title"], "plan_status": plan["status"],
                "provenance": plan["provenance"], "total": len(cards), "counts": counts,
                "cards": cards, "failed": failed, "blocked": blocked,
                "settled": plan["status"] == "committed" and self._settled(plan),
                "synthesis": plan.get("synthesis"), "source_mode": self._mode}

    def manager_status(self, session_id: str) -> dict[str, Any]:
        plans = [self.plan_status(p["plan_id"]) for p in self._s3.plans_for_session(session_id)
                 if p["status"] == "committed"]
        counts: dict[str, int] = {}
        for p in plans:
            for k, v in p["counts"].items():
                counts[k] = counts.get(k, 0) + v
        return {"session_id": session_id, "plans": plans, "counts": counts,
                "failed": [f for p in plans for f in p["failed"]],
                "blocked": [b for p in plans for b in p["blocked"]],
                "source_mode": self._mode}

    # ─────────────────────── blocked announcement ───────────────────────
    async def announce_blocked_dependents(self, plan: dict[str, Any], failed_row: dict) -> int:
        """After a card fails/cancels, tell the stream which cards are blocked and why."""
        n = 0
        for _entry, row in self._rows(plan):
            info = self.blocked_info(row)
            if not info or failed_row["id"] not in info["blocked_by"]:
                continue
            await self._bus.publish(
                row["session_id"], EventTypeV2.task_progress,
                {"revision": row["revision"], "task_state": BLOCKED, "plan_id": plan["plan_id"],
                 "blocked_reason": info["reason"], "blocked_by": info["blocked_by"],
                 "depends_on": self._deps(row), "source_mode": self._mode},
                task_id=row["id"], agent_id=row["agent_id"],
                event_id=f"evt2-plan-{row['id']}-blockedby-{failed_row['id']}-r{failed_row['revision']}")
            n += 1
        return n

    # ─────────────────────── retry ───────────────────────
    async def retry_card(self, session_id: str, plan_id: str, task_id: str) -> dict[str, Any]:
        plan = self._s3.get_plan(plan_id)
        if plan is None or plan["session_id"] != session_id or plan["status"] != "committed":
            raise PlanCommandError("NOT_FOUND", f"no committed plan {plan_id}", status=404)
        if task_id not in {e["task_id"] for e in plan.get("committed_tasks", [])}:
            raise PlanCommandError("NOT_FOUND", "that card does not belong to this plan", status=404)
        row = self._s2.get_task_v2(task_id)
        if row is None:
            raise PlanCommandError("NOT_FOUND", "unknown card", status=404)
        st = row["task_state"]
        if st != FAILED:
            if st in (TaskStateV2.queued.value, TaskStateV2.running.value, DONE):
                # Already retried (or never failed): a repeat is a no-op, not an error.
                return {"task_id": task_id, "retried": False, "state": st, "revision": row["revision"]}
            raise PlanCommandError("NOT_RETRYABLE", f"only a failed card can be retried (it is {st})",
                                   status=409)
        ready = all((d := self._s2.get_task_v2(i)) and d["task_state"] == DONE
                    for i in self._deps(row))
        new_state = TaskStateV2.queued if ready else TaskStateV2.blocked
        updated = self._s2.update_task_v2(task_id, task_state=new_state.value, now=utcnow_iso())
        await self._bus.publish(
            session_id, EventTypeV2.task_resumed,
            {"revision": updated["revision"], "task_state": updated["task_state"],
             "reason": "retry_requested", "plan_id": plan_id, "source_mode": self._mode},
            task_id=task_id, agent_id=updated["agent_id"],
            event_id=f"evt2-plan-{task_id}-r{updated['revision']}-retry")
        # A retried plan is no longer finished; its old synthesis is stale.
        if plan.get("synthesis"):
            await self._save(plan_id, lambda p: p.update(synthesis=None))
        return {"task_id": task_id, "retried": True, "state": updated["task_state"],
                "revision": updated["revision"]}

    async def _save(self, plan_id: str, mutate: Callable[[dict], Any]) -> None:
        for _ in range(3):
            plan = self._s3.get_plan(plan_id)
            if plan is None:
                return
            mutate(plan)
            rev = plan["revision"]
            plan["revision"] = rev + 1
            if self._s3.save_plan(plan, rev, utcnow_iso()):
                return

    # ─────────────────────── synthesis ───────────────────────
    def _settled(self, plan: dict[str, Any]) -> bool:
        rows = self._rows(plan)
        if not rows:
            return False
        for _e, row in rows:
            st = row["task_state"]
            if st in TERMINAL:
                continue
            if st == BLOCKED and self.root_failures(row):
                continue
            return False
        return True

    def rollup(self, plan: dict[str, Any]) -> str:
        sid = plan["session_id"]
        rows = self._rows(plan)
        n = len(rows)
        c = {k: sum(1 for _e, r in rows if r["task_state"] == k) for k in (DONE, FAILED, CANCELLED)}
        nblocked = sum(1 for _e, r in rows if r["task_state"] == BLOCKED)
        lines = [f"Plan: {plan['title']}", f"Objective: {plan.get('objective', '')}",
                 f"Result: {c[DONE]} of {n} steps completed, {c[FAILED]} failed, "
                 f"{nblocked} blocked, {c[CANCELLED]} cancelled.", "", "Steps:"]
        for i, (entry, row) in enumerate(rows, 1):
            lines.append(f"{i}. [{row['task_state']}] {row['title']} ({row['agent_id']}): "
                         f"{self._result_text(sid, row)}")
        if c[FAILED] or nblocked or c[CANCELLED]:
            lines += ["", "Open issues: not every step finished. Retry failed steps to continue."]
        return "\n".join(lines)

    async def synthesize_if_settled(self, plan_id: str) -> str | None:
        """Create the plan's one summary artifact once every card has finished. Returns the
        artifact id, or None if not settled / already done."""
        plan = self._s3.get_plan(plan_id)
        if (plan is None or plan["status"] != "committed" or plan.get("synthesis")
                or plan_id in self._synth_inflight or not self._settled(plan)):
            return None
        self._synth_inflight.add(plan_id)
        try:
            return await self._synthesize(plan)
        finally:
            self._synth_inflight.discard(plan_id)

    async def _synthesize(self, plan: dict[str, Any]) -> str | None:
        sid = plan["session_id"]
        rows = self._rows(plan)
        facts = self.rollup(plan)
        text, label, prov = facts, LABEL_ROLLUP, None
        if self._mode == "demo":
            label = LABEL_SCRIPTED
        else:
            provider = self._provider()
            if provider is not None:
                try:
                    out = await asyncio.wait_for(
                        provider.complete(system=SYNTH_SYSTEM,
                                          messages=[{"role": "user", "content": facts}],
                                          max_tokens=1500, purpose="plan"),
                        timeout=self._timeout)
                    out = sanitize_text(out or "").strip()[:6000]
                    if out:
                        text, label, prov = out, LABEL_MODEL, "model_text_no_tools"
                except Exception:  # noqa: BLE001 - fall back to the factual roll-up
                    log.warning("plan synthesis model call failed; using roll-up")
        if self._s3.get_plan(plan["plan_id"]).get("synthesis"):
            return None
        done = [r for _e, r in rows if r["task_state"] == DONE]
        anchor = (done or [r for _e, r in rows])[-1]
        c = sum(1 for _e, r in rows if r["task_state"] == DONE)
        summary = f"Plan summary: {c} of {len(rows)} steps completed"
        if self._mode == "demo":
            summary = "[SCRIPTED DEMO] " + summary
        art = await ArtifactService(self._s2, self._bus, self._mode).create_artifact(
            sid, anchor["id"], ArtifactKind.completion.value, summary,
            [{"page": 0, "title": f"Plan summary ({label})"[:120],
              "content": f"{label}\n\n{text}", "line_refs": []}],
            source_mode=self._mode, work_provenance=prov)
        mode_name = ("scripted_demo" if self._mode == "demo"
                     else "model" if prov else "rollup")
        await self._save(plan["plan_id"], lambda p: p.update(
            synthesis={"artifact_id": art.artifact_id, "task_id": anchor["id"],
                       "mode": mode_name, "completed": c, "total": len(rows)}))
        return art.artifact_id
