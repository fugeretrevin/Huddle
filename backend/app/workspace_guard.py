"""Safe, isolated execution sandbox for the live workers.

Guarantees:
- Every file op is canonicalized and must resolve INSIDE the session workspace
  (rejects ``..`` traversal and symlink escape).
- The only process ever spawned is the pinned test command (this interpreter's
  ``-m pytest``) against the workspace. No arbitrary shell, no network, no deletes
  outside the workspace root.
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import re
from dataclasses import dataclass
from pathlib import Path

_TEMPLATE = Path(__file__).resolve().parents[2] / "samples" / "auth_demo_template"


class WorkspaceError(Exception):
    pass


@dataclass
class TestResult:
    passed: int
    failed: int
    returncode: int
    output: str
    timed_out: bool = False


class WorkspaceGuard:
    def __init__(self, root: str, template: Path | None = None) -> None:
        self.root = Path(root).resolve()
        self.template = (template or _TEMPLATE).resolve()
        self.root.mkdir(parents=True, exist_ok=True)

    # ─────────────────────── lifecycle ───────────────────────
    def create_workspace(self, session_id: str) -> Path:
        safe = re.sub(r"[^A-Za-z0-9_.-]", "_", session_id)[:64]
        ws = (self.root / safe).resolve()
        if (os.path.commonpath([str(ws), str(self.root)]) != str(self.root)
                or ws == self.root):
            # ws == root happens for ids like "." / "": rmtree below would wipe every
            # session's workspace.
            raise WorkspaceError("workspace path escapes root")
        if ws.exists():
            shutil.rmtree(ws)
        if not self.template.exists():
            raise WorkspaceError(f"template missing: {self.template}")
        shutil.copytree(self.template, ws)
        return ws

    def open_workspace(self, name: str) -> Path:
        """A persistent, initially empty workspace (Manager plan folders): created once with
        only a pytest.ini that anchors rootdir; never wiped, so later steps build on it."""
        safe = re.sub(r"[^A-Za-z0-9_.-]", "_", name)[:64]
        ws = (self.root / safe).resolve()
        if (os.path.commonpath([str(ws), str(self.root)]) != str(self.root)
                or ws == self.root):
            raise WorkspaceError("workspace path escapes root")
        ws.mkdir(parents=True, exist_ok=True)
        ini = ws / "pytest.ini"
        if not ini.exists():
            ini.write_text("[pytest]\n# Anchors pytest's rootdir to this sandbox folder.\n",
                           encoding="utf-8")
        return ws

    def destroy_workspace(self, ws: Path) -> None:
        ws = Path(ws).resolve()
        if os.path.commonpath([str(ws), str(self.root)]) == str(self.root) and ws != self.root:
            shutil.rmtree(ws, ignore_errors=True)

    # ─────────────────────── guarded path ops ───────────────────────
    def _resolve_in(self, ws: Path, rel: str) -> Path:
        ws = Path(ws).resolve()
        # Reject absolute paths and traversal before touching the filesystem.
        if os.path.isabs(rel) or rel.startswith(("/", "\\")):
            raise WorkspaceError(f"absolute path not allowed: {rel}")
        target = (ws / rel).resolve()
        try:
            inside = os.path.commonpath([str(target), str(ws)]) == str(ws)
        except ValueError:  # e.g. a drive-qualified path ("D:evil.py") on another drive
            inside = False
        if not inside:
            raise WorkspaceError(f"path escapes workspace: {rel}")
        return target

    def write_file(self, ws: Path, rel: str, content: str) -> Path:
        target = self._resolve_in(ws, rel)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")
        return target

    def read_file(self, ws: Path, rel: str) -> str:
        return self._resolve_in(ws, rel).read_text(encoding="utf-8")

    # ─────────────────────── the ONLY allowed command ───────────────────────
    def run_tests(self, ws: Path, timeout: int = 90) -> TestResult:
        ws = Path(ws).resolve()
        # --rootdir + the workspace's own pytest.ini isolate this run from any parent
        # project's pytest config (otherwise a parent testpaths= could collect 0 tests).
        # -I: isolated (no user site, no PYTHON* env, no cwd on sys.path); -B: never write .pyc, so a
        # file rewritten within the same second (a fix) is never shadowed by stale bytecode.
        cmd = [sys.executable, "-I", "-B", "-m", "pytest", "-q", "-p", "no:cacheprovider",
               "--rootdir", str(ws), str(ws)]
        try:
            proc = subprocess.run(
                cmd, cwd=str(ws), capture_output=True, text=True, timeout=timeout,
                env=_child_env(),
            )
        except subprocess.TimeoutExpired as e:
            partial = e.output or ""
            if isinstance(partial, bytes):  # TimeoutExpired can carry bytes even with text=True
                partial = partial.decode("utf-8", errors="replace")
            return TestResult(0, 0, -1, partial[-4000:] + "\n[timeout]", timed_out=True)
        out = (proc.stdout or "") + (proc.stderr or "")
        passed = _count(out, r"(\d+) passed")
        failed = _count(out, r"(\d+) failed") + _count(out, r"(\d+) error")
        return TestResult(passed, failed, proc.returncode, out[-4000:])


_SECRET_ENV = re.compile(r"(?i)(^MUSEDESK_|KEY|TOKEN|SECRET|PASSWORD|PASSWD|CREDENTIAL)")


def _child_env() -> dict[str, str]:
    """Environment for the sandboxed test process: the host environment minus anything
    that looks like a credential (and all MUSEDESK_* backend config, which includes the
    provider API key). Code executed in the workspace must not be able to read them."""
    env = {k: v for k, v in os.environ.items() if not _SECRET_ENV.search(k)}
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    # Only pytest itself: no third-party plugins auto-loaded from the backend's environment.
    env["PYTEST_DISABLE_PLUGIN_AUTOLOAD"] = "1"
    return env


def _count(text: str, pattern: str) -> int:
    m = re.search(pattern, text)
    return int(m.group(1)) if m else 0
