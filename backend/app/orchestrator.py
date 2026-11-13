"""Deterministic 'auth_demo' task producer (TASK-04 handoff).

This is a SCRIPTED producer: it emits the full P0 event timeline so the Unity
client can be built and tested end-to-end before the real sandboxed workers
(TASK-05) exist. Because it executes no real tools, every artifact/event it
emits is honestly stamped source_mode="demo" (configurable). The approval gate
is real: the dependent OAuth branch stays 'blocked' and only a server-validated
decision resolution resumes it.
"""
from __future__ import annotations

import asyncio
import uuid
from datetime import datetime, timezone

from .events import EventBus, utcnow_iso
from .models import (
    AgentId,
    AgentState,
    DecisionAction,
    EventType,
    TaskState,
)
from .run_recovery import fail_run
from .settings import Settings
from .storage import Storage
from .workers import (
    CHANGES_LIMIT_MESSAGE,
    CHANGES_REQUESTED_MESSAGE,
    RESEARCH_PROGRESS_MESSAGE,
    RESEARCH_PROVENANCE,
    RESEARCH_PROVENANCE_LABEL,
    proposal_revision_limit,
    revised_proposal_fields,
    revising_progress_message,
)


class Orchestrator:
    def __init__(self, storage: Storage, bus: EventBus, settings: Settings,
                 board=None) -> None:
        self._storage = storage
        self._bus = bus
        self._settings = settings
        # Optional ``BoardRunBridge``: when a run was launched from a task card, the
        # card is kept in step with the run on the v2 stream.
        self._board = board
        self._waiters: dict[str, asyncio.Future[str]] = {}
        self._tasks: set[asyncio.Task] = set()

    @property
    def _mode(self) -> str:
        return self._settings.producer_source_mode

    async def _step(self) -> None:
        if self._settings.producer_step_seconds > 0:
            await asyncio.sleep(self._settings.producer_step_seconds)

    def notify_resolved(self, decision_id: str, action: str) -> None:
        """Called by the respond endpoint after a real, atomic resolution."""
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
        """Same rule as the live producer: a timeline that raises is REPORTED (tasks
        failed, agents settled, pending decision expired) instead of dying silently and
        leaving agents 'working' forever."""
        try:
            await self._run_auth_demo(session_id, parent_task_id, board_task_id)
        except asyncio.CancelledError:
            raise
        except Exception as exc:  # noqa: BLE001 - reported to the client below
            try:
                await fail_run(self._storage, self._bus, session_id, parent_task_id,
                               code="RUN_FAILED", reason=type(exc).__name__,
                               agent_message="Run stopped: internal error", mode=self._mode,
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

    # ─────────────────────── the timeline ───────────────────────
    async def _run_auth_demo(self, session_id: str, parent_task_id: str,
                             board_task_id: str | None = None) -> None:
        now = utcnow_iso()
        mode = self._mode
        # Additive linkage to the task card that launched this run (absent for the
        # card-less ``POST /v1/.../tasks`` path, whose payloads are unchanged).
        link = {"board_task_id": board_task_id} if board_task_id else {}
        board = self._board if board_task_id else None

        research_task = f"{parent_task_id}-research"
        dev_task = f"{parent_task_id}-dev"
        test_task = f"{parent_task_id}-test"
        oauth_task = f"{parent_task_id}-oauth"  # dependent, gated by the decision

        # Three independent children start; the OAuth implementation is blocked.
        for tid, agent, dep in (
            (research_task, AgentId.researcher, []),
            (dev_task, AgentId.developer, []),
            (test_task, AgentId.tester, []),
        ):
            self._storage.upsert_task(tid, session_id, agent.value, parent_task_id,
                                      TaskState.running.value, "auth_demo", dep, now, now)
            await self._bus.publish(session_id, EventType.task_created,
                                    {"task_id": tid, "task_state": "running",
                                     "agent_id": agent.value, **link, "source_mode": mode},
                                    task_id=tid, agent_id=agent.value)
            await self._bus.publish(session_id, EventType.task_started,
                                    {"task_id": tid, "agent_id": agent.value,
                                     **link, "source_mode": mode},
                                    task_id=tid, agent_id=agent.value)

        self._storage.upsert_task(oauth_task, session_id, AgentId.developer.value,
                                  parent_task_id, TaskState.blocked.value, "auth_demo",
                                  [research_task], now, now)
        await self._bus.publish(session_id, EventType.task_created,
                                {"task_id": oauth_task, "task_state": "blocked",
                                 "agent_id": "developer", "depends_on": research_task,
                                 **link, "source_mode": mode},
                                task_id=oauth_task, agent_id="developer")

        await self._set_agent(session_id, AgentId.researcher, AgentState.researching,
                              TaskState.running, RESEARCH_PROGRESS_MESSAGE)
        await self._set_agent(session_id, AgentId.developer, AgentState.coding,
                              TaskState.running, "Scaffolding login UI (provider-agnostic)")
        await self._set_agent(session_id, AgentId.tester, AgentState.testing,
                              TaskState.running, "Running baseline unit tests")

        await self._step()

        # Tester finishes its independent baseline.
        self._storage.set_task_status(test_task, TaskState.completed.value, utcnow_iso())
        await self._set_agent(session_id, AgentId.tester, AgentState.complete,
                              TaskState.completed, "Baseline tests passed (3/3)")
        await self._bus.publish(session_id, EventType.task_completed,
                                {"task_id": test_task, "result_summary":
                                 "Baseline unit tests passed (3/3)",
                                 "task_state": TaskState.completed.value,
                                 **link, "source_mode": mode},
                                task_id=test_task, agent_id="tester")

        # Developer's independent scaffold done; the gated OAuth part waits.
        await self._set_agent(session_id, AgentId.developer, AgentState.waiting,
                              TaskState.blocked, "Login UI stub ready; awaiting auth decision")

        await self._step()

        # Researcher raises the blocking decision. "Request changes" does not end the
        # run: the researcher prepares a narrower TEMPLATE proposal and raises a NEW
        # decision, at most ``max_revisions`` times. Nothing is built in between.
        max_revisions = proposal_revision_limit(self._settings.max_proposal_revisions)
        revision = 0
        previous_id: str | None = None
        loop = asyncio.get_running_loop()
        while True:
            decision_id = f"dec-{uuid.uuid4().hex[:10]}"
            proposal = (revised_proposal_fields(revision) if revision > 0 else {
                "title": "Choose authentication approach",
                "summary": "Use OAuth instead of storing passwords?",
                "recommendation": "approve",
                "rationale": "Avoid building and maintaining custom password storage in the first version.",
                "alternatives": [
                    {"id": "oauth", "label": "OAuth", "tradeoff": "External identity dependency"},
                    {"id": "passwords", "label": "Email/password", "tradeoff": "More security maintenance"},
                ],
                "risk_level": "medium",
            })
            details = {
                **proposal,
                "priority": "blocking",
                "allowed_actions": ["approve", "reject", "request_changes"],
                "impact": f"Developer task {oauth_task} is blocked on this decision",
                # The recommendation is a fixed template, not research: say so in a
                # machine-readable field and in a sentence the client can show.
                "research_provenance": RESEARCH_PROVENANCE,
                "research_provenance_label": RESEARCH_PROVENANCE_LABEL,
                # 0 for the first proposal; 1.. for a revision of ``revises_decision_id``.
                "revision": revision,
                "decision_revision": revision,
                "revises_decision_id": previous_id,
                **link,
                "source_mode": mode,
            }
            self._storage.create_decision(decision_id, session_id, research_task,
                                          AgentId.researcher.value, details["summary"],
                                          details["recommendation"], details)

            fut: asyncio.Future[str] = loop.create_future()
            self._waiters[decision_id] = fut

            self._storage.set_task_status(research_task, TaskState.awaiting_approval.value,
                                          utcnow_iso())
            await self._set_agent(session_id, AgentId.researcher, AgentState.waiting,
                                  TaskState.awaiting_approval, "Needs a decision")
            await self._bus.publish(session_id, EventType.decision_requested, details,
                                    task_id=research_task, agent_id=AgentId.researcher.value,
                                    decision_id=decision_id)
            if board is not None:
                await board.decision_requested(parent_task_id, decision_id, {
                    "summary": details["summary"],
                    "research_provenance": RESEARCH_PROVENANCE,
                    "research_provenance_label": RESEARCH_PROVENANCE_LABEL,
                    "decision_revision": revision,
                    "revises_decision_id": previous_id})

            # Block here until the backend records a real resolution.
            action = await fut
            self._waiters.pop(decision_id, None)
            if action != DecisionAction.request_changes.value or revision >= max_revisions:
                break
            # Changes requested and a revision is still available: back to research.
            revision += 1
            previous_id = decision_id
            if board is not None:
                await board.changes_requested(parent_task_id, decision_id, revision)
            self._storage.set_task_status(research_task, TaskState.running.value, utcnow_iso())
            await self._set_agent(session_id, AgentId.researcher, AgentState.researching,
                                  TaskState.running, revising_progress_message(revision))
            await self._step()

        if board is not None and action == DecisionAction.approve.value:
            await board.decision_approved(parent_task_id, decision_id)
        outcome = await self._apply_branch(session_id, parent_task_id, research_task,
                                           dev_task, oauth_task, decision_id, action, mode,
                                           board_task_id, revision=revision)
        # Card first, run parent last: the session only accepts a new run once the card
        # already shows this one's outcome.
        if board is not None:
            await board.run_finished(parent_task_id, *outcome)
        self._storage.set_task_status(parent_task_id, TaskState.completed.value, utcnow_iso())

    async def _apply_branch(self, session_id: str, parent_task_id: str, research_task: str,
                            dev_task: str, oauth_task: str, decision_id: str,
                            action: str, mode: str,
                            board_task_id: str | None = None,
                            revision: int = 0) -> tuple[str, str]:
        """Returns ``(card_final_state, result_summary)`` for the linked task card."""
        link = {"board_task_id": board_task_id} if board_task_id else {}
        self._storage.set_task_status(research_task, TaskState.completed.value, utcnow_iso())
        await self._set_agent(session_id, AgentId.researcher, AgentState.complete,
                              TaskState.completed, "Decision delivered")

        if action == DecisionAction.approve.value:
            await self._set_agent(session_id, AgentId.developer, AgentState.coding,
                                  TaskState.running, "Implementing OAuth scaffold")
            self._storage.set_task_status(oauth_task, TaskState.running.value, utcnow_iso())
            await self._step()

            artifact_id = f"art-{uuid.uuid4().hex[:10]}"
            content = {
                "files_changed": ["app/auth/oauth_login.py", "tests/test_oauth_login.py"],
                "diff_excerpt": "+ def start_oauth_login(provider):\n+     return redirect(provider.authorize_url)\n",
                "test_command": "pytest tests/test_oauth_login.py",
                "tests_passed": 5,
                "tests_failed": 0,
                "elapsed_seconds": 2,
            }
            self._storage.create_artifact(artifact_id, session_id, oauth_task, "code_change",
                                          "OAuth login scaffold complete; 5/5 tests passed",
                                          content, mode, utcnow_iso())
            await self._bus.publish(session_id, EventType.artifact_created,
                                    {"artifact_id": artifact_id, "artifact_type": "code_change",
                                     "summary": "OAuth login scaffold complete; 5/5 tests passed",
                                     **link, "source_mode": mode, **content},
                                    task_id=oauth_task, agent_id="developer")
            if board_task_id and self._board is not None:
                # The card gets a v2 artifact it can open. It is the SCRIPTED excerpt,
                # stamped with the scripted producer's mode; nothing was executed.
                from .phase2.artifacts import ArtifactService
                await ArtifactService(self._board.store, self._board.bus, mode).create_artifact(
                    session_id, board_task_id, "code_diff",
                    "Scripted demo: OAuth login scaffold (not executed)",
                    [{"page": 0, "title": "app/auth/oauth_login.py",
                      "content": content["diff_excerpt"],
                      "line_refs": [f"file:{f}" for f in content["files_changed"]]}])
            self._storage.set_task_status(oauth_task, TaskState.completed.value, utcnow_iso())
            await self._set_agent(session_id, AgentId.developer, AgentState.complete,
                                  TaskState.completed, "OAuth scaffold complete")
            await self._bus.publish(session_id, EventType.task_completed,
                                    {"task_id": oauth_task,
                                     "result_summary": "OAuth login scaffold complete; 5/5 tests passed",
                                     "task_state": TaskState.completed.value,
                                     "artifact_id": artifact_id, **link, "source_mode": mode},
                                    task_id=oauth_task, agent_id="developer")
            return (TaskState.completed.value,
                    "OAuth login scaffold complete; 5/5 tests passed")

        # Rejected, or changes requested with no revision left: the gated branch is
        # cancelled and the run ends. No further proposal is promised.
        rejected = action == DecisionAction.reject.value
        msg = ("Rejected: no OAuth changes made" if rejected
               else CHANGES_LIMIT_MESSAGE if revision > 0 else CHANGES_REQUESTED_MESSAGE)
        self._storage.set_task_status(oauth_task, TaskState.cancelled.value, utcnow_iso())
        await self._set_agent(session_id, AgentId.developer, AgentState.waiting,
                              TaskState.cancelled,
                              "OAuth branch not taken" if rejected else msg)
        # Announced as task.completed (the contract has no cancel event) but carrying
        # the real terminal state, matching what the snapshot reports for this task.
        await self._bus.publish(session_id, EventType.task_completed,
                                {"task_id": oauth_task, "result_summary": msg,
                                 "task_state": TaskState.cancelled.value,
                                 **link, "source_mode": mode},
                                task_id=oauth_task, agent_id="developer")
        return (TaskState.cancelled.value, msg)

    async def _set_agent(self, session_id: str, agent: AgentId, state: AgentState,
                        task_state: TaskState, progress: str) -> None:
        self._storage.set_agent_state(session_id, agent.value, state.value, utcnow_iso())
        await self._bus.publish(session_id, EventType.agent_status_changed,
                                {"state": state.value, "task_state": task_state.value,
                                 "progress_message": progress,
                                 "source_mode": self._mode},
                                agent_id=agent.value)
