"""rc25: open the program the agents built in a window on the PC, so the headset's screen panel can show it and the
user can tap it (owner: "make an app that does X and then we can see it in MuseDesk").

Generic: nothing here knows what the program does. The target is the newest restricted-workspace folder of the
session (a Manager plan folder or a board task's folder) unless a plan / task is named. Inside it, the module that
defines ``build_ui(ui)`` is preferred (the program draws its own window from ``app_window_host``'s small toolkit),
otherwise one that defines ``main(argv)`` (a run panel shows its printed output).

Trust (same honest limits as the restricted workspace's pytest runs):
- Every program file in the folder must pass ``sandbox_work.check_name`` / ``check_python`` again right before the
  launch; one refused file means nothing is opened.
- The window process is this interpreter, isolated (-I -B), started in the folder, with the credential-free
  environment the test runs get. It is a child process on the PC, not an operating-system sandbox.
- One app window at a time. It closes from its own Close key, from the headset, when a new one opens, and when the
  backend stops.
- Only when the backend was started with MUSEDESK_APP_WINDOWS=true (scripts/start_wall_backend.ps1 sets it with the
  screen panel; -NoScreen leaves both off) and only on Windows.
"""
from __future__ import annotations

import ast
import atexit
import os
import subprocess
import sys
import threading
import time
from collections import deque
from dataclasses import dataclass, field
from pathlib import Path

from ..workspace_guard import _child_env
from .sandbox_work import check_name, check_python

ENV_FLAG = "MUSEDESK_APP_WINDOWS"
HOST = Path(__file__).resolve().with_name("app_window_host.py")
READY_WAIT_SECONDS = 8.0


def enabled() -> bool:
    return os.environ.get(ENV_FLAG, "").strip().lower() in ("1", "true", "yes", "on") and sys.platform == "win32"


class LaunchError(Exception):
    """Why nothing was opened, in words for the headset."""


@dataclass
class Target:
    folder: Path
    module: str
    kind: str          # "ui" (build_ui) | "run" (main)
    label: str


def _defines(source: str) -> set[str]:
    try:
        tree = ast.parse(source)
    except SyntaxError:
        return set()
    return {n.name for n in tree.body if isinstance(n, (ast.FunctionDef, ast.AsyncFunctionDef))}


def program_files(folder: Path) -> list[Path]:
    return sorted(p for p in folder.glob("*.py") if p.is_file() and not p.name.startswith("test_"))


def find_module(folder: Path) -> tuple[str, str] | None:
    """(module name, kind) of the program to open, or None. ``build_ui`` wins over ``main``; ties go to the file
    changed last."""
    best: tuple[int, float, str, str] | None = None
    for p in program_files(folder):
        try:
            names = _defines(p.read_text(encoding="utf-8"))
        except (OSError, UnicodeDecodeError):
            continue
        rank = 2 if "build_ui" in names else 1 if "main" in names else 0
        if rank == 0:
            continue
        key = (rank, p.stat().st_mtime, p.stem, "ui" if rank == 2 else "run")
        if best is None or key[:2] > best[:2]:
            best = key
    return (best[2], best[3]) if best else None


def check_folder(folder: Path) -> list[str]:
    """Reasons the folder's program files must not run (empty = allowed): the restricted workspace's own checks."""
    files = program_files(folder)
    locals_ = {p.stem for p in folder.glob("*.py")}
    problems: list[str] = []
    for p in files:
        why = check_name(p.name, False)
        if why:
            problems.append(why)
            continue
        try:
            src = p.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            problems.append(f"{p.name}: could not be read")
            continue
        problems += check_python(p.name, src, locals_)
    return problems[:6]


@dataclass
class _Running:
    proc: subprocess.Popen
    target: Target
    started: float
    hwnd: int = 0
    lines: deque = field(default_factory=lambda: deque(maxlen=40))
    ready: threading.Event = field(default_factory=threading.Event)


