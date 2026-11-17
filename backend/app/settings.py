"""Typed runtime configuration. No secrets in source; read from env / .env."""
from __future__ import annotations

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="MUSEDESK_",
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    env: str = "development"
    host: str = "127.0.0.1"
    port: int = 8765
    db_path: str = "./data/musedesk.sqlite3"
    workspace_root: str = "./sandbox_workspaces"
    task_timeout_seconds: int = 90
    require_pairing: bool = True
    log_level: str = "INFO"

    # "demo" = deterministic scripted producer; "live" = real sandboxed workers (TASK-05).
    producer: str = "demo"

    # Deterministic demo producer pacing. Tests set this to 0 for instant stepping.
    producer_step_seconds: float = 1.0
    # Source mode stamped on producer-generated artifacts/events. The TASK-04
    # producer is scripted (no real tools), so it is honestly "demo". TASK-05
    # real workers will emit "live" only for actually executed work.
    producer_source_mode: str = "demo"

    # Bounded revise loop: revised template proposals after "request changes" (so at
    # most 1 + this many decisions per run; capped by the templates that exist).
    max_proposal_revisions: int = 2
    # Bounded retest loop (live producer): extra developer attempts after the tests on
    # new code did not pass (total attempts = 1 + this).
    max_dev_revision_attempts: int = 1

    # A gated decision nobody answers must not hold a run forever: after this many
    # seconds the decision expires and the run fails (DECISION_TIMEOUT). 0 = no bound.
    decision_timeout_seconds: float = 3600.0

    @property
    def state_source_mode(self) -> str:
        """Mode stamped on backend-AUTHORITATIVE state the API itself records: the
        session, a human decision resolution, and the v2 board (task cards, focus).

        ``producer_source_mode`` describes the scripted producer's output and defaults
        to "demo". Using it for API-level state meant a live run (MUSEDESK_PRODUCER=live)
        carried mixed labels — worker events said "live" while session.ready,
        decision.resolved and every v2 card said "demo". Real state recorded during a
        live run is "live"; anything scripted or canned keeps its own explicit label.
        """
        return "live" if self.producer == "live" else self.producer_source_mode

    # Optional provider config lives here, injected via env only — never shipped in the APK.
    model_provider: str | None = None
    provider_api_key: str | None = None

    # ── Phase 2 feature flags (default OFF in live builds; enabled per-fixture once
    # validated). The /v2 API surface is always mounted and harmless; these flags are
    # read by the client/integration layer to gate each visual feature.
    p2_board: bool = False
    p2_artifacts: bool = False
    p2_handoffs: bool = False
    p2_factory: bool = False
    p2_focus: bool = False
    p2_room: bool = False
    p2_diagrams: bool = False

    # ── Phase 3 "Wall Habitat" flags. Unlike p2 flags these are ENFORCED server-side:
    # a disabled feature answers 404 FEATURE_DISABLED. Default OFF (the competition
    # release keeps Phase 3 dark); enable with MUSEDESK_P3_MANAGER_ENABLED=true etc.
    p3_side_conversation_enabled: bool = False
    p3_manager_enabled: bool = False
    # Read-only /btw side conversation limits.
    p3_side_timeout_seconds: float = 30.0
    p3_side_max_concurrency: int = 4
    p3_side_history_exchanges: int = 8
    # Manager planner model call deadline.
    p3_plan_timeout_seconds: float = 60.0
    # Plan runner: works through cards a committed Manager plan created (text-only model
    # work, or scripted in demo mode). OFF by default; MUSEDESK_P3_PLAN_RUNNER_ENABLED=true.
    p3_plan_runner_enabled: bool = False
    p3_work_timeout_seconds: float = 180.0
    p3_runner_poll_seconds: float = 2.0
    # Scripted (demo-mode) runner pacing per card. Tests set 0.
    p3_runner_demo_step_seconds: float = 4.0
    # Real work for plan cards (live mode with a model): Developer steps write real files
    # into <workspace_root>/plans/<plan_id> and Tester steps write real pytest tests; the
    # tests really run (statically checked first, timeout, no credentials in the env).
    # OFF by default; scripts/start_wall_backend.ps1 turns it on for LIVE runs.
    p3_execute_code: bool = False
    p3_sandbox_test_timeout_seconds: int = 60
    p3_sandbox_fix_attempts: int = 2
    # Model used when MUSEDESK_MODEL_PROVIDER=anthropic (key: MUSEDESK_PROVIDER_API_KEY or
    # ANTHROPIC_API_KEY). Effort per purpose: low | medium | high | xhigh | max.
    p3_model: str = "claude-opus-5-5"
    p3_side_effort: str = "low"
    p3_plan_effort: str = "medium"
    p3_work_effort: str = "medium"
    p3_provider_timeout_seconds: float = 170.0
    p3_refusal_fallbacks: bool = True
    # Meta Model API (MUSEDESK_MODEL_PROVIDER=meta). Docs: https://dev.meta.ai/docs
    p3_meta_base_url: str = "https://api.meta.ai/v1"
    p3_meta_model_plan: str = "muse-spark-1.3"
    p3_meta_model_work: str = "muse-spark-1.3"
    p3_meta_model_side: str = "muse-ember-1.0"
    p3_meta_side_effort: str = "low"
    p3_meta_plan_effort: str = "medium"
    p3_meta_work_effort: str = "medium"


_settings: Settings | None = None


def get_settings() -> Settings:
    global _settings
    if _settings is None:
        _settings = Settings()
    return _settings


def reset_settings_cache() -> None:
    """Test hook: force re-read of environment on next get_settings()."""
    global _settings
    _settings = None
