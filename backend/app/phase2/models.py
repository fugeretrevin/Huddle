"""Phase 2 Pydantic DTOs. Field names are FROZEN and must match
``contracts/phase2/*.schema.json`` and the Unity Phase2Models C# DTOs. Do not
rename without a coordinated migration across contracts, backend, client,
fixtures and tests.

These are ``schema_version=2`` models and are entirely separate from the v1
models in ``app.models`` (which remain untouched).
"""
from __future__ import annotations

from enum import Enum
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field

SCHEMA_VERSION_2 = 2

SourceMode = Literal["live", "demo"]


# ─────────────────────────── enums ───────────────────────────
class AgentIdV2(str, Enum):
    researcher = "researcher"
    developer = "developer"
    tester = "tester"


class TaskStateV2(str, Enum):
    queued = "queued"
    running = "running"
    awaiting_approval = "awaiting_approval"
    blocked = "blocked"
    pause_requested = "pause_requested"
    paused = "paused"
    completed = "completed"
    failed = "failed"
    cancelled = "cancelled"


# Terminal states a task can no longer be reassigned / mutated from.
TERMINAL_STATES = {TaskStateV2.completed, TaskStateV2.failed, TaskStateV2.cancelled}
# States from which a reassignment is a *safe* checkpoint.
SAFE_ASSIGN_STATES = {TaskStateV2.queued, TaskStateV2.paused}


class FocusMode(str, Enum):
    normal = "normal"
    focus = "focus"
    do_not_disturb = "do_not_disturb"


class HandoffKind(str, Enum):
    blueprint = "blueprint"
    implementation = "implementation"
    test_request = "test_request"
    test_failure = "test_failure"
    review = "review"


class HandoffState(str, Enum):
    created = "created"
    received = "received"
    failed = "failed"


class ArtifactKind(str, Enum):
    code_diff = "code_diff"
    test_report = "test_report"
    research_report = "research_report"
    implementation_plan = "implementation_plan"
    completion = "completion"


class DiagramTemplate(str, Enum):
    auth_flow = "auth_flow"
    task_dependency_graph = "task_dependency_graph"
    module_dependency_graph = "module_dependency_graph"


class NodeIcon(str, Enum):
    file = "file"
    code = "code"
    database = "database"
    user = "user"
    shield = "shield"
    test = "test"
    service = "service"
    queue = "queue"


class EdgeKind(str, Enum):
    data_flow = "data_flow"
    depends_on = "depends_on"
    calls = "calls"
    tests = "tests"


class EventTypeV2(str, Enum):
    # v1 lifecycle types mirrored into the v2 projection
    session_ready = "session.ready"
    agent_status_changed = "agent.status_changed"
    task_created = "task.created"
    task_started = "task.started"
    task_progress = "task.progress"
    decision_requested = "decision.requested"
    decision_resolved = "decision.resolved"
    artifact_created = "artifact.created"
    task_completed = "task.completed"
    task_failed = "task.failed"
    session_reset = "session.reset"
    system_warning = "system.warning"
    # Phase 2 additive types
    task_assigned = "task.assigned"
    task_priority_changed = "task.priority_changed"
    task_pause_requested = "task.pause_requested"
    task_paused = "task.paused"
    task_resumed = "task.resumed"
    handoff_created = "handoff.created"
    handoff_received = "handoff.received"
    handoff_failed = "handoff.failed"
    artifact_page_ready = "artifact.page_ready"
    focus_changed = "focus.changed"
    inbox_updated = "inbox.updated"
    diagram_created = "diagram.created"
    station_milestone = "station.milestone"


# ─────────────────────────── snapshot projections ───────────────────────────
class TaskCardV2(BaseModel):
    model_config = ConfigDict(extra="forbid")
    task_id: str
    title: str = Field(max_length=120)
    task_state: TaskStateV2
    agent_id: str | None = None
    priority: int = Field(ge=0, le=3)
    revision: int = Field(ge=0)
    dependencies: list[str] = Field(default_factory=list)
    source_mode: SourceMode
    # Additive (board -> pipeline bridge). ``executable`` is true only for a card whose
    # ``task_template`` names a pipeline the backend can really run; every other card is
    # a plain board card that no agent executes. ``run_task_id`` is the v1 run (parent
    # task id) the card launched, once it has launched one.
    task_template: str | None = None
    executable: bool = False
    run_task_id: str | None = None


