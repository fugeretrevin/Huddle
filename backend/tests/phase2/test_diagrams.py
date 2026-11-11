"""Diagram service unit tests: templates, bounds, cycles (MASTER §7, §13.3)."""
from __future__ import annotations

import asyncio

import pytest

from app.phase2.errors import CommandError
from app.phase2.models import DiagramTemplate
from tests.conftest import p2_fresh

SID = "ses-test"


def test_auth_flow_template_builds_and_validates():
    async def body():
        store, bus, svc = p2_fresh()
        dia = await svc["diagrams"].create_diagram(SID, DiagramTemplate.auth_flow, None, None)
        assert dia.template.value == "auth_flow"
        assert 0 < len(dia.nodes) <= 12
        assert dia.source_mode == "demo"
        types = [e["type"] for e in store.events_after_v2(SID, 0)]
        assert "diagram.created" in types
    asyncio.run(body())


def test_task_dependency_graph_is_acyclic_from_real_tasks():
    async def body():
        store, bus, svc = p2_fresh()
        g = svc["graph"]
        a = g.create_task(SID, "a", None, 1, [], None)
        g.create_task(SID, "b", None, 1, [a.task_id], None)
        dia = await svc["diagrams"].create_diagram(SID, DiagramTemplate.task_dependency_graph,
                                                   None, None)
        assert len(dia.edges) == 1
    asyncio.run(body())


def test_bad_endpoint_rejected():
    svc = p2_fresh()[2]["diagrams"]
    with pytest.raises(CommandError) as ei:
        svc.validate_graph(DiagramTemplate.auth_flow,
                           [{"id": "a", "label": "A", "icon": "user"}],
                           [{"source": "a", "target": "ghost", "kind": "calls"}])
    assert ei.value.code == "DIAGRAM_BAD_ENDPOINT"


def test_unsupported_icon_rejected():
    svc = p2_fresh()[2]["diagrams"]
    with pytest.raises(CommandError) as ei:
        svc.validate_graph(DiagramTemplate.auth_flow,
                           [{"id": "a", "label": "A", "icon": "banana"}], [])
    assert ei.value.code == "DIAGRAM_BAD_ICON"


def test_too_many_nodes_rejected():
    svc = p2_fresh()[2]["diagrams"]
    nodes = [{"id": f"n{i}", "label": str(i), "icon": "code"} for i in range(13)]
    with pytest.raises(CommandError) as ei:
        svc.validate_graph(DiagramTemplate.module_dependency_graph, nodes, [])
    assert ei.value.code == "DIAGRAM_TOO_LARGE"


def test_cycle_rejected_for_dependency_template():
    svc = p2_fresh()[2]["diagrams"]
    nodes = [{"id": "a", "label": "A", "icon": "queue"},
             {"id": "b", "label": "B", "icon": "queue"}]
    edges = [{"source": "a", "target": "b", "kind": "depends_on"},
             {"source": "b", "target": "a", "kind": "depends_on"}]
    with pytest.raises(CommandError) as ei:
        svc.validate_graph(DiagramTemplate.task_dependency_graph, nodes, edges)
    assert ei.value.code == "DIAGRAM_CYCLE"
