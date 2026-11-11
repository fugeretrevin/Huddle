"""Rich artifact service (P2-02 foundation).

Transforms raw diff/test/research output into normalized, sanitized, paginated
artifact records. All content is run through the sanitizer (ANSI stripped,
secrets redacted, absolute paths removed) BOTH on ingest and on read. Content is
content-hashed so a client can detect mutation (``stale``).
"""
from __future__ import annotations

import hashlib
import json
import uuid

from .errors import NotFoundError
from .events import Phase2EventBus, utcnow_iso
from .models import (
    ArtifactKind,
    ArtifactPage,
    ArtifactPageResponse,
    ArtifactV2,
    EventTypeV2,
)
from .sanitize import sanitize_text
from .store import Phase2Store


def _hash_pages(pages: list[dict]) -> str:
    h = hashlib.sha256()
    for p in pages:
        h.update(str(p.get("page", "")).encode())
        h.update(b"\x00")
        h.update(p.get("content", "").encode("utf-8"))
        h.update(b"\x00")
    return h.hexdigest()


class ArtifactService:
    def __init__(self, store: Phase2Store, bus: Phase2EventBus, source_mode: str,
                 workspace_root: str | None = None) -> None:
        self._store = store
        self._bus = bus
        self._mode = source_mode
        self._ws_root = workspace_root

    async def create_artifact(self, session_id: str, task_id: str, kind: str, summary: str,
                            raw_pages: list[dict]) -> ArtifactV2:
        clean_pages: list[dict] = []
        for i, p in enumerate(raw_pages):
            content = sanitize_text(p.get("content", ""), self._ws_root)[:12000]
            refs = [sanitize_text(r, self._ws_root) for r in p.get("line_refs", [])][:500]
            clean_pages.append({
                "page": p.get("page", i),
                "title": sanitize_text(p.get("title", f"Page {i}"), self._ws_root)[:120],
                "content": content,
                "line_refs": refs,
            })
        if not clean_pages:
            clean_pages = [{"page": 0, "title": "empty", "content": "", "line_refs": []}]
        content_hash = _hash_pages(clean_pages)
        clean_summary = sanitize_text(summary, self._ws_root)[:300]
        artifact_id = f"a2-{uuid.uuid4().hex[:10]}"
        now = utcnow_iso()
        self._store.create_artifact_v2(artifact_id, session_id, task_id, kind, clean_summary,
                                       self._mode, content_hash, clean_pages, now)
        await self._bus.publish(session_id, EventTypeV2.artifact_created,
                                {"kind": kind, "summary": clean_summary, "source_mode": self._mode},
                                task_id=task_id, artifact_id=artifact_id)
        return ArtifactV2(artifact_id=artifact_id, task_id=task_id, kind=ArtifactKind(kind),
                          summary=clean_summary, source_mode=self._mode, content_hash=content_hash,
                          created_at=now, pages=[ArtifactPage(**p) for p in clean_pages])

    def get_page(self, session_id: str, artifact_id: str, page: int,
                 page_size: int) -> ArtifactPageResponse:
        row = self._store.get_artifact_v2(artifact_id)
        if row is None or row["session_id"] != session_id:
            raise NotFoundError(f"unknown artifact {artifact_id}")
        all_pages = json.loads(row["pages_json"])
        total = len(all_pages)
        page_size = max(1, min(page_size, 50))
        start = page * page_size
        window = all_pages[start:start + page_size]
        # Defense in depth: sanitize again on read.
        out_pages = [
            ArtifactPage(
                page=p["page"],
                title=sanitize_text(p.get("title", ""), self._ws_root)[:120],
                content=sanitize_text(p.get("content", ""), self._ws_root)[:12000],
                line_refs=[sanitize_text(r, self._ws_root) for r in p.get("line_refs", [])],
            )
            for p in window
        ]
        return ArtifactPageResponse(
            artifact_id=artifact_id, task_id=row["task_id"], kind=ArtifactKind(row["kind"]),
            summary=row["summary"], source_mode=row["source_mode"],
            content_hash=row["content_hash"], created_at=row["created_at"],
            page=page, page_size=page_size, total_pages=max(1, (total + page_size - 1) // page_size),
            pages=out_pages, stale=False,
        )
