"""Bounded revise-and-retest loop tests (both demo and live producers)."""
from __future__ import annotations

import pytest
from fastapi.testclient import TestClient

from app.workers import (
    CHANGES_LIMIT_MESSAGE,
    DeveloperWorker,
    RESEARCH_PROVENANCE_LABEL,
)
from tests.test_board_bridge import (
    FAIL,
    PASS,
    PRODUCERS,
    TERMINAL,
    _app,
    _card,
    _events1,
    _events2,
    _fake_tester,
    _launch,
    _open,
    _pending,
    _respond,
    _runs,
    _snap2,
    _wait,
    _wait_card,
)


def _pending_new(client, sid, h, previous_ids: set[str]) -> str:
    def find():
        from tests.test_board_bridge import _snap1
        for d in _snap1(client, sid, h)["decisions"]:
            if d["state"] == "requested" and d["decision_id"] not in previous_ids:
                return d["decision_id"]
        return None
    return _wait(find, "a newly raised revised decision")


@pytest.mark.parametrize("producer", PRODUCERS)
def test_request_changes_revises_proposal_and_approve_completes(tmp_path, monkeypatch, producer):
    _fake_tester(monkeypatch, PASS, PASS)
    with TestClient(_app(producer, tmp_path)) as client:
        sid, token, h = _open(client)
        card, did0 = _launch(client, sid, h, "rev-create-0001")
        assert _wait_card(client, sid, h, card["task_id"], ("awaiting_approval",))["task_state"] == "awaiting_approval"

        # Request changes once -> card resumes (reason=changes_requested), raises revised decision did1
        r1 = _respond(client, sid, h, did0, "request_changes", "rev-reqchg-0001")
        assert r1.status_code == 200 and r1.json()["state"] == "changes_requested"
        did1 = _pending_new(client, sid, h, {did0})
        assert did1 != did0
        waiting1 = _wait_card(client, sid, h, card["task_id"], ("awaiting_approval",))
        assert waiting1["task_state"] == "awaiting_approval"

        # Approve the revised proposal -> executes and completes the card
        r2 = _respond(client, sid, h, did1, "approve", "rev-approve-0001")
        assert r2.status_code == 200 and r2.json()["applied"] is True
        done = _wait_card(client, sid, h, card["task_id"])
        assert done["task_state"] == "completed"
        _wait(lambda: _runs(client, sid, h)[0]["status"] in TERMINAL, "run to finish")

        # Verify v1 decision lineage & template provenance are preserved
        decs1 = [e for e in _events1(client, sid, token, h) if e["type"] == "decision.requested"]
        assert len(decs1) == 2
        assert decs1[0]["payload"]["revision"] == 0
        assert decs1[0]["payload"]["revises_decision_id"] is None
        assert decs1[1]["payload"]["revision"] == 1
        assert decs1[1]["payload"]["revises_decision_id"] == did0
        assert decs1[1]["payload"]["research_provenance"] == "template"
        assert decs1[1]["payload"]["research_provenance_label"] == RESEARCH_PROVENANCE_LABEL

        # Verify v2 card trajectory: queued -> running -> awaiting_approval -> running -> awaiting_approval -> running -> completed
        mine = [e for e in _events2(client, sid, token, h)
                if e.get("task_id") == card["task_id"] and e["type"] != "artifact.created"]
        assert [e["payload"].get("task_state") for e in mine] == [
            "queued", None, "running", "awaiting_approval",
            "running", "awaiting_approval", "running", "completed"
        ]
        resumed_for_changes = [e for e in mine if e["type"] == "task.resumed"][0]
        assert resumed_for_changes["payload"]["reason"] == "changes_requested"


@pytest.mark.parametrize("producer", PRODUCERS)
def test_request_changes_exhausting_revision_cap_cancels_cleanly(tmp_path, monkeypatch, producer):
    _fake_tester(monkeypatch, PASS)
    with TestClient(_app(producer, tmp_path)) as client:
        sid, token, h = _open(client)
        card, did0 = _launch(client, sid, h, "rev-cap-create-01")
        seen = {did0}

        # Exhaust revisions: rev0 -> rev1 -> rev2 -> cancel on 3rd request_changes
        assert _respond(client, sid, h, did0, "request_changes", "rev-cap-0").status_code == 200
        did1 = _pending_new(client, sid, h, seen); seen.add(did1)
        _wait_card(client, sid, h, card["task_id"], ("awaiting_approval",))

        assert _respond(client, sid, h, did1, "request_changes", "rev-cap-1").status_code == 200
        did2 = _pending_new(client, sid, h, seen); seen.add(did2)
        _wait_card(client, sid, h, card["task_id"], ("awaiting_approval",))

        assert _respond(client, sid, h, did2, "request_changes", "rev-cap-2").status_code == 200
        done = _wait_card(client, sid, h, card["task_id"])
        assert done["task_state"] == "cancelled"
        assert _snap2(client, sid, h)["artifacts"] == []
        mine = [e for e in _events2(client, sid, token, h) if e.get("task_id") == card["task_id"]]
        assert mine[-1]["payload"]["result_summary"] == CHANGES_LIMIT_MESSAGE
        if producer == "live":
            assert not (tmp_path / "ws" / sid / "oauth_login.py").exists()


def test_live_retest_loop_recovers_on_second_attempt(tmp_path, monkeypatch):
    dev_calls = []
    orig_impl = DeveloperWorker.implement_oauth

    def track_impl(self, guard, ws):
        dev_calls.append(1)
        return orig_impl(self, guard, ws)

    monkeypatch.setattr(DeveloperWorker, "implement_oauth", track_impl)
    # Baseline passes, first post-approval attempt fails, bounded retest passes!
    _fake_tester(monkeypatch, PASS, FAIL, PASS)

    with TestClient(_app("live", tmp_path)) as client:
        sid, token, h = _open(client)
        card, did = _launch(client, sid, h, "retest-ok-create-01")
        assert _respond(client, sid, h, did, "approve", "retest-ok-app-01").status_code == 200
        done = _wait_card(client, sid, h, card["task_id"])
        assert done["task_state"] == "completed"
        _wait(lambda: _runs(client, sid, h)[0]["status"] in TERMINAL, "run to finish")

        assert len(dev_calls) == 2
        ev1 = _events1(client, sid, token, h)
        art = next(e for e in ev1 if e["type"] == "artifact.created")["payload"]
        assert art["attempts"] == 2
        assert art["first_attempt"]["tests_failed"] == 1
        assert art["tests_passed"] == 3 and art["tests_failed"] == 0
        assert not [e for e in ev1 if e["type"] == "task.failed"]
