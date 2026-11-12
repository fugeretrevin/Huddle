"""Live orchestrator: genuine multi-step execution behind the real approval gate.

Same public surface and event shapes as the deterministic demo producer, so the
backend/API and Unity client are unchanged. The difference is truth: the developer
writes real files and the tester runs real pytest in an isolated per-session
workspace. Executed artifacts are stamped source_mode="live"; the deterministic
research step discloses provenance="deterministic".
"""
from __future__ import annotations

import asyncio
import uuid

from .events import EventBus, utcnow_iso
from .models import AgentId, AgentState, DecisionAction, EventType, TaskState
from .settings import Settings
from .storage import Storage
from .workers import ArtifactData, DeveloperWorker, ResearchWorker, TesterWorker
from .workspace_guard import WorkspaceGuard


class LiveOrchestrator:
    def __init__(self, storage: Storage, bus: EventBus, settings: Settings,
                 phase2_store=None, phase2_bus=None) -> None:
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
            await self._p2_artifacts.ingest_code_diff(session_id, task_id, diff_text,
                                                       source_mode="live")
            await self._p2_artifacts.ingest_test_report(session_id, task_id, test_output,
                                                        duration=duration, source_mode="live")
        except Exception:  # noqa: BLE001 - artifact capture must never break the run
            pass

    def notify_resolved(self, decision_id: str, action: str) -> None:
        fut = self._waiters.get(decision_id)
        if fut is not None and not fut.done():
            fut.set_result(action)

    def start_auth_demo(self, session_id: str, parent_task_id: str) -> None:
        task = asyncio.create_task(self._run(session_id, parent_task_id))
        self._tasks.add(task)
        task.add_done_callback(self._tasks.discard)

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
                         task_state: TaskState, progress: str, mode: str) -> None:
        self._storage.set_agent_state(session_id, agent.value, state.value, utcnow_iso())
        await self._bus.publish(session_id, EventType.agent_status_changed,
                                {"state": state.value, "task_state": task_state.value,
                                 "progress_message": progress, "source_mode": mode},
                                agent_id=agent.value)

    async def _run(self, session_id: str, parent_task_id: str) -> None:
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
                                    {"task_id": tid, "agent_id": agent.value, "source_mode": "live"},
                                    task_id=tid, agent_id=agent.value)
        self._storage.upsert_task(oauth_task, session_id, AgentId.developer.value, parent_task_id,
                                  TaskState.blocked.value, "auth_demo", [research_task], now, now)
        await self._bus.publish(session_id, EventType.task_created,
                                {"task_id": oauth_task, "task_state": "blocked",
                                 "agent_id": "developer", "depends_on": research_task,
                                 "source_mode": "live"}, task_id=oauth_task, agent_id="developer")

        await self._set_agent(session_id, AgentId.researcher, AgentState.researching,
                              TaskState.running, "Evaluating authentication options", "live")
        await self._set_agent(session_id, AgentId.tester, AgentState.testing,
                              TaskState.running, "Running baseline tests", "live")

        # Tester does REAL independent work up front.
        baseline = await asyncio.to_thread(self._tester.run, self._guard, ws,
                                           self._settings.task_timeout_seconds)
        self._storage.set_task_status(test_task, TaskState.completed.value, utcnow_iso())
        await self._set_agent(session_id, AgentId.tester, AgentState.complete, TaskState.completed,
                              f"Baseline tests: {baseline.passed} passed / {baseline.failed} failed", "live")
        await self._bus.publish(session_id, EventType.task_completed,
                                {"task_id": test_task, "source_mode": "live",
                                 "result_summary": f"Baseline {baseline.passed} passed, {baseline.failed} failed"},
                                task_id=test_task, agent_id="tester")

        # Researcher raises the gated decision (deterministic, disclosed).
        proposal = self._research.evaluate(f"dec-{uuid.uuid4().hex[:10]}")
        details = {
            "title": proposal.title, "summary": proposal.summary,
            "recommendation": proposal.recommendation, "rationale": proposal.rationale,
            "risk_level": proposal.risk_level, "priority": "blocking",
            "allowed_actions": ["approve", "reject", "request_changes"],
            "alternatives": proposal.alternatives, "provenance": proposal.provenance,
            "impact": f"Developer task {oauth_task} is blocked on this decision",
            "source_mode": "live",
        }
        self._storage.create_decision(proposal.decision_id, session_id, research_task,
                                      AgentId.researcher.value, proposal.summary,
                                      proposal.recommendation, details)
        loop = asyncio.get_running_loop()
        fut: asyncio.Future[str] = loop.create_future()
        self._waiters[proposal.decision_id] = fut
        self._storage.set_task_status(research_task, TaskState.awaiting_approval.value, utcnow_iso())
        await self._set_agent(session_id, AgentId.researcher, AgentState.waiting,
                              TaskState.awaiting_approval, "Needs a decision", "live")
        await self._bus.publish(session_id, EventType.decision_requested, details,
                                task_id=research_task, agent_id=AgentId.researcher.value,
                                decision_id=proposal.decision_id)

        action = await fut
        self._waiters.pop(proposal.decision_id, None)
        self._storage.set_task_status(research_task, TaskState.completed.value, utcnow_iso())
        await self._set_agent(session_id, AgentId.researcher, AgentState.complete,
                              TaskState.completed, "Decision delivered", "live")

        if action == DecisionAction.approve.value:
            await self._set_agent(session_id, AgentId.developer, AgentState.coding,
                                  TaskState.running, "Implementing OAuth scaffold", "live")
            self._storage.set_task_status(oauth_task, TaskState.running.value, utcnow_iso())
            dev = await asyncio.to_thread(self._dev.implement_oauth, self._guard, ws)
            await self._set_agent(session_id, AgentId.tester, AgentState.testing,
                                  TaskState.running, "Running tests on new code", "live")
            result = await asyncio.to_thread(self._tester.run, self._guard, ws,
                                             self._settings.task_timeout_seconds)
            art = ArtifactData(
                summary=f"OAuth login scaffold complete; {result.passed} passed / {result.failed} failed",
                files_changed=dev.files_changed, diff_excerpt=dev.diff_excerpt,
                tests_passed=result.passed, tests_failed=result.failed, timed_out=result.timed_out,
            )
            artifact_id = f"art-{uuid.uuid4().hex[:10]}"
            self._storage.create_artifact(artifact_id, session_id, oauth_task, "code_change",
                                          art.summary, art.to_payload(), "live", utcnow_iso())
            await self._bus.publish(session_id, EventType.artifact_created,
                                    {"artifact_id": artifact_id, "artifact_type": "code_change",
                                     **art.to_payload()}, task_id=oauth_task, agent_id="developer")
            # Also capture the REAL diff + REAL pytest output as sanitized v2 artifacts
            # (source_mode=live) — only here, after genuine execution behind the gate.
            await self._emit_live_artifacts(session_id, oauth_task, ws, dev.files_changed,
                                            result.output)
            final = TaskState.completed if result.failed == 0 and not result.timed_out else TaskState.failed
            self._storage.set_task_status(oauth_task, final.value, utcnow_iso())
            await self._set_agent(session_id, AgentId.developer,
                                  AgentState.complete if final == TaskState.completed else AgentState.error,
                                  final, art.summary, "live")
            await self._bus.publish(session_id, EventType.task_completed,
                                    {"task_id": oauth_task, "result_summary": art.summary,
                                     "artifact_id": artifact_id, "source_mode": "live"},
                                    task_id=oauth_task, agent_id="developer")
        else:
            self._storage.set_task_status(oauth_task, TaskState.cancelled.value, utcnow_iso())
            msg = ("Rejected: no OAuth files written" if action == DecisionAction.reject.value
                   else "Changes requested: revised proposal pending")
            await self._set_agent(session_id, AgentId.developer, AgentState.waiting,
                                  TaskState.cancelled, msg, "live")
            await self._bus.publish(session_id, EventType.task_completed,
                                    {"task_id": oauth_task, "result_summary": msg, "source_mode": "live"},
                                    task_id=oauth_task, agent_id="developer")

        self._storage.set_task_status(parent_task_id, TaskState.completed.value, utcnow_iso())
