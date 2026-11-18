"""rc19: a step that leaves FEWER tests than the folder had is asked once to restore them; if it still has fewer, the
card's result says so (live run: a change step took a folder from 51 to 45 tests and nobody was told)."""
from __future__ import annotations

import asyncio

from app.phase3.sandbox_work import RESTORE_SYSTEM, SandboxWork

MODULE = '''FILE: calc.py
```python
def add(a, b):
    return a + b
```
'''

THREE_TESTS = '''FILE: test_calc.py
```python
from calc import add

def test_one():
    assert add(1, 1) == 2

def test_two():
    assert add(2, 2) == 4

def test_three():
    assert add(0, 0) == 0
```
Summary: add with three tests.
'''

ONE_TEST = '''FILE: test_calc.py
```python
from calc import add

def test_negative():
    assert add(-1, 1) == 0
```
Summary: negative numbers.
'''

FOUR_TESTS = THREE_TESTS.replace("def test_three():", "def test_negative():\n    assert add(-1, 1) == 0\n\ndef test_three():")


class Model:
    """Answers by system prompt: the step's files, then (if asked) the restore."""

    def __init__(self, step: str, restore: str | None):
        self.step, self.restore = step, restore
        self.calls: list[str] = []

    async def __call__(self, system: str, prompt: str) -> str:
        if system == RESTORE_SYSTEM:
            self.calls.append("restore")
            assert "Before this step the folder had 3 tests" in prompt and "def test_two" in prompt
            return self.restore or ""
        self.calls.append("step")
        return self.step


def _first(work: SandboxWork) -> None:
    out = asyncio.run(work.run("developer", "plan-x", "Write add with tests.", Model(MODULE + THREE_TESTS, None)))
    assert out.ok and out.passed == 3 and out.tests_dropped == 0 and out.tests_before == -1


def test_lost_tests_are_restored(tmp_path):
    work = SandboxWork(str(tmp_path), test_timeout=60, fix_attempts=0)
    _first(work)
    m = Model(ONE_TEST, FOUR_TESTS)
    out = asyncio.run(work.run("developer", "plan-x", "Handle negative numbers.", m))
    assert m.calls == ["step", "restore"]
    assert out.ok and out.passed == 4 and out.tests_dropped == 0 and out.tests_before == 3
    assert "fewer tests" not in out.summary
    assert any("asked to restore" in (p.get("content") or "") for p in out.pages)


def test_still_missing_tests_are_said_on_the_card(tmp_path):
    work = SandboxWork(str(tmp_path), test_timeout=60, fix_attempts=0)
    _first(work)
    m = Model(ONE_TEST, None)                      # the restore gives nothing usable
    out = asyncio.run(work.run("developer", "plan-x", "Handle negative numbers.", m))
    assert m.calls == ["step", "restore"]
    assert out.passed == 1 and out.tests_dropped == 2
    assert "2 fewer tests than before (3 -> 1)" in out.summary


def test_no_restore_when_nothing_was_lost(tmp_path):
    work = SandboxWork(str(tmp_path), test_timeout=60, fix_attempts=0)
    _first(work)
    m = Model(FOUR_TESTS, None)
    out = asyncio.run(work.run("developer", "plan-x", "Handle negative numbers.", m))
    assert m.calls == ["step"]
    assert out.ok and out.passed == 4 and out.tests_dropped == 0 and "fewer" not in out.summary


def test_other_plans_are_not_compared(tmp_path):
    work = SandboxWork(str(tmp_path), test_timeout=60, fix_attempts=0)
    _first(work)
    m = Model(MODULE + ONE_TEST, None)
    out = asyncio.run(work.run("developer", "plan-y", "Handle negative numbers.", m))
    assert m.calls == ["step"] and out.tests_dropped == 0 and out.tests_before == -1
