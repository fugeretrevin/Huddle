"""Plan runner: works through the board cards a COMMITTED Manager plan created.

Scope and trust model:
- It only touches cards listed in a committed plan's ``committed_tasks`` (the user approved
  each one, and dangerous ones were explicitly authorized). Pipeline (``auth_demo``) cards,
  hand-made board cards and cards of open/rejected plans are never touched.
- A card is picked up only when it is ``queued`` (dependencies completed). One plan card runs
  at a time per session. ``pipeline_busy`` can hold the runner (not wired by default: a
  pipeline decision can wait for the human indefinitely and must not block plan work).
- The work is TEXT ONLY. In live mode the configured model writes the step's deliverable
  (findings, a proposed change, a test plan) with no tools: nothing is executed, no file is
  written and no repository is touched. Every artifact says so. In demo mode the
  deliverable is scripted and labelled ``[SCRIPTED DEMO]``. In live mode with no model,
  cards are left queued and one warning is published.
- If the user pauses or cancels a card while it is being worked, the result is discarded.
- A model failure, refusal or timeout fails the card (dependents stay blocked).
- After a restart, a plan card left ``running`` is failed (nothing is driving it).
"""
from __future__ import annotations

import asyncio
import logging
from typing import Any, Callable

from ..phase2.artifacts import ArtifactService
from ..phase2.board_bridge import BoardRunBridge
from ..phase2.events import Phase2EventBus, utcnow_iso
from ..phase2.handoffs import HandoffService
from ..phase2.models import ArtifactKind, EventTypeV2, HandoffKind, TaskStateV2
from ..phase2.sanitize import sanitize_text
from ..phase2.store import Phase2Store
from .provider import ModelProvider
from .store import Phase3Store

log = logging.getLogger("musedesk.phase3.runner")

MAX_DEP_CHARS = 3000
MAX_OUTPUT_CHARS = 10000

WORKER_SYSTEM = {
    "researcher": (
        "You are the Researcher on a small software team. Write the research deliverable for "
        "ONE plan step: the findings, the options with their trade-offs, and a recommendation. "
        "You have no tools and cannot read the repository, so reason from the objective and "
        "the inputs given, and state clearly what would need checking in the real code."),
    "developer": (
        "You are the Developer on a small software team. Write the implementation deliverable "
        "for ONE plan step: the approach, the files or modules likely to change, and the key "
        "code as a proposed change. You have no tools: nothing you write is applied or run, "
        "so present it as a proposal and list what must be verified."),
    "tester": (
        "You are the Tester on a small software team. Write the test deliverable for ONE plan "
        "step: a test plan with concrete cases (inputs, expected results), edge cases, and the "
        "acceptance checks. You have no tools and ran no tests, so never claim a test passed."),
}
COMMON_RULES = (
    " Use plain text with short headings. Stay under 500 words. Do not invent results, "
    "test outcomes, file contents or deployments. End with a line starting 'Summary:' that "
    "gives the outcome in one sentence.")

KIND_FOR_AGENT = {
    "researcher": ArtifactKind.research_report,
    "developer": ArtifactKind.implementation_plan,
    "tester": ArtifactKind.implementation_plan,
}
HANDOFF_FOR_TARGET = {
    "developer": HandoffKind.blueprint,
    "tester": HandoffKind.test_request,
    "researcher": HandoffKind.review,
}
TITLE_FOR_AGENT = {"researcher": "Research write-up", "developer": "Proposed change",
                   "tester": "Test plan"}


def _scripted(agent: str, item: dict[str, Any], objective: str) -> str:
    crit = "; ".join(item.get("acceptance_criteria") or []) or "the step's acceptance criteria"
    body = {
        "researcher": f"Findings for '{objective}': the existing structure is reused and the "
                      "options are compared on effort and risk. Recommendation: the simplest "
                      "option that meets the criteria.",
        "developer": f"Proposed change for '{objective}': the relevant module gains the new "
                     "behaviour behind its existing interface. Nothing was applied.",
        "tester": f"Test plan for '{objective}': one happy-path case, one failure case and one "
                  "edge case per acceptance criterion. No tests were run.",
    }.get(agent, "Deliverable drafted.")
    return (f"[SCRIPTED DEMO] No model was used.\n\n{body}\n\nAcceptance criteria: {crit}\n\n"
            f"Summary: scripted {TITLE_FOR_AGENT.get(agent, 'deliverable').lower()} for "
            f"'{item.get('title', '')}'.")


def _summary_line(text: str, fallback: str) -> str:
    for line in reversed(text.splitlines()):
        s = line.strip()
        if s.lower().startswith("summary:"):
            return s[8:].strip() or fallback
    return fallback


