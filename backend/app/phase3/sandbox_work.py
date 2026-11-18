"""Real work for Manager plan cards (MUSEDESK_P3_EXECUTE_CODE=true).

The Developer's step makes the model write REAL files into an isolated per-plan sandbox
folder; the Tester's step makes it write REAL pytest tests for that code. The tests are then
actually run (``WorkspaceGuard.run_tests``: this interpreter's ``-m pytest``, the workspace as
cwd, a timeout, credentials removed from the environment) and the counts reported are the
counts pytest printed. When the tests fail, the Developer gets one bounded attempt to fix
the code with the real failure output, and the tests run again.

Trust model (honest limits):
- Files are only written inside ``<workspace_root>/plans/<plan_id>`` (WorkspaceGuard path
  checks), at the top level, with a short name and an allowed extension.
- Before ANY run, every .py file is checked statically: only a list of harmless standard
  modules (and the folder's own modules) may be imported, and process / file / network /
  reflection builtins and dunder attributes are refused. A refused file means nothing runs.
  This check is a strong filter, not an operating-system sandbox: the run itself happens in
  a child process on the developer's PC (no network blocking, no container).
- Research steps stay model text (no tools): nothing here applies to them.
"""
from __future__ import annotations

import ast
import asyncio
import logging
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Awaitable, Callable

from ..workspace_guard import TestResult, WorkspaceGuard

MAX_FILES = 6
MAX_FILE_CHARS = 30000
MAX_CONTEXT_CHARS = 14000
MAX_OUTPUT_TAIL = 3500

ALLOWED_EXT = (".py", ".md", ".txt", ".json", ".html", ".css", ".js")
_NAME_OK = re.compile(r"^[A-Za-z_][A-Za-z0-9_-]{0,60}\.(py|md|txt|json|html|css|js)$")

# Standard modules the generated code may import (plus the folder's own modules and pytest).
ALLOWED_MODULES = frozenset({
    "abc", "base64", "binascii", "bisect", "calendar", "collections", "contextlib", "copy",
    "dataclasses", "datetime", "decimal", "difflib", "enum", "fractions", "functools",
    "hashlib", "heapq", "hmac", "html", "itertools", "json", "math", "numbers",
    "pytest", "random", "re", "secrets", "statistics", "string", "textwrap", "time",
    "typing", "unicodedata", "uuid", "warnings", "zoneinfo",
})
# Left out on purpose: operator (attrgetter/methodcaller reach any attribute by name), unittest
# (mock.patch objects hand back real modules), typing_extensions, os/sys/pathlib/io/subprocess/...
DENIED_CALLS = frozenset({
    "eval", "exec", "compile", "__import__", "open", "getattr", "setattr", "delattr",
    "globals", "locals", "vars", "breakpoint", "input", "memoryview", "exit", "quit", "help",
})
# Attributes that reach modules, frames, files or evaluated strings indirectly.
DENIED_ATTRS = frozenset({
    "importorskip", "import_module", "get_field", "get_value", "vformat", "Formatter",
    "attrgetter", "methodcaller", "get_type_hints", "ForwardRef", "_eval_type", "evaluate",
    "pluginmanager", "_importer", "chdir", "rmtree", "f_globals", "f_locals", "f_back", "f_builtins", "gi_frame",
    "cr_frame", "tb_frame", "tb_next", "mro",
})
# pytest fixtures that hand the test the file system, the import machinery or the config.
DENIED_FIXTURES = frozenset({
    "tmp_path", "tmp_path_factory", "tmpdir", "tmpdir_factory", "monkeypatch", "request",
    "pytestconfig", "pytester", "testdir", "cache", "doctest_namespace", "record_property",
    "record_testsuite_property", "record_xml_attribute",
})
# Names a sandbox file may not take: a file named like a standard module could stand in for it
# while the real one is already loaded (or the other way round).
try:
    _STDLIB = frozenset(sys.stdlib_module_names) | {"pytest", "_pytest", "pluggy", "py", "conftest"}
