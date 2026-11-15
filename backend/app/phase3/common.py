"""Shared Phase 3 helpers: errors, feature gate, idempotent command runner."""
from __future__ import annotations

from typing import Any, Awaitable, Callable

from fastapi import Request

from ..phase2.errors import CommandError
from ..phase2.keylock import idempotency_lock
from ..phase2.store import Phase2Store, body_hash


class PlanCommandError(CommandError):
    """CommandError that may carry the canonical plan and structured details."""

    def __init__(self, code: str, message: str, *, status: int = 409,
                 canonical_plan: dict[str, Any] | None = None,
                 details: list[dict[str, Any]] | None = None) -> None:
        super().__init__(code, message, status=status)
        self.canonical_plan = canonical_plan
        self.details = details

    def body(self) -> dict[str, Any]:
        out = super().body()
        if self.canonical_plan is not None:
            out["canonical_plan"] = self.canonical_plan
        if self.details is not None:
            out["details"] = self.details
        return out


def require_flag(request: Request, flag: str) -> None:
    if not getattr(request.app.state.settings, flag):
        raise CommandError("FEATURE_DISABLED",
                           f"This feature is turned off on the backend ({flag}=false).",
                           status=404)


async def idempotent(store: Phase2Store, session_id: str, route: str, body_dict: dict[str, Any],
                     coro: Callable[[], Awaitable[dict[str, Any]]],
                     cache: Callable[[dict[str, Any]], bool] | None = None) -> dict[str, Any]:
    """Same contract as phase2's ``_idempotent_command`` (session, key, route+body hash;
    same key + different body -> 409 IDEMPOTENCY_KEY_REUSE). ``cache`` lets a command
    decline to remember a transient outcome (busy / timeout) so a retry can succeed."""
    key = body_dict["idempotency_key"]
    bhash = body_hash({"route": route, **body_dict})
    # Held across check -> run -> save so an overlapping retry with the same key waits
    # and replays instead of executing a second time.
    async with idempotency_lock(session_id, key):
        prior = store.get_idempotent_v2(session_id, key)
        if prior is not None:
            if prior["body_hash"] == bhash:
                return prior["result"]
            raise CommandError("IDEMPOTENCY_KEY_REUSE",
                               "idempotency key reused with a different body", status=409)
        result = await coro()
        if cache is None or cache(result):
            store.save_idempotent_v2(session_id, key, route, bhash, result)
        return result
