"""Truthful worker roles for the live orchestrator.

- ResearchWorker: deterministic evaluation (no external LLM here) — disclosed via
  ``provenance="deterministic"`` so it is never passed off as model reasoning.
- DeveloperWorker: writes REAL files into the isolated workspace after approval.
- TesterWorker: runs REAL pytest in the workspace and reports actual counts.

Only the developer/tester produce executed artifacts; those are stamped source_mode="live".
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path

from .workspace_guard import WorkspaceGuard, TestResult

_SECRET = re.compile(r"(?i)(api[_-]?key|token|secret|password)\s*[:=]\s*\S+")


def redact(text: str) -> str:
    return _SECRET.sub(r"\1=[REDACTED]", text or "")


@dataclass
class DecisionProposal:
    decision_id: str
    title: str
    summary: str
    recommendation: str
    rationale: str
    risk_level: str
    alternatives: list[dict]
    provenance: str = "deterministic"


@dataclass
class DevResult:
    files_changed: list[str]
    diff_excerpt: str


@dataclass
class ArtifactData:
    summary: str
    files_changed: list[str] = field(default_factory=list)
    diff_excerpt: str = ""
    test_command: str = "pytest -q"
    tests_passed: int = 0
    tests_failed: int = 0
    timed_out: bool = False

    def to_payload(self) -> dict:
        return {
            "summary": self.summary,
            "files_changed": self.files_changed,
            "diff_excerpt": redact(self.diff_excerpt)[:4000],
            "test_command": self.test_command,
            "tests_passed": self.tests_passed,
            "tests_failed": self.tests_failed,
            "timed_out": self.timed_out,
            "source_mode": "live",
        }


class ResearchWorker:
    def evaluate(self, decision_id: str) -> DecisionProposal:
        return DecisionProposal(
            decision_id=decision_id,
            title="Choose authentication approach",
            summary="Use OAuth instead of storing passwords?",
            recommendation="approve",
            rationale="A managed identity provider avoids building and securing custom "
                      "password storage in the first version.",
            risk_level="medium",
            alternatives=[
                {"id": "oauth", "label": "OAuth", "tradeoff": "External identity dependency"},
                {"id": "passwords", "label": "Email/password", "tradeoff": "More security maintenance"},
            ],
            provenance="deterministic",
        )


class DeveloperWorker:
    """Writes a real OAuth login module + tests into the workspace (only on approve)."""

    def implement_oauth(self, guard: WorkspaceGuard, ws: Path) -> DevResult:
        module = (
            "\"\"\"OAuth login scaffold written by the MuseDesk developer worker.\"\"\"\n\n"
            "def authorize_url(provider: str, redirect_uri: str) -> str:\n"
            "    if not provider:\n"
            "        raise ValueError('provider is required')\n"
            "    return f'https://auth.example.com/{provider}/authorize?redirect_uri={redirect_uri}'\n\n\n"
            "def start_oauth_login(provider: str) -> dict:\n"
            "    return {'provider': provider, 'url': authorize_url(provider, 'app://callback')}\n"
        )
        tests = (
            "from oauth_login import authorize_url, start_oauth_login\n"
            "import pytest\n\n"
            "def test_authorize_url_contains_provider():\n"
            "    assert 'google' in authorize_url('google', 'app://cb')\n\n"
            "def test_authorize_url_requires_provider():\n"
            "    with pytest.raises(ValueError):\n"
            "        authorize_url('', 'app://cb')\n\n"
            "def test_start_login_shape():\n"
            "    r = start_oauth_login('github')\n"
            "    assert r['provider'] == 'github' and r['url'].startswith('https://')\n"
        )
        guard.write_file(ws, "oauth_login.py", module)
        guard.write_file(ws, "test_oauth_login.py", tests)
        diff = "+++ oauth_login.py\n" + "\n".join("+" + l for l in module.splitlines()[:12])
        return DevResult(files_changed=["oauth_login.py", "test_oauth_login.py"], diff_excerpt=diff)


class TesterWorker:
    def run(self, guard: WorkspaceGuard, ws: Path, timeout: int = 90) -> TestResult:
        return guard.run_tests(ws, timeout=timeout)
