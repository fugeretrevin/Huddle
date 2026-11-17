"""Document-level plan validation (wraps the item validator in ``validation.py``).

``validate_items`` checks each item (roles, kinds, ids, dependencies, cycles, size in items).
This adds the checks that belong to the plan as a whole: a non-empty objective, a JSON object
shape and an overall size cap. Model output that fails any of them never becomes cards.
"""
from __future__ import annotations

import json
from typing import Any

from .validation import Issue, ValidationResult, validate_items

MAX_PLAN_CHARS = 24000
MAX_OBJECTIVE_CHARS = 500


def validate_plan_document(raw: Any, objective: str) -> ValidationResult:
    head: list[Issue] = []
    if not isinstance(objective, str) or not objective.strip():
        head.append(Issue("EMPTY_OBJECTIVE", "the objective must not be empty"))
    elif len(objective.strip()) > MAX_OBJECTIVE_CHARS:
        head.append(Issue("OBJECTIVE_TOO_LONG",
                          f"the objective may be at most {MAX_OBJECTIVE_CHARS} characters"))
    if not isinstance(raw, dict):
        res = ValidationResult()
        res.issues = head + [Issue("PLAN_NOT_OBJECT", "a plan must be a JSON object with items")]
        return res
    try:
        size = len(json.dumps(raw))
    except (TypeError, ValueError):
        size = MAX_PLAN_CHARS + 1
    if size > MAX_PLAN_CHARS:
        head.append(Issue("PLAN_TOO_LARGE", f"the plan is larger than {MAX_PLAN_CHARS} characters"))
        res = ValidationResult()
        res.issues = head
        return res
    res = validate_items(raw.get("items"))
    res.issues = head + res.issues
    return res