except AttributeError:  # Python < 3.10
    _STDLIB = frozenset({"os", "sys", "subprocess", "socket", "shutil", "pathlib", "io", "pytest"})
ALLOWED_DUNDERS = frozenset({"__name__", "__doc__", "__init__", "__post_init__", "__eq__",
                             "__repr__", "__str__", "__hash__", "__lt__", "__len__"})

RULES = (
    "Rules for the files: Python 3 code plus optional .md/.html/.css/.js/.json files, all at "
    "the top level of the folder (no sub-folders, no pytest.ini or conftest.py). Import only "
    "these standard modules: " + ", ".join(sorted(ALLOWED_MODULES - {"typing_extensions"})) +
    ", and the folder's own modules. No file, process, network or environment access; do not "
    "use eval, exec, open, getattr, setattr, globals or dunder attributes other than __init__. "
    "Do not name a file after a standard module (no email.py, string.py, html.py); do not use "
    "the pytest fixtures tmp_path, monkeypatch or request, pytest.importorskip, string "
    "conditions in skipif/xfail, or pytest_plugins. Keep it small, complete and runnable.\n"
    "Output format, exactly: for each file, a line 'FILE: <name>' followed by ONE fenced code "
    "block containing the whole file. After the last file, one line starting 'Summary:' that "
    "says what the files do in one sentence. Nothing else.")

# rc25 (owner: "make an app that does X and then we can see it in MuseDesk"): the program can show itself. MuseDesk's
# window host (app_window_host.py) calls build_ui(ui) with this small toolkit; the program never imports tkinter.
UI_RULES = (
    "When the user wants an app, a game, a window, buttons or anything they would see or tap, "
    "the main module must also define def build_ui(ui): MuseDesk opens it in a window on the "
    "PC and shows it in the user's headset, where they tap it. Do not import tkinter; use only "
    "the ui object you are given: ui.title(text); ui.label(text, size=14, bold=False, "
    "color='#24272b') returns an object with .set(text); ui.button(text, on_click) with "
    ".set(text); ui.text_input(placeholder='', on_enter=None) with .get() and .set(text); "
    "ui.text_box(height=8) with .write(line), .clear(), .set(text); ui.canvas(width, height, "
    "background='white') with .clear(), .circle(x, y, r, fill, outline), .oval(x0, y0, x1, y1, "
    "fill), .rect(x0, y0, x1, y1, fill), .line(x0, y0, x1, y1, color, width), .arc(x0, y0, x1, "
    "y1, start, extent, fill), .polygon([(x, y), ...], fill), .text(x, y, text, color, size), "
    ".on_click(fn) where fn(x, y) gets canvas pixels, and .width/.height; ui.row() gives the "
    "same calls laid out side by side; ui.every(ms, fn) repeats fn (animation); ui.after(ms, "
    "fn) runs it once. Colours are names or '#rrggbb'. Keep the logic in plain classes and "
    "functions the tests check; build_ui only wires them to the ui. Use big buttons (the user "
    "taps from a headset) and a canvas no larger than 640x420. Test build_ui too, with a small "
    "fake ui class in the test file that records the calls. ")

DEV_SYSTEM = (
    "You are the Developer on a small software team. You write REAL code for ONE plan step: "
    "your files are saved into a sandbox folder and the tests in it are run with pytest, so "
    "the code must actually work. Include a pytest file named test_<module>.py with focused "
    "tests for this step's acceptance criteria. Extend the files already in the folder when "
    "they exist (write the whole updated file). When you rewrite an existing test file, keep "
    "every test already in it and add yours. "
    # rc20 (live run, "a program that prints a triangle of stars"): the plan promised a CLI and the change asked to "let
    # the user choose the character", but __main__ ignored its arguments and always printed 5 stars.
    # sys and argparse are NOT allowed in the restricted workspace (on purpose), so the inputs go through main(argv).
    "When the code is a program the user runs, every input the step mentions (sizes, options, "
    "characters) must be a real option of def main(argv=None), parsed by hand from the list of "
    "strings (sys and argparse are not available here), and tested by calling main([...]) and "
    "checking the printed output. " + UI_RULES + RULES)

