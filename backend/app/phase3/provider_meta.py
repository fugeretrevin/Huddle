"""Meta model provider (Meta Model API, OpenAI-compatible chat completions).

Contract (https://dev.meta.ai/docs/protocols/chat-completions): POST {base}/chat/completions,
``Authorization: Bearer <key>``, body ``model``, ``messages`` (``developer`` role for
instructions), ``max_completion_tokens``, ``reasoning_effort``; reply text at
``choices[0].message.content``. 429 is retried with backoff (honouring Retry-After).

No tools, text in / text out. The key is only ever placed in the Authorization header; it is
never logged, never put in an exception message, and prompts are never logged.
"""
from __future__ import annotations

import asyncio
import logging
import random
import ssl
from typing import Any

import httpx

from .provider import ProviderRefused

log = logging.getLogger("musedesk.phase3.provider.meta")

DEFAULT_BASE_URL = "https://api.meta.ai/v1"


class ProviderError(RuntimeError):
    """Provider call failed. The message never contains the key or the prompt."""


class MetaLlamaProvider:
    MIN_TOKENS = 4000          # reasoning tokens count toward max_completion_tokens
    MAX_RETRIES = 2            # retries after the first attempt (429 / 5xx / transport)

    def __init__(self, *, api_key: str, models: dict[str, str], efforts: dict[str, str],
                 base_url: str = DEFAULT_BASE_URL, timeout: float = 120.0,
                 transport: httpx.AsyncBaseTransport | None = None,
                 backoff_base: float = 0.5) -> None:
        if not api_key:
            raise ValueError("api key required")
        self._key = api_key
        self._models = dict(models)
        self._efforts = dict(efforts)
        self._url = base_url.rstrip("/") + "/chat/completions"
        self._timeout = float(timeout)
        self._transport = transport
        self._backoff = backoff_base

    def model_for(self, purpose: str) -> str:
        return self._models.get(purpose) or self._models.get("work") or "muse-spark-1.3"

    def _body(self, system: str, messages: list[dict[str, str]], max_tokens: int,
              purpose: str) -> dict[str, Any]:
        msgs: list[dict[str, str]] = []
        if system:
            msgs.append({"role": "developer", "content": system})
        msgs.extend({"role": m["role"], "content": m["content"]} for m in messages)
        body: dict[str, Any] = {
            "model": self.model_for(purpose),
            "messages": msgs,
            "max_completion_tokens": max(int(max_tokens), self.MIN_TOKENS),
        }
        effort = self._efforts.get(purpose)
        if effort:
            body["reasoning_effort"] = effort
        return body

    @staticmethod
    def _retry_after(resp: httpx.Response) -> float | None:
        try:
            return min(float(resp.headers.get("Retry-After", "")), 30.0)
        except ValueError:
            return None

    async def complete(self, *, system: str, messages: list[dict[str, str]],
                       max_tokens: int = 600, purpose: str = "side") -> str:
        body = self._body(system, messages, max_tokens, purpose)
        headers = {"Authorization": f"Bearer {self._key}", "Content-Type": "application/json"}
        last = "no response"
        # OS trust store: httpx's bundled certifi lacks api.meta.ai's issuer on this machine.
        kw: dict[str, Any] = ({"transport": self._transport} if self._transport
                              else {"verify": ssl.create_default_context()})
        async with httpx.AsyncClient(timeout=self._timeout, **kw) as client:
            for attempt in range(self.MAX_RETRIES + 1):
                wait = self._backoff * (2 ** attempt) + random.random() * 0.1
                try:
                    resp = await client.post(self._url, json=body, headers=headers)
                except httpx.TimeoutException:
                    raise TimeoutError("model provider timed out") from None
                except httpx.TransportError as exc:
                    last = f"transport error {type(exc).__name__}"
                    if attempt < self.MAX_RETRIES:
                        await asyncio.sleep(wait)
                        continue
                    raise ProviderError(last) from None
                status = resp.status_code
                if status == 429 or status >= 500:
                    last = f"HTTP {status}"
                    if attempt < self.MAX_RETRIES:
                        ra = self._retry_after(resp) if status == 429 else None
                        await asyncio.sleep(ra if ra is not None else wait)
                        continue
                    raise ProviderError(f"provider unavailable ({last}) after retries")
                if status >= 400:
                    # Body can echo request details; report status only.
                    raise ProviderError(f"provider rejected the request (HTTP {status})")
                return self._parse(resp)
        raise ProviderError(last)  # pragma: no cover

    @staticmethod
    def _parse(resp: httpx.Response) -> str:
        try:
            choice = resp.json()["choices"][0]
            msg = choice.get("message") or {}
        except (ValueError, KeyError, IndexError, TypeError, AttributeError):
            raise ProviderError("provider returned an unexpected response shape") from None
        finish = choice.get("finish_reason")
        if finish in ("content_filter", "refusal") or msg.get("refusal"):
            raise ProviderRefused("the model declined this request")
        content = msg.get("content")
        if isinstance(content, list):  # content parts
            content = "".join(p.get("text", "") for p in content if isinstance(p, dict))
        text = (content or "").strip()
        if not text:
            raise ProviderError(f"provider returned no text (finish_reason={finish})")
        return text