# Templates a card can launch. Each maps to a real producer entry point.
EXECUTABLE_TEMPLATES = frozenset({"auth_demo"})
# The pipeline's first agent: the only valid assignee for starting an executable card.
PIPELINE_FIRST_AGENT = "researcher"


class HandoffV2(BaseModel):
    model_config = ConfigDict(extra="forbid")
    handoff_id: str
    from_agent_id: str
    to_agent_id: str
    task_id: str
    artifact_id: str
    kind: HandoffKind
    state: HandoffState
    source_mode: SourceMode


class ArtifactRefV2(BaseModel):
    """Lightweight artifact reference embedded in the snapshot (the full body is
    fetched via ``GET /v2/.../artifacts/{aid}``)."""
    model_config = ConfigDict(extra="forbid")
    artifact_id: str
    kind: ArtifactKind
    source_mode: SourceMode


class FocusSnapshotV2(BaseModel):
    model_config = ConfigDict(extra="forbid")
    mode: FocusMode
    revision: int = Field(ge=0)
    until: str | None = None


class InboxItemV2(BaseModel):
    model_config = ConfigDict(extra="forbid")
    item_id: str
    kind: str
    summary: str
    priority: int = Field(ge=0, le=3)
    critical: bool = False
    created_at: str
    source_mode: SourceMode


class SnapshotV2(BaseModel):
    model_config = ConfigDict(extra="forbid")
    schema_version: Literal[2] = SCHEMA_VERSION_2
    session_id: str
    last_seq: int = Field(ge=0)
    source_mode: SourceMode
    tasks: list[TaskCardV2] = Field(default_factory=list)
    handoffs: list[HandoffV2] = Field(default_factory=list)
    artifacts: list[ArtifactRefV2] = Field(default_factory=list)
    focus: FocusSnapshotV2
    inbox: list[InboxItemV2] = Field(default_factory=list)


# ─────────────────────────── event envelope ───────────────────────────
class EventEnvelopeV2(BaseModel):
    model_config = ConfigDict(extra="forbid")
    schema_version: Literal[2] = SCHEMA_VERSION_2
    event_id: str
    seq: int = Field(ge=1)
    session_id: str
    occurred_at: str
    type: EventTypeV2
    task_id: str | None = None
    agent_id: str | None = None
    artifact_id: str | None = None
    handoff_id: str | None = None
    payload: dict[str, Any] = Field(default_factory=dict)

    def wire(self) -> dict[str, Any]:
        """Serialize for the wire, dropping ``None`` optional id fields so the
        result validates against event_v2.schema.json (whose id fields are typed
        ``string``, not nullable)."""
        return self.model_dump(mode="json", exclude_none=True)


# ─────────────────────────── full entity DTOs ───────────────────────────
class ArtifactPage(BaseModel):
    model_config = ConfigDict(extra="forbid")
    page: int = Field(ge=0)
    title: str = Field(max_length=120)
    content: str = Field(max_length=12000)
    line_refs: list[str] = Field(default_factory=list, max_length=500)


class ArtifactV2(BaseModel):
    model_config = ConfigDict(extra="forbid")
    schema_version: Literal[2] = SCHEMA_VERSION_2
    artifact_id: str
    task_id: str
    kind: ArtifactKind
    summary: str = Field(max_length=300)
    source_mode: SourceMode
    content_hash: str
    created_at: str
    pages: list[ArtifactPage] = Field(min_length=1, max_length=200)


class ArtifactPageResponse(BaseModel):
    """Paginated response from ``GET /v2/.../artifacts/{aid}``."""
    model_config = ConfigDict(extra="forbid")
    schema_version: Literal[2] = SCHEMA_VERSION_2
    artifact_id: str
    task_id: str
    kind: ArtifactKind
    summary: str
    source_mode: SourceMode
    content_hash: str
    created_at: str
    page: int
    page_size: int
    total_pages: int
    pages: list[ArtifactPage]
    stale: bool = False


class FocusPolicyV2(BaseModel):
    """Full focus policy entity (the snapshot carries the trimmed projection)."""
    model_config = ConfigDict(extra="forbid")
    mode: FocusMode = FocusMode.normal
    until: str | None = None
    quiet_priorities: list[int] = Field(default_factory=list)
    updated_at: str | None = None
    revision: int = Field(default=0, ge=0)


class DiagramNode(BaseModel):
    model_config = ConfigDict(extra="forbid")
    id: str = Field(max_length=36)
    label: str = Field(max_length=60)
    icon: NodeIcon
    evidence_ref: str | None = Field(default=None, max_length=100)


