"""Artifact/text sanitization for Phase 2 (P2-02 foundation).

Guarantees enforced here (see MASTER_PHASE2_IMPLEMENTATION §5 and §13.3):
- Strip ANSI/VT control sequences so raw terminal output never reaches the headset.
- Redact secret-like patterns (tokens, bearer headers, API keys, passwords).
- Remove absolute host paths; only sandbox-relative paths survive.

Nothing here executes code; it is pure string transformation.
"""
from __future__ import annotations

import re

REDACTION = "***REDACTED***"

# CSI / OSC / other escape sequences.
_ANSI_RE = re.compile(r"\x1b\[[0-9;?]*[ -/]*[@-~]|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)|\x1b[@-Z\\-_]")
# Other C0 control characters except tab/newline/carriage-return.
_CTRL_RE = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]")

# Secret-like patterns. Order matters (more specific first).
_SECRET_PATTERNS = [
    re.compile(r"(?i)\bbearer\s+[A-Za-z0-9._\-]{8,}"),
    re.compile(r"(?i)\b(api[_-]?key|secret|token|password|passwd|pwd)\s*[:=]\s*\S+"),
    re.compile(r"\bsk-[A-Za-z0-9]{16,}\b"),                 # OpenAI-style keys
    re.compile(r"\bAKIA[0-9A-Z]{16}\b"),                    # AWS access key id
    re.compile(r"\bghp_[A-Za-z0-9]{20,}\b"),                # GitHub PAT
    re.compile(r"\beyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{5,}\b"),  # JWT
    re.compile(r"\b[A-Fa-f0-9]{32,}\b"),                    # long hex blobs (hashes/keys)
]

# Absolute path patterns (Windows drive, UNC, POSIX).
_WIN_PATH_RE = re.compile(r"\b[A-Za-z]:\\[^\s:*?\"<>|]+")
_UNC_PATH_RE = re.compile(r"\\\\[^\s]+")
_POSIX_PATH_RE = re.compile(r"(?<![\w/])/(?:home|Users|root|var|etc|tmp|mnt|opt|private)/[^\s:*?\"<>|]+")


def strip_ansi(text: str) -> str:
    text = _ANSI_RE.sub("", text)
    return _CTRL_RE.sub("", text)


def redact_secrets(text: str) -> str:
    for pat in _SECRET_PATTERNS:
        text = pat.sub(REDACTION, text)
    return text


def strip_abs_paths(text: str, workspace_root: str | None = None) -> str:
    """Replace absolute host paths with a sandbox-relative form. If the path is
    under ``workspace_root`` it is rewritten relative to ``<workspace>``; any
    other absolute path collapses to its basename under ``<path>``."""
    if workspace_root:
        root = workspace_root.replace("/", "\\")
        text = re.sub(re.escape(root) + r"[\\/]?", "<workspace>/", text, flags=re.IGNORECASE)
        text = re.sub(re.escape(workspace_root) + r"[\\/]?", "<workspace>/", text, flags=re.IGNORECASE)

    def _win(m: re.Match[str]) -> str:
        tail = m.group(0).replace("\\", "/").rsplit("/", 1)[-1]
        return f"<path>/{tail}"

    text = _WIN_PATH_RE.sub(_win, text)
    text = _UNC_PATH_RE.sub(lambda m: "<path>/" + m.group(0).replace("\\", "/").rsplit("/", 1)[-1], text)
    text = _POSIX_PATH_RE.sub(lambda m: "<path>/" + m.group(0).rsplit("/", 1)[-1], text)
    return text


def sanitize_text(text: str, workspace_root: str | None = None) -> str:
    """Full sanitization pipeline applied to any text that may reach the client."""
    if not text:
        return text
    text = strip_ansi(text)
    text = strip_abs_paths(text, workspace_root)
    text = redact_secrets(text)
    return text


def contains_abs_path(text: str) -> bool:
    return bool(_WIN_PATH_RE.search(text) or _UNC_PATH_RE.search(text) or _POSIX_PATH_RE.search(text))