# rc19 (owner, live run on a new objective): a later step rewrote a test file and the folder went from 51 to 45 tests
# with nobody told. When a step leaves FEWER tests than the folder had before it, the Developer gets one chance to put the
# earlier tests back (it is shown the files as they were); if the count is still lower, the card says so in its result.
RESTORE_SYSTEM = (
    "You work on a small software team. Your last change to the sandbox folder "
    "removed tests that were there before. Put the earlier tests back: write the whole content "
    "of each test file you change, keeping every earlier test and the new ones. Change an "
    "earlier test only if this step's requested change makes it wrong. " + RULES)

TEST_SYSTEM = (
    "You are the Tester on a small software team. Write REAL pytest tests for the code that "
    "is already in the sandbox folder, checking this step's acceptance criteria with concrete "
    "inputs and expected results, including edge cases. Only write test files (names start "
    "with test_ and end with .py); never change the code under test. The tests will be run "
    "and their real results reported, so test what the code really does and what the criteria "
    "require. Never delete existing tests: add new test files, or keep every test already in a "
    "file you rewrite. " + RULES)

FIX_SYSTEM = (
    "You are the Developer on a small software team. The tests in the sandbox folder were run "
    "and did not all pass. Fix the CODE so the tests pass; change a test only if it clearly "
    "contradicts the acceptance criteria. Write the whole content of each file you change. "
    + RULES)

_FILE_BLOCK = re.compile(r"^FILE:\s*`?([^\s`]+)`?\s*\n+```[^\n]*\n(.*?)\n```", re.M | re.S)


@dataclass
class ParsedFile:
    name: str
    content: str


@dataclass
class SandboxOutcome:
    ok: bool                      # the card completed (tests passed, or no tests and files written)
    summary: str                  # one line for the card (<= 280)
    pages: list[dict[str, Any]]   # artifact pages
    kind: str                     # code_diff | test_report
    fail_code: str = ""
    fail_reason: str = ""
    files: list[str] = field(default_factory=list)
    passed: int = 0
    failed: int = 0
    tests_ran: bool = False
    attempts: int = 1
    tests_before: int = -1        # tests the folder had before this step (-1: unknown / first run)
    tests_dropped: int = 0        # how many fewer tests the folder has after this step (0: none lost)


def parse_files(text: str) -> tuple[list[ParsedFile], str]:
    """The model's files and its Summary line ('' when missing)."""
    files: list[ParsedFile] = []
    seen: set[str] = set()
    for m in _FILE_BLOCK.finditer(text or ""):
        name = m.group(1).strip().lstrip("./")
        if name in seen:
            files = [f for f in files if f.name != name]
        seen.add(name)
        files.append(ParsedFile(name, m.group(2).rstrip() + "\n"))
    summary = ""
    for line in reversed((text or "").splitlines()):
        s = line.strip()
        if s.lower().startswith("summary:"):
            summary = s[8:].strip()
            break
    return files, summary


def check_name(name: str, tests_only: bool) -> str | None:
    """A reason the file name is refused, or None."""
    if not _NAME_OK.match(name):
        return f"{name}: only short top-level names with {', '.join(ALLOWED_EXT)} are allowed"
    if name.lower() in ("conftest.py", "pytest.ini", "setup.py", "sitecustomize.py",
                        "usercustomize.py"):
        return f"{name}: this file name is reserved"
    if tests_only and not (name.startswith("test_") and name.endswith(".py")):
        return f"{name}: the Tester only writes test_*.py files"
    if name.endswith(".py") and name[:-3] in _STDLIB:
        return f"{name}: named like the standard module {name[:-3]!r}; choose another name"
    return None


