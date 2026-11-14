"""Shared helpers for Phase 3 tests."""
from __future__ import annotations

import asyncio
import json
from contextlib import contextmanager

from fastapi.testclient import TestClient

from app.main import create_app
from app.phase2.task_graph import TaskGraphService
from app.settings import Settings


class FakeProvider:
    """Injectable model provider. Records calls; no tools, text in / text out."""

    def __init__(self, reply: str = "Fake model answer.", delay: float = 0.0,
                 fail: bool = False) -> None:
        self.reply, self.delay, self.fail = reply, delay, fail
        self.calls: list[dict] = []

    async def complete(self, *, system, messages, max_tokens=600):
        self.calls.append({"system": system, "messages": messages})
        if self.delay:
            await asyncio.sleep(self.delay)
        if self.fail:
            raise RuntimeError("boom")
        return self.reply


def settings(mode: str = "live", **kw) -> Settings:
    base = dict(db_path=":memory:", producer_step_seconds=0.0, require_pairing=True,
                producer_source_mode=mode, p3_side_conversation_enabled=True,
                p3_manager_enabled=True)
    base.update(kw)
    return Settings(**base)


@contextmanager
def p3_client(mode: str = "live", provider=None, **kw):
    app = create_app(settings(mode, **kw))
    with TestClient(app) as c:
        app.state.p3_provider = provider
        yield c


def open_session(client):
    r = client.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local",
                                          "client": "quest"})
    assert r.status_code == 201, r.text
    b = r.json()
    return b["session_id"], {"Authorization": f"Bearer {b['token']}"}


def make_task(client, sid, headers, title="Build login", agent=None, key="mk-task-00000001"):
    body = {"schema_version": 2, "title": title, "priority": 1, "dependencies": [],
            "idempotency_key": key}
    if agent:
        body["agent_id"] = agent
    r = client.post(f"/v2/sessions/{sid}/tasks", headers=headers, json=body)
    assert r.status_code == 201, r.text
    return r.json()


def store_state(client, sid) -> str:
    """Byte-comparable snapshot of the task + event stores for one session."""
    s2 = client.app.state.phase2_store
    return json.dumps({"tasks": s2.list_tasks_v2(sid), "events": s2.events_after_v2(sid, 0),
                       "last_seq": s2.last_seq_v2(sid)}, sort_keys=True)


def ask(client, sid, headers, agent="developer", question="What is the status?", *,
        key="ask-key-00000001", request_id="sq-1", visit="visit-1", task_id=None,
        context_revision=None):
    body = {"request_id": request_id, "visit_session_id": visit, "question": question,
            "idempotency_key": key}
    if task_id:
        body["task_id"] = task_id
    if context_revision is not None:
        body["context_revision"] = context_revision
    return client.post(f"/v2/sessions/{sid}/agents/{agent}/side-question", headers=headers,
                       json=body)


def event_types(client, sid) -> list[str]:
    return [e["type"] for e in client.app.state.phase2_store.events_after_v2(sid, 0)]


def side_stack(provider=None, mode="live", **kw):
    """Service-level stack (no HTTP) for concurrency tests. Returns (svc, store2, graph)."""
    from app.phase2.events import Phase2EventBus
    from app.phase2.store import Phase2Store
    from app.phase3.side import SideConversationService
    from app.phase3.store import Phase3Store
    from app.storage import Storage

    storage = Storage(":memory:")
    s2 = Phase2Store(storage)
    s3 = Phase3Store(storage)
    graph = TaskGraphService(s2, Phase2EventBus(s2), mode)
    svc = SideConversationService(s2, s3, settings(mode, **kw), lambda: provider)
    return svc, s2, graph
