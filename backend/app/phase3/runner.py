"""Plan runner: works through the board cards a COMMITTED Manager plan created.

Scope and trust model:
- It only touches cards listed in a committed plan's ``committed_tasks`` (the user approved
  each one, and dangerous ones were explicitly authorized). Pipeline (``auth_demo``) cards,
  hand-made board cards and cards of open/rejected plans are never touched.
- A card is picked up only when it is ``queued`` (dependencies completed). One plan card runs
  at a time per session. ``pipeline_busy`` can hold the runner (not wired by default: a
  pipeline decision can wait for the human indefinitely and must not block plan work).
- Research cards are TEXT ONLY: the configured model writes the findings with no tools, and
  the artifact says so. Developer and Tester cards are text only too unless
  ``p3_execute_code`` is on (scripts/start_wall_backend.ps1 turns it on for LIVE runs): then
  they do REAL work in the plan's sandbox folder (``sandbox_work.SandboxWork``): files are
  written, pytest really runs, and the card completes only on a clean run (one bounded fix
  attempt). No repository outside that folder is touched. In demo mode the deliverable is
  scripted and labelled ``[SCRIPTED DEMO]``. In live mode with no model, cards are left
  queued and one warning is published.
- If the user pauses or cancels a card while it is being worked, the result is discarded.
- A model failure, refusal or timeout fails the card (dependents stay blocked).
- After a restart, a plan card left ``running`` is failed (nothing is driving it).

Ad-hoc cards (rc21 owner: "the developer only works when the manager is doing things"):
- A card the USER created and assigned to the researcher, developer or tester (the board's
  create / assign commands) is worked the same way, as a one-step plan, when the backend is
  live with a model. The user's own create + assign is the authorization; ``request_adhoc``
  records it (``adhoc_tasks_v3``) at that moment. Nothing is recorded in demo mode, without a
  model, for pipeline (``auth_demo``) cards, plan cards, terminal cards or unassigned cards, and
  an old card is never picked up just because it is queued.
- Same machinery, timeouts and one-card-at-a-time per session as plan cards. Plan cards and
  ad-hoc cards form ONE backlog per session: the highest priority ready card goes next (the
  board's 0 Low / 1 Normal / 2 High / 3 Urgent, so a higher number first), ties by creation
  order; a blocked card is never taken early. Developer / Tester work goes into
  ``<workspace_root>/tasks/<folder>``: the card's own folder, or the folder of the ad-hoc card it depends on, or (a Tester card with no
  dependency) the folder of this session's latest ad-hoc work, so "test it" tests that code.
"""
from __future__ import annotations

import asyncio
import json
import logging
import time
from typing import Any, Callable

from ..phase2.artifacts import ArtifactService
from ..phase2.board_bridge import BoardRunBridge
from ..phase2.events import Phase2EventBus, utcnow_iso
from ..phase2.handoffs import HandoffService
from ..phase2.models import TERMINAL_STATES, ArtifactKind, EventTypeV2, HandoffKind, TaskStateV2
from ..phase2.sanitize import sanitize_text
from ..phase2.store import Phase2Store
from .plan_ops import PlanOps
from .provider import ModelProvider, ProviderRefused
from .provider_meta import ProviderError
from .sandbox_work import SandboxWork
from .store import Phase3Store

log = logging.getLogger("musedesk.phase3.runner")

MAX_DEP_CHARS = 3000
MAX_OUTPUT_CHARS = 10000
# Whole files are rewritten in one answer and the model's reasoning shares the budget: 8000 ran out live
# (finish_reason=length, no text) on a fix attempt.
WORK_MAX_TOKENS = 16000

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