class DiagramEdge(BaseModel):
    model_config = ConfigDict(extra="forbid")
    source: str
    target: str
    kind: EdgeKind


class DiagramV2(BaseModel):
    model_config = ConfigDict(extra="forbid")
    schema_version: Literal[2] = SCHEMA_VERSION_2
    diagram_id: str
    template: DiagramTemplate
    source_mode: SourceMode
    nodes: list[DiagramNode] = Field(max_length=12)
    edges: list[DiagramEdge] = Field(max_length=18)


class StationPose(BaseModel):
    model_config = ConfigDict(extra="forbid")
    station_id: str
    position: list[float] = Field(min_length=3, max_length=3)
    rotation: list[float] = Field(min_length=4, max_length=4)


class StationLayoutV2(BaseModel):
    """Local-to-device layout. Never uploaded with raw room geometry; only poses
    relative to a local anchor reference are stored."""
    model_config = ConfigDict(extra="forbid")
    schema_version: Literal[2] = SCHEMA_VERSION_2
    local_anchor_ref: str
    layout_revision: int = Field(ge=0)
    station_poses: list[StationPose] = Field(default_factory=list)
    scale: float = 1.0


class VisualCueV2(BaseModel):
    """Non-authoritative, idempotent visual cue that can be skipped safely."""
    model_config = ConfigDict(extra="forbid")
    cue_id: str
    cue_kind: str
    canonical_ref: str
    status: str = "pending"


# ─────────────────────────── command bodies ───────────────────────────
class AssignCommand(BaseModel):
    model_config = ConfigDict(extra="forbid")
    schema_version: Literal[2] = SCHEMA_VERSION_2
    target_agent_id: AgentIdV2
    expected_revision: int = Field(ge=0)
    idempotency_key: str = Field(min_length=8, max_length=200)


class PriorityCommand(BaseModel):
    model_config = ConfigDict(extra="forbid")
    schema_version: Literal[2] = SCHEMA_VERSION_2
    priority: int = Field(ge=0, le=3)
    expected_revision: int = Field(ge=0)
    idempotency_key: str = Field(min_length=8, max_length=200)


class PauseCommand(BaseModel):
    model_config = ConfigDict(extra="forbid")
    schema_version: Literal[2] = SCHEMA_VERSION_2
    expected_revision: int = Field(ge=0)
    idempotency_key: str = Field(min_length=8, max_length=200)


class ResumeCommand(BaseModel):
    model_config = ConfigDict(extra="forbid")
    schema_version: Literal[2] = SCHEMA_VERSION_2
    expected_revision: int = Field(ge=0)
    idempotency_key: str = Field(min_length=8, max_length=200)


class FocusCommand(BaseModel):
    model_config = ConfigDict(extra="forbid")
    schema_version: Literal[2] = SCHEMA_VERSION_2
    mode: FocusMode
    until: str | None = None
    quiet_priorities: list[int] = Field(default_factory=list)
    idempotency_key: str = Field(min_length=8, max_length=200)


class DiagramCommand(BaseModel):
    model_config = ConfigDict(extra="forbid")
    schema_version: Literal[2] = SCHEMA_VERSION_2
    template: DiagramTemplate
    task_id: str | None = None
    artifact_id: str | None = None
    idempotency_key: str = Field(min_length=8, max_length=200)


class TaskCreateV2(BaseModel):
    """Additive P2-00 foundation endpoint used to seed v2 task cards (tasks also
    arrive from the orchestrator in later tracks)."""
    model_config = ConfigDict(extra="forbid")
    schema_version: Literal[2] = SCHEMA_VERSION_2
    title: str = Field(max_length=120)
    agent_id: AgentIdV2 | None = None
    priority: int = Field(default=1, ge=0, le=3)
    dependencies: list[str] = Field(default_factory=list)
    role_lock: AgentIdV2 | None = None
    # Additive. Only "auth_demo" is executable; any other value is rejected by the
    # service with UNKNOWN_TASK_TEMPLATE (typed ``str`` so the refusal carries a code).
    task_template: str | None = Field(default=None, max_length=60)
    idempotency_key: str = Field(min_length=8, max_length=200)


# ─────────────────────────── error body ───────────────────────────
class RevisionConflictBody(BaseModel):
    model_config = ConfigDict(extra="forbid")
    code: str
    message: str
    canonical_task: TaskCardV2 | None = None
