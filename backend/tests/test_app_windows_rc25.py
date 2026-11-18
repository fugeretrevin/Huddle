"""rc25 (owner: "make an app that does X and then we can see it in MuseDesk"): the program the agents built opens in a
window on the PC, the screen panel shows only that window, and taps from the headset reach its buttons. Generic: the
launcher picks build_ui(ui) or main(argv) from whatever the folder holds."""
from __future__ import annotations

import os
import sys
import time
import zlib
from pathlib import Path

import pytest

from app import screen_capture
from app.phase3 import app_launcher as al
from app.phase3.planner import PLANNER_SYSTEM
from app.phase3.sandbox_work import DEV_SYSTEM
from tests.conftest import open_session

UI_APP = '''"""Tap counter."""
class Counter:
    def __init__(self):
        self.n = 0
    def add(self):
        self.n += 1
        return self.n

def build_ui(ui):
    c = Counter()
    lbl = ui.label("0")
    ui.button("Add one", lambda: lbl.set(str(c.add())))
'''

RUN_APP = '''"""Echo."""
def main(argv=None):
    print("hello", *(argv or []))
    return 0
'''


def _folder(tmp_path: Path, files: dict[str, str]) -> Path:
    d = tmp_path / "work"
    d.mkdir()
    for n, s in files.items():
        (d / n).write_text(s, encoding="utf-8")
    return d


def test_build_ui_wins_over_main(tmp_path):
    d = _folder(tmp_path, {"echo.py": RUN_APP, "counter.py": UI_APP, "test_counter.py": "def test_x():\n    pass\n"})
    assert al.find_module(d) == ("counter", "ui")


def test_main_only_opens_as_a_run_panel(tmp_path):
    d = _folder(tmp_path, {"echo.py": RUN_APP})
    assert al.find_module(d) == ("echo", "run")


def test_nothing_to_open(tmp_path):
    d = _folder(tmp_path, {"helpers.py": "X = 1\n"})
    assert al.find_module(d) is None
    with pytest.raises(al.LaunchError):
        al.AppLauncher().launch(d, "x")


def test_refused_code_is_never_opened(tmp_path):
    d = _folder(tmp_path, {"app1.py": "import os\n\ndef build_ui(ui):\n    os.system('calc')\n"})
    assert any("imports os" in p for p in al.check_folder(d))
    launcher = al.AppLauncher(python="no-such-python-should-not-start")
    with pytest.raises(al.LaunchError) as e:
        launcher.launch(d, "x")
    assert "refuse" in str(e.value)
    assert launcher.running() is None


def test_test_files_are_not_the_program(tmp_path):
    d = _folder(tmp_path, {"test_app.py": "def build_ui(ui):\n    pass\n"})
    assert al.find_module(d) is None


def test_agents_are_told_how_to_make_a_window():
    for text in (DEV_SYSTEM, PLANNER_SYSTEM):
        assert "build_ui(ui)" in text
        assert "tkinter" in text            # ... and told not to use it
    assert "ui.button(text, on_click)" in DEV_SYSTEM and "ui.canvas(" in DEV_SYSTEM


def test_streamer_crops_to_the_app_and_says_so():
    seen = []

    def grab(width, region=None):
        seen.append(region)
        return 64, 32, bytes(64 * 32 * 4)
    s = screen_capture.Streamer(grab_fn=grab, idle_stop=0.3, region_fn=lambda: (10, 20, 640, 320))
    f = s.get(1280, None, 2.0)
    assert f is not None and f.app is True
    assert seen[0] == (10, 20, 640, 320)


def test_streamer_whole_screen_without_an_app():
    def grab(width):
        return 64, 32, bytes(64 * 32 * 4)
    s = screen_capture.Streamer(grab_fn=grab, idle_stop=0.3, region_fn=lambda: None)
    f = s.get(1280, None, 2.0)
    assert f is not None and f.app is False


def test_apps_are_off_without_the_flag(client, monkeypatch):
    monkeypatch.delenv("MUSEDESK_APP_WINDOWS", raising=False)
    body, headers = open_session(client)
    sid = body["session_id"]
    assert client.post(f"/v2/sessions/{sid}/apps/open", json={}, headers=headers).status_code == 403
    assert client.post(f"/v2/sessions/{sid}/apps/tap", json={"u": 0.5, "v": 0.5}, headers=headers).status_code == 403
    st = client.get(f"/v2/sessions/{sid}/apps", headers=headers).json()
    assert st["enabled"] is False and st["running"] is False


@pytest.mark.skipif(sys.platform != "win32", reason="app windows are Windows only")
def test_open_with_nothing_built_says_so(tmp_path, monkeypatch):
    from fastapi.testclient import TestClient
    from app.main import create_app
    from tests.conftest import make_settings
    monkeypatch.setenv("MUSEDESK_APP_WINDOWS", "true")
    # An empty workspace of its own: the real one may hold the owner's apps (the fallback would open one).
    with TestClient(create_app(make_settings().model_copy(update={"workspace_root": str(tmp_path / "ws")}))) as client:
        body, headers = open_session(client)
        sid = body["session_id"]
        r = client.post(f"/v2/sessions/{sid}/apps/open", json={}, headers=headers)
        assert r.status_code == 200
        assert r.json()["opened"] is False and "Nothing has been built" in r.json()["reason"]
        r = client.post(f"/v2/sessions/{sid}/apps/open", json={"plan_id": "mp-nope"}, headers=headers)
        assert r.json()["opened"] is False and "no program" in r.json()["reason"]
        bad = client.post(f"/v2/sessions/{sid}/apps/tap", json={"u": 2, "v": 0.5}, headers=headers)
        assert bad.status_code == 422


def test_apps_need_a_paired_session(client, monkeypatch):
    monkeypatch.setenv("MUSEDESK_APP_WINDOWS", "true")
    body, _ = open_session(client)
    r = client.post(f"/v2/sessions/{body['session_id']}/apps/open", json={}, headers={"Authorization": "Bearer nope"})
    assert r.status_code in (401, 403)


# A real window on the desktop: run with MUSEDESK_TEST_APP_WINDOW=1 (it flashes a window on screen).
@pytest.mark.skipif(sys.platform != "win32" or os.environ.get("MUSEDESK_TEST_APP_WINDOW") != "1",
                    reason="opens a real window; set MUSEDESK_TEST_APP_WINDOW=1")
def test_real_window_taps_and_closes(tmp_path):
    d = _folder(tmp_path, {"counter.py": UI_APP})
    launcher = al.AppLauncher()
    st = launcher.launch(d, "Tap counter")
    try:
        assert st["running"] and st["ready"] and st["kind"] == "ui"
        rect = al.client_rect(launcher.hwnd())
        assert rect is not None and rect[2] > 100
        hit = False
        for v in [i / 20 for i in range(4, 20)]:
            launcher.tap(0.1, v)
            time.sleep(0.05)
            if any("TAP button Add one" in ln for ln in launcher.running().lines):
                hit = True
                break
        assert hit
        w, h, raw = screen_capture.grab(1280, rect)
        assert (w, h) == (rect[2], rect[3]) and len(raw) == w * h * 4
        assert len(zlib.compress(raw)) > 0
    finally:
        assert launcher.close()
    assert launcher.running() is None