class AppLauncher:
    def __init__(self, python: str | None = None, host: Path | None = None) -> None:
        self._python = python or sys.executable
        self._host = host or HOST
        self._lock = threading.Lock()
        self._run: _Running | None = None
        atexit.register(self.close)

    # ───────────────────────── state ─────────────────────────
    def running(self) -> _Running | None:
        r = self._run
        if r is not None and r.proc.poll() is not None:
            return None
        return r

    def status(self) -> dict:
        r = self.running()
        if r is None:
            return {"running": False}
        return {"running": True, "label": r.target.label, "module": r.target.module, "kind": r.target.kind,
                "ready": r.ready.is_set(), "last": list(r.lines)[-5:]}

    def hwnd(self) -> int:
        r = self.running()
        return r.hwnd if r is not None else 0

    # ───────────────────────── launch / close ─────────────────────────
    def launch(self, folder: Path, label: str) -> dict:
        folder = Path(folder).resolve()
        if not folder.is_dir():
            raise LaunchError("Nothing has been built yet.")
        found = find_module(folder)
        if found is None:
            raise LaunchError("The work has no program to open (no build_ui(ui) or main(argv)).")
        problems = check_folder(folder)
        if problems:
            raise LaunchError("Not opened: the restricted workspace rules refuse it (" + "; ".join(problems)[:200] + ").")
        target = Target(folder, found[0], found[1], label)
        self.close()
        cmd = [self._python, "-I", "-B", str(self._host), "--folder", str(folder), "--module", target.module,
               "--title", label[:60]]
        flags = 0x08000000 if sys.platform == "win32" else 0          # CREATE_NO_WINDOW: no console, the Tk window only
        proc = subprocess.Popen(cmd, cwd=str(folder), env=_child_env(), stdin=subprocess.PIPE,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8",
                                errors="replace", bufsize=1, creationflags=flags)
        run = _Running(proc, target, time.monotonic())
        with self._lock:
            self._run = run
        threading.Thread(target=self._read, args=(run,), name="app-window-out", daemon=True).start()
        run.ready.wait(READY_WAIT_SECONDS)
        if proc.poll() is not None and not run.ready.is_set():
            tail = " ".join(list(run.lines)[-3:])[:200]
            raise LaunchError("The program's window did not open" + (": " + tail if tail else "."))
        return self.status()

    def close(self) -> bool:
        with self._lock:
            run, self._run = self._run, None
        if run is None or run.proc.poll() is not None:
            return False
        try:
            run.proc.stdin.write("close\n")
            run.proc.stdin.flush()
            run.proc.wait(2.0)
        except Exception:  # noqa: BLE001 - it did not close politely: end it
            run.proc.kill()
        return True

    def send(self, line: str) -> bool:
        r = self.running()
        if r is None:
            return False
        try:
            r.proc.stdin.write(line.replace("\n", " ") + "\n")
            r.proc.stdin.flush()
            return True
        except (OSError, ValueError):
            return False

    def tap(self, u: float, v: float) -> bool:
        return self.send(f"tap {float(u):.4f} {float(v):.4f}")

    def type_text(self, text: str) -> bool:
        return self.send("type " + str(text)[:200])

    def _read(self, run: _Running) -> None:
        for line in run.proc.stdout:
            line = line.rstrip()
            if line.startswith("READY"):
                try:
                    run.hwnd = int(line.split()[1])
                except (IndexError, ValueError):
                    run.hwnd = 0
                run.ready.set()
            run.lines.append(line[:300])


_launcher: AppLauncher | None = None


def launcher() -> AppLauncher:
    global _launcher
    if _launcher is None:
        _launcher = AppLauncher()
    return _launcher


def client_rect(hwnd: int) -> tuple[int, int, int, int] | None:
    """The window's inside on the screen in physical pixels (x, y, w, h), or None when it is gone or minimised."""
    if not hwnd or sys.platform != "win32":
        return None
    import ctypes
    from ctypes import wintypes
    user32 = ctypes.windll.user32
    if not user32.IsWindow(hwnd) or user32.IsIconic(hwnd) or not user32.IsWindowVisible(hwnd):
        return None
    rc = wintypes.RECT()
    if not user32.GetClientRect(hwnd, ctypes.byref(rc)):
        return None
    pt = wintypes.POINT(0, 0)
    user32.ClientToScreen(hwnd, ctypes.byref(pt))
    w, h = rc.right - rc.left, rc.bottom - rc.top
    if w < 40 or h < 40:
        return None
    return pt.x, pt.y, w, h
