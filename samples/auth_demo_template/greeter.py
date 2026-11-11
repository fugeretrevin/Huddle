"""Tiny sample module the MuseDesk live workers operate on.

This is a DISPOSABLE template. The orchestrator copies it into an isolated
per-session workspace; workers only ever touch that copy, never this original.
"""
from __future__ import annotations


def greet(name: str) -> str:
    if not name:
        raise ValueError("name is required")
    return f"Hello, {name}!"


def login_banner() -> str:
    # TODO(auth): the chosen authentication flow is wired up here after approval.
    return "Sign in to continue."
