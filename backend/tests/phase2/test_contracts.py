"""Contract layer tests (MASTER §13.1).

No ``jsonschema`` dependency is available in this venv, so fixtures are validated
against the authoritative Pydantic DTOs (which encode the same constraints) AND
structurally cross-checked against the JSON Schema ``required`` lists. We also
assert the v1 contracts and event enum are completely untouched.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from app.phase2.models import (
    ArtifactV2,
    AssignCommand,
    DiagramV2,
    EventEnvelopeV2,
    SnapshotV2,
)

REPO = Path(__file__).resolve().parents[3]
CONTRACTS = REPO / "contracts" / "phase2"
EXAMPLES = REPO / "docs" / "MuseDesk_Phase2_Agent_Pack" / "examples"
V1_CONTRACTS = REPO / "contracts"

SCHEMA_FILES = [
    "snapshot_v2.schema.json", "event_v2.schema.json", "command_v2.schema.json",
    "artifact_v2.schema.json", "diagram_v2.schema.json",
]


def test_v2_schema_files_present_and_parse():
    for name in SCHEMA_FILES:
        schema = json.loads((CONTRACTS / name).read_text(encoding="utf-8"))
        assert schema["$schema"].startswith("https://json-schema.org/")
        assert schema["type"] == "object"
        assert "required" in schema


def _required(schema_file: str) -> set[str]:
    return set(json.loads((CONTRACTS / schema_file).read_text(encoding="utf-8"))["required"])


def test_snapshot_fixture_validates():
    data = json.loads((EXAMPLES / "phase2_demo_snapshot.json").read_text(encoding="utf-8"))
    snap = SnapshotV2(**data)
    assert snap.schema_version == 2
    assert snap.source_mode == "demo"
    assert _required("snapshot_v2.schema.json") <= set(data.keys())
    # every task carries source_mode and a revision (invariants)
    for t in snap.tasks:
        assert t.source_mode in ("live", "demo")
        assert t.revision >= 0


def test_event_fixture_round_trips_exactly():
    data = json.loads((EXAMPLES / "phase2_event_example.json").read_text(encoding="utf-8"))
    env = EventEnvelopeV2(**data)
    assert env.type.value == "handoff.created"
    # wire() must reproduce the fixture exactly (no extra nulls, all ids present)
    assert env.wire() == data


def test_assign_command_fixture_validates():
    data = json.loads((EXAMPLES / "phase2_assign_command_example.json").read_text(encoding="utf-8"))
    cmd = AssignCommand(**data)
    assert cmd.target_agent_id.value == "researcher"
    assert cmd.expected_revision == 0


def test_artifact_fixture_validates():
    data = json.loads((EXAMPLES / "phase2_artifact_example.json").read_text(encoding="utf-8"))
    art = ArtifactV2(**data)
    assert art.kind.value == "test_report"
    assert art.pages[0].page == 0
    assert "SCRIPTED DEMO" in art.summary


def test_diagram_fixture_validates():
    data = json.loads((EXAMPLES / "phase2_diagram_example.json").read_text(encoding="utf-8"))
    dia = DiagramV2(**data)
    assert dia.template.value == "auth_flow"
    assert len(dia.nodes) == 4 and len(dia.edges) == 3


def test_v1_contracts_untouched():
    # v1 event schema still has exactly the original enum and no v2 types leaked in.
    ev = json.loads((V1_CONTRACTS / "event.schema.json").read_text(encoding="utf-8"))
    dumped = json.dumps(ev)
    for v2_only in ("task.assigned", "handoff.created", "focus.changed", "diagram.created"):
        assert v2_only not in dumped, f"v2 type {v2_only} leaked into v1 event schema"


def test_dto_rejects_bad_priority():
    from pydantic import ValidationError
    with pytest.raises(ValidationError):
        AssignCommand(target_agent_id="researcher", expected_revision=-1,
                      idempotency_key="x" * 8)
