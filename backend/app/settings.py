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

    # Deterministic demo producer pacing. Tests set this to 0 for instant stepping.
    producer_step_seconds: float = 1.0
    # Source mode stamped on producer-generated artifacts/events. The TASK-04
    # producer is scripted (no real tools), so it is honestly "demo". TASK-05
    # real workers will emit "live" only for actually executed work.
    producer_source_mode: str = "demo"

    # Optional provider config lives here, injected via env only — never shipped in the APK.
    model_provider: str | None = None
    provider_api_key: str | None = None


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
