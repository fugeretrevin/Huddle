# Meta model provider (all agent work)

Date 2026-11-15. Backend only. Provider id: `meta` (aliases `llama`, `meta_llama`).

## Contract (checked against the live docs)

The old "Llama API" host/shape in memory (`api.llama.com`, `completion_message.content.text`) is NOT current. Docs now live at `https://dev.meta.ai` (the old `llama.developer.meta.com` redirects there).

- Base URL `https://api.meta.ai/v1` (https://dev.meta.ai/docs/models). Endpoint `POST /v1/chat/completions` is OpenAI-compatible (https://dev.meta.ai/docs/protocols/chat-completions). A native `/v1/responses` also exists; not used.
- Auth: `Authorization: Bearer <key>`.
- Request: `model`, `messages` (roles `developer` for instructions, `user`, `assistant`; `system` kept for compatibility), `max_completion_tokens` (`max_tokens` deprecated, `max_output_tokens` rejected), `reasoning_effort` (`minimal|low|medium|high|xhigh|max`; default `high` Spark, `max` Wonder/Ember; `none` is a 400), `temperature` left unset (Spark is tuned for the default).
- Response: `choices[0].message.content`, `choices[0].finish_reason`, `usage`. The docs list only `stop`; no refusal value is documented, so we treat `content_filter`, `refusal` or a non-empty `message.refusal` as a refusal.
- Errors: OpenAI-style envelope. 400 = fix request (not retried), 429 = back off with jitter and honour `Retry-After` (https://dev.meta.ai/docs/pricing-rate-limits; limits are per team, per model), 504 = non-streaming request ran too long.
- Models (https://dev.meta.ai/docs/models, /docs/choose-a-model): `muse-wonder-1.0` (most capable, preview), `muse-spark-1.3` (recommended, agentic/coding/reasoning), `muse-ember-1.0` (fast, cheap, sub-agent/routing). Docs recommend Wonder/Spark for planning and Ember for fast replies.
- Key format: docs do not specify one. The owner's `LLM_<id>_<secret>` key authenticated fine as a Bearer token at `api.meta.ai` (HTTP 200). No `|` variant was needed.

## Implementation

- `backend/app/phase3/provider_meta.py` `MetaLlamaProvider` (httpx.AsyncClient, no tools). Max 2 retries on 429/5xx/transport errors with backoff; timeout = `p3_provider_timeout_seconds` and raises `TimeoutError` (existing `asyncio.TimeoutError` paths); refusal raises `ProviderRefused`; other failures raise `ProviderError` (status only, no body, no key, no prompt). `max_completion_tokens` is floored at 4000 since reasoning tokens count against it.
- TLS uses the OS trust store (`ssl.create_default_context()`): httpx's bundled certifi failed with `CERTIFICATE_VERIFY_FAILED` against api.meta.ai on this PC.
- `build_provider` selects it; key from `MUSEDESK_PROVIDER_API_KEY`, else env `MUSE_LLM_API_KEY` / `LLAMA_API_KEY`. Anthropic provider untouched.
- Settings: `p3_meta_base_url`, `p3_meta_model_plan|work|side` (defaults `muse-spark-1.3`, `muse-spark-1.3`, `muse-ember-1.0`), `p3_meta_{plan,work,side}_effort` (medium, medium, low). Env prefix `MUSEDESK_`.
- Demo mode never calls the model (unchanged: provider only used when producer is live).
- `httpx` added to the `model` extra. The APK never holds the key; the live model is used only when the Quest talks to this backend on the PC.

## Run live

`backend\.env` (git-ignored) holds `MUSEDESK_MODEL_PROVIDER=meta` and `MUSEDESK_PROVIDER_API_KEY=...`. Then `scripts\start_wall_backend.ps1` runs live with Meta (it only tests that a key exists, never prints it); `-Demo` forces the scripted demo; no key means demo. Template in `backend/.env.example`.

## Evidence

- `backend/tests/test_p3_provider_meta.py` (MockTransport, no network): request shape, per-purpose model, 429 retry, 5xx exhaustion, 4xx no-retry, refusal, timeout, bad shape, key never in exceptions/logs, build_provider selection. Full suite passes.
- Live smoke (`backend/scripts/meta_smoke.py`): `muse-spark-1.3` and `muse-ember-1.0` both HTTP 200, reply `OK`, parsed.
- Live end to end (port 8766, worktree code): `POST /v2/.../manager/plans` returned 201 with `provenance: LIVE_MODEL` and six validated items; `/btw` returned an honest `unavailable` because the fresh session had no task data (no model call is made without recorded state).
