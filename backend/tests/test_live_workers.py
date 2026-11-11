from __future__ import annotations

import os
import tempfile
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app.main import create_app
from app.settings import Settings
from app.workspace_guard import WorkspaceError, WorkspaceGuard
from tests.conftest import drain_until


# ─────────────────────── sandbox unit tests ───────────────────────
def test_workspace_create_and_baseline_tests():
    with tempfile.TemporaryDirectory() as root:
        guard = WorkspaceGuard(root)
        ws = guard.create_workspace("ses-abc")
        assert (ws / "greeter.py").exists()
        res = guard.run_tests(ws, timeout=60)
        assert res.passed == 3 and res.failed == 0


def test_path_traversal_denied():
    with tempfile.TemporaryDirectory() as root:
        guard = WorkspaceGuard(root)
        ws = guard.create_workspace("ses-xyz")
        for bad in ["../escape.py", "../../etc/passwd", "/abs/evil.py"]:
            with pytest.raises(WorkspaceError):
                guard.write_file(ws, bad, "x = 1")


def test_write_inside_is_allowed():
    with tempfile.TemporaryDirectory() as root:
        guard = WorkspaceGuard(root)
        ws = guard.create_workspace("ses-ok")
        p = guard.write_file(ws, "sub/new.py", "x = 1\n")
        assert Path(p).exists()
        assert guard.read_file(ws, "sub/new.py").strip() == "x = 1"


# ─────────────────────── live integration ───────────────────────
def _live_client(tmp_root: str) -> TestClient:
    settings = Settings(
        db_path=":memory:", producer="live", producer_step_seconds=0.0,
        require_pairing=True, workspace_root=tmp_root, task_timeout_seconds=60,
    )
    return TestClient(create_app(settings))


def _open(client):
    r = client.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local", "client": "quest"})
    b = r.json()
    return b, {"Authorization": f"Bearer {b['token']}"}


def test_live_approve_writes_real_files_and_runs_tests():
    with tempfile.TemporaryDirectory() as root:
        with _live_client(root) as client:
            body, headers = _open(client)
            sid, token = body["session_id"], body["token"]
            with client.websocket_connect(
                    f"/v1/sessions/{sid}/events?after_seq=1&token={token}") as ws:
                client.post(f"/v1/sessions/{sid}/tasks", headers=headers,
                            json={"schema_version": 1, "task_template": "auth_demo",
                                  "idempotency_key": "live-create-0001"})
                pre = drain_until(ws, "decision.requested")
                did = pre[-1]["decision_id"]
                # research step discloses it is deterministic, not model output
                assert pre[-1]["payload"]["provenance"] == "deterministic"
                assert pre[-1]["payload"]["source_mode"] == "live"

                client.post(f"/v1/sessions/{sid}/decisions/{did}/respond", headers=headers,
                            json={"schema_version": 1, "action": "approve",
                                  "idempotency_key": "live-approve-0001"})
                post = drain_until(ws, "artifact.created")

            art = post[-1]["payload"]
            assert art["source_mode"] == "live"
            assert "oauth_login.py" in art["files_changed"]
            # 3 baseline + 3 new oauth tests actually executed
            assert art["tests_passed"] == 6 and art["tests_failed"] == 0

            # the real file exists on disk in the isolated workspace
            ws_dir = Path(root) / sid
            assert (ws_dir / "oauth_login.py").exists()


def test_live_reject_writes_no_oauth_files():
    with tempfile.TemporaryDirectory() as root:
        with _live_client(root) as client:
            body, headers = _open(client)
            sid, token = body["session_id"], body["token"]
            with client.websocket_connect(
                    f"/v1/sessions/{sid}/events?after_seq=1&token={token}") as ws:
                client.post(f"/v1/sessions/{sid}/tasks", headers=headers,
                            json={"schema_version": 1, "task_template": "auth_demo",
                                  "idempotency_key": "live-create-0002"})
                pre = drain_until(ws, "decision.requested")
                did = pre[-1]["decision_id"]
                client.post(f"/v1/sessions/{sid}/decisions/{did}/respond", headers=headers,
                            json={"schema_version": 1, "action": "reject",
                                  "idempotency_key": "live-reject-0002"})
                drain_until(ws, "task.completed")

            ws_dir = Path(root) / sid
            assert not (ws_dir / "oauth_login.py").exists()
