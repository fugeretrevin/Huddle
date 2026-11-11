"""Typed service-layer errors translated to HTTP by the v2 routes."""
from __future__ import annotations

from typing import Any


class NotFoundError(Exception):
    def __init__(self, message: str) -> None:
        super().__init__(message)
        self.message = message


class CommandError(Exception):
    """A rejected command. ``status`` is the HTTP status the route should emit and
    ``code`` is the stable machine code the client reconciles on."""

    def __init__(self, code: str, message: str, *, status: int = 409,
                 canonical: dict[str, Any] | None = None) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.status = status
        self.canonical = canonical

    def body(self) -> dict[str, Any]:
        out: dict[str, Any] = {"code": self.code, "message": self.message}
        if self.canonical is not None:
            out["canonical_task"] = self.canonical
        return out
