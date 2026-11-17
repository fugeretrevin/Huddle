"""Real plan-card work: Developer/Tester files go into the plan's sandbox and pytest really runs."""
from __future__ import annotations

import json

from app.phase3.sandbox_work import check_name, check_python, parse_files
from tests.phase3.p3helpers import open_session
from tests.phase3.test_p3_runner import _client, _committed, artifacts, state, tick

GOOD_CODE = '''FILE: login_rules.py
```python
import re

def valid_email(s: str) -> bool:
    return bool(re.fullmatch(r"[^@\\s]+@[^@\\s]+\\.[^@\\s]+", s or ""))
```

FILE: test_login_rules.py
```python
from login_rules import valid_email

def test_ok():
    assert valid_email("a@b.co")

def test_bad():
    assert not valid_email("nope")
```
Summary: email validation with two tests.
'''

BUGGY_CODE = GOOD_CODE.replace('return bool(re.fullmatch', 'return not bool(re.fullmatch')

TESTER_FILES = '''FILE: test_login_edge.py
```python
from login_rules import valid_email

def test_empty():
    assert not valid_email("")

def test_spaces():
    assert not valid_email("a b@c.de")
```
Summary: edge cases.
'''

EVIL = '''FILE: helper.py
```python
import subprocess
subprocess.run("echo hi", shell=True)
```
Summary: nope.
'''


class ScriptedProvider:
    """Answers by purpose/system: researcher text, developer files, tester files, fixes."""

    def __init__(self, dev=GOOD_CODE, test=TESTER_FILES, fix=GOOD_CODE):
        self.dev, self.test, self.fix = dev, test, fix
        self.calls: list[str] = []

    async def complete(self, *, system, messages, max_tokens=600, purpose="side"):
        if "Tester" in system:
            self.calls.append("tester")
            return self.test
        if "did not all pass" in system:
            self.calls.append("fix")
            return self.fix
        if "Developer" in system and "sandbox" in system:
            self.calls.append("developer")
            return self.dev
        self.calls.append(purpose)
        return "Findings.\nSummary: research done."


def _run_all(c, n=6):
    for _ in range(n):
        if tick(c) == 0:
            break


def _client_exec(tmp_path, prov, **kw):
    return _client(provider=prov, p3_execute_code=True, workspace_root=str(tmp_path), **kw)


def test_developer_and_tester_write_real_files_and_pytest_really_runs(tmp_path):
    prov = ScriptedProvider()
    with _client_exec(tmp_path, prov) as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        assert plan["work_execution"] == "sandbox"
        _run_all(c)
        a, b, t = (x["task_id"] for x in cards)
        assert [state(c, x) for x in (a, b, t)] == ["completed"] * 3
        folder = tmp_path / "plans" / plan["plan_id"]
        assert (folder / "login_rules.py").exists() and (folder / "test_login_edge.py").exists()

        dev = artifacts(c, sid, b)[0]
        assert dev["kind"] == "code_diff"
        assert dev["summary"].startswith("Code written: login_rules.py, test_login_rules.py")
        assert "pytest 2 passed, 0 failed" in dev["summary"]
        tst = artifacts(c, sid, t)[0]
        assert tst["kind"] == "test_report" and tst["summary"] == "Tests run: 4 passed, 0 failed"
        page = json.loads(tst["pages_json"])
        assert any(p["title"] == "pytest output" and "4 passed" in p["content"] for p in page)

        done = {e["task_id"]: e["payload"] for e in c.app.state.phase2_store.events_after_v2(sid, 0)
                if e["type"] == "task.completed"}
        assert done[t]["work_mode"] == "sandbox" and done[t]["tests_passed"] == 4
        created = [e["payload"] for e in c.app.state.phase2_store.events_after_v2(sid, 0)
                   if e["type"] == "artifact.created" and e["task_id"] == t]
        assert created[0]["work_provenance"] == "sandbox_execution"


