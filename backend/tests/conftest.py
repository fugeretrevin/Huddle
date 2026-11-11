from __future__ import annotations

import pytest
from fastapi.testclient import TestClient

from app.main import create_app
from app.settings import Settings


def make_settings() -> Settings:
    # In-memory DB, instant producer stepping, pairing enforced.
    return Settings(db_path=":memory:", producer_step_seconds=0.0,
                    require_pairing=True, producer_source_mode="demo")


@pytest.fixture()
def client() -> TestClient:
    app = create_app(make_settings())
    with TestClient(app) as c:
        yield c


def open_session(client: TestClient) -> tuple[str, dict[str, str]]:
    r = client.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local",
                                          "client": "quest"})
    assert r.status_code == 201, r.text
    body = r.json()
    headers = {"Authorization": f"Bearer {body['token']}"}
    return body, headers


def p2_fresh(source_mode: str = "demo"):
    """Build an isolated in-memory Phase 2 stack (store + bus + services) for
    service-layer unit tests. Returns ``(store, bus, services)``. Build/await it
    inside a single ``asyncio.run`` body so the bus locks bind to one loop."""
    from app.phase2.artifacts import ArtifactService
    from app.phase2.diagrams import DiagramService
    from app.phase2.events import Phase2EventBus
    from app.phase2.focus import FocusService
    from app.phase2.handoffs import HandoffService
    from app.phase2.store import Phase2Store
    from app.phase2.task_graph import TaskGraphService
    from app.storage import Storage

    storage = Storage(":memory:")
    store = Phase2Store(storage)
    bus = Phase2EventBus(store)
    services = {
        "graph": TaskGraphService(store, bus, source_mode),
        "handoffs": HandoffService(store, bus, source_mode),
        "artifacts": ArtifactService(store, bus, source_mode),
        "focus": FocusService(store, bus, source_mode),
        "diagrams": DiagramService(store, bus, source_mode),
    }
    return store, bus, services


def seed_v2_task(client, sid, headers, *, title="Task", agent_id=None, priority=1,
                 dependencies=None, role_lock=None, key="seed-task-00000000"):
    """Create a v2 task card over HTTP and return its canonical card dict."""
    body = {"schema_version": 2, "title": title, "priority": priority,
            "dependencies": dependencies or [], "idempotency_key": key}
    if agent_id:
        body["agent_id"] = agent_id
    if role_lock:
        body["role_lock"] = role_lock
    r = client.post(f"/v2/sessions/{sid}/tasks", headers=headers, json=body)
    assert r.status_code == 201, r.text
    return r.json()


def drain_until(ws, event_type: str, cap: int = 100) -> list[dict]:
    """Read events until one of `event_type` is seen (inclusive). Returns all read."""
    out = []
    for _ in range(cap):
        ev = ws.receive_json()
        out.append(ev)
        if ev["type"] == event_type:
            return out
    raise AssertionError(f"did not see {event_type} within {cap} events")
