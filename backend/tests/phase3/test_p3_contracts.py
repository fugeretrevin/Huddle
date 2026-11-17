"""Phase 3 contract files parse and the event enum stays in sync with the DTO."""
from __future__ import annotations

import json
from pathlib import Path

from app.phase2.models import EventTypeV2
from tests.phase3.p3helpers import ask, make_task, open_session, p3_client
from tests.phase3.test_p3_manager import commit, propose

REPO = Path(__file__).resolve().parents[3]
P3 = REPO / "contracts" / "phase3"
FILES = ["manager_plan", "manager_commit_request", "manager_commit_result", "side_question",
         "side_reply"]


def _schema(name: str) -> dict:
    return json.loads((P3 / f"{name}.schema.json").read_text(encoding="utf-8"))


def test_schemas_parse():
    for n in FILES:
        s = _schema(n)
        assert s["$schema"].startswith("https://json-schema.org/") and "required" in s


def test_event_schema_enum_matches_dto_for_manager_types():
    enum = set(json.loads((REPO / "contracts" / "phase2" / "event_v2.schema.json")
                          .read_text(encoding="utf-8"))["properties"]["type"]["enum"])
    assert enum == {e.value for e in EventTypeV2}
    for t in ("manager.plan_proposed", "manager.plan_revised", "manager.plan_rejected",
              "manager.plan_committed", "manager.plan_failed"):
        assert t in enum


def test_payloads_carry_the_required_fields():
    with p3_client() as c:
        sid, h = open_session(c)
        plan = propose(c, sid, h).json()
        assert set(_schema("manager_plan")["required"]) <= set(plan)
        item_req = set(_schema("manager_plan")["properties"]["items"]["items"]["required"])
        assert all(item_req <= set(i) for i in plan["items"])
        res = commit(c, sid, h, plan).json()
        assert set(_schema("manager_commit_result")["required"]) <= set(res)
        t_req = set(_schema("manager_plan")["properties"]["committed_tasks"]["items"]["required"])
        assert all(t_req <= set(t) for t in res["tasks"])
        make_task(c, sid, h, agent="developer")
        reply = ask(c, sid, h).json()
        assert set(_schema("side_reply")["required"]) <= set(reply)
        assert reply["provenance"] in _schema("side_reply")["properties"]["provenance"]["enum"]
        assert reply["status"] in _schema("side_reply")["properties"]["status"]["enum"]
        # every v2 event on the wire is a known enum member
        for ev in c.app.state.phase2_store.events_after_v2(sid, 0):
            assert ev["type"] in {e.value for e in EventTypeV2}
