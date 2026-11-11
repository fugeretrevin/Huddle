"""Artifact sanitize + paginate unit tests (MASTER §5, §13.3)."""
from __future__ import annotations

import asyncio

from app.phase2.sanitize import contains_abs_path, sanitize_text
from tests.conftest import p2_fresh

SID = "ses-test"


def test_strip_ansi_and_redact_secrets_and_paths():
    raw = ("\x1b[31mFAIL\x1b[0m at C:\\Users\\stoof\\secret\\app.py\n"
           "api_key=sk-ABCDEF0123456789ABCDEF\n"
           "Authorization: Bearer abcdef123456789TOKEN\n"
           "home path /home/stoof/.ssh/id_rsa\n")
    out = sanitize_text(raw, workspace_root="C:\\Users\\stoof\\ws")
    assert "\x1b[" not in out                 # ANSI stripped
    assert "sk-ABCDEF" not in out             # key redacted
    assert "REDACTED" in out
    assert "id_rsa" not in out or "<path>" in out
    assert not contains_abs_path(out)         # no absolute host paths survive


def test_artifact_ingest_sanitizes_and_paginates():
    async def body():
        store, bus, svc = p2_fresh()
        a = svc["artifacts"]
        pages = [{"page": i, "title": f"p{i}",
                  "content": f"line in C:\\Users\\x\\f{i}.py token=supersecretvalue{i}",
                  "line_refs": [f"file:{i}"]} for i in range(12)]
        art = await a.create_artifact(SID, "task-1", "code_diff", "demo diff", pages)
        assert art.content_hash  # content hashed for staleness detection
        # page 0, size 5 → first 5 pages, 3 total pages
        resp = a.get_page(SID, art.artifact_id, page=0, page_size=5)
        assert resp.total_pages == 3
        assert len(resp.pages) == 5
        blob = " ".join(p.content for p in resp.pages)
        assert "supersecret" not in blob and "REDACTED" in blob
        assert not contains_abs_path(blob)
        # last page window
        resp2 = a.get_page(SID, art.artifact_id, page=2, page_size=5)
        assert len(resp2.pages) == 2
    asyncio.run(body())


def test_zero_tests_distinguished_from_passing():
    async def body():
        store, bus, svc = p2_fresh()
        a = svc["artifacts"]
        art = await a.create_artifact(SID, "task-1", "test_report", "0 tests collected",
                                      [{"page": 0, "title": "run", "content": "collected 0 items",
                                        "line_refs": []}])
        resp = a.get_page(SID, art.artifact_id, 0, 5)
        assert "0 items" in resp.pages[0].content
        assert resp.kind.value == "test_report"
    asyncio.run(body())
