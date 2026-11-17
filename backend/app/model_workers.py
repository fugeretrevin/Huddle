"""Model-driven workers for the desk task (live mode with a model and MUSEDESK_P3_EXECUTE_CODE).

- ModelResearchWorker: the Researcher's decision is written by the model (no web, no tools):
  title, a yes/no question, a recommendation, rationale, risk and two alternatives. Revisions
  ("request changes") ask the model for a narrower, safer variant of its previous proposal.
  Labelled ``research_provenance="model"``.
- ModelDeveloperWorker: after approval, the model writes the code for the approved proposal
  (plus its pytest tests) into the session's sandbox workspace, through the same static checks
  as the Manager-plan cards (phase3.sandbox_work.write_checked). On a retest it gets the real
  pytest output to fix the code.

Both return None when the model cannot be used (no answer, bad format, refused code); the
orchestrator then falls back to the fixed template / fixed starter AND says so in the
labels. Nothing here ever claims work that did not happen.
"""
from __future__ import annotations

import asyncio
import json
import logging
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .phase2.sanitize import sanitize_text
from .phase3.sandbox_work import RULES, ParsedFile, parse_files, workspace_listing, write_checked
from .workers import DecisionProposal, DevResult
from .workspace_guard import WorkspaceGuard

log = logging.getLogger("musedesk.model_workers")

DESK_OBJECTIVE = ("Add user login to the small Python sample app in the workspace: validate the "
                  "email address, enforce password requirements, and cover it with tests.")

MODEL_RESEARCH_PROVENANCE = "model"
MODEL_RESEARCH_LABEL = "Model-written recommendation (no web research, no tools)."
TEMPLATE_FALLBACK_LABEL = "Template recommendation (the model did not answer). Not live AI research."

RESEARCH_SYSTEM = (
    "You are the Researcher on a small software team. Before any code is written you bring the "
    "human ONE decision about how to build the task. You have no tools and no web access: reason "
    "from the task. Output ONLY one JSON object, no prose, with keys: title (<= 60 chars), "
    "summary (ONE yes/no question for the human, <= 110 chars), recommendation ('approve' or "
    "'reject': whether YOU recommend saying yes), rationale (<= 280 chars), risk_level ('low', "
    "'medium' or 'high'), alternatives (exactly 2 objects with id, label <= 30 chars, tradeoff "
    "<= 70 chars). The first alternative is the approach in the question.")

DEV_SYSTEM = (
    "You are the Developer on a small software team. The human approved the Researcher's "
    "proposal below. Write the REAL code for it into the sandbox folder, plus a pytest file "
    "named test_<module>.py covering the requirements. Your files are saved and the tests are "
    "run with pytest, so the code must actually work. Keep the existing files working. " + RULES)

FIX_SYSTEM = (
    "You are the Developer on a small software team. The tests in the sandbox folder were run "
    "and did not all pass. Fix the CODE so they pass (change a test only if it contradicts the "
    "approved proposal). Write the whole content of each file you change. " + RULES)


def _clip(s: Any, n: int) -> str:
    s = sanitize_text(str(s or "")).strip().replace("\n", " ")
    return s if len(s) <= n else s[: n - 1].rstrip() + "…"


def _json_object(text: str) -> dict | None:
    m = re.search(r"\{.*\}", text or "", re.S)
    if not m:
        return None
    try:
        obj = json.loads(m.group(0))
    except (ValueError, TypeError):
        return None
    return obj if isinstance(obj, dict) else None


def proposal_from_model(text: str, decision_id: str) -> DecisionProposal | None:
    """A validated proposal from the model's JSON, or None."""
    obj = _json_object(text)
    if obj is None:
        return None
    title, summary = _clip(obj.get("title"), 60), _clip(obj.get("summary"), 110)
    rec = str(obj.get("recommendation", "")).strip().lower()
    risk = str(obj.get("risk_level", "")).strip().lower()
    alts_in = obj.get("alternatives") if isinstance(obj.get("alternatives"), list) else []
    alts = []
    for i, a in enumerate(alts_in[:3]):
        if not isinstance(a, dict):
            continue
        label = _clip(a.get("label"), 30)
        if not label:
            continue
        aid = re.sub(r"[^a-z0-9_-]", "", str(a.get("id") or f"opt{i + 1}").lower())[:24] or f"opt{i + 1}"
        alts.append({"id": aid, "label": label, "tradeoff": _clip(a.get("tradeoff"), 70)})
    if not title or not summary or rec not in ("approve", "reject") or risk not in ("low", "medium", "high") \
            or len(alts) < 2:
        return None
    return DecisionProposal(
        decision_id=decision_id, title=title, summary=summary, recommendation=rec,
        rationale=_clip(obj.get("rationale"), 280), risk_level=risk, alternatives=alts,
        provenance="model", research_provenance=MODEL_RESEARCH_PROVENANCE,
        research_provenance_label=MODEL_RESEARCH_LABEL)


