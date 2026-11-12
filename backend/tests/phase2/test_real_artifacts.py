"""Real sandboxed-worker artifact ingest tests (MASTER §5, §13.1, §13.3).

These exercise GENUINE output: a real unified diff of a sample-repo file and REAL
pytest runs via the Phase 1 WorkspaceGuard (actual subprocess), proving the v2
ArtifactService captures correct file names / +/- counts, distinguishes a
0-collected run from a passing run, surfaces the failed assertion, and that the
sanitizer strips absolute host paths and masks token-like values.
"""
from __future__ import annotations

import asyncio

from app.phase2.artifacts import ArtifactService
from app.phase2.diffing import compute_unified_diff, parse_unified_diff
from app.phase2.events import Phase2EventBus
from app.phase2.sanitize import contains_abs_path
from app.phase2.store import Phase2Store
from app.storage import Storage
from app.workspace_guard import WorkspaceGuard
from app.workers import DeveloperWorker

SID = "ses-real"


def _svc(ws_root: str):
    storage = Storage(":memory:")
    store = Phase2Store(storage)
    bus = Phase2EventBus(store)
    return ArtifactService(store, bus, "demo", workspace_root=ws_root)


def test_real_sample_diff_has_file_names_counts_and_no_abs_paths(tmp_path):
    async def body():
        guard = WorkspaceGuard(str(tmp_path))
        ws = guard.create_workspace("diffsess")
        ws_abs = str(ws)
        old = guard.read_file(ws, "greeter.py")
        # A real, mixed change: one replacement (+1/-1) + an appended function, and a
        # comment that leaks the ABSOLUTE workspace path (must be scrubbed on ingest).
        new = old.replace('"Sign in to continue."', '"Please sign in to continue."')
        new += f"\n\n# generated in {ws_abs}\ndef farewell(name):\n    return f'Bye, {{name}}!'\n"
        guard.write_file(ws, "greeter.py", new)

        diff_text = compute_unified_diff(old, new, "greeter.py")
        parsed = parse_unified_diff(diff_text)
        assert len(parsed) == 1 and parsed[0].path == "greeter.py"
        exp_add, exp_del = parsed[0].additions, parsed[0].deletions
        assert exp_add >= 2 and exp_del >= 1  # genuine additions and a deletion

        svc = _svc(ws_abs)
        art = await svc.ingest_code_diff(SID, "t-dev", diff_text, source_mode="live")
        assert art.kind.value == "code_diff"
        assert art.source_mode == "live"          # genuinely executed output
        titles = [p.title for p in art.pages]
        assert "greeter.py" in titles
        joined_refs = " ".join(r for p in art.pages for r in p.line_refs)
        assert f"stat:greeter.py:+{exp_add}/-{exp_del}" in joined_refs
        # NO absolute host path survives anywhere in the stored artifact.
        resp = svc.get_page(SID, art.artifact_id, 0, 10)
        blob = " ".join(p.content for p in resp.pages)
        assert ws_abs not in blob
        assert not contains_abs_path(blob)
        assert art.summary.startswith("1 file(s) changed")
    asyncio.run(body())


def test_developer_worker_written_files_ingest_as_live_diff(tmp_path):
    """The actual DeveloperWorker writes real files; capture them as a live diff."""
    async def body():
        guard = WorkspaceGuard(str(tmp_path))
        ws = guard.create_workspace("devsess")
        dev = DeveloperWorker().implement_oauth(guard, ws)
        parts = [compute_unified_diff("", guard.read_file(ws, rel), rel)
                 for rel in dev.files_changed]
        diff_text = "\n".join(parts)
        svc = _svc(str(ws))
        art = await svc.ingest_code_diff(SID, "t-dev", diff_text, source_mode="live")
        titles = [p.title for p in art.pages]
        assert "oauth_login.py" in titles and "test_oauth_login.py" in titles
        assert art.summary.startswith("2 file(s) changed")
        resp = svc.get_page(SID, art.artifact_id, 0, 10)
        assert not contains_abs_path(" ".join(p.content for p in resp.pages))
    asyncio.run(body())


def test_zero_run_distinguished_from_passing(tmp_path):
    async def body():
        # Passing run: the real sample template has 3 passing tests.
        guard = WorkspaceGuard(str(tmp_path / "pass"))
        ws = guard.create_workspace("passsess")
        passing = guard.run_tests(ws, timeout=90)
        assert passing.passed >= 1 and passing.failed == 0

        # Zero-run: an empty workspace collects no tests.
        empty = tmp_path / "empty"
        empty.mkdir()
        guard2 = WorkspaceGuard(str(empty))
        zero = guard2.run_tests(empty, timeout=90)

        svc = _svc(str(ws))
        art_pass = await svc.ingest_test_report(SID, "t-test", passing.output,
                                                source_mode="live")
        art_zero = await svc.ingest_test_report(SID, "t-test", zero.output,
                                                source_mode="live")
        pass_page = svc.get_page(SID, art_pass.artifact_id, 0, 5).pages[0].content
        zero_page = svc.get_page(SID, art_zero.artifact_id, 0, 5).pages[0].content

        assert "tests_ran: true" in pass_page
        assert "passed" in art_pass.summary
        assert "tests_ran: false" in zero_page
        assert "0 tests ran" in art_zero.summary
        # The two are clearly NOT the same outcome.
        assert art_pass.summary != art_zero.summary
    asyncio.run(body())


def test_failed_run_shows_failing_test_and_assertion(tmp_path):
    async def body():
        guard = WorkspaceGuard(str(tmp_path))
        ws = guard.create_workspace("failsess")
        # Inject a genuinely failing test; run REAL pytest.
        guard.write_file(ws, "test_broken.py",
                         "def test_redirect_contract():\n"
                         "    expected = 302\n"
                         "    got = 403\n"
                         "    assert got == expected, 'redirect contract violated'\n")
        res = guard.run_tests(ws, timeout=90)
        assert res.failed >= 1

        svc = _svc(str(ws))
        art = await svc.ingest_test_report(SID, "t-test", res.output, source_mode="live")
        assert "failed" in art.summary
        page = svc.get_page(SID, art.artifact_id, 0, 5).pages[0]
        assert "test_broken.py::test_redirect_contract" in page.content
        assert "AssertionError" in page.content or "redirect contract" in page.content
        # The failing node id is a stable line reference.
        assert any("test_broken.py::test_redirect_contract" in r for r in page.line_refs)
        assert not contains_abs_path(page.content)
    asyncio.run(body())


def test_sanitizer_masks_token_like_values_in_test_report(tmp_path):
    async def body():
        svc = _svc(str(tmp_path))
        poisoned = ("collected 1 item\n\ntest_x.py .\n\n"
                    "log: api_key=supersecretvalue123456 Authorization: Bearer abcdef1234567890TOKEN\n"
                    "1 passed in 0.01s\n")
        art = await svc.ingest_test_report(SID, "t-test", poisoned, source_mode="live")
        content = svc.get_page(SID, art.artifact_id, 0, 5).pages[0].content
        assert "supersecretvalue123456" not in content
        assert "abcdef1234567890TOKEN" not in content
        assert "REDACTED" in content
    asyncio.run(body())
