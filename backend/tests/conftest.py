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


def drain_until(ws, event_type: str, cap: int = 100) -> list[dict]:
    """Read events until one of `event_type` is seen (inclusive). Returns all read."""
    out = []
    for _ in range(cap):
        ev = ws.receive_json()
        out.append(ev)
        if ev["type"] == event_type:
            return out
    raise AssertionError(f"did not see {event_type} within {cap} events")