def check_python(name: str, source: str, local_modules: set[str]) -> list[str]:
    """Reasons a Python file must not run (empty = allowed). A syntax error is not refused:
    such a file cannot execute anything and pytest reports the error honestly."""
    try:
        tree = ast.parse(source)
    except SyntaxError:
        return []
    problems: list[str] = []

    def mod_ok(mod: str | None) -> bool:
        top = (mod or "").split(".")[0]
        return top in ALLOWED_MODULES or top in local_modules

    for node in ast.walk(tree):
        line = getattr(node, "lineno", 0)
        if isinstance(node, ast.Import):
            for a in node.names:
                if not mod_ok(a.name):
                    problems.append(f"{name}:{line} imports {a.name}")
        elif isinstance(node, ast.ImportFrom):
            if node.level == 0 and not mod_ok(node.module):
                problems.append(f"{name}:{line} imports {node.module}")
            for a in node.names:
                if a.name in DENIED_ATTRS or a.name in DENIED_CALLS:
                    problems.append(f"{name}:{line} imports {a.name}")
        elif isinstance(node, ast.Name):
            # Called or merely referenced ("f = getattr"): either way it is refused.
            if node.id in DENIED_CALLS or node.id in ("__builtins__", "__loader__", "__spec__"):
                problems.append(f"{name}:{line} uses {node.id}")
            elif node.id == "pytest_plugins" and isinstance(node.ctx, ast.Store):
                problems.append(f"{name}:{line} sets pytest_plugins")
        elif isinstance(node, ast.Attribute):
            a = node.attr
            if (a.startswith("__") and a.endswith("__") and a not in ALLOWED_DUNDERS) or a in DENIED_ATTRS:
                problems.append(f"{name}:{line} uses .{a}")
        elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.Lambda)):
            args = node.args
            for arg in args.posonlyargs + args.args + args.kwonlyargs:
                if arg.arg in DENIED_FIXTURES:
                    problems.append(f"{name}:{line} uses the pytest fixture {arg.arg}")
        elif isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute) \
                and node.func.attr in ("skipif", "xfail"):
            conds = list(node.args[:1]) + [k.value for k in node.keywords if k.arg == "condition"]
            if any(isinstance(c, ast.Constant) and isinstance(c.value, str) for c in conds):
                problems.append(f"{name}:{line} gives {node.func.attr} a string condition (pytest evaluates it)")
    return problems[:8]


def _local_modules(ws: Path, extra: list[ParsedFile]) -> set[str]:
    names = {p.stem for p in ws.glob("*.py")}
    names |= {f.name[:-3] for f in extra if f.name.endswith(".py")}
    return names


def workspace_listing(ws: Path, budget: int = MAX_CONTEXT_CHARS) -> str:
    """The folder's current files for the prompt (bounded)."""
    out, used = [], 0
    for p in sorted(ws.iterdir()):
        if not p.is_file() or p.name == "pytest.ini" or p.suffix not in ALLOWED_EXT:
            continue
        try:
            body = p.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            continue
        chunk = f"FILE: {p.name}\n```\n{body}\n```"
        if used + len(chunk) > budget:
            out.append(f"FILE: {p.name} (not shown: the prompt budget is used up)")
            continue
        out.append(chunk)
        used += len(chunk)
    return "\n\n".join(out) or "(the folder is empty)"


def _clip(s: str, n: int) -> str:
    return s if len(s) <= n else s[: n - 1] + "…"


log = logging.getLogger("musedesk.phase3.sandbox")

Complete = Callable[[str, str], Awaitable[str]]     # (system, user prompt) -> model text


