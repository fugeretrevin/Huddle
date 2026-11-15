"""Live orchestrator: genuine multi-step execution behind the real approval gate.

Same public surface and event shapes as the deterministic demo producer, so the
backend/API and Unity client are unchanged. The difference is truth: the developer
writes real files and the tester runs real pytest in an isolated per-session
workspace. Executed artifacts are stamped source_mode="live" and
work_provenance="sandbox_execution"; the research step is a fixed template and says
so (provenance="deterministic", research_provenance="template").
"""
from __future__ import annotations

import asyncio
import uuid

from .events import EventBus, utcnow_iso
from .models import AgentId, AgentState, DecisionAction, EventType, TaskState
from .decision_wait import wait_for_decision
from .run_recovery import fail_run
from .settings import Settings
from .storage import Storage
from .workers import (
    CHANGES_LIMIT_MESSAGE,
    CHANGES_REQUESTED_MESSAGE,
    RESEARCH_PROGRESS_MESSAGE,
    WORK_PROVENANCE_SANDBOX,
    ArtifactData,
    DeveloperWorker,
    ResearchWorker,
    TesterWorker,
    proposal_revision_limit,
    retest_reason,
    revising_progress_message,
)
from .workspace_guard import WorkspaceGuard


class LiveOrchestrator:
    def __init__(self, storage: Storage, bus: EventBus, settings: Settings,
                 phase2_store=None, phase2_bus=None, board=None) -> None:
        # Optional ``BoardRunBridge``: keeps the task card that launched a run in step
        # with it on the v2 stream.
        self._board = board
        self._storage = storage
        self._bus = bus
        self._settings = settings
        self._guard = WorkspaceGuard(settings.workspace_root)
        self._research = ResearchWorker()
        self._dev = DeveloperWorker()
        self._tester = TesterWorker()
        self._waiters: dict[str, asyncio.Future[str]] = {}
        self._tasks: set[asyncio.Task] = set()
        self._workspaces: dict[str, object] = {}
        # Optional v2 artifact sink. When present, genuinely executed diff/test
        # output is also captured as sanitized v2 artifacts stamped source_mode=live.
        self._p2_artifacts = None
        if phase2_store is not None and phase2_bus is not None:
            from .phase2.artifacts import ArtifactService
            self._p2_artifacts = ArtifactService(
                phase2_store, phase2_bus, "live", settings.workspace_root)

    async def _emit_live_artifacts(self, session_id: str, task_id: str, ws,
                                   files_changed, test_output: str,
                                   duration: float | None = None) -> None:
        """Capture the REAL changed files as a unified diff + the REAL pytest output
        as sanitized v2 artifacts (source_mode=live). Behind the approval gate: this
        only runs in the approve branch after real execution. Never fatal."""
        if self._p2_artifacts is None:
            return
        try:
            from .phase2.diffing import compute_unified_diff
            parts = []
            for rel in files_changed:
                try:
                    new_text = self._guard.read_file(ws, rel)
                except Exception:  # noqa: BLE001
                    continue
                # Workers write brand-new files, so the "before" is empty.
                parts.append(compute_unified_diff("", new_text, rel))
            diff_text = "\n".join(p for p in parts if p)
            await self._p2_artifacts.ingest_code_diff(
                session_id, task_id, diff_text, source_mode="live",
                work_provenance=WORK_PROVENANCE_SANDBOX)
            await self._p2_artifacts.ingest_test_report(
                session_id, task_id, test_output, duration=duration, source_mode="live",
                work_provenance=WORK_PROVENANCE_SANDBOX)
        except Exception:  # noqa: BLE001 - artifact capture must never break the run
            pass

    def notify_resolved(self, decision_id: str, action: str) -> None:
        fut = self._waiters.get(decision_id)
        if fut is not None and not fut.done():
            fut.set_result(action)

    def start_auth_demo(self, session_id: str, parent_task_id: str,
                        board_task_id: str | None = None) -> None:
        task = asyncio.create_task(
            self._run_guarded(session_id, parent_task_id, board_task_id))
        self._tasks.add(task)
        task.add_done_callback(self._tasks.discard)

    async def _run_guarded(self, session_id: str, parent_task_id: str,
                           board_task_id: str | None = None) -> None:
        """Run the pipeline; if it raises, REPORT it instead of dying silently.

        Previously an exception in a worker (missing template, a failed file write,
        a subprocess error) just ended the background task: no event was emitted, so
        the client showed agents "working" forever with nothing to explain it."""
        try:
            await self._run(session_id, parent_task_id, board_task_id)
        except asyncio.CancelledError:
            raise
        except Exception as exc:  # noqa: BLE001 - reported to the client below
            await self._report_run_failure(session_id, parent_task_id, exc)

    async def _report_run_failure(self, session_id: str, parent_task_id: str,
                                  exc: Exception) -> None:
        from .phase2.sanitize import sanitize_text
        reason = sanitize_text(f"{type(exc).__name__}: {exc}",
                               self._settings.workspace_root)[:300]
        try:
            await fail_run(self._storage, self._bus, session_id, parent_task_id,
                           code="RUN_FAILED", reason=reason,
                           agent_message="Run stopped: internal error", mode="live",
                           board=self._board)
        except Exception:  # noqa: BLE001 - reporting must never raise out of the task
            pass

    async def shutdown(self) -> None:
        for t in list(self._tasks):
            t.cancel()
        for t in list(self._tasks):
            try:
                await t
            except (asyncio.CancelledError, Exception):  # noqa: BLE001
                pass
        for ws in list(self._workspaces.values()):
            self._guard.destroy_workspace(ws)

    async def _step(self) -> None:
        if self._settings.producer_step_seconds > 0:
            await asyncio.sleep(self._settings.producer_step_seconds)

    async def _set_agent(self, session_id, agent: AgentId, state: AgentState,
                         task_state: TaskState, progress: str, mode: str,
                         executed: bool = False) -> None:
        """``executed=True`` marks a status that reports REAL sandbox work (developer
        writing files, tester running pytest) with ``work_provenance``."""
        self._storage.set_agent_state(session_id, agent.value, state.value, utcnow_iso())
        payload = {"state": state.value, "task_state": task_state.value,
                   "progress_message": progress}
        if executed:
            payload["work_provenance"] = WORK_PROVENANCE_SANDBOX
        payload["source_mode"] = mode
        await self._bus.publish(session_id, EventType.agent_status_changed, payload,
                                agent_id=agent.value)

    async def _run(self, session_id: str, parent_task_id: str,
                   board_task_id: str | None = None) -> None:
        # Additive linkage to the task card that launched this run (absent for the
        # card-less ``POST /v1/.../tasks`` path, whose payloads are unchanged).
        link = {"board_task_id": board_task_id} if board_task_id else {}
        board = self._board if board_task_id else None
        now = utcnow_iso()
        ws = self._guard.create_workspace(session_id)
        self._workspaces[session_id] = ws

        research_task = f"{parent_task_id}-research"
        oauth_task = f"{parent_task_id}-oauth"
        test_task = f"{parent_task_id}-test"

        for tid, agent in ((research_task, AgentId.researcher), (test_task, AgentId.tester)):
            self._storage.upsert_task(tid, session_id, agent.value, parent_task_id,
                                      TaskState.running.value, "auth_demo", [], now, now)
            await self._bus.publish(session_id, EventType.task_started,
                                    {"task_id": tid, "agent_id": agent.value, **link,
                                     "source_mode": "live"},
                                    task_id=tid, agent_id=agent.value)
        self._storage.upsert_task(oauth_task, session_id, AgentId.developer.value, parent_task_id,
                                  TaskState.blocked.value, "auth_demo", [research_task], now, now)
        await self._bus.publish(session_id, EventType.task_created,
                                {"task_id": oauth_task, "task_state": "blocked",
                                 "agent_id": "developer", "depends_on": research_task,
                                 **link, "source_mode": "live"},
                                task_id=oauth_task, agent_id="developer")

        await self._set_agent(session_id, AgentId.researcher, AgentState.researching,
                              TaskState.running, RESEARCH_PROGRESS_MESSAGE, "live")
        await self._set_agent(session_id, AgentId.tester, AgentState.testing,
                              TaskState.running, "Running baseline tests", "live", executed=True)

        # Tester does REAL independent work up front.
        baseline = await asyncio.to_thread(self._tester.run, self._guard, ws,
                                           self._settings.task_timeout_seconds)
        # Same honesty rule as the post-approval run: only a clean pytest exit is a pass.
        baseline_ok = (baseline.returncode == 0 and baseline.failed == 0
                       and not baseline.timed_out)
        baseline_state = TaskState.completed if baseline_ok else TaskState.failed
        self._storage.set_task_status(test_task, baseline_state.value, utcnow_iso())
        await self._set_agent(session_id, AgentId.tester,
                              AgentState.complete if baseline_ok else AgentState.error,
                              baseline_state,
                              f"Baseline tests: {baseline.passed} passed / {baseline.failed} failed",
                              "live", executed=True)
        await self._bus.publish(session_id,
                                EventType.task_completed if baseline_ok else EventType.task_failed,
                                {"task_id": test_task, "source_mode": "live",
                                 "task_state": baseline_state.value,
                                 "work_provenance": WORK_PROVENANCE_SANDBOX, **link,
                                 "result_summary": f"Baseline {baseline.passed} passed, {baseline.failed} failed"},
                                task_id=test_task, agent_id="tester")

        # Researcher raises the gated decision. The recommendation is a fixed TEMPLATE
        # (no model call, no web research): disclosed in machine-readable fields.
        # "Request changes" revises the template recommendation up to ``max_revisions``
        # times without executing any developer work in between.
        max_revisions = proposal_revision_limit(self._settings.max_proposal_revisions)
        revision = 0
        previous_id: str | None = None
        loop = asyncio.get_running_loop()
        while True:
            proposal = self._research.evaluate(f"dec-{uuid.uuid4().hex[:10]}", revision=revision)
            details = {
                "title": proposal.title, "summary": proposal.summary,
                "recommendation": proposal.recommendation, "rationale": proposal.rationale,
                "risk_level": proposal.risk_level, "priority": "blocking",
                "allowed_actions": ["approve", "reject", "request_changes"],
                "alternatives": proposal.alternatives, "provenance": proposal.provenance,
                "research_provenance": proposal.research_provenance,
                "research_provenance_label": proposal.research_provenance_label,
                "revision": revision,
                "decision_revision": revision,
                "revises_decision_id": previous_id,
                "impact": f"Developer task {oauth_task} is blocked on this decision",
                **link,
                "source_mode": "live",
            }
            self._storage.create_decision(proposal.decision_id, session_id, research_task,
                                          AgentId.researcher.value, proposal.summary,
                                          proposal.recommendation, details)
            fut: asyncio.Future[str] = loop.create_future()
            self._waiters[proposal.decision_id] = fut
            self._storage.set_task_status(research_task, TaskState.awaiting_approval.value, utcnow_iso())
            await self._set_agent(session_id, AgentId.researcher, AgentState.waiting,
                                  TaskState.awaiting_approval, "Needs a decision", "live")
            await self._bus.publish(session_id, EventType.decision_requested, details,
                                    task_id=research_task, agent_id=AgentId.researcher.value,
                                    decision_id=proposal.decision_id)
            if board is not None:
                await board.decision_requested(parent_task_id, proposal.decision_id, {
                    "summary": proposal.summary,
                    "research_provenance": proposal.research_provenance,
                    "research_provenance_label": proposal.research_provenance_label,
                    "decision_revision": revision,
                    "revises_decision_id": previous_id})

            action = await wait_for_decision(self._storage, fut, proposal.decision_id,
                                             self._settings.decision_timeout_seconds)
            self._waiters.pop(proposal.decision_id, None)
            if action is None:
                await fail_run(self._storage, self._bus, session_id, parent_task_id,
                               code="DECISION_TIMEOUT",
                               reason="no decision was made in time",
                               agent_message="Run stopped: decision timed out", mode="live",
                               board=self._board)
                return
            if action !=DecisionAction.request_changes.value or revision >= max_revisions:
                break
            revision += 1
            previous_id = proposal.decision_id
            if board is not None:
                await board.changes_requested(parent_task_id, proposal.decision_id, revision)
            self._storage.set_task_status(research_task, TaskState.running.value, utcnow_iso())
            await self._set_agent(session_id, AgentId.researcher, AgentState.researching,
                                  TaskState.running, revising_progress_message(revision), "live")
            await self._step()

        if board is not None and action == DecisionAction.approve.value:
            await board.decision_approved(parent_task_id, proposal.decision_id)
        self._storage.set_task_status(research_task, TaskState.completed.value, utcnow_iso())
        await self._set_agent(session_id, AgentId.researcher, AgentState.complete,
                              TaskState.completed, "Decision delivered", "live")

        parent_final = TaskState.completed
        card_final, card_summary = TaskState.completed, ""
        if action == DecisionAction.approve.value:
            self._storage.set_task_status(oauth_task, TaskState.running.value, utcnow_iso())
            max_retries = max(0, int(self._settings.max_dev_revision_attempts))
            attempts = 0
            first_attempt: dict | None = None
            while True:
                attempts += 1
                prog = ("Implementing OAuth scaffold" if attempts == 1
                        else f"Revising OAuth scaffold (attempt {attempts})")
                await self._set_agent(session_id, AgentId.developer, AgentState.coding,
                                      TaskState.running, prog, "live", executed=True)
                dev = await asyncio.to_thread(self._dev.implement_oauth, self._guard, ws)
                await self._set_agent(session_id, AgentId.tester, AgentState.testing,
                                      TaskState.running,
                                      "Running tests on new code" if attempts == 1 else f"Retesting code (attempt {attempts})",
                                      "live", executed=True)
                result = await asyncio.to_thread(self._tester.run, self._guard, ws,
                                                 self._settings.task_timeout_seconds)
                # pytest's exit code is the authority: 0 means every collected test passed.
                passed_ok = (result.returncode == 0 and result.failed == 0
                             and not result.timed_out)
                if passed_ok or attempts > max_retries:
                    break
                if first_attempt is None:
                    first_attempt = {
                        "tests_passed": result.passed,
                        "tests_failed": result.failed,
                        "timed_out": result.timed_out,
                        "returncode": result.returncode,
                    }
                reason = retest_reason(result)
                await self._set_agent(session_id, AgentId.tester, AgentState.testing,
                                      TaskState.running, f"Tests failed ({reason}); requesting fix",
                                      "live", executed=True)

            counts = f"{result.passed} passed / {result.failed} failed"
            # The tester's own run is over: without this it stayed in "testing" forever.
            await self._set_agent(session_id, AgentId.tester,
                                  AgentState.complete if passed_ok else AgentState.error,
                                  TaskState.completed if passed_ok else TaskState.failed,
                                  f"Tests on new code: {counts}"
                                  + (" (timed out)" if result.timed_out else ""), "live",
                                  executed=True)
            art = ArtifactData(
                summary=(f"OAuth login scaffold complete; {counts}" if passed_ok
                         else f"OAuth login scaffold written but tests did not pass; {counts}"),
                files_changed=dev.files_changed, diff_excerpt=dev.diff_excerpt,
                tests_passed=result.passed, tests_failed=result.failed, timed_out=result.timed_out,
                attempts=attempts, first_attempt=first_attempt,
            )
            artifact_id = f"art-{uuid.uuid4().hex[:10]}"
            self._storage.create_artifact(artifact_id, session_id, oauth_task, "code_change",
                                          art.summary, art.to_payload(), "live", utcnow_iso())
            await self._bus.publish(session_id, EventType.artifact_created,
                                    {"artifact_id": artifact_id, "artifact_type": "code_change",
                                     **art.to_payload(), **link},
                                    task_id=oauth_task, agent_id="developer")
            # Also capture the REAL diff + REAL pytest output as sanitized v2 artifacts
            # (source_mode=live) — only here, after genuine execution behind the gate.
            # They reference the task CARD when the run was launched from one, so the
            # client can attach them to it (a v1 task id has no card).
            await self._emit_live_artifacts(session_id, board_task_id or oauth_task, ws,
                                            dev.files_changed, result.output)
            final = TaskState.completed if passed_ok else TaskState.failed
            parent_final = final
            card_final, card_summary = final, art.summary
            self._storage.set_task_status(oauth_task, final.value, utcnow_iso())
            await self._set_agent(session_id, AgentId.developer,
                                  AgentState.complete if final == TaskState.completed else AgentState.error,
                                  final, art.summary, "live", executed=True)
            # A failed run is reported as task.failed, never task.completed: the client
            # counts task.completed as finished work (shelf token, success cues).
            await self._bus.publish(session_id,
                                    EventType.task_completed if final == TaskState.completed
                                    else EventType.task_failed,
                                    {"task_id": oauth_task, "result_summary": art.summary,
                                     "task_state": final.value,
                                     "artifact_id": artifact_id,
                                     "work_provenance": WORK_PROVENANCE_SANDBOX,
                                     **link, "source_mode": "live"},
                                    task_id=oauth_task, agent_id="developer")
        else:
            self._storage.set_task_status(oauth_task, TaskState.cancelled.value, utcnow_iso())
            rejected = action == DecisionAction.reject.value
            msg = ("Rejected: no OAuth files written" if rejected
                   else CHANGES_LIMIT_MESSAGE if revision > 0 else CHANGES_REQUESTED_MESSAGE)
            card_final, card_summary = TaskState.cancelled, msg
            parent_final = TaskState.cancelled  # not "completed": the gated work was not done
            await self._set_agent(session_id, AgentId.developer, AgentState.waiting,
                                  TaskState.cancelled, msg, "live")
            # No "task.cancelled" event type exists in the frozen contract, so the end of
            # this branch is still announced as task.completed — but the payload carries
            # the task's REAL terminal state (``task_state: "cancelled"``, the same value
            # the snapshot reports) and no artifact_id, so a consumer never has to infer
            # "completed" from the event type alone.
            await self._bus.publish(session_id, EventType.task_completed,
                                    {"task_id": oauth_task, "result_summary": msg,
                                     "task_state": TaskState.cancelled.value,
                                     **link, "source_mode": "live"},
                                    task_id=oauth_task, agent_id="developer")

        # Card first, run parent last: the session only accepts a new run once the card
        # already shows this one's outcome.
        if board is not None:
            await board.run_finished(parent_task_id, card_final.value, card_summary)
        self._storage.set_task_status(parent_task_id, parent_final.value, utcnow_iso())
