"""Desk Start task with a model + real work on: the Researcher's decision and the Developer's code are
model-written; without a usable model answer the fixed template / starter is used and labelled."""
from __future__ import annotations

import tempfile
from pathlib import Path

from fastapi.testclient import TestClient

from app.main import create_app
from app.model_workers import MODEL_RESEARCH_LABEL, TEMPLATE_FALLBACK_LABEL, proposal_from_model
from app.settings import Settings
from tests.conftest import drain_until

PROPOSAL = ('{"title": "Email + password login", "summary": "Build login with email and password rules?", '
            '"recommendation": "approve", "rationale": "Small, testable, no external service.", '
            '"risk_level": "low", "alternatives": [{"id": "pw", "label": "Email + password", '
            '"tradeoff": "We own password rules"}, {"id": "oauth", "label": "OAuth", "tradeoff": "External dependency"}]}')

CODE = '''FILE: login.py
```python
import re

def valid_email(s):
    return bool(re.fullmatch(r"[^@\\s]+@[^@\\s]+\\.[^@\\s]+", s or ""))

def valid_password(p):
    return len(p or "") >= 12 and any(c.isdigit() for c in p)
```

FILE: test_login.py
```python
from login import valid_email, valid_password

def test_email():
    assert valid_email("a@b.co") and not valid_email("x")

def test_password():
    assert valid_password("abcdefghijk1") and not valid_password("short1")
```
Summary: login rules.
'''
BUGGY = CODE.replace("len(p or \"\") >= 12", "len(p or \"\") >= 99")


class DeskProvider:
    def __init__(self, research=PROPOSAL, code=CODE, fix=CODE, fail=False):
        self.research, self.code, self.fix, self.fail = research, code, fix, fail
        self.calls: list[str] = []

    async def complete(self, *, system, messages, max_tokens=600, purpose="side"):
        if self.fail:
            raise RuntimeError("down")
        if system.startswith("You are the Researcher"):
            self.calls.append("research")
            return self.research
        if "did not all pass" in system:
            self.calls.append("fix")
            return self.fix
        self.calls.append("dev")
        return self.code


def _client(root, provider, execute=True):
    s = Settings(db_path=":memory:", producer="live", producer_step_seconds=0.0, require_pairing=True,
                 workspace_root=root, task_timeout_seconds=60, p3_execute_code=execute)
    app = create_app(s)
    c = TestClient(app)
    return c, app, provider


def _run(root, provider, action="approve", execute=True):
    c, app, prov = _client(root, provider, execute)
    with c:
        app.state.p3_provider = prov
        b = c.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local", "client": "quest"}).json()
        sid, tok = b["session_id"], b["token"]
        h = {"Authorization": f"Bearer {tok}"}
        with c.websocket_connect(f"/v1/sessions/{sid}/events?after_seq=1&token={tok}") as ws:
            c.post(f"/v1/sessions/{sid}/tasks", headers=h,
                   json={"schema_version": 1, "task_template": "auth_demo", "idempotency_key": "desk-model-0001"})
            pre = drain_until(ws, "decision.requested")
            dec = pre[-1]
            c.post(f"/v1/sessions/{sid}/decisions/{dec['decision_id']}/respond", headers=h,
                   json={"schema_version": 1, "action": action, "idempotency_key": "desk-model-0002"})
            post = drain_until(ws, "artifact.created" if action == "approve" else "task.completed")
        files = sorted(p.name for p in (Path(root) / sid).glob("*.py"))   # before shutdown removes the workspace
        return files, dec["payload"], post


def test_model_writes_the_decision_and_the_code():
    with tempfile.TemporaryDirectory() as root:
        prov = DeskProvider()
        files, dec, post = _run(root, prov)
        assert dec["research_provenance"] == "model" and dec["research_provenance_label"] == MODEL_RESEARCH_LABEL
        assert dec["summary"] == "Build login with email and password rules?"
        art = post[-1]["payload"]
        assert art["summary"].startswith("Code written by the model (login.py, test_login.py)")
        assert art["tests_passed"] == 5 and art["tests_failed"] == 0       # 3 baseline + 2 new, really run
        assert "login.py" in files and "oauth_login.py" not in files


def test_failing_model_code_gets_a_real_fix():
    with tempfile.TemporaryDirectory() as root:
        prov = DeskProvider(code=BUGGY, fix=CODE)
        _, _, post = _run(root, prov)
        art = post[-1]["payload"]
        assert "fix" in prov.calls and art["attempts"] == 2
        assert art["first_attempt"]["tests_failed"] == 1 and art["tests_failed"] == 0


def test_model_down_falls_back_to_labelled_template_and_starter():
    with tempfile.TemporaryDirectory() as root:
        _, dec, post = _run(root, DeskProvider(fail=True))
        assert dec["research_provenance"] == "template" and dec["research_provenance_label"] == TEMPLATE_FALLBACK_LABEL
        art = post[-1]["payload"]
        assert art["summary"].startswith("Fixed OAuth starter (the model's code could not be used)")
        assert art["tests_passed"] == 6


def test_execution_off_keeps_the_original_desk_run():
    with tempfile.TemporaryDirectory() as root:
        prov = DeskProvider()
        _, dec, post = _run(root, prov, execute=False)
        assert prov.calls == [] and dec["provenance"] == "deterministic"
        assert post[-1]["payload"]["summary"].startswith("OAuth login scaffold complete")


def test_reject_with_model_writes_nothing():
    with tempfile.TemporaryDirectory() as root:
        prov = DeskProvider()
        files, _, post = _run(root, prov, action="reject")
        assert "dev" not in prov.calls
        assert post[-1]["payload"]["result_summary"] == "Rejected: no code written"
        assert "login.py" not in files


def test_proposal_validation():
    assert proposal_from_model(PROPOSAL, "d1").risk_level == "low"
    assert proposal_from_model("not json", "d1") is None
    assert proposal_from_model(PROPOSAL.replace('"approve"', '"maybe"'), "d1") is None
    assert proposal_from_model(PROPOSAL.replace(', {"id": "oauth", "label": "OAuth", "tradeoff": "External dependency"}', ''), "d1") is None
