"""Pydantic request/response and event models. Field names are FROZEN and must
match contracts/*.schema.json and the Unity client models. Do not rename without
a coordinated migration across contracts, backend, client, fixtures and tests."""
from __future__ import annotations

from enum import Enum
from typing import Any, Literal

from pydantic import BaseModel, Field

SCHEMA_VERSION = 1


# ─────────────────────────── enums ───────────────────────────
class SessionMode(str, Enum):
    live_local = "live_local"
    live_lan = "live_lan"
    hosted = "hosted"
    demo = "demo"


class AgentId(str, Enum):
    researcher = "researcher"
    developer = "developer"
    tester = "tester"
    system = "system"


class AgentState(str, Enum):
    idle = "idle"
    researching = "researching"
    coding = "coding"
    testing = "testing"
    reporting = "reporting"
    approaching = "approaching"
    waiting = "waiting"
    complete = "complete"
    error = "error"


class TaskState(str, Enum):
    queued = "queued"
    running = "running"
    blocked = "blocked"
    awaiting_approval = "awaiting_approval"
    completed = "completed"
    failed = "failed"
    cancelled = "cancelled"


class DecisionState(str, Enum):
    requested = "requested"
    presented = "presented"
    approved = "approved"
    rejected = "rejected"
    changes_requested = "changes_requested"
    expired = "expired"


class DecisionAction(str, Enum):
    approve = "approve"
    reject = "reject"
    request_changes = "request_changes"


class EventType(str, Enum):
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


SourceMode = Literal["live", "demo"]


# ─────────────────────────── REST bodies ───────────────────────────
class SessionCreate(BaseModel):
    schema_version: int = SCHEMA_VERSION
    mode: SessionMode = SessionMode.live_local
    client: str = "quest"


class SessionCreated(BaseModel):
    schema_version: int = SCHEMA_VERSION
    session_id: str
    token: str
    mode: SessionMode
    created_at: str
    last_seq: int


class TaskCreate(BaseModel):
    schema_version: int = SCHEMA_VERSION
    task_template: Literal["auth_demo"]
    idempotency_key: str = Field(min_length=8, max_length=200)


class DecisionResponse(BaseModel):
    schema_version: int = SCHEMA_VERSION
    action: DecisionAction
    idempotency_key: str = Field(min_length=8, max_length=200)
    comment: str | None = Field(default=None, max_length=2000)


class DecisionResolution(BaseModel):
    schema_version: int = SCHEMA_VERSION
    decision_id: str
    state: DecisionState
    action: DecisionAction
    resolved_at: str
    applied: bool


class QuestionRequest(BaseModel):
    schema_version: int = SCHEMA_VERSION
    agent_id: AgentId
    decision_id: str | None = None
    question: str = Field(min_length=1, max_length=1000)


class QuestionAnswer(BaseModel):
    schema_version: int = SCHEMA_VERSION
    answer: str
    evidence: list[str] = Field(default_factory=list)
    source_mode: SourceMode


# ─────────────────────────── events / snapshot ───────────────────────────
class EventEnvelope(BaseModel):
    schema_version: int = SCHEMA_VERSION
    event_id: str
    seq: int
    session_id: str
    occurred_at: str
    type: EventType
    task_id: str | None = None
    agent_id: AgentId | None = None
    decision_id: str | None = None
    payload: dict[str, Any] = Field(default_factory=dict)


class AgentView(BaseModel):
    agent_id: AgentId
    role: str
    display_name: str
    state: AgentState


class TaskView(BaseModel):
    task_id: str
    agent_id: AgentId | None = None
    parent_task_id: str | None = None
    status: TaskState
    template: str | None = None
    dependencies: list[str] = Field(default_factory=list)


class DecisionView(BaseModel):
    decision_id: str
    task_id: str | None = None
    agent_id: AgentId | None = None
    state: DecisionState
    summary: str
    recommendation: str | None = None


class Snapshot(BaseModel):
    schema_version: int = SCHEMA_VERSION
    session_id: str
    mode: SessionMode
    last_seq: int
    agents: list[AgentView] = Field(default_factory=list)
    tasks: list[TaskView] = Field(default_factory=list)
    decisions: list[DecisionView] = Field(default_factory=list)


class HealthResponse(BaseModel):
    status: str = "ok"
    schema_version: int = SCHEMA_VERSION
    version: str


class ErrorBody(BaseModel):
    error_code: str
    user_message: str
    detail: str | None = None
