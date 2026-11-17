"""Plan producers. Both return RAW item dicts; neither is trusted. The caller runs
``validation.validate_items`` on whatever comes back."""
from __future__ import annotations

import json
import re
from typing import Any

from .validation import MAX_TITLE

TEMPLATE_NOTE = ("Planning is template-based (research, implement, test chain derived from "
                 "your objective text). No model reviewed this plan.")
MODEL_NOTE = "Plan drafted by a configured model and then validated by the backend."
DEMO_NOTE = "SCRIPTED DEMO: template plan for the offline demonstration. No model was used."

PLANNER_SYSTEM = (
    "You are the planning step of a software team manager. Output ONLY one JSON object, no "
    "prose, with keys: title (string), assumptions (list of strings), risks (list of strings), "
    "items (list). Each item: client_item_id (short unique string), title (<=120 chars), "
    "kind (one of research, code, implement, test, verify, doc), proposed_agent (researcher for "
    "research; developer for code/implement; tester for test/verify; researcher or developer for "
    "doc), depends_on (list of client_item_ids of earlier items), acceptance_criteria (list of "
    "short strings), required_permission (one of none, read_repo, workspace_write, run_tests). "
    "The manager never owns items. Do not invent tools, credentials or deployments. Max 12 items. "
    # rc20 (owner, worn rc19): the user gives the Manager NEW goals by voice ("build a program that prints stars to
    # the console"). The plan must fit that goal, not a template: name the real module, size it to the goal.
    "The objective is the user's own words, usually spoken: read it for what they really want "
    "built. The team works in a fresh Python folder (the Developer writes modules and pytest "
    "tests; the Tester adds tests and runs them), so plan concrete Python modules and name them "
    "in the titles. Size the plan to the objective: a small program needs 2 to 4 items, a "
    "larger feature up to 8. In assumptions, state the context you decided on (inputs, outputs, "
    "file names) so the user can check it before approving."
)


def _subject(objective: str, room: int) -> str:
    s = re.sub(r"\s+", " ", objective).strip().rstrip(".")
    return s if len(s) <= room else s[: max(room - 3, 1)].rstrip() + "..."


def template_plan(objective: str) -> dict[str, Any]:
    """Deterministic research -> implement -> test chain from the objective text."""
    subj_t = _subject(objective, MAX_TITLE - len("Implement: "))
    subj_c = _subject(objective, 120)
    return {
        "title": _subject(objective, 80) or "Plan",
        "assumptions": [
            "The existing repository and workspace are used as they are.",
            "Each step is a plain board card owned by one worker; the user approves before any card exists.",
        ],
        "risks": [
            "Template plan: it does not analyse the objective, so scope and dependencies may need revising.",
        ],
        "items": [
            {"client_item_id": "a", "title": f"Research: {subj_t}"[:MAX_TITLE], "kind": "research",
             "proposed_agent": "researcher", "depends_on": [],
             "acceptance_criteria": [f"Findings and options are written up for: {subj_c}"[:200]],
             "required_permission": "read_repo"},
            {"client_item_id": "b", "title": f"Implement: {subj_t}"[:MAX_TITLE], "kind": "code",
             "proposed_agent": "developer", "depends_on": ["a"],
             "acceptance_criteria": [f"The change for the objective is implemented: {subj_c}"[:200]],
             "required_permission": "workspace_write"},
            {"client_item_id": "c", "title": f"Test: {subj_t}"[:MAX_TITLE], "kind": "test",
             "proposed_agent": "tester", "depends_on": ["b"],
             "acceptance_criteria": ["Tests covering the change pass and the result is reported."],
             "required_permission": "run_tests"},
        ],
    }


def build_model_messages(objective: str, board_summary: str) -> list[dict[str, str]]:
    return [{"role": "user", "content":
             f"Objective: {objective}\n\nCurrent board (read-only snapshot):\n{board_summary}\n\n"
             "Return the JSON plan."}]


def parse_model_plan(text: str) -> dict[str, Any] | None:
    """Extract the first JSON object from model text. None if it isn't a JSON object."""
    if not isinstance(text, str):
        return None
    t = text.strip()
    t = re.sub(r"^```(?:json)?\s*|\s*```$", "", t, flags=re.I)
    try:
        obj = json.loads(t)
    except (ValueError, TypeError):
        m = re.search(r"\{.*\}", t, re.S)
        if not m:
            return None
        try:
            obj = json.loads(m.group(0))
        except (ValueError, TypeError):
            return None
    return obj if isinstance(obj, dict) else None