class SandboxWork:
    """Runs one Developer or Tester plan card for real. Never raises for model output
    problems: they come back as a failed outcome with an honest reason."""

    def __init__(self, workspace_root: str, test_timeout: int = 60, fix_attempts: int = 1,
                 subdir: str = "plans") -> None:
        # ``subdir``: "plans" for Manager plan folders (one per plan), "tasks" for cards the user
        # assigned directly (one folder per card, see PlanRunner._adhoc_folder).
        self.guard = WorkspaceGuard(str(Path(workspace_root) / subdir))
        self.test_timeout = int(test_timeout)
        self.fix_attempts = max(0, int(fix_attempts))
        self._tests_seen: dict[str, int] = {}     # plan id -> tests in its folder after the last real run

    def folder(self, plan_id: str) -> Path:
        return self.guard.open_workspace(plan_id)

    # ─────────────────────── one card ───────────────────────
    async def run(self, agent: str, plan_id: str, step_prompt: str, complete: Complete) -> SandboxOutcome:
        ws = self.folder(plan_id)
        tests_only = agent == "tester"
        before_tests = _read_tests(ws)                 # the test files as they were before this step
        prior = self._tests_seen.get(plan_id)          # tests after the last real run in this folder
        system = TEST_SYSTEM if tests_only else DEV_SYSTEM
        prompt = (step_prompt + "\n\nThe sandbox folder now contains:\n" + workspace_listing(ws))
        text = await complete(system, prompt)
        files, summary = parse_files(text)
        if not files:
            text = await _try(complete, system, prompt + "\n\nYour previous answer had no files in the "
                              "required format. Answer again: 'FILE: <name>' lines, each "
                              "followed by one fenced code block, then 'Summary:'.")
            files, summary = parse_files(text or "")
        if not files:
            return self._failed("NO_CODE", "The model returned no files in the required format, "
                                "so nothing was written or run.", ws, [], kind_for(agent))

        written, notes, problems = self._write(ws, files, tests_only)
        if problems:
            # One chance to rewrite within the rules; otherwise nothing runs.
            text = await _try(complete, system, prompt + "\n\nThese files were refused by the sandbox "
                              "rules, so nothing ran:\n- " + "\n- ".join(problems) +
                              "\nWrite them again within the rules.")
            files2, summary2 = parse_files(text or "")
            w2, n2, problems = self._write(ws, files2, tests_only)
            written = sorted(set(written) | set(w2))
            notes += n2
            summary = summary2 or summary
            if problems:
                # Refused files were never written (an earlier, allowed version may remain).
                return self._failed("SANDBOX_REFUSED", "The sandbox rules refused the code ("
                                    + _clip("; ".join(problems), 160) + "); nothing was run.",
                                    ws, written, kind_for(agent), notes)
        if not written:
            return self._failed("NO_CODE", "No file passed the sandbox's name rules; nothing "
                                "was written or run.", ws, [], kind_for(agent), notes)

        has_tests = any(p.name.startswith("test_") for p in ws.glob("test_*.py"))
        if not has_tests:
            return self._outcome(True, agent, ws, written, None, summary, notes, 1)

        result = await asyncio.to_thread(self.guard.run_tests, ws, self.test_timeout)
        attempts = 1
        first = result
        while not _clean(result) and attempts <= self.fix_attempts:
            attempts += 1
            fix_prompt = (step_prompt + "\n\nThe sandbox folder now contains:\n" +
                          workspace_listing(ws) + "\n\npytest output (tail):\n" +
                          result.output[-MAX_OUTPUT_TAIL:])
            text = await _try(complete, FIX_SYSTEM, fix_prompt)
            if text is None:
                # The model gave no usable answer (timeout, empty, refused): the last REAL result stands.
                notes.append(f"Developer fix attempt {attempts - 1}: the model gave no usable answer; "
                             "the last real test result stands")
                break
            fixes, _ = parse_files(text)
            w3, n3, bad = self._write(ws, fixes, False)
            notes += n3 + [f"Developer fix attempt {attempts - 1}: rewrote "
                           + (", ".join(w3) if w3 else "nothing")]
            if bad:
                notes.append("Refused in the fix: " + _clip("; ".join(bad), 200))
            written = sorted(set(written) | set(w3))
            result = await asyncio.to_thread(self.guard.run_tests, ws, self.test_timeout)

        # Fewer tests than before this step: one chance to restore them, then say so on the card.
        if _dropped(prior, result) > 0:
            lost = _dropped(prior, result)
            changed = {n: s for n, s in before_tests.items() if _read_tests(ws).get(n) != s}
            if changed:
                earlier = "\n\n".join(f"FILE (before this step): {n}\n```python\n{s[:5000]}\n```" for n, s in changed.items())
                text = await _try(complete, RESTORE_SYSTEM, step_prompt + "\n\nThe sandbox folder now contains:\n"
                                  + workspace_listing(ws) + f"\n\nBefore this step the folder had {prior} tests; "
                                  f"now it has {_total(result)} ({lost} fewer). The earlier versions of the test "
                                  "files this step rewrote:\n\n" + earlier)
                if text is not None:
                    fixes, _ = parse_files(text)
                    w4, n4, bad = self._write(ws, [f for f in fixes if f.name.startswith("test_")], True)
                    notes += n4 + [f"{lost} earlier test(s) were missing: the Developer was asked to restore them "
                                   "and rewrote " + (", ".join(w4) if w4 else "nothing")]
                    if bad:
                        notes.append("Refused in the restore: " + _clip("; ".join(bad), 200))
                    if w4:
                        written = sorted(set(written) | set(w4))
                        result = await asyncio.to_thread(self.guard.run_tests, ws, self.test_timeout)
        dropped = _dropped(prior, result)
        if dropped > 0:
            notes.append(f"This step left {dropped} fewer test(s) than the folder had before it "
                         f"({prior} -> {_total(result)}).")
        out = self._outcome(_clean(result), agent, ws, written, result, summary, notes, attempts)
        out.tests_before = prior if prior is not None else -1
        out.tests_dropped = dropped
        if dropped > 0:
            out.summary = _clip(out.summary + f" · {dropped} fewer tests than before ({prior} -> {_total(result)})", 280)
            if not out.ok:
                out.fail_reason = out.summary
        if not result.timed_out and result.returncode in (0, 1):
            self._tests_seen[plan_id] = _total(result)
        if attempts > 1:
            out.pages.insert(1, {"page": 1, "title": "First run (before the fix)",
                                 "content": _test_header(first) + "\n\n" + first.output[-6000:],
                                 "line_refs": []})
            for i, p in enumerate(out.pages):
                p["page"] = i
        return out

    # ─────────────────────── helpers ───────────────────────
    def _write(self, ws: Path, files: list[ParsedFile], tests_only: bool) -> tuple[list[str], list[str], list[str]]:
        return write_checked(self.guard, ws, files, tests_only)


    def _outcome(self, ok: bool, agent: str, ws: Path, written: list[str],
                 result: TestResult | None, summary: str, notes: list[str], attempts: int) -> SandboxOutcome:
        files_txt = ", ".join(written)
        if result is None:
            head = f"Code written: {files_txt} · no tests in the folder yet"
            line = "No tests were run (the folder has no test files yet)."
        else:
            counts = _counts(result)
            head = (f"Tests run: {counts}" if agent == "tester" else f"Code written: {files_txt} · pytest {counts}")
            line = _test_header(result)
        if attempts > 1:
            head += " (after 1 fix)" if attempts == 2 else f" (after {attempts - 1} fixes)"
        pages = [{"page": 0, "title": "Result (restricted workspace: real files, real pytest run)",
                  "content": "\n".join([
                      "Written by the model into a restricted workspace folder and run with "
                      "pytest on the PC. Counts are pytest's own.",
                      "", line, "", "Files written this step: " + (files_txt or "none"),
                      *(["", "Notes:"] + [f"- {n}" for n in notes] if notes else []),
                      *(["", "Summary: " + summary] if summary else [])]),
                  "line_refs": [f"file:{n}" for n in written]}]
        for name in written:
            try:
                body = (ws / name).read_text(encoding="utf-8")
            except OSError:
                continue
            pages.append({"page": len(pages), "title": name, "content": body[:11000],
                          "line_refs": [f"file:{name}"]})
        if result is not None:
            pages.append({"page": len(pages), "title": "pytest output",
                          "content": result.output[-11000:], "line_refs": []})
        out = SandboxOutcome(ok=ok, summary=_clip(head, 280), pages=pages, kind=kind_for(agent),
                             files=written, attempts=attempts)
        if result is not None:
            out.passed, out.failed, out.tests_ran = result.passed, result.failed, True
            if not ok:
                out.fail_code = "TESTS_TIMED_OUT" if result.timed_out else "TESTS_FAILED"
                out.fail_reason = _clip(head, 280)
        return out

    def _failed(self, code: str, reason: str, ws: Path, written: list[str], kind: str,
                notes: list[str] | None = None) -> SandboxOutcome:
        pages = [{"page": 0, "title": "Not run", "content": reason + (
            "\n\nNotes:\n" + "\n".join(f"- {n}" for n in notes) if notes else ""), "line_refs": []}]
        return SandboxOutcome(ok=False, summary=_clip(reason, 280), pages=pages, kind=kind,
                              fail_code=code, fail_reason=_clip(reason, 280), files=written)


