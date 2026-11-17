"""rc20: the PC screen panel's frame route. Off unless MUSEDESK_SCREEN_SHARE is set; a paired session only; real pixels."""
from __future__ import annotations

import sys
import zlib

import pytest

from tests.conftest import open_session


def test_screen_is_off_by_default(client, monkeypatch):
    monkeypatch.delenv("MUSEDESK_SCREEN_SHARE", raising=False)
    body, headers = open_session(client)
    sid = body["session_id"]
    r = client.get(f"/v2/sessions/{sid}/screen/frame", headers=headers)
    assert r.status_code == 403


def test_screen_needs_a_paired_session(client, monkeypatch):
    monkeypatch.setenv("MUSEDESK_SCREEN_SHARE", "true")
    r = client.get("/v2/sessions/ses-nope/screen/frame")
    assert r.status_code in (401, 404)


@pytest.mark.skipif(sys.platform != "win32", reason="GDI capture is Windows only")
def test_screen_frame_is_real_pixels(client, monkeypatch):
    monkeypatch.setenv("MUSEDESK_SCREEN_SHARE", "true")
    body, headers = open_session(client)
    sid = body["session_id"]
    r = client.get(f"/v2/sessions/{sid}/screen/frame?w=640", headers=headers)
    if r.status_code == 503:
        pytest.skip("no interactive desktop to read (service session)")
    assert r.status_code == 200
    w, h = int(r.headers["x-width"]), int(r.headers["x-height"])
    assert w == 640 and 200 <= h <= 640
    raw = zlib.decompress(r.content)
    assert len(raw) == w * h * 4
    assert r.headers["cache-control"] == "no-store"

