"""Model-provider seam for Phase 3.

The backend has no model adapter today (``Settings.model_provider`` /
``provider_api_key`` are declared but unused), so none is invented here. A deployment that
wants live answers/plans sets ``app.state.p3_provider`` to any object implementing
:class:`ModelProvider`. With no provider the side conversation answers from recorded task
state only (``TASK_SNAPSHOT``) and the Manager uses the deterministic template planner
(``TEMPLATE_PLAN``). Providers are given NO tools: text in, text out.
"""
from __future__ import annotations

from typing import Any, Protocol


class ModelProvider(Protocol):
    async def complete(self, *, system: str, messages: list[dict[str, str]],
                       max_tokens: int = 600) -> str:
        """Return the model's text reply. ``messages`` are ``{"role", "content"}`` dicts."""
        ...


def get_provider(app: Any) -> ModelProvider | None:
    return getattr(app.state, "p3_provider", None)