WORKER_AGENTS = ("researcher", "developer", "tester")
ADHOC_CRITERIA = {
    "researcher": "Give the findings, the options with their trade-offs, and a recommendation.",
    "developer": "Include focused pytest tests that check the behaviour the task asks for.",
    "tester": "Write pytest tests with concrete inputs and expected results, including edge cases.",
}
ADHOC_TESTER_EMPTY = (
    "\n\nThe folder has no code yet. Put a small reference implementation of the behaviour the "
    "task describes inside your test file, test it, and say so in the Summary line.")


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
        self._ops_cache: PlanOps | None = None
        self._sandbox_cache: SandboxWork | None = None
        self._adhoc_sandbox_cache: SandboxWork | None = None

    @property
    def ops(self) -> PlanOps:
        if self._ops_cache is None:
            self._ops_cache = PlanOps(self._s2, self._s3, self._bus, self._mode, self._provider,
                                      timeout=float(self._settings.p3_work_timeout_seconds))
        return self._ops_cache

    async def _after_card_ended(self, task_id: str) -> None:
        """A plan card reached a terminal state: report who is blocked, then summarise the
        plan if everything has finished. Never raises into the work loop."""
        try:
            row = self._s2.get_task_v2(task_id)
            for plan in self._s3.committed_plans():
                if task_id not in {e["task_id"] for e in plan.get("committed_tasks", [])}:
                    continue
                if row and row["task_state"] in ("failed", "cancelled"):
                    await self.ops.announce_blocked_dependents(plan, row)
                await self.ops.synthesize_if_settled(plan["plan_id"])
        except Exception:  # noqa: BLE001
            log.exception("post-card bookkeeping failed for %s", task_id)

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
                    await self._after_card_ended(row["id"])
                    n += 1
        for sid in self._s3.adhoc_sessions():
            for entry in self._s3.adhoc_entries(sid):
                row = self._s2.get_task_v2(entry["task_id"])
                if row and row["task_state"] == TaskStateV2.running.value and not row.get("run_task_id"):
                    await self._finish(row, TaskStateV2.failed, EventTypeV2.task_failed,
                                       "Stopped: the backend restarted while this task was "
                                       "being worked. Add it again as a new task to retry.",
                                       {"code": "RUN_INTERRUPTED", "adhoc": True}, tag="task")
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
    @staticmethod
    def _order_key(row: dict, seq: int) -> tuple:
        """Backlog order: highest priority first (the board's 0 Low, 1 Normal, 2 High,
        3 Urgent: a HIGHER number is more urgent), ties by creation order."""
        return (-int(row.get("priority") or 0), row.get("created_at") or "", seq)

    def _plan_candidates(self, plans: list[dict[str, Any]]) -> list[tuple[tuple, tuple]]:
        """Ready plan cards (queued, dependencies completed) as (order key, work args)."""
        out = []
        seq = 0
        for plan in plans:
            items = {it["client_item_id"]: it for it in plan.get("items", [])}
            for entry in plan.get("committed_tasks", []):
                seq += 1
                row = self._s2.get_task_v2(entry["task_id"])
                if row is None or row.get("run_task_id") or row.get("task_template"):
                    continue
                if row.get("removed") or row["task_state"] != TaskStateV2.queued.value:
                    continue
                if not self._board.dependencies_ready(row):
                    continue
                out.append((self._order_key(row, seq),
                            (plan, entry, items.get(entry["client_item_id"], {}))))
        return out

    def _next_card(self, plans: list[dict[str, Any]]) -> tuple[dict, dict, dict] | None:
        cands = self._plan_candidates(plans)
        return min(cands, key=lambda c: c[0])[1] if cands else None

    async def tick(self) -> int:
        """Work at most one card per session: the highest priority ready card among plan
        cards and the user's ad-hoc cards. Returns the number of cards worked."""
        by_session: dict[str, list[dict[str, Any]]] = {}
        for plan in self._s3.committed_plans():
            by_session.setdefault(plan["session_id"], []).append(plan)
        worked = 0
        for plans in by_session.values():       # sweep: user-cancelled cards also settle a plan
            for p in plans:
                if not p.get("synthesis"):
                    await self._after_sweep(p)
        demo = self._mode == "demo"
        sessions = list(by_session)
        if not demo:                            # demo mode never works an ad-hoc card
            sessions += [s for s in self._s3.adhoc_sessions() if s not in by_session]
        for sid in sessions:
            plans = by_session.get(sid, [])
            if self._session_busy(sid, plans):
                continue
            if self._pipeline_busy(sid):
                continue
            # One backlog per session: plan cards and the user's own cards together, highest
            # priority first, ties by creation order. Blocked cards are never candidates.
            cands = [(k, "plan", w) for k, w in self._plan_candidates(plans)]
            if not demo:
                cands += [(k, "adhoc", r) for k, r in self._adhoc_candidates(sid)]
            if not cands:
                continue
            _, kind, work = min(cands, key=lambda c: c[0])
            nxt = work if kind == "plan" else None
            adhoc = work if kind == "adhoc" else None
            provider = None if demo else self._provider()
            if not demo and provider is None:
                warn_key = sid if nxt is not None else f"{sid}#adhoc"
                if warn_key not in self._warned:
                    self._warned.add(warn_key)
                    what = "Manager plan cards" if nxt is not None else "Assigned board tasks"
                    await self._bus.publish(sid, EventTypeV2.system_warning, {
                        "code": "NO_MODEL_CONFIGURED",
                        "message": f"{what} are waiting: no model is configured, "
                                   "so nothing can work them.", "source_mode": self._mode})
                continue
            if nxt is not None:
                await self._work(sid, *nxt, provider)
            else:
                await self._work_adhoc(sid, adhoc, provider)
            worked += 1
        return worked

    def _session_busy(self, sid: str, plans: list[dict[str, Any]]) -> bool:
        """A plan card or an ad-hoc card of this session is already running."""
        ids = [e["task_id"] for p in plans for e in p.get("committed_tasks", [])]
        ids += [e["task_id"] for e in self._s3.adhoc_entries(sid)]
        for task_id in ids:
            row = self._s2.get_task_v2(task_id)
            if row and row["task_state"] == TaskStateV2.running.value:
                return True
        return False

    # ─────────────────────── ad-hoc cards (the user's own board tasks) ───────────────────────
    def _plan_task_ids(self, session_id: str) -> set[str]:
        return {e["task_id"] for p in self._s3.plans_for_session(session_id)
                for e in p.get("committed_tasks") or []}

    def _is_adhoc_candidate(self, row: dict | None, session_id: str) -> bool:
        """A card nothing else works: a user's card, assigned to a worker, not terminal."""
        if row is None or row["session_id"] != session_id or row.get("removed"):
            return False
        if row.get("task_template") or row.get("run_task_id"):
            return False                        # the auth_demo pipeline drives it
        if row.get("agent_id") not in WORKER_AGENTS:
            return False                        # unassigned, or the manager
        if TaskStateV2(row["task_state"]) in TERMINAL_STATES:
            return False
        return row["id"] not in self._plan_task_ids(session_id)   # the plan path works it

    def request_adhoc(self, session_id: str, task_id: str) -> bool:
        """The user created or assigned this card to a worker: record that it is to be worked.
        Only when the backend is live with a model (else nothing changes: no model call, no
        new state). Returns True when the card was newly recorded. Never raises."""
        try:
            if self._mode == "demo" or self._provider() is None:
                return False
            row = self._s2.get_task_v2(task_id)
            if not self._is_adhoc_candidate(row, session_id):
                return False
            if row["task_state"] == TaskStateV2.running.value:
                return False
            return self._s3.mark_adhoc(task_id, session_id, time.time())
        except Exception:  # noqa: BLE001 - recording must never fail the user's command
            log.exception("could not record ad-hoc work for %s", task_id)
            return False

    def _adhoc_candidates(self, sid: str) -> list[tuple[tuple, dict]]:
        """Ready ad-hoc cards of this session as (order key, row)."""
        delay = float(getattr(self._settings, "p3_adhoc_start_delay_seconds", 0.0) or 0.0)
        now = time.time()
        out = []
        for seq, entry in enumerate(self._s3.adhoc_entries(sid)):
            row = self._s2.get_task_v2(entry["task_id"])
            if not self._is_adhoc_candidate(row, sid):
                continue
            if row["task_state"] != TaskStateV2.queued.value:
                continue                        # blocked / paused / awaiting: not yet
            if now - float(entry["requested_at"]) < delay:
                continue
            if not self._board.dependencies_ready(row):
                continue
            out.append((self._order_key(row, seq), row))
        return out

    def _next_adhoc(self, sid: str) -> dict | None:
        cands = self._adhoc_candidates(sid)
        return min(cands, key=lambda c: c[0])[1] if cands else None

    def _adhoc_sandbox(self) -> SandboxWork:
        if self._adhoc_sandbox_cache is None:
            self._adhoc_sandbox_cache = SandboxWork(
                self._settings.workspace_root,
                test_timeout=int(getattr(self._settings, "p3_sandbox_test_timeout_seconds", 60)),
                fix_attempts=int(getattr(self._settings, "p3_sandbox_fix_attempts", 1)),
                subdir="tasks")
        return self._adhoc_sandbox_cache

    def _adhoc_folder(self, sid: str, row: dict) -> str:
        """Folder name under <workspace_root>/tasks for this card's real work."""
        own = self._s3.adhoc_entry(row["id"])
        if own and own.get("folder"):
            return own["folder"]                # resumed card: same folder as before
        for dep_id in json.loads(row["dependency_json"]):
            dep = self._s3.adhoc_entry(dep_id)
            if dep and dep.get("folder"):
                return dep["folder"]            # builds on the card it depends on
        if row.get("agent_id") == "tester":
            for e in reversed(self._s3.adhoc_entries(sid)):
                if e.get("folder") and e["task_id"] != row["id"]:
                    return e["folder"]          # "test it": this session's latest ad-hoc work
        return row["id"]

    def _adhoc_prompt(self, row: dict, agent: str) -> str:
        return (f"Task from the user (they added it to the board and assigned it to the {agent}): "
                f"{row['title']}\n"
                "This is one task on its own, not a step of a larger plan.\n\n"
                "Acceptance criteria:\n- Do what the task asks, completely and correctly.\n"
                f"- {ADHOC_CRITERIA.get(agent, ADHOC_CRITERIA['developer'])}\n\n"
                f"Inputs from earlier steps:\n{self._dependency_inputs(row)}")

    async def _work_adhoc(self, sid: str, row: dict, provider: ModelProvider | None) -> None:
        agent = row["agent_id"]
        prompt = self._adhoc_prompt(row, agent)
        ids: dict[str, Any] = {"adhoc": True}
        if self._executes(agent, provider):
            key = self._adhoc_folder(sid, row)
            self._s3.set_adhoc_folder(row["id"], key)
            sandbox = self._adhoc_sandbox()
            folder = sandbox.folder(key)
            if agent == "tester" and not any(p.suffix == ".py" for p in folder.iterdir()):
                prompt += ADHOC_TESTER_EMPTY
            ids["workspace"] = f"tasks/{key}"
            row = await self._start(sid, row, agent, "sandbox", ids, tag="task")
            await self._sandbox_card(sid, row, agent, provider, sandbox, key, prompt, ids,
                                     plan=None, tag="task")
            return
        work_mode = "model_text" if provider else "scripted"
        row = await self._start(sid, row, agent, work_mode, ids, tag="task")
        item = {"title": row["title"], "acceptance_criteria": [
            "Do what the task asks, completely and correctly.", ADHOC_CRITERIA.get(agent, "")]}
        await self._text_card(sid, row, agent, provider, prompt, ids, work_mode,
                              scripted=lambda: _scripted(agent, item, row["title"]),
                              plan=None, tag="task")

    async def _after_sweep(self, plan: dict[str, Any]) -> None:
        try:
            await self.ops.synthesize_if_settled(plan["plan_id"])
        except Exception:  # noqa: BLE001
            log.exception("plan synthesis failed for %s", plan.get("plan_id"))

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

    def _executes(self, agent: str, provider: ModelProvider | None) -> bool:
        return (provider is not None and bool(getattr(self._settings, "p3_execute_code", False))
                and agent in ("developer", "tester"))

    def _sandbox(self) -> SandboxWork:
        if self._sandbox_cache is None:
            self._sandbox_cache = SandboxWork(
                self._settings.workspace_root,
                test_timeout=int(getattr(self._settings, "p3_sandbox_test_timeout_seconds", 60)),
                fix_attempts=int(getattr(self._settings, "p3_sandbox_fix_attempts", 1)))
        return self._sandbox_cache

    def _step_prompt(self, plan: dict, item: dict, row: dict) -> str:
        return (f"Objective: {plan.get('objective', '')}\nPlan: {plan.get('title', '')}\n\n"
                f"Your step: {item.get('title', row['title'])}\nKind: {item.get('kind', '')}\n"
                "Acceptance criteria:\n" + "\n".join(
                    f"- {c}" for c in item.get("acceptance_criteria") or []) +
                f"\n\nInputs from earlier steps:\n{self._dependency_inputs(row)}")

    async def _start(self, sid: str, row: dict, agent: str, work_mode: str,
                     ids: dict[str, Any], tag: str = "plan") -> dict:
        """queued -> running, announced with task.started (``ids``: plan_id or adhoc)."""
        row = self._s2.update_task_v2(row["id"], task_state=TaskStateV2.running.value,
                                      now=utcnow_iso())
        await self._bus.publish(sid, EventTypeV2.task_started, {
            "revision": row["revision"], "task_state": row["task_state"],
            **ids, "work_mode": work_mode, "source_mode": self._mode},
            task_id=row["id"], agent_id=agent,
            event_id=f"evt2-{tag}-{row['id']}-r{row['revision']}-started")
        return row

    async def _work_sandbox(self, sid: str, plan: dict, item: dict, row: dict, agent: str,
                            provider: ModelProvider) -> None:
        await self._sandbox_card(sid, row, agent, provider, self._sandbox(), plan["plan_id"],
                                 self._step_prompt(plan, item, row),
                                 {"plan_id": plan["plan_id"]}, plan=plan, tag="plan")

    async def _sandbox_card(self, sid: str, row: dict, agent: str, provider: ModelProvider,
                            sandbox: SandboxWork, folder_key: str, prompt: str,
                            ids: dict[str, Any], *, plan: dict | None, tag: str) -> None:
        """Developer / Tester card, for real: files written into the card's restricted
        workspace folder, pytest really run. Card completes only on a clean run (or files
        with no tests yet). ``plan`` is None for an ad-hoc card."""
        noun = "step" if plan is not None else "task"
        budget = float(self._settings.p3_work_timeout_seconds)

        async def complete(system: str, prompt: str) -> str:
            text = await asyncio.wait_for(
                provider.complete(system=system, messages=[{"role": "user", "content": prompt}],
                                  max_tokens=WORK_MAX_TOKENS, purpose="work"), timeout=budget)
            # NOT sanitized: redaction would corrupt code ("token = ..." / password literals in tests
            # became ***REDACTED*** and broke the build). The files stay in the local sandbox; every
            # artifact page shown to the client is sanitized on ingest (ArtifactService).
            return text or ""

        try:
            out = await sandbox.run(agent, folder_key, prompt, complete)
        except asyncio.TimeoutError:
            await self._fail_if_running(row["id"], "MODEL_TIMEOUT",
                                        f"The model did not finish this {noun} in time.", tag=tag)
            return
        except (ProviderError, ProviderRefused):
            log.warning("sandbox work: the model gave no usable answer for %s", row["id"])
            await self._fail_if_running(row["id"], "MODEL_UNAVAILABLE",
                                        f"The model gave no usable answer for this {noun}. Retry it.",
                                        tag=tag)
            return
        except Exception:  # noqa: BLE001 - provider/sandbox details never leak to the client
            log.exception("sandbox work failed for %s", row["id"])
            await self._fail_if_running(row["id"], "SANDBOX_ERROR",
                                        f"The restricted workspace could not work this {noun}.",
                                        tag=tag)
            return

        current = self._s2.get_task_v2(row["id"])
        if current is None or current["task_state"] != TaskStateV2.running.value:
            log.info("card %s changed while worked (%s); result discarded", row["id"],
                     current and current["task_state"])
            return
        artifacts = ArtifactService(self._s2, self._bus, self._mode, self._settings.workspace_root)
        art = await artifacts.create_artifact(
            sid, row["id"], out.kind, out.summary, out.pages, source_mode=self._mode,
            work_provenance="sandbox_execution" if out.tests_ran else "sandbox_files")
        extra = {"artifact_id": art.artifact_id, **ids, "work_mode": "sandbox",
                 "files_changed": out.files, "tests_passed": out.passed,
                 "tests_failed": out.failed, "tests_ran": out.tests_ran, "attempts": out.attempts}
        if out.ok:
            if not await self._finish(current, TaskStateV2.completed, EventTypeV2.task_completed,
                                      out.summary, extra, tag=tag):
                return
            await self._board.release_dependents(sid, row["id"])
            if plan is not None:
                await self._handoffs(sid, plan, row["id"], agent, art.artifact_id)
        else:
            await self._finish(current, TaskStateV2.failed, EventTypeV2.task_failed,
                               out.fail_reason or out.summary, {**extra, "code": out.fail_code},
                               tag=tag)
        if plan is not None:
            await self._after_card_ended(row["id"])

    async def _work(self, sid: str, plan: dict, entry: dict, item: dict,
                    provider: ModelProvider | None) -> None:
        row = self._s2.get_task_v2(entry["task_id"])
        agent = row["agent_id"] or item.get("proposed_agent") or "developer"
        ids = {"plan_id": plan["plan_id"]}
        if self._executes(agent, provider):
            row = await self._start(sid, row, agent, "sandbox", ids)
            await self._work_sandbox(sid, plan, item, row, agent, provider)
            return
        work_mode = "model_text" if provider else "scripted"
        row = await self._start(sid, row, agent, work_mode, ids)
        await self._text_card(sid, row, agent, provider, self._step_prompt(plan, item, row), ids,
                              work_mode,
                              scripted=lambda: _scripted(agent, item, plan.get("objective", "")),
                              plan=plan, tag="plan")

    async def _text_card(self, sid: str, row: dict, agent: str, provider: ModelProvider | None,
                         prompt: str, ids: dict[str, Any], work_mode: str, *,
                         scripted: Callable[[], str], plan: dict | None, tag: str) -> None:
        """Model text (no tools), or the scripted demo text when there is no provider."""
        noun = "step" if plan is not None else "task"
        try:
            if provider is None:
                await asyncio.sleep(float(self._settings.p3_runner_demo_step_seconds))
                text = scripted()
            else:
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
                                        f"The model did not finish this {noun} in time.", tag=tag)
            return
        except Exception:  # noqa: BLE001 - provider details never leak to the client
            log.warning("runner model call failed for %s", row["id"])
            await self._fail_if_running(row["id"], "MODEL_UNAVAILABLE",
                                        f"The model could not work this {noun}.", tag=tag)
            return

        current = self._s2.get_task_v2(row["id"])
        if current is None or current["task_state"] != TaskStateV2.running.value:
            log.info("card %s changed while worked (%s); result discarded", row["id"],
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

        if not await self._finish(current, TaskStateV2.completed, EventTypeV2.task_completed,
                                  summary,
                                  {"artifact_id": art.artifact_id, **ids, "work_mode": work_mode},
                                  tag=tag):
            return
        await self._board.release_dependents(sid, row["id"])
        if plan is not None:
            await self._handoffs(sid, plan, row["id"], agent, art.artifact_id)
            await self._after_card_ended(row["id"])

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

    async def _fail_if_running(self, task_id: str, code: str, reason: str,
                               tag: str = "plan") -> None:
        row = self._s2.get_task_v2(task_id)
        if row and row["task_state"] == TaskStateV2.running.value:
            extra: dict[str, Any] = {"code": code}
            if tag != "plan":
                extra["adhoc"] = True
            await self._finish(row, TaskStateV2.failed, EventTypeV2.task_failed, reason,
                               extra, tag=tag)
            if tag == "plan":
                await self._after_card_ended(task_id)

    async def _finish(self, row: dict, state: TaskStateV2, type_: EventTypeV2, summary: str,
                      extra: dict[str, Any], tag: str = "plan") -> bool:
        """running -> ``state``. False (nothing written or sent) when the card is no longer
        running: removed or paused while its result was being recorded never lands."""
        cur = self._s2.get_task_v2(row["id"])
        if cur is None or cur.get("removed") or cur["task_state"] != TaskStateV2.running.value:
            log.info("card %s is no longer running (%s); not finished", row["id"],
                     cur and cur["task_state"])
            return False
        updated = self._s2.update_task_v2(row["id"], task_state=state.value, now=utcnow_iso())
        await self._bus.publish(row["session_id"], type_, {
            "revision": updated["revision"], "task_state": updated["task_state"],
            "result_summary": summary, **extra, "source_mode": self._mode},
            task_id=row["id"], agent_id=updated["agent_id"],
            event_id=f"evt2-{tag}-{row['id']}-r{updated['revision']}-{state.value}")
        return True