class ModelResearchWorker:
    def __init__(self, provider, timeout: float) -> None:
        self._provider = provider
        self._timeout = timeout

    async def propose(self, decision_id: str, objective: str, revision: int,
                      previous: DecisionProposal | None) -> DecisionProposal | None:
        prompt = f"Task: {objective}"
        if revision > 0 and previous is not None:
            prompt += ("\n\nThe human asked for changes to your previous proposal:\n"
                       f"Question: {previous.summary}\nRationale: {previous.rationale}\n"
                       f"This is revision {revision}: propose a narrower, safer variant of the same "
                       "feature (smaller scope, fewer moving parts) and say what changed in the rationale.")
        for _ in range(2):
            try:
                text = await asyncio.wait_for(
                    self._provider.complete(system=RESEARCH_SYSTEM,
                                            messages=[{"role": "user", "content": prompt}],
                                            max_tokens=1500, purpose="plan"),
                    timeout=self._timeout)
            except Exception:  # noqa: BLE001 - provider details never leak; caller falls back
                log.warning("model research failed")
                return None
            p = proposal_from_model(text, decision_id)
            if p is not None:
                return p
            prompt += "\n\nYour previous answer was not one valid JSON object with all keys. Answer again."
        return None


@dataclass
class ModelDevOutcome:
    result: DevResult
    notes: list[str]


class ModelDeveloperWorker:
    def __init__(self, provider, timeout: float) -> None:
        self._provider = provider
        self._timeout = timeout

    async def _ask(self, system: str, prompt: str) -> str:
        return await asyncio.wait_for(
            self._provider.complete(system=system, messages=[{"role": "user", "content": prompt}],
                                    max_tokens=16000, purpose="work"),
            timeout=self._timeout)

    async def implement(self, guard: WorkspaceGuard, ws: Path, objective: str,
                        proposal: DecisionProposal, test_output: str | None = None) -> ModelDevOutcome | None:
        """Write (or, with ``test_output``, fix) the code. None when nothing usable was written."""
        approved = (f"Approved proposal: {proposal.title}\nQuestion the human said yes to: {proposal.summary}\n"
                    f"Rationale: {proposal.rationale}")
        prompt = (f"Task: {objective}\n\n{approved}\n\nThe sandbox folder now contains:\n"
                  + workspace_listing(ws))
        system = DEV_SYSTEM
        if test_output is not None:
            system = FIX_SYSTEM
            prompt += "\n\npytest output (tail):\n" + test_output[-3500:]
        try:
            text = await self._ask(system, prompt)
            files, _ = parse_files(text)
            if not files:
                text = await self._ask(system, prompt + "\n\nYour answer had no files in the required "
                                       "format: 'FILE: <name>' lines, each followed by one fenced code block.")
                files, _ = parse_files(text)
        except Exception:  # noqa: BLE001
            log.warning("model developer call failed")
            return None
        if not files:
            return None
        written, notes, problems = write_checked(guard, ws, files, False)
        if problems:
            try:
                text = await self._ask(system, prompt + "\n\nThese files were refused by the sandbox rules, so "
                                       "nothing ran:\n- " + "\n- ".join(problems) + "\nWrite them again within the rules.")
                w2, n2, problems = write_checked(guard, ws, parse_files(text)[0], False)
                written = sorted(set(written) | set(w2))
                notes += n2
            except Exception:  # noqa: BLE001
                log.warning("model developer rewrite failed")
            if problems:
                notes.append("Refused by the sandbox rules: " + "; ".join(problems)[:200])
        if not written:
            return None
        diff = "\n".join(f"+++ {n}" for n in written)
        try:
            first = guard.read_file(ws, written[0])
            diff += "\n" + "\n".join("+" + line for line in first.splitlines()[:12])
        except Exception:  # noqa: BLE001
            pass
        return ModelDevOutcome(DevResult(files_changed=written, diff_excerpt=diff), notes)


def model_ready(settings, provider) -> bool:
    """The desk run uses the model only in live mode with a model AND real work switched on."""
    return provider is not None and bool(getattr(settings, "p3_execute_code", False))
