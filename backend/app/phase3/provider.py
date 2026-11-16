"""Model-provider seam for Phase 3.

Services ask ``get_provider(app)`` for an object implementing :class:`ModelProvider`. With
no provider the side conversation answers from recorded task state only (``TASK_SNAPSHOT``),
the Manager uses the deterministic template planner (``TEMPLATE_PLAN``) and the plan runner
does not run cards in live mode. Providers are given NO tools: text in, text out.

The one shipped provider is :class:`AnthropicProvider` (Claude via the official SDK). It is
built at startup by :func:`build_provider` only when ``MUSEDESK_MODEL_PROVIDER=anthropic`` and
an API key is available (``MUSEDESK_PROVIDER_API_KEY`` or the SDK's own ``ANTHROPIC_API_KEY``).
The key is read from the environment only and is never logged or returned.
"""
from __future__ import annotations

import logging
import os
from typing import Any, Protocol

log = logging.getLogger("musedesk.phase3.provider")

# Purposes a caller can name. Each maps to an effort level in settings.
PURPOSES = ("side", "plan", "work")


class ModelProvider(Protocol):
    async def complete(self, *, system: str, messages: list[dict[str, str]],
                       max_tokens: int = 600, purpose: str = "side") -> str:
        """Return the model's text reply. ``messages`` are ``{"role", "content"}`` dicts."""
        ...


class ProviderRefused(RuntimeError):
    """The model declined the request (``stop_reason == "refusal"``)."""


def get_provider(app: Any) -> ModelProvider | None:
    return getattr(app.state, "p3_provider", None)


class AnthropicProvider:
    """Claude through ``anthropic.AsyncAnthropic``.

    - Thinking stays at the model default (adaptive); depth is set per purpose with
      ``output_config.effort``. Thinking tokens count toward ``max_tokens``, so the caller's
      budget is raised to a floor; reply length is set by the prompts instead.
    - Server-side refusal fallback is on (``fallbacks="default"``) so a policy decline is
      re-served by a fallback model inside the same call; a refusal that survives it raises
      :class:`ProviderRefused` and the caller falls back to its honest non-model path.
    """

    MIN_TOKENS = 4000
    FALLBACK_BETA = "server-side-fallback-2026-07-01"

    def __init__(self, *, model: str, efforts: dict[str, str], api_key: str | None = None,
                 timeout: float = 120.0, use_fallbacks: bool = True, client: Any = None) -> None:
        if client is None:
            import anthropic  # optional dependency: only imported when configured
            kwargs: dict[str, Any] = {"timeout": timeout, "max_retries": 2}
            if api_key:
                kwargs["api_key"] = api_key
            client = anthropic.AsyncAnthropic(**kwargs)
        self._client = client
        self.model = model
        self._efforts = dict(efforts)
        self._fallbacks = use_fallbacks

    def effort_for(self, purpose: str) -> str:
        return self._efforts.get(purpose, "medium")

    async def complete(self, *, system: str, messages: list[dict[str, str]],
                       max_tokens: int = 600, purpose: str = "side") -> str:
        kwargs: dict[str, Any] = dict(
            model=self.model,
            max_tokens=max(int(max_tokens), self.MIN_TOKENS),
            system=system,
            messages=messages,
            output_config={"effort": self.effort_for(purpose)},
        )
        if self._fallbacks:
            resp = await self._client.beta.messages.create(
                betas=[self.FALLBACK_BETA], fallbacks="default", **kwargs)
        else:
            resp = await self._client.messages.create(**kwargs)
        if getattr(resp, "stop_reason", None) == "refusal":
            raise ProviderRefused("the model declined this request")
        parts = [b.text for b in resp.content if getattr(b, "type", None) == "text"]
        return "".join(parts).strip()


def _build_meta(settings: Any) -> ModelProvider | None:
    key = (settings.provider_api_key or os.environ.get("MUSE_LLM_API_KEY")
           or os.environ.get("LLAMA_API_KEY"))
    if not key:
        log.warning("MUSEDESK_MODEL_PROVIDER=meta but no API key is set "
                    "(MUSEDESK_PROVIDER_API_KEY, MUSE_LLM_API_KEY or LLAMA_API_KEY); "
                    "running without a model")
        return None
    from .provider_meta import MetaLlamaProvider
    provider = MetaLlamaProvider(
        api_key=key, base_url=settings.p3_meta_base_url,
        models={"plan": settings.p3_meta_model_plan, "work": settings.p3_meta_model_work,
                "side": settings.p3_meta_model_side},
        efforts={"side": settings.p3_meta_side_effort, "plan": settings.p3_meta_plan_effort,
                 "work": settings.p3_meta_work_effort},
        timeout=float(settings.p3_provider_timeout_seconds))
    log.info("model provider: meta plan=%s work=%s side=%s", settings.p3_meta_model_plan,
             settings.p3_meta_model_work, settings.p3_meta_model_side)
    return provider


def build_provider(settings: Any) -> ModelProvider | None:
    """Return the configured provider, or None (logged, never raised)."""
    name = (settings.model_provider or "").strip().lower()
    if not name or name == "none":
        return None
    if name in ("meta", "llama", "meta_llama"):
        return _build_meta(settings)
    if name not in ("anthropic", "claude"):
        log.warning("unknown MUSEDESK_MODEL_PROVIDER=%r; running without a model", name)
        return None
    key = settings.provider_api_key or os.environ.get("ANTHROPIC_API_KEY")
    if not key:
        log.warning("MUSEDESK_MODEL_PROVIDER=anthropic but no API key is set "
                    "(MUSEDESK_PROVIDER_API_KEY or ANTHROPIC_API_KEY); running without a model")
        return None
    try:
        provider = AnthropicProvider(
            model=settings.p3_model,
            efforts={"side": settings.p3_side_effort, "plan": settings.p3_plan_effort,
                     "work": settings.p3_work_effort},
            api_key=key, timeout=float(settings.p3_provider_timeout_seconds),
            use_fallbacks=bool(settings.p3_refusal_fallbacks))
    except ImportError:
        log.warning("the 'anthropic' package is not installed (pip install -e .[model]); "
                    "running without a model")
        return None
    log.info("model provider: anthropic model=%s", settings.p3_model)
    return provider