class PlanRunner:
    def __init__(self, store2: Phase2Store, store3: Phase3Store, bus: Phase2EventBus,
                 settings: Any, provider_getter: Callable[[], ModelProvider | None],
                 pipeline_busy: Callable[[str], bool] | None = None) -> None:
        self._s2 = store2
        self._s3 = store3
        self._bus = bus
        self._settings = settings
        self._provider = provider_getter
        self._pipeline_busy = pipeline_busy or (lambda _sid: False)
        self._board = BoardRunBridge(store2, bus)
        self._warned: set[str] = set()
        self._stop = asyncio.Event()

    # ─────────────────────── lifecycle ───────────────────────
    @property
    def _mode(self) -> str:
        return self._settings.state_source_mode

    async def recover(self) -> int:
        """Fail plan cards a previous process left ``running``."""
        n = 0
        for plan in self._s3.committed_plans():
            for entry in plan.get("committed_tasks", []):
                row = self._s2.get_task_v2(entry["task_id"])
                if row and row["task_state"] == TaskStateV2.running.value and not row.get("run_task_id"):
                    await self._finish(row, TaskStateV2.failed, EventTypeV2.task_failed,
                                       "Stopped: the backend restarted while this step was "
                                       "being worked. Resume it to try again.",
                                       {"code": "RUN_INTERRUPTED"})
                    n += 1
        return n

    async def run_forever(self) -> None:
        await self.recover()
        while not self._stop.is_set():
            try:
                await self.tick()
            except Exception:  # noqa: BLE001 - the loop must survive one bad card
                log.exception("plan runner tick failed")
            try:
                await asyncio.wait_for(self._stop.wait(),
                                       timeout=float(self._settings.p3_runner_poll_seconds))
            except asyncio.TimeoutError:
                pass

    def stop(self) -> None:
        self._stop.set()

    # ─────────────────────── scheduling ───────────────────────
    def _next_card(self, plans: list[dict[str, Any]]) -> tuple[dict, dict, dict] | None:
        for plan in plans:
            items = {it["client_item_id"]: it for it in plan.get("items", [])}
            for entry in plan.get("committed_tasks", []):
                row = self._s2.get_task_v2(entry["task_id"])
                if row is None or row.get("run_task_id") or row.get("task_template"):
                    continue
                if row["task_state"] != TaskStateV2.queued.value:
                    continue
                if not self._board.dependencies_ready(row):
                    continue
                return plan, entry, items.get(entry["client_item_id"], {})
        return None

    async def tick(self) -> int:
        """Work at most one card per session. Returns the number of cards worked."""
        by_session: dict[str, list[dict[str, Any]]] = {}
        for plan in self._s3.committed_plans():
            by_session.setdefault(plan["session_id"], []).append(plan)
        worked = 0
        for sid, plans in by_session.items():
            if any(self._s2.get_task_v2(e["task_id"]) and
                   self._s2.get_task_v2(e["task_id"])["task_state"] == TaskStateV2.running.value
                   for p in plans for e in p.get("committed_tasks", [])):
                continue
            if self._pipeline_busy(sid):
                continue
            nxt = self._next_card(plans)
            if nxt is None:
                continue
            demo = self._mode == "demo"
            provider = None if demo else self._provider()
            if not demo and provider is None:
                if sid not in self._warned:
                    self._warned.add(sid)
                    await self._bus.publish(sid, EventTypeV2.system_warning, {
                        "code": "NO_MODEL_CONFIGURED",
                        "message": "Manager plan cards are waiting: no model is configured, "
                                   "so nothing can work them.", "source_mode": self._mode})
                continue
            await self._work(sid, *nxt, provider)
            worked += 1
        return worked

    # ─────────────────────── one card ───────────────────────
    def _dependency_inputs(self, row: dict) -> str:
        import json
        out = []
        for dep_id in json.loads(row["dependency_json"]):
            dep = self._s2.get_task_v2(dep_id)
            arts = [a for a in self._s2.list_artifacts_v2(row["session_id"]) if a["task_id"] == dep_id]
            if not dep or not arts:
                continue
            pages = json.loads(arts[-1]["pages_json"])
            text = "\n".join(p.get("content", "") for p in pages)[:MAX_DEP_CHARS]
            out.append(f"--- From '{dep['title']}' ({dep['agent_id']}) ---\n{text}")
        return "\n\n".join(out) or "(no earlier steps)"

    async def _work(self, sid: str, plan: dict, entry: dict, item: dict,
                    provider: ModelProvider | None) -> None:
        row = self._s2.get_task_v2(entry["task_id"])
        agent = row["agent_id"] or item.get("proposed_agent") or "developer"
        work_mode = "model_text" if provider else "scripted"
        row = self._s2.update_task_v2(row["id"], task_state=TaskStateV2.running.value,
                                      now=utcnow_iso())
        await self._bus.publish(sid, EventTypeV2.task_started, {
            "revision": row["revision"], "task_state": row["task_state"],
            "plan_id": plan["plan_id"], "work_mode": work_mode, "source_mode": self._mode},
            task_id=row["id"], agent_id=agent,
            event_id=f"evt2-plan-{row['id']}-r{row['revision']}-started")

        try:
            if provider is None:
                await asyncio.sleep(float(self._settings.p3_runner_demo_step_seconds))
                text = _scripted(agent, item, plan.get("objective", ""))
            else:
                prompt = (f"Objective: {plan.get('objective', '')}\nPlan: {plan.get('title', '')}\n\n"
                          f"Your step: {item.get('title', row['title'])}\nKind: {item.get('kind', '')}\n"
                          "Acceptance criteria:\n" + "\n".join(
                              f"- {c}" for c in item.get("acceptance_criteria") or []) +
                          f"\n\nInputs from earlier steps:\n{self._dependency_inputs(row)}")
                text = await asyncio.wait_for(
                    provider.complete(system=WORKER_SYSTEM.get(agent, WORKER_SYSTEM["developer"])
                                      + COMMON_RULES,
                                      messages=[{"role": "user", "content": prompt}],
                                      max_tokens=8000, purpose="work"),
                    timeout=float(self._settings.p3_work_timeout_seconds))
                text = sanitize_text(text or "").strip()[:MAX_OUTPUT_CHARS]
                if not text:
                    raise RuntimeError("empty model output")
        except asyncio.TimeoutError:
            await self._fail_if_running(row["id"], "MODEL_TIMEOUT",
                                        "The model did not finish this step in time.")
            return
        except Exception:  # noqa: BLE001 - provider details never leak to the client
            log.warning("plan runner model call failed for %s", row["id"])
            await self._fail_if_running(row["id"], "MODEL_UNAVAILABLE",
                                        "The model could not work this step.")
            return

        current = self._s2.get_task_v2(row["id"])
        if current is None or current["task_state"] != TaskStateV2.running.value:
            log.info("plan card %s changed while worked (%s); result discarded", row["id"],
                     current and current["task_state"])
            return

        title = TITLE_FOR_AGENT.get(agent, "Deliverable")
        label = ("Model-written, no tools: nothing was run or applied." if provider
                 else "SCRIPTED DEMO: no model was used.")
        summary = f"{title}: {_summary_line(text, row['title'])}"[:280]
        artifacts = ArtifactService(self._s2, self._bus, self._mode)
        art = await artifacts.create_artifact(
            sid, row["id"], KIND_FOR_AGENT.get(agent, ArtifactKind.implementation_plan).value,
            summary, [{"page": 0, "title": f"{title} ({label})", "content": f"{label}\n\n{text}",
                       "line_refs": []}],
            source_mode=self._mode,
            work_provenance="model_text_no_tools" if provider else None)

        await self._finish(current, TaskStateV2.completed, EventTypeV2.task_completed, summary,
                           {"artifact_id": art.artifact_id, "plan_id": plan["plan_id"],
                            "work_mode": work_mode})
        await self._board.release_dependents(sid, row["id"])
        await self._handoffs(sid, plan, row["id"], agent, art.artifact_id)

    async def _handoffs(self, sid: str, plan: dict, done_id: str, from_agent: str,
                        artifact_id: str) -> None:
        import json
        svc = HandoffService(self._s2, self._bus, self._mode)
        for entry in plan.get("committed_tasks", []):
            dep_row = self._s2.get_task_v2(entry["task_id"])
            if not dep_row or done_id not in json.loads(dep_row["dependency_json"]):
                continue
            to_agent = dep_row["agent_id"]
            if not to_agent or to_agent == from_agent:
                continue
            kind = HANDOFF_FOR_TARGET.get(to_agent, HandoffKind.review).value
            try:
                await svc.create(sid, from_agent, to_agent, dep_row["id"], artifact_id, kind,
                                 handoff_id=f"h2-plan-{done_id}-{dep_row['id']}")
            except Exception:  # noqa: BLE001 - a handoff cue is cosmetic; never fail the card
                log.warning("plan handoff %s -> %s not created", done_id, dep_row["id"])

    async def _fail_if_running(self, task_id: str, code: str, reason: str) -> None:
        row = self._s2.get_task_v2(task_id)
        if row and row["task_state"] == TaskStateV2.running.value:
            await self._finish(row, TaskStateV2.failed, EventTypeV2.task_failed, reason,
                               {"code": code})

    async def _finish(self, row: dict, state: TaskStateV2, type_: EventTypeV2, summary: str,
                      extra: dict[str, Any]) -> None:
        updated = self._s2.update_task_v2(row["id"], task_state=state.value, now=utcnow_iso())
        await self._bus.publish(row["session_id"], type_, {
            "revision": updated["revision"], "task_state": updated["task_state"],
            "result_summary": summary, **extra, "source_mode": self._mode},
            task_id=row["id"], agent_id=updated["agent_id"],
            event_id=f"evt2-plan-{row['id']}-r{updated['revision']}-{state.value}")
