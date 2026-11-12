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

from .diffing import TestSummary, parse_pytest_output, parse_unified_diff
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

# A single artifact page is capped at 12000 chars by the schema; keep real diff
# bodies well under that and paginate by file.
_PAGE_CHAR_CAP = 11000


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
                            raw_pages: list[dict], *, source_mode: str | None = None) -> ArtifactV2:
        # ``source_mode`` is explicit: it is NEVER inferred from the connection. The
        # caller must pass "live" only for genuinely executed output; otherwise the
        # service's configured mode (demo) is used.
        mode = source_mode or self._mode
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
                                       mode, content_hash, clean_pages, now)
        await self._bus.publish(session_id, EventTypeV2.artifact_created,
                                {"kind": kind, "summary": clean_summary, "source_mode": mode},
                                task_id=task_id, artifact_id=artifact_id)
        return ArtifactV2(artifact_id=artifact_id, task_id=task_id, kind=ArtifactKind(kind),
                          summary=clean_summary, source_mode=mode, content_hash=content_hash,
                          created_at=now, pages=[ArtifactPage(**p) for p in clean_pages])

    # ─────────────────────── real sandboxed-worker ingest (P2-02) ───────────────────────
    async def ingest_code_diff(self, session_id: str, task_id: str, diff_text: str,
                             *, summary: str | None = None,
                             source_mode: str = "live") -> ArtifactV2:
        """Turn a REAL unified diff produced inside the sandbox into a ``code_diff``
        artifact: one page per changed file, with accurate add/delete counts, run
        through the sanitizer so no absolute host path or secret survives. Default
        ``source_mode="live"`` because this is called from the genuinely-live path."""
        files = parse_unified_diff(diff_text or "")
        raw_pages: list[dict] = []
        total_add = total_del = 0
        for i, f in enumerate(files):
            total_add += f.additions
            total_del += f.deletions
            raw_pages.append({
                "page": i,
                "title": f.path,
                "content": (f.body or diff_text)[:_PAGE_CHAR_CAP],
                "line_refs": [f"file:{f.path}",
                              f"stat:{f.path}:+{f.additions}/-{f.deletions}"],
            })
        if not raw_pages:
            raw_pages = [{"page": 0, "title": "no changes",
                          "content": sanitize_text(diff_text or "", self._ws_root)[:_PAGE_CHAR_CAP],
                          "line_refs": []}]
        auto = (f"{len(files)} file(s) changed, +{total_add} / -{total_del}"
                if files else "no file changes detected")
        return await self.create_artifact(
            session_id, task_id, ArtifactKind.code_diff.value, summary or auto,
            raw_pages, source_mode=source_mode,
        )

    async def ingest_test_report(self, session_id: str, task_id: str, output: str,
                               *, summary: str | None = None, runner: str = "pytest -q",
                               duration: float | None = None,
                               source_mode: str = "live") -> ArtifactV2:
        """Turn REAL pytest output into a ``test_report`` artifact. Parses
        passed/failed/skipped counts and failing test node ids, and makes the
        *0-tests-collected* case explicit (so it is never shown as 'passing')."""
        s: TestSummary = parse_pytest_output(output or "")
        if not s.ran:
            headline = "0 tests ran (collected 0 items)"
        else:
            parts = [f"{s.passed} passed", f"{s.failed} failed"]
            if s.skipped:
                parts.append(f"{s.skipped} skipped")
            if s.errors:
                parts.append(f"{s.errors} error")
            headline = ", ".join(parts)
        if duration is not None:
            headline += f" in {duration:.2f}s"
        header = (f"runner: {runner}\nresult: {headline}\n"
                  f"collected: {s.collected if s.collected is not None else 'unknown'}\n"
                  f"tests_ran: {str(s.ran).lower()}\n")
        if s.failed_tests:
            header += "failing:\n" + "\n".join(f"  - {t}" for t in s.failed_tests) + "\n"
        content = header + "\n--- raw output ---\n" + (output or "")
        raw_pages = [{
            "page": 0,
            "title": f"Test run ({runner})",
            "content": content[:_PAGE_CHAR_CAP],
            "line_refs": [f"test:{t}" for t in s.failed_tests],
        }]
        return await self.create_artifact(
            session_id, task_id, ArtifactKind.test_report.value, summary or headline,
            raw_pages, source_mode=source_mode,
        )

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
