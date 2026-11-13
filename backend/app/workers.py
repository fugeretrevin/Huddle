"""Truthful worker roles for the live orchestrator.

- ResearchWorker: returns a fixed TEMPLATE recommendation (no model call, no web
  research) — disclosed via ``provenance="deterministic"`` and
  ``research_provenance="template"`` so it is never passed off as research.
- DeveloperWorker: writes REAL files into the isolated workspace after approval.
- TesterWorker: runs REAL pytest in the workspace and reports actual counts.

Only the developer/tester produce executed artifacts; those are stamped source_mode="live".
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path

from .workspace_guard import WorkspaceGuard, TestResult

# The research step is a fixed template (no model call, no web research, no citations).
# These travel on ``decision.requested`` so a client can label the recommendation
# without inferring anything from ``source_mode``.
RESEARCH_PROVENANCE = "template"
RESEARCH_PROVENANCE_LABEL = "Template recommendation. Not live AI research."
RESEARCH_PROGRESS_MESSAGE = "Preparing a template recommendation"
# Developer/tester output in live mode comes from real execution in the sandbox.
WORK_PROVENANCE_SANDBOX = "sandbox_execution"
# Used only when revisions are switched off (max_proposal_revisions=0): then nothing
# produces a revised proposal after "request changes", so none is promised.
CHANGES_REQUESTED_MESSAGE = ("Changes requested: run stopped. "
                             "Start the task again after revising it.")

# ── Bounded revise loop (request changes -> revised TEMPLATE proposal -> new decision) ──
# At most this many revised proposals per run, i.e. at most 1 + this many decisions.
# Default of ``Settings.max_proposal_revisions``; capped by the templates that exist.
MAX_PROPOSAL_REVISIONS = 2
# "Request changes" on the last allowed revision ends the run.
CHANGES_LIMIT_MESSAGE = ("Changes requested again: no further revisions are available. "
                         "Run stopped.")

# ── Bounded retest loop (failed tests -> developer re-runs the implementation once) ──
# Extra developer attempts after a failed test run. Default of
# ``Settings.max_dev_revision_attempts``; total attempts = 1 + this.
MAX_DEV_REVISION_ATTEMPTS = 1


def revising_progress_message(revision: int) -> str:
    return f"Revising the template recommendation (revision {revision})"


# Revised proposals are fixed TEMPLATE text (no model call, and the reviewer's comment
# is not interpreted). Each is a narrower statement of the SAME OAuth scaffold the
# developer worker writes, so approving any of them leads to the same implementation.
_REVISED_PROPOSALS: tuple[dict, ...] = (
    {
        "title": "Choose authentication approach (revision 1)",
        "summary": "Use OAuth, limited to a login scaffold with tests?",
        "recommendation": "approve",
        "rationale": "Narrower scope: only an OAuth authorize-URL scaffold and its unit "
                     "tests are written. No password storage, no token exchange and no "
                     "network calls.",
        "risk_level": "low",
        "alternatives": [
            {"id": "oauth", "label": "OAuth scaffold only",
             "tradeoff": "Token exchange is left for a later task"},
            {"id": "passwords", "label": "Email/password",
             "tradeoff": "More security maintenance"},
        ],
    },
    {
        "title": "Choose authentication approach (revision 2)",
        "summary": "Use OAuth, added as two new files with no existing code changed?",
        "recommendation": "approve",
        "rationale": "Smallest safe step: the OAuth scaffold and its tests are added as "
                     "two new files. Existing files are left untouched, so the change is "
                     "easy to review and to remove.",
        "risk_level": "low",
        "alternatives": [
            {"id": "oauth", "label": "OAuth scaffold in new files",
             "tradeoff": "Not wired into the app yet"},
            {"id": "passwords", "label": "Email/password",
             "tradeoff": "More security maintenance"},
        ],
    },
)


def proposal_revision_limit(configured: int) -> int:
    """Effective number of revised proposals: the setting, never more than the
    templates that exist (a revision must actually differ from the one before)."""
    return max(0, min(int(configured), len(_REVISED_PROPOSALS)))


def revised_proposal_fields(revision: int) -> dict:
    """Template content of revised proposal ``revision`` (1-based)."""
    src = _REVISED_PROPOSALS[revision - 1]
    return {**src, "alternatives": [dict(a) for a in src["alternatives"]]}


def retest_reason(result) -> str:
    """Real outcome of a test run that was not a clean pass, for a progress line."""
    if result.timed_out:
        return "timed out"
    if result.failed:
        return f"{result.failed} failed"
    return f"exit code {result.returncode}"

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
    research_provenance: str = RESEARCH_PROVENANCE
    research_provenance_label: str = RESEARCH_PROVENANCE_LABEL


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
    # How many implement+test attempts the run made (1, or 2 after a retest), and the
    # first attempt's real result when there was a retry.
    attempts: int = 1
    first_attempt: dict | None = None

    def to_payload(self) -> dict:
        retry = {"first_attempt": self.first_attempt} if self.first_attempt else {}
        return {
            "attempts": self.attempts,
            **retry,
            "summary": self.summary,
            "files_changed": self.files_changed,
            "diff_excerpt": redact(self.diff_excerpt)[:4000],
            "test_command": self.test_command,
            "tests_passed": self.tests_passed,
            "tests_failed": self.tests_failed,
            "timed_out": self.timed_out,
            "work_provenance": WORK_PROVENANCE_SANDBOX,
            "source_mode": "live",
        }


class ResearchWorker:
    def evaluate(self, decision_id: str, revision: int = 0) -> DecisionProposal:
        """``revision`` 0 is the first proposal; 1.. are the narrower template variants."""
        if revision > 0:
            f = revised_proposal_fields(revision)
            return DecisionProposal(
                decision_id=decision_id, title=f["title"], summary=f["summary"],
                recommendation=f["recommendation"], rationale=f["rationale"],
                risk_level=f["risk_level"], alternatives=f["alternatives"],
                provenance="deterministic")
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
