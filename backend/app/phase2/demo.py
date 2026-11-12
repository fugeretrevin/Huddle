"""Scripted offline demo producer (MASTER_PHASE2_IMPLEMENTATION.md §14).

``DemoGatewayV2`` replays the full demonstration scenario *through the real v2
machinery* — the same :class:`Phase2Store`, :class:`Phase2EventBus`,
:class:`ArtifactService` and :class:`HandoffService` a live session uses. It does
NOT fabricate a parallel event format: it writes genuine ``*_v2`` rows and emits
genuine ``EventEnvelopeV2`` events with a monotonic, gap-free per-session ``seq``,
so a Unity client gets the whole story over ``GET /v2/.../snapshot`` + the
``/v2/.../events`` websocket with no PC, no tokens and no internet.

Every entity is stamped ``source_mode="demo"``. This producer can NEVER emit
``live`` — the honesty invariant (scripted activity is never shown as live work)
is enforced structurally here by hard-coding the mode.

The narrative content (task titles, decision text, artifact pages) is loaded from
``contracts/phase2/phase2_demo.json`` so the script is data-driven; the ordered
beats of the story live in :meth:`DemoGatewayV2.replay`.
"""
from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

from .artifacts import ArtifactService
from .events import Phase2EventBus, utcnow_iso
from .handoffs import HandoffService
from .models import EventTypeV2, HandoffKind, TaskStateV2
from .store import Phase2Store

DEMO_SOURCE_MODE = "demo"
DEMO_FIXTURE_PATH = (
    Path(__file__).resolve().parents[3] / "contracts" / "phase2" / "phase2_demo.json"
)


def load_demo_script(path: str | Path | None = None) -> dict:
    """Load the scripted-demo fixture (the content catalog the producer replays)."""
    p = Path(path) if path is not None else DEMO_FIXTURE_PATH
    return json.loads(p.read_text(encoding="utf-8"))


@dataclass
class DemoResult:
    session_id: str
    already_ran: bool
    last_seq: int
    task_ids: dict[str, str] = field(default_factory=dict)
    artifact_ids: dict[str, str] = field(default_factory=dict)
    handoff_ids: list[str] = field(default_factory=list)
    event_types: list[str] = field(default_factory=list)


