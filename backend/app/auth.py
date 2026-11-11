"""Per-session bearer tokens. Tokens are random, short-lived, and stored only as
a salted hash. The APK never ships a durable admin token."""
from __future__ import annotations

import hashlib
import hmac
import secrets

_TOKEN_BYTES = 32


def new_token() -> str:
    return secrets.token_urlsafe(_TOKEN_BYTES)


def hash_token(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def verify_token(token: str, token_hash: str) -> bool:
    return hmac.compare_digest(hash_token(token), token_hash)
