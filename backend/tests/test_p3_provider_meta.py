"""MetaLlamaProvider: no network (httpx.MockTransport). The key below is a placeholder."""
from __future__ import annotations

import asyncio
import logging
from types import SimpleNamespace

import httpx
import pytest

from app.phase3.provider import ProviderRefused, build_provider
from app.phase3.provider_meta import MetaLlamaProvider, ProviderError

KEY = "LLM_PLACEHOLDER_notarealkey"


def _prov(handler, **kw):
    return MetaLlamaProvider(
        api_key=KEY, base_url="https://example.test/v1",
        models={"plan": "m-plan", "work": "m-work", "side": "m-side"},
        efforts={"plan": "medium", "work": "medium", "side": "low"},
        transport=httpx.MockTransport(handler), backoff_base=0.0, **kw)


def _ok(text="hello", finish="stop"):
    return httpx.Response(200, json={"choices": [
        {"index": 0, "message": {"role": "assistant", "content": text}, "finish_reason": finish}]})


def _run(coro):
    return asyncio.run(coro)


def test_request_shape_and_parse():
    seen = {}

    def h(req: httpx.Request):
        seen["url"] = str(req.url)
        seen["auth"] = req.headers["authorization"]
        import json
        seen["body"] = json.loads(req.content)
        return _ok(" hi ")

    out = _run(_prov(h).complete(system="SYS", messages=[{"role": "user", "content": "q"}],
                                 max_tokens=100, purpose="plan"))
    assert out == "hi"
    assert seen["url"] == "https://example.test/v1/chat/completions"
    assert seen["auth"] == f"Bearer {KEY}"
    b = seen["body"]
    assert b["model"] == "m-plan" and b["reasoning_effort"] == "medium"
    assert b["max_completion_tokens"] >= 4000
    assert b["messages"][0] == {"role": "developer", "content": "SYS"}
    assert b["messages"][1] == {"role": "user", "content": "q"}


@pytest.mark.parametrize("purpose,model", [("plan", "m-plan"), ("work", "m-work"), ("side", "m-side")])
def test_model_per_purpose(purpose, model):
    import json
    got = {}

    def h(req):
        got["m"] = json.loads(req.content)["model"]
        return _ok()

    _run(_prov(h).complete(system="", messages=[{"role": "user", "content": "q"}], purpose=purpose))
    assert got["m"] == model


def test_429_then_success():
    n = {"c": 0}

    def h(req):
        n["c"] += 1
        return httpx.Response(429, headers={"Retry-After": "0"}) if n["c"] == 1 else _ok("done")

    assert _run(_prov(h).complete(system="s", messages=[])) == "done"
    assert n["c"] == 2


def test_5xx_exhaustion_is_error_without_key(caplog):
    n = {"c": 0}

    def h(req):
        n["c"] += 1
        return httpx.Response(503, text=f"echo {KEY}")

    with caplog.at_level(logging.DEBUG):
        with pytest.raises(ProviderError) as ei:
            _run(_prov(h).complete(system="s", messages=[]))
    assert n["c"] == 3  # 1 + 2 retries
    assert KEY not in str(ei.value) and KEY not in caplog.text


def test_4xx_not_retried_and_hides_body():
    n = {"c": 0}

    def h(req):
        n["c"] += 1
        return httpx.Response(401, text=KEY)

    with pytest.raises(ProviderError) as ei:
        _run(_prov(h).complete(system="s", messages=[]))
    assert n["c"] == 1 and "401" in str(ei.value) and KEY not in str(ei.value)


@pytest.mark.parametrize("payload", [
    {"choices": [{"message": {"content": ""}, "finish_reason": "content_filter"}]},
    {"choices": [{"message": {"content": None, "refusal": "no"}, "finish_reason": "stop"}]},
])
def test_refusal(payload):
    with pytest.raises(ProviderRefused):
        _run(_prov(lambda r: httpx.Response(200, json=payload)).complete(system="s", messages=[]))


def test_timeout_maps_to_timeouterror():
    def h(req):
        raise httpx.ReadTimeout("slow")

    with pytest.raises(asyncio.TimeoutError):
        _run(_prov(h).complete(system="s", messages=[]))


def test_bad_shape_is_error():
    with pytest.raises(ProviderError):
        _run(_prov(lambda r: httpx.Response(200, json={"x": 1})).complete(system="s", messages=[]))


def _settings(**kw):
    base = dict(model_provider="meta", provider_api_key=KEY, p3_meta_base_url="https://x/v1",
                p3_meta_model_plan="a", p3_meta_model_work="b", p3_meta_model_side="c",
                p3_meta_side_effort="low", p3_meta_plan_effort="medium",
                p3_meta_work_effort="medium", p3_provider_timeout_seconds=10)
    base.update(kw)
    return SimpleNamespace(**base)


@pytest.mark.parametrize("name", ["meta", "llama", "meta_llama", "META"])
def test_build_provider_selects_meta(name):
    p = build_provider(_settings(model_provider=name))
    assert isinstance(p, MetaLlamaProvider)
    assert (p.model_for("plan"), p.model_for("work"), p.model_for("side")) == ("a", "b", "c")


def test_build_provider_env_key_and_missing(monkeypatch):
    monkeypatch.delenv("MUSE_LLM_API_KEY", raising=False)
    monkeypatch.delenv("LLAMA_API_KEY", raising=False)
    assert build_provider(_settings(provider_api_key=None)) is None
    monkeypatch.setenv("MUSE_LLM_API_KEY", KEY)
    assert isinstance(build_provider(_settings(provider_api_key=None)), MetaLlamaProvider)
