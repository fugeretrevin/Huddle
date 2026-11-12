"""Pure parsers that turn RAW sandboxed-worker output into structured, bounded
artifact material (MASTER_PHASE2_IMPLEMENTATION.md §5, §11.4).

Nothing here executes code, spawns a process or touches the network — it is string
analysis over output the Phase 1 ``WorkspaceGuard`` already produced inside the
sandbox. Path privacy, ANSI stripping and secret redaction are applied later by
``sanitize.sanitize_text`` when the artifact pages are built; these functions only
measure structure (file names, +/- counts, pass/fail/skip, failing test names).
"""
from __future__ import annotations

import difflib
import re
from dataclasses import dataclass, field


# ─────────────────────── diffs ───────────────────────
@dataclass
class FileDiff:
    path: str
    additions: int = 0
    deletions: int = 0
    body: str = ""


def compute_unified_diff(old_text: str, new_text: str, path: str) -> str:
    """Build a genuine unified diff between two file revisions (sandbox-relative
    ``path``). Deterministic; used to capture a real before/after change."""
    old_lines = old_text.splitlines(keepends=True)
    new_lines = new_text.splitlines(keepends=True)
    diff = difflib.unified_diff(old_lines, new_lines,
                                fromfile=f"a/{path}", tofile=f"b/{path}", lineterm="")
    return "\n".join(diff)


def _strip_file_marker(line: str) -> str:
    # "+++ b/pkg/mod.py\t2026-..."  ->  "pkg/mod.py"
    rest = line[4:].strip()
    rest = rest.split("\t", 1)[0].strip()
    if rest.startswith(("a/", "b/")):
        rest = rest[2:]
    return rest


def parse_unified_diff(diff_text: str) -> list[FileDiff]:
    """Split a (possibly multi-file) unified / ``git diff`` into per-file entries
    with real added/deleted line counts. Robust to both ``diff --git`` headers and
    bare ``---``/``+++`` file markers."""
    files: list[FileDiff] = []
    current: FileDiff | None = None
    lines: list[str] = []

    def flush() -> None:
        nonlocal current
        if current is not None:
            current.body = "\n".join(lines)
            files.append(current)

    for raw in diff_text.splitlines():
        if raw.startswith("diff --git"):
            flush()
            m = re.search(r"b/(\S+)\s*$", raw)
            current = FileDiff(path=(m.group(1) if m else "unknown"))
            lines = [raw]
            continue
        if raw.startswith("+++ "):
            path = _strip_file_marker(raw)
            if current is None:
                current = FileDiff(path=path or "unknown")
                lines = []
            else:
                # A new file marker inside a multi-file bare diff (no git header).
                if current.body or current.additions or current.deletions or any(
                    l.startswith(("+", "-")) and not l.startswith(("+++", "---"))
                    for l in lines
                ):
                    flush()
                    current = FileDiff(path=path or "unknown")
                    lines = []
                else:
                    current.path = path or current.path
            lines.append(raw)
            continue
        if current is not None:
            lines.append(raw)
            if raw.startswith("+") and not raw.startswith("+++"):
                current.additions += 1
            elif raw.startswith("-") and not raw.startswith("---"):
                current.deletions += 1
    flush()
    return files


# ─────────────────────── pytest output ───────────────────────
@dataclass
class TestSummary:
    passed: int = 0
    failed: int = 0
    skipped: int = 0
    errors: int = 0
    collected: int | None = None
    ran: bool = False
    failed_tests: list[str] = field(default_factory=list)

    @property
    def total(self) -> int:
        return self.passed + self.failed + self.skipped + self.errors


def _count(text: str, pattern: str) -> int:
    m = re.search(pattern, text)
    return int(m.group(1)) if m else 0


def parse_pytest_output(output: str) -> TestSummary:
    """Extract pass/fail/skip/error counts and failing test ids from raw pytest
    output. Critically distinguishes a run that *collected 0 tests* (``ran`` False)
    from a run where tests actually executed (``ran`` True)."""
    s = TestSummary()
    s.passed = _count(output, r"(\d+) passed")
    s.failed = _count(output, r"(\d+) failed")
    s.skipped = _count(output, r"(\d+) skipped")
    s.errors = _count(output, r"(\d+) error")

    m = re.search(r"collected (\d+) item", output)
    if m:
        s.collected = int(m.group(1))

    # Failing test node ids, e.g. "FAILED test_x.py::test_name - AssertionError: ...".
    for m in re.finditer(r"^(?:FAILED|ERROR)\s+(\S+)", output, flags=re.MULTILINE):
        node = m.group(1)
        if node not in s.failed_tests:
            s.failed_tests.append(node)

    zero_run = (s.collected == 0) or bool(re.search(r"no tests ran", output))
    s.ran = (s.total > 0) and not zero_run
    return s
