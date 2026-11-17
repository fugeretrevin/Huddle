"""rc20/rc21: the PC screen panel's frame route. Off unless MUSEDESK_SCREEN_SHARE is set; a paired session only; real pixels."""
from __future__ import annotations

import sys
import time
import zlib

import pytest

from app import screen_capture
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


def test_screen_wrong_token_is_refused(client, monkeypatch):
    monkeypatch.setenv("MUSEDESK_SCREEN_SHARE", "true")
    body, _ = open_session(client)
    r = client.get(f"/v2/sessions/{body['session_id']}/screen/frame", headers={"Authorization": "Bearer nope"})
    assert r.status_code in (401, 403)


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
    seq = int(r.headers["x-seq"])
    # Asking again with the sequence we hold: a newer frame, or 304 when the picture has not changed.
    r2 = client.get(f"/v2/sessions/{sid}/screen/frame?w=640&since={seq}", headers=headers)
    assert r2.status_code in (200, 304)
    if r2.status_code == 200:
        assert int(r2.headers["x-seq"]) != seq


def _fake_grab(counter, still=False):
    def grab(width):
        counter[0] += 1
        n = 0 if still else counter[0]
        return 320, 2, bytes([n % 250]) * (320 * 2 * 4)
    return grab


def test_streamer_serves_latest_and_skips_unchanged():
    n = [0]
    s = screen_capture.Streamer(grab_fn=_fake_grab(n, still=True), idle_stop=0.3)
    f = s.get(320, None, 2.0)
    assert f is not None and f.seq == 1 and len(zlib.decompress(f.data)) == 320 * 2 * 4
    assert s.get(320, f.seq, 0.2) is None            # nothing changed: no new frame
    assert s.unchanged >= 1
    time.sleep(0.6)
    assert not s.running                              # stops when nobody asks
    assert s._frame is None                           # and keeps nothing


def test_streamer_new_frame_when_picture_changes():
    n = [0]
    s = screen_capture.Streamer(grab_fn=_fake_grab(n), idle_stop=0.3)
    f = s.get(320, None, 2.0)
    g = s.get(320, f.seq, 2.0)
    assert g is not None and g.seq > f.seq


def test_streamer_reports_unreadable_screen():
    def bad(width):
        raise RuntimeError("x")
    s = screen_capture.Streamer(grab_fn=bad, idle_stop=0.3)
    with pytest.raises(RuntimeError):
        s.get(320, None, 2.0)
