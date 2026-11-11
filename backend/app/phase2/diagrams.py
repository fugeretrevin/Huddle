"""Structured spatial explanation service (P2-04 foundation).

Only THREE fixed templates are supported: ``auth_flow``,
``task_dependency_graph``, ``module_dependency_graph``. The backend emits a
schema-validated node/edge graph — never raw Unity commands, GameObject
descriptions, URLs or scripts. Graphs are bounded (<=12 nodes, <=18 edges) and
dependency templates must be acyclic (DAG). Malformed output fails into an
explanatory text fallback rather than crashing.
"""
from __future__ import annotations

import json
import uuid

from .errors import CommandError, NotFoundError
from .events import Phase2EventBus, utcnow_iso
from .models import (
    DiagramEdge,
    DiagramNode,
    DiagramTemplate,
    DiagramV2,
    EdgeKind,
    EventTypeV2,
    NodeIcon,
)
from .store import Phase2Store

MAX_NODES = 12
MAX_EDGES = 18
_DAG_TEMPLATES = {DiagramTemplate.task_dependency_graph}
_VALID_ICONS = {i.value for i in NodeIcon}
_VALID_EDGE_KINDS = {k.value for k in EdgeKind}


def _has_cycle(node_ids: set[str], edges: list[dict]) -> bool:
    adj: dict[str, list[str]] = {n: [] for n in node_ids}
    for e in edges:
        if e["source"] in adj:
            adj[e["source"]].append(e["target"])
    WHITE, GREY, BLACK = 0, 1, 2
    color = {n: WHITE for n in node_ids}

    def dfs(u: str) -> bool:
        color[u] = GREY
        for v in adj.get(u, []):
            if color.get(v) == GREY:
                return True
            if color.get(v) == WHITE and dfs(v):
                return True
        color[u] = BLACK
        return False

    return any(color[n] == WHITE and dfs(n) for n in node_ids)


class DiagramService:
    def __init__(self, store: Phase2Store, bus: Phase2EventBus, source_mode: str) -> None:
        self._store = store
        self._bus = bus
        self._mode = source_mode

    # ─────────────────────── validation ───────────────────────
    def validate_graph(self, template: DiagramTemplate, nodes: list[dict],
                       edges: list[dict]) -> None:
        if len(nodes) == 0:
            raise CommandError("DIAGRAM_EMPTY", "diagram has no nodes", status=422)
        if len(nodes) > MAX_NODES:
            raise CommandError("DIAGRAM_TOO_LARGE", f"{len(nodes)} nodes exceeds {MAX_NODES}",
                               status=422)
        if len(edges) > MAX_EDGES:
            raise CommandError("DIAGRAM_TOO_LARGE", f"{len(edges)} edges exceeds {MAX_EDGES}",
                               status=422)
        ids: set[str] = set()
        for n in nodes:
            if n["id"] in ids:
                raise CommandError("DIAGRAM_DUP_NODE", f"duplicate node id {n['id']}", status=422)
            ids.add(n["id"])
            if n.get("icon") not in _VALID_ICONS:
                raise CommandError("DIAGRAM_BAD_ICON", f"unsupported icon {n.get('icon')}",
                                   status=422)
        for e in edges:
            if e.get("kind") not in _VALID_EDGE_KINDS:
                raise CommandError("DIAGRAM_BAD_EDGE_KIND", f"unsupported edge kind {e.get('kind')}",
                                   status=422)
            if e["source"] not in ids or e["target"] not in ids:
                raise CommandError("DIAGRAM_BAD_ENDPOINT",
                                   f"edge {e['source']}->{e['target']} references unknown node",
                                   status=422)
        if template in _DAG_TEMPLATES and _has_cycle(ids, edges):
            raise CommandError("DIAGRAM_CYCLE",
                               f"template {template.value} must be acyclic but a cycle was found",
                               status=422)

    # ─────────────────────── building ───────────────────────
    def _build_nodes_edges(self, session_id: str, template: DiagramTemplate,
                          task_id: str | None) -> tuple[list[dict], list[dict]]:
        if template == DiagramTemplate.auth_flow:
            nodes = [
                {"id": "u", "label": "User", "icon": "user"},
                {"id": "f", "label": "Frontend", "icon": "code"},
                {"id": "a", "label": "Auth service", "icon": "shield"},
                {"id": "d", "label": "Database", "icon": "database"},
            ]
            edges = [
                {"source": "u", "target": "f", "kind": "data_flow"},
                {"source": "f", "target": "a", "kind": "calls"},
                {"source": "a", "target": "d", "kind": "data_flow"},
            ]
            return nodes, edges
        if template == DiagramTemplate.module_dependency_graph:
            nodes = [
                {"id": "api", "label": "api", "icon": "service"},
                {"id": "auth", "label": "auth", "icon": "shield"},
                {"id": "store", "label": "storage", "icon": "database"},
                {"id": "tests", "label": "tests", "icon": "test"},
            ]
            edges = [
                {"source": "api", "target": "auth", "kind": "depends_on"},
                {"source": "api", "target": "store", "kind": "depends_on"},
                {"source": "tests", "target": "api", "kind": "tests"},
            ]
            return nodes, edges
        # task_dependency_graph — derived deterministically from the session's tasks.
        tasks = self._store.list_tasks_v2(session_id)[:MAX_NODES]
        nodes = [{"id": t["id"][:36], "label": t["title"][:60], "icon": "queue"} for t in tasks]
        node_ids = {n["id"] for n in nodes}
        edges = []
        for t in tasks:
            for dep in json.loads(t["dependency_json"]):
                if dep[:36] in node_ids and t["id"][:36] in node_ids:
                    edges.append({"source": dep[:36], "target": t["id"][:36], "kind": "depends_on"})
        return nodes, edges[:MAX_EDGES]

    async def create_diagram(self, session_id: str, template: DiagramTemplate,
                           task_id: str | None, artifact_id: str | None) -> DiagramV2:
        if artifact_id is not None and self._store.get_artifact_v2(artifact_id) is None:
            raise NotFoundError(f"unknown artifact {artifact_id}")
        nodes, edges = self._build_nodes_edges(session_id, template, task_id)
        self.validate_graph(template, nodes, edges)  # raises CommandError on violation
        diagram_id = f"d2-{uuid.uuid4().hex[:10]}"
        now = utcnow_iso()
        self._store.create_diagram_v2(diagram_id, session_id, template.value, self._mode,
                                      nodes, edges, now)
        await self._bus.publish(session_id, EventTypeV2.diagram_created,
                                {"template": template.value, "source_mode": self._mode},
                                task_id=task_id)
        return DiagramV2(diagram_id=diagram_id, template=template, source_mode=self._mode,
                         nodes=[DiagramNode(**n) for n in nodes],
                         edges=[DiagramEdge(**e) for e in edges])