def test_failing_tests_get_one_real_fix_and_rerun(tmp_path):
    prov = ScriptedProvider(dev=BUGGY_CODE, fix=GOOD_CODE)
    with _client_exec(tmp_path, prov) as c:
        sid, h = open_session(c)
        _, cards = _committed(c, sid, h)
        _run_all(c)
        b = cards[1]["task_id"]
        assert state(c, b) == "completed"
        assert "fix" in prov.calls
        art = artifacts(c, sid, b)[0]
        assert "(after 1 fix)" in art["summary"]
        titles = [p["title"] for p in json.loads(art["pages_json"])]
        assert "First run (before the fix)" in titles


def test_still_failing_after_the_fix_fails_the_card_with_real_counts(tmp_path):
    prov = ScriptedProvider(dev=BUGGY_CODE, fix=BUGGY_CODE)
    with _client_exec(tmp_path, prov) as c:
        sid, h = open_session(c)
        _, cards = _committed(c, sid, h)
        _run_all(c)
        b, t = cards[1]["task_id"], cards[2]["task_id"]
        assert state(c, b) == "failed" and state(c, t) == "blocked"
        failed = [e["payload"] for e in c.app.state.phase2_store.events_after_v2(sid, 0)
                  if e["type"] == "task.failed" and e["task_id"] == b][0]
        assert failed["code"] == "TESTS_FAILED"
        assert "0 passed, 2 failed" in failed["result_summary"]


def test_refused_code_is_never_written_or_run(tmp_path):
    prov = ScriptedProvider(dev=EVIL)
    with _client_exec(tmp_path, prov) as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        _run_all(c)
        b = cards[1]["task_id"]
        assert state(c, b) == "failed"
        assert not (tmp_path / "plans" / plan["plan_id"] / "helper.py").exists()
        failed = [e["payload"] for e in c.app.state.phase2_store.events_after_v2(sid, 0)
                  if e["type"] == "task.failed" and e["task_id"] == b][0]
        assert failed["code"] == "SANDBOX_REFUSED" and "subprocess" in failed["result_summary"]


SECRETY = GOOD_CODE.replace("def test_bad():", "def test_literal():\n    password = \"Secret123!\"\n    token = \"abc\"\n    assert password and token\n\ndef test_bad():")


def test_code_is_written_verbatim_not_redacted(tmp_path):
    """Live rc16 bug: the secret filter turned `token = ...` lines in tests into ***REDACTED***."""
    prov = ScriptedProvider(dev=SECRETY)
    with _client_exec(tmp_path, prov) as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        _run_all(c)
        body = (tmp_path / "plans" / plan["plan_id"] / "test_login_rules.py").read_text(encoding="utf-8")
        assert 'password = "Secret123!"' in body and "REDACTED" not in body
        assert state(c, cards[1]["task_id"]) == "completed"


class FlakyFixProvider(ScriptedProvider):
    """Live rc17 bug: a fix attempt came back empty (finish_reason=length) and the card read 'The sandbox could not work
    this step'. A failed follow-up call must leave the REAL test result standing."""

    def __init__(self, first_fails=False):
        super().__init__(dev=BUGGY_CODE)
        self.first_fails = first_fails

    async def complete(self, *, system, messages, max_tokens=600, purpose="side"):
        from app.phase3.provider_meta import ProviderError
        if "did not all pass" in system or (self.first_fails and "Developer" in system and "sandbox" in system):
            raise ProviderError("provider returned no text (finish_reason=length)")
        return await super().complete(system=system, messages=messages, max_tokens=max_tokens, purpose=purpose)


def test_a_failed_fix_call_keeps_the_real_test_result(tmp_path):
    with _client_exec(tmp_path, FlakyFixProvider()) as c:
        sid, h = open_session(c)
        _, cards = _committed(c, sid, h)
        _run_all(c)
        b = cards[1]["task_id"]
        failed = [e["payload"] for e in c.app.state.phase2_store.events_after_v2(sid, 0)
                  if e["type"] == "task.failed" and e["task_id"] == b][0]
        assert failed["code"] == "TESTS_FAILED" and "0 passed, 2 failed" in failed["result_summary"]
        page = json.loads(artifacts(c, sid, b)[0]["pages_json"])[0]["content"]
        assert "the model gave no usable answer" in page