def kind_for(agent: str) -> str:
    return "test_report" if agent == "tester" else "code_diff"


def _total(r: TestResult) -> int:
    return r.passed + r.failed


def _dropped(prior: int | None, r: TestResult) -> int:
    """How many fewer tests the folder has than after the last real run (0 when unknown, timed out or not fewer)."""
    if prior is None or r.timed_out or r.returncode not in (0, 1):
        return 0
    return max(0, prior - _total(r))


def _read_tests(ws: Path) -> dict[str, str]:
    out: dict[str, str] = {}
    for p in sorted(ws.glob("test_*.py")):
        try:
            out[p.name] = p.read_text(encoding="utf-8")
        except OSError:
            continue
    return out


def _clean(r: TestResult) -> bool:
    return (not r.timed_out) and r.failed == 0 and r.returncode == 0 and r.passed > 0


def _counts(r: TestResult) -> str:
    if r.timed_out:
        return "timed out"
    if r.passed == 0 and r.failed == 0:
        return "0 tests ran" if r.returncode in (0, 5) else f"did not run (exit code {r.returncode})"
    return f"{r.passed} passed, {r.failed} failed"


def _test_header(r: TestResult) -> str:
    return f"pytest: {_counts(r)} (exit code {r.returncode})"


def write_checked(guard: WorkspaceGuard, ws: Path, files: list[ParsedFile],
                  tests_only: bool) -> tuple[list[str], list[str], list[str]]:
    """Write the files that pass the name and static checks. Returns (written, notes,
    problems); a refused file is never written."""
    written: list[str] = []
    notes: list[str] = []
    problems: list[str] = []
    locals_ = _local_modules(ws, files)
    for f in files[:MAX_FILES]:
        why = check_name(f.name, tests_only)
        if why:
            if "standard module" in why:
                problems.append(why)          # the model can fix it: the retry prompt names it
            else:
                notes.append("Skipped " + why)
            continue
        if len(f.content) > MAX_FILE_CHARS:
            notes.append(f"Skipped {f.name}: longer than {MAX_FILE_CHARS} characters")
            continue
        if f.name.endswith(".py"):
            bad = check_python(f.name, f.content, locals_)
            if bad:
                problems += bad
                continue
        guard.write_file(ws, f.name, f.content)
        written.append(f.name)
    if len(files) > MAX_FILES:
        notes.append(f"Only the first {MAX_FILES} files were used")
    return written, notes, problems


async def _try(complete: "Complete", system: str, prompt: str) -> str | None:
    """A follow-up model call (format retry, rewrite, fix) that may fail without failing the card."""
    try:
        return await complete(system, prompt)
    except Exception:  # noqa: BLE001 - timeouts, empty answers, refusals: the caller keeps the real state
        log.warning("sandbox follow-up model call gave no usable answer")
        return None