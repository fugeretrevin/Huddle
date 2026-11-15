"""Bounded wait for a human decision.

A run that raises a gated decision used to wait on its future forever: if the user
never answered (headset off, app closed) the run stayed ``awaiting_approval`` for the
life of the process. ``wait_for_decision`` bounds that wait by
``Settings.decision_timeout_seconds`` (0 disables the bound).
"""
from __future__ import annotations

import asyncio

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
        # An answer may have been recorded in the same instant the timer fired.
        row = storage.get_decision(decision_id)
        if row and row["resolved_at"] is not None and row["status"] != "expired" \
                and row["resolved_action"]:
            return row["resolved_action"]
        return None
