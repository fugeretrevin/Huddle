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
import re
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
    "hashlib", "heapq", "hmac", "html", "itertools", "json", "math", "numbers", "operator",
    "pytest", "random", "re", "secrets", "statistics", "string", "textwrap", "time",
    "typing", "typing_extensions", "unicodedata", "unittest", "uuid", "warnings", "zoneinfo",
})
DENIED_CALLS = frozenset({
    "eval", "exec", "compile", "__import__", "open", "getattr", "setattr", "delattr",
    "globals", "locals", "vars", "breakpoint", "input", "memoryview", "exit", "quit",
})
ALLOWED_DUNDERS = frozenset({"__name__", "__doc__", "__init__", "__post_init__", "__eq__",
                             "__repr__", "__str__", "__hash__", "__lt__", "__len__"})

RULES = (
    "Rules for the files: Python 3 code plus optional .md/.html/.css/.js/.json files, all at "
    "the top level of the folder (no sub-folders, no pytest.ini or conftest.py). Import only "
    "these standard modules: " + ", ".join(sorted(ALLOWED_MODULES - {"typing_extensions"})) +
    ", and the folder's own modules. No file, process, network or environment access; do not "
    "use eval, exec, open, getattr, setattr, globals or dunder attributes other than __init__. "
    "Keep it small, complete and runnable.\n"
    "Output format, exactly: for each file, a line 'FILE: <name>' followed by ONE fenced code "
    "block containing the whole file. After the last file, one line starting 'Summary:' that "
    "says what the files do in one sentence. Nothing else.")

DEV_SYSTEM = (
    "You are the Developer on a small software team. You write REAL code for ONE plan step: "
    "your files are saved into a sandbox folder and the tests in it are run with pytest, so "
    "the code must actually work. Include a pytest file named test_<module>.py with focused "
    "tests for this step's acceptance criteria. Extend the files already in the folder when "
    "they exist (write the whole updated file). " + RULES)

TEST_SYSTEM = (
    "You are the Tester on a small software team. Write REAL pytest tests for the code that "
    "is already in the sandbox folder, checking this step's acceptance criteria with concrete "
    "inputs and expected results, including edge cases. Only write test files (names start "
    "with test_ and end with .py); never change the code under test. The tests will be run "
    "and their real results reported, so test what the code really does and what the criteria "
    "require. " + RULES)

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
        if isinstance(node, ast.Import):
            for a in node.names:
                if not mod_ok(a.name):
                    problems.append(f"{name}:{node.lineno} imports {a.name}")
        elif isinstance(node, ast.ImportFrom):
            if node.level == 0 and not mod_ok(node.module):
                problems.append(f"{name}:{node.lineno} imports {node.module}")
        elif isinstance(node, ast.Call) and isinstance(node.func, ast.Name):
            if node.func.id in DENIED_CALLS:
                problems.append(f"{name}:{node.lineno} calls {node.func.id}()")
        elif isinstance(node, ast.Name) and node.id in ("__builtins__", "__loader__", "__spec__"):
            problems.append(f"{name}:{node.lineno} uses {node.id}")
        elif isinstance(node, ast.Attribute):
            a = node.attr
            if a.startswith("__") and a.endswith("__") and a not in ALLOWED_DUNDERS:
                problems.append(f"{name}:{node.lineno} uses .{a}")
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


Complete = Callable[[str, str], Awaitable[str]]     # (system, user prompt) -> model text


class SandboxWork:
    """Runs one Developer or Tester plan card for real. Never raises for model output
    problems: they come back as a failed outcome with an honest reason."""

    def __init__(self, workspace_root: str, test_timeout: int = 60, fix_attempts: int = 1) -> None:
        self.guard = WorkspaceGuard(str(Path(workspace_root) / "plans"))
        self.test_timeout = int(test_timeout)
        self.fix_attempts = max(0, int(fix_attempts))

    def folder(self, plan_id: str) -> Path:
        return self.guard.open_workspace(plan_id)

    # ─────────────────────── one card ───────────────────────
    async def run(self, agent: str, plan_id: str, step_prompt: str, complete: Complete) -> SandboxOutcome:
        ws = self.folder(plan_id)
        tests_only = agent == "tester"
        system = TEST_SYSTEM if tests_only else DEV_SYSTEM
        prompt = (step_prompt + "\n\nThe sandbox folder now contains:\n" + workspace_listing(ws))
        text = await complete(system, prompt)
        files, summary = parse_files(text)
        if not files:
            text = await complete(system, prompt + "\n\nYour previous answer had no files in the "
                                  "required format. Answer again: 'FILE: <name>' lines, each "
                                  "followed by one fenced code block, then 'Summary:'.")
            files, summary = parse_files(text)
        if not files:
            return self._failed("NO_CODE", "The model returned no files in the required format, "
                                "so nothing was written or run.", ws, [], kind_for(agent))

        written, notes, problems = self._write(ws, files, tests_only)
        if problems:
            # One chance to rewrite within the rules; otherwise nothing runs.
            text = await complete(system, prompt + "\n\nThese files were refused by the sandbox "
                                  "rules, so nothing ran:\n- " + "\n- ".join(problems) +
                                  "\nWrite them again within the rules.")
            files2, summary2 = parse_files(text)
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
            text = await complete(FIX_SYSTEM, fix_prompt)
            fixes, _ = parse_files(text)
            w3, n3, bad = self._write(ws, fixes, False)
            notes += n3 + [f"Developer fix attempt {attempts - 1}: rewrote "
                           + (", ".join(w3) if w3 else "nothing")]
            if bad:
                notes.append("Refused in the fix: " + _clip("; ".join(bad), 200))
            written = sorted(set(written) | set(w3))
            result = await asyncio.to_thread(self.guard.run_tests, ws, self.test_timeout)
        out = self._outcome(_clean(result), agent, ws, written, result, summary, notes, attempts)
        if attempts > 1:
            out.pages.insert(1, {"page": 1, "title": "First run (before the fix)",
                                 "content": _test_header(first) + "\n\n" + first.output[-6000:],
                                 "line_refs": []})
            for i, p in enumerate(out.pages):
                p["page"] = i
        return out

    # ─────────────────────── helpers ───────────────────────
    def _write(self, ws: Path, files: list[ParsedFile], tests_only: bool) -> tuple[list[str], list[str], list[str]]:
        written, notes, problems = [], [], []
        locals_ = _local_modules(ws, files)
        for f in files[:MAX_FILES]:
            why = check_name(f.name, tests_only)
            if why:
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
            self.guard.write_file(ws, f.name, f.content)
            written.append(f.name)
        if len(files) > MAX_FILES:
            notes.append(f"Only the first {MAX_FILES} files were used")
        return written, notes, problems


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
        pages = [{"page": 0, "title": "Result (sandbox: real files, real pytest run)",
                  "content": "\n".join([
                      "Written by the model into the plan's sandbox folder and run with pytest "
                      "on the PC. Counts are pytest's own.",
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
