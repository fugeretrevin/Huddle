"""Bounded wait for a human decision.

A run that raises a gated decision used to wait on its future forever: if the user
never answered (headset off, app closed) the run stayed ``awaiting_approval`` for the
life of the process. ``wait_for_decision`` bounds that wait by
``Settings.decision_timeout_seconds`` (0 disables the bound).
"""
from __future__ import annotations

import asyncio

from .events import utcnow_iso
from .storage import Storage


async def wait_for_decision(storage: Storage, fut: "asyncio.Future[str]", decision_id: str,
                            timeout_seconds: float) -> str | None:
    """Return the resolved action, or ``None`` if the wait timed out with the decision
    still unanswered (the caller then terminates the run via ``fail_run``, which also
    expires the decision)."""
    if not timeout_seconds or timeout_seconds <= 0:
        return await fut
    try:
        return await asyncio.wait_for(fut, timeout_seconds)
    except asyncio.TimeoutError:
        # Decide the outcome with ONE atomic compare-and-set on the decision row: either
        # this expiry wins (the decision becomes ``expired``; a late answer then fails
        # its own ``resolve_decision`` with 409) or an answer already won (the row is
        # resolved and its action is honoured). Exactly one outcome, never both.
        if storage.expire_decision(decision_id, utcnow_iso()):
            return None
        row = storage.get_decision(decision_id)
        if row and row["resolved_action"]:
            return row["resolved_action"]
        return None