class DemoGatewayV2:
    """Deterministic offline producer for the v2 surface. Always ``demo``."""

    SOURCE_MODE = DEMO_SOURCE_MODE

    def __init__(self, store: Phase2Store, bus: Phase2EventBus,
                 script: dict | None = None) -> None:
        self._store = store
        self._bus = bus
        self._script = script or load_demo_script()
        # Services are constructed in demo mode: they can only ever stamp "demo".
        self._artifacts = ArtifactService(store, bus, self.SOURCE_MODE)
        self._handoffs = HandoffService(store, bus, self.SOURCE_MODE)

    # ─────────────────────── helpers ───────────────────────
    def _tid(self, session_id: str, logical: str) -> str:
        """Session-scoped physical task id for a logical fixture key (tasks_v2.id
        is a global primary key, so the same scenario must not collide across
        sessions)."""
        return f"{session_id}--{logical}"

    async def _emit(self, session_id: str, tag: str, type_: EventTypeV2,
                    payload: dict, **ids: str | None):
        """Publish a lifecycle event with a canonical, session-scoped event_id so a
        re-run (or a dropped-websocket replay) de-duplicates instead of doubling."""
        clean = {k: v for k, v in ids.items() if v is not None}
        return await self._bus.publish(
            session_id, type_, {**payload, "source_mode": self.SOURCE_MODE},
            event_id=f"evt2-demo-{session_id}-{tag}", **clean,
        )

    def _create_task(self, session_id: str, logical: str, *, state: TaskStateV2,
                     agent_id: str | None) -> str:
        spec = self._script["tasks"][logical]
        tid = self._tid(session_id, logical)
        deps = [self._tid(session_id, d) for d in spec.get("dependencies", [])]
        self._store.create_task_v2(
            tid, session_id, spec["title"], state.value, agent_id,
            spec.get("priority", 1), deps, spec.get("role_lock"),
            self.SOURCE_MODE, utcnow_iso(),
        )
        return tid

    def _set_state(self, tid: str, state: TaskStateV2, *, agent_id: str | None = None):
        self._store.update_task_v2(tid, task_state=state.value, agent_id=agent_id,
                                   now=utcnow_iso())

    async def _artifact(self, session_id: str, logical: str) -> str:
        spec = self._script["artifacts"][logical]
        tid = self._tid(session_id, spec["task"])
        art = await self._artifacts.create_artifact(
            session_id, tid, spec["kind"], spec["summary"], spec["pages"],
        )
        return art.artifact_id

    async def _handoff(self, session_id: str, tag: str, kind: HandoffKind,
                       from_agent: str, to_agent: str, logical_task: str,
                       artifact_id: str) -> str:
        hid = f"{session_id}-h-{tag}"
        ho = await self._handoffs.create(
            session_id, from_agent, to_agent, self._tid(session_id, logical_task),
            artifact_id, kind.value, handoff_id=hid,
        )
        await self._handoffs.mark_received(session_id, ho.handoff_id)
        return ho.handoff_id

    # ─────────────────────── the scenario ───────────────────────
    async def replay(self, session_id: str) -> DemoResult:
        """Play the full task-card → research → approval → handoff → diff → failed
        test → recovery → passing test → completion story. Idempotent at the
        scenario level: a second call is a no-op that returns the current tail."""
        research_tid = self._tid(session_id, "research-auth")
        if self._store.get_task_v2(research_tid) is not None:
            return DemoResult(session_id=session_id, already_ran=True,
                              last_seq=self._store.last_seq_v2(session_id))

        dec = self._script["decision"]
        result = DemoResult(session_id=session_id, already_ran=False, last_seq=0)

        # 1) Three task cards appear on the board. Research is unassigned (the user
        #    will "drag" it to the Researcher); the dependents are blocked.
        r_tid = self._create_task(session_id, "research-auth",
                                  state=TaskStateV2.queued, agent_id=None)
        d_tid = self._create_task(session_id, "develop-auth",
                                  state=TaskStateV2.blocked, agent_id="developer")
        t_tid = self._create_task(session_id, "test-auth",
                                  state=TaskStateV2.blocked, agent_id="tester")
        result.task_ids = {"research-auth": r_tid, "develop-auth": d_tid, "test-auth": t_tid}
        await self._emit(session_id, "task-created-research", EventTypeV2.task_created,
                         {"title": self._script["tasks"]["research-auth"]["title"],
                          "task_state": "queued"}, task_id=r_tid)
        await self._emit(session_id, "task-created-develop", EventTypeV2.task_created,
                         {"title": self._script["tasks"]["develop-auth"]["title"],
                          "task_state": "blocked", "depends_on": r_tid},
                         task_id=d_tid, agent_id="developer")
        await self._emit(session_id, "task-created-test", EventTypeV2.task_created,
                         {"title": self._script["tasks"]["test-auth"]["title"],
                          "task_state": "blocked", "depends_on": d_tid},
                         task_id=t_tid, agent_id="tester")

        # 2) User drags the research card onto the Researcher station -> assign.
        self._set_state(r_tid, TaskStateV2.queued, agent_id="researcher")
        await self._emit(session_id, "assign-research", EventTypeV2.task_assigned,
                         {"target_agent_id": "researcher"}, task_id=r_tid, agent_id="researcher")

        # 3) Researcher starts work.
        self._set_state(r_tid, TaskStateV2.running, agent_id="researcher")
        await self._emit(session_id, "start-research", EventTypeV2.task_started,
                         {}, task_id=r_tid, agent_id="researcher")
        await self._emit(session_id, "status-researcher-working",
                         EventTypeV2.agent_status_changed,
                         {"state": "researching", "progress_message": "Comparing auth options"},
                         agent_id="researcher", task_id=r_tid)

        # 4) Researcher produces a research_report artifact + raises a gated decision.
        a_research = await self._artifact(session_id, "research_report")
        result.artifact_ids["research_report"] = a_research
        self._set_state(r_tid, TaskStateV2.awaiting_approval, agent_id="researcher")
        await self._emit(session_id, "decision-requested", EventTypeV2.decision_requested,
                         {"decision_id": dec["decision_id"], "title": dec["title"],
                          "summary": dec["summary"], "recommendation": dec["recommendation"],
                          "rationale": dec["rationale"], "risk_level": dec["risk_level"],
                          "priority": dec.get("priority", "blocking"),
                          "allowed_actions": dec["allowed_actions"],
                          "alternatives": dec["alternatives"],
                          "provenance": dec.get("provenance", "scripted")},
                         task_id=r_tid, agent_id="researcher", artifact_id=a_research)

        # 5) User approves. The backend is the authority: only now do dependents move.
        await self._emit(session_id, "decision-resolved", EventTypeV2.decision_resolved,
                         {"decision_id": dec["decision_id"], "action": dec["resolution"],
                          "state": "approved"},
                         task_id=r_tid, agent_id="researcher")
        self._set_state(r_tid, TaskStateV2.completed, agent_id="researcher")
        await self._emit(session_id, "complete-research", EventTypeV2.task_completed,
                         {"result_summary": "Recommendation delivered and approved"},
                         task_id=r_tid, agent_id="researcher")
        await self._emit(session_id, "status-researcher-done",
                         EventTypeV2.agent_status_changed,
                         {"state": "complete", "progress_message": "Decision delivered"},
                         agent_id="researcher", task_id=r_tid)

        # 6) Authoritative Researcher -> Developer blueprint handoff (courier cue).
        result.handoff_ids.append(await self._handoff(
            session_id, "blueprint", HandoffKind.blueprint, "researcher", "developer",
            "develop-auth", a_research))

        # 7) Developer begins implementing against the approved blueprint.
        self._set_state(d_tid, TaskStateV2.running, agent_id="developer")
        await self._emit(session_id, "start-develop", EventTypeV2.task_started,
                         {}, task_id=d_tid, agent_id="developer")
        await self._emit(session_id, "status-developer-coding",
                         EventTypeV2.agent_status_changed,
                         {"state": "coding", "progress_message": "Writing OAuth scaffold"},
                         agent_id="developer", task_id=d_tid)

        # 8) Developer produces a code_diff and requests testing.
        a_diff = await self._artifact(session_id, "code_diff_v1")
        result.artifact_ids["code_diff_v1"] = a_diff
        result.handoff_ids.append(await self._handoff(
            session_id, "test-request-1", HandoffKind.test_request, "developer", "tester",
            "test-auth", a_diff))

        # 9) Tester runs — and the first run FAILS (the recovery branch).
        self._set_state(t_tid, TaskStateV2.running, agent_id="tester")
        await self._emit(session_id, "start-test-1", EventTypeV2.task_started,
                         {}, task_id=t_tid, agent_id="tester")
        await self._emit(session_id, "status-tester-run1",
                         EventTypeV2.agent_status_changed,
                         {"state": "testing", "progress_message": "Running login tests"},
                         agent_id="tester", task_id=t_tid)
        a_test_fail = await self._artifact(session_id, "test_report_failed")
        result.artifact_ids["test_report_failed"] = a_test_fail
        self._set_state(t_tid, TaskStateV2.failed, agent_id="tester")
        await self._emit(session_id, "fail-test-1", EventTypeV2.task_failed,
                         {"result_summary": "2 passed, 1 failed", "passed": 2, "failed": 1},
                         task_id=t_tid, agent_id="tester", artifact_id=a_test_fail)
        await self._emit(session_id, "status-tester-fail",
                         EventTypeV2.agent_status_changed,
                         {"state": "error", "progress_message": "1 test failed"},
                         agent_id="tester", task_id=t_tid)

        # 10) Tester hands the FAILED report back to the Developer (authoritative).
        result.handoff_ids.append(await self._handoff(
            session_id, "test-failure", HandoffKind.test_failure, "tester", "developer",
            "develop-auth", a_test_fail))

        # 11) Developer fixes the defect (develop-auth resumes coding).
        self._set_state(d_tid, TaskStateV2.running, agent_id="developer")
        await self._emit(session_id, "resume-develop", EventTypeV2.task_resumed,
                         {"task_state": "running"}, task_id=d_tid, agent_id="developer")
        await self._emit(session_id, "status-developer-fix",
                         EventTypeV2.agent_status_changed,
                         {"state": "coding", "progress_message": "Fixing redirect_uri handling"},
                         agent_id="developer", task_id=d_tid)
        a_diff_fix = await self._artifact(session_id, "code_diff_fix")
        result.artifact_ids["code_diff_fix"] = a_diff_fix
        result.handoff_ids.append(await self._handoff(
            session_id, "test-request-2", HandoffKind.test_request, "developer", "tester",
            "test-auth", a_diff_fix))

        # 12) Tester re-runs — now PASSING. test-auth completes.
        self._set_state(t_tid, TaskStateV2.running, agent_id="tester")
        await self._emit(session_id, "start-test-2", EventTypeV2.task_started,
                         {}, task_id=t_tid, agent_id="tester")
        await self._emit(session_id, "status-tester-run2",
                         EventTypeV2.agent_status_changed,
                         {"state": "testing", "progress_message": "Re-running login tests"},
                         agent_id="tester", task_id=t_tid)
        a_test_pass = await self._artifact(session_id, "test_report_passed")
        result.artifact_ids["test_report_passed"] = a_test_pass
        self._set_state(t_tid, TaskStateV2.completed, agent_id="tester")
        await self._emit(session_id, "complete-test", EventTypeV2.task_completed,
                         {"result_summary": "3 passed, 0 failed", "passed": 3, "failed": 0},
                         task_id=t_tid, agent_id="tester", artifact_id=a_test_pass)
        await self._emit(session_id, "status-tester-pass",
                         EventTypeV2.agent_status_changed,
                         {"state": "complete", "progress_message": "All tests passing"},
                         agent_id="tester", task_id=t_tid)

        # 13) Completion artifact goes to the output shelf; develop-auth completes.
        a_completion = await self._artifact(session_id, "completion")
        result.artifact_ids["completion"] = a_completion
        self._set_state(d_tid, TaskStateV2.completed, agent_id="developer")
        await self._emit(session_id, "complete-develop", EventTypeV2.task_completed,
                         {"result_summary": "OAuth login feature complete and verified"},
                         task_id=d_tid, agent_id="developer", artifact_id=a_completion)
        await self._emit(session_id, "status-developer-done",
                         EventTypeV2.agent_status_changed,
                         {"state": "complete", "progress_message": "Feature shipped to shelf"},
                         agent_id="developer", task_id=d_tid)

        # 14) Factory "released" milestone (visual cue only; business truth is above).
        await self._emit(session_id, "station-released", EventTypeV2.station_milestone,
                         {"cue": "released", "station_id": "output-shelf",
                          "task_id": d_tid}, task_id=d_tid)

        result.last_seq = self._store.last_seq_v2(session_id)
        result.event_types = [e["type"] for e in self._store.events_after_v2(session_id, 0)]
        return result