def test_a_failed_first_call_says_the_model_not_the_sandbox(tmp_path):
    with _client_exec(tmp_path, FlakyFixProvider(first_fails=True)) as c:
        sid, h = open_session(c)
        _, cards = _committed(c, sid, h)
        _run_all(c)
        b = cards[1]["task_id"]
        failed = [e["payload"] for e in c.app.state.phase2_store.events_after_v2(sid, 0)
                  if e["type"] == "task.failed" and e["task_id"] == b][0]
        assert failed["code"] == "MODEL_UNAVAILABLE"


def test_execution_off_keeps_text_only_work(tmp_path):
    prov = ScriptedProvider()
    with _client(provider=prov, workspace_root=str(tmp_path)) as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        assert plan["work_execution"] == "text_only"
        _run_all(c)
        assert not (tmp_path / "plans").exists()
        assert "developer" not in prov.calls


def test_parse_and_rules():
    files, summary = parse_files(GOOD_CODE)
    assert [f.name for f in files] == ["login_rules.py", "test_login_rules.py"]
    assert summary == "email validation with two tests."
    assert check_name("../x.py", False) and check_name("conftest.py", False)
    assert check_name("a/b.py", False) and check_name("code.py", True)
    assert check_name("test_ok.py", True) is None
    assert check_python("x.py", "import os\n", set())
    assert check_python("x.py", "from pathlib import Path\n", set())
    assert check_python("x.py", "open('f')\n", set())
    assert check_python("x.py", "().__class__.__bases__\n", set())
    assert check_python("x.py", "import re, hashlib\nfrom mod import f\n", {"mod"}) == []
    assert check_python("x.py", "def f(:\n", set()) == []     # cannot execute; pytest reports it


import pytest  # noqa: E402


@pytest.mark.parametrize("src", [
    "import pytest\nos = pytest.importorskip('os')\n",                       # module by name
    "def test_x(tmp_path):\n    (tmp_path.parent / 'x').write_text('1')\n",   # file system fixture
    "def test_x(monkeypatch):\n    pass\n",
    "def test_x(request):\n    pass\n",
    "import operator\n",                                                       # attrgetter by name
    "from unittest import mock\n",                                             # patch().getter()
    "import string\nstring.Formatter().get_field('0.__class__', [()], {})\n",
    "from string import Formatter\n",
    "import typing\ntyping.get_type_hints(f)\n",                               # evaluates annotations
    "from typing import ForwardRef\n",
    "import pytest\n@pytest.mark.skipif(\"__import__('os').system('x')\")\ndef test_a():\n    pass\n",
    "import pytest\n@pytest.mark.xfail(condition='1')\ndef test_a():\n    pass\n",
    "pytest_plugins = ['evil']\n",
    "f = getattr\n",                                                           # referenced, not called
    "x = help\n",
])
def test_hardened_checks_refuse(src):
    assert check_python("t.py", src, set()), src


@pytest.mark.parametrize("src", [
    "import re, string, hashlib\nSPECIALS = set(string.punctuation)\n",
    "import pytest\n@pytest.mark.parametrize('v', ['a@b.co'])\ndef test_ok(v):\n    assert v\n",
    "import pytest\n@pytest.mark.skipif(True, reason='x')\ndef test_a():\n    pass\n",
    "items = [1, 2]\nitems.remove(1)\n",
    "from dataclasses import dataclass\n@dataclass\nclass User:\n    email: str\n",
    "def test_raises():\n    import pytest\n    with pytest.raises(ValueError):\n        int('x')\n",
])
def test_ordinary_code_still_passes(src):
    assert check_python("t.py", src, set()) == [], src


def test_files_named_like_standard_modules_are_refused():
    assert "standard module" in check_name("email.py", False)
    assert "standard module" in check_name("socket.py", False)
    assert check_name("email_rules.py", False) is None
