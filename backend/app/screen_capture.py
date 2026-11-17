"""rc20/rc21: the PC's primary screen, scaled down, as raw BGRA pixels for the headset's screen panel.

Windows only, standard library only (ctypes + GDI): no new packages. It is served ONLY when the backend was started with
MUSEDESK_SCREEN_SHARE=true (the owner's own launcher sets it), only to a paired session (bearer token), and only on
127.0.0.1 (the Quest reaches it over USB with adb reverse). Frames are NEVER written to disk.

rc21 (owner: "the framerate could definitely be improved"): a capture thread runs ONLY while the headset is asking for
frames (it stops ~2 s after the last request and drops its frame), and always holds the latest compressed frame, so a
request is answered at once. Capture of frame N+1 overlaps the compression of frame N. A frame that is identical to the
last one is not sent again (the sequence number does not move; the route answers 304).
"""
from __future__ import annotations

import os
import sys
import threading
import time
import zlib
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass

ENV_FLAG = "MUSEDESK_SCREEN_SHARE"
MAX_WIDTH = 1600
MIN_WIDTH = 320
IDLE_STOP_SECONDS = 2.0        # the capture thread stops this long after the last request
MIN_FRAME_PERIOD = 1.0 / 30.0  # never capture faster than this


def enabled() -> bool:
    return os.environ.get(ENV_FLAG, "").strip().lower() in ("1", "true", "yes", "on") and sys.platform == "win32"


@dataclass
class Frame:
    width: int
    height: int
    data: bytes          # BGRA, top row first; zlib-compressed when ``compressed``
    compressed: bool
    seq: int = 0


_lock = threading.Lock()
_dpi_set = False


def grab(width: int = 1280) -> tuple[int, int, bytes]:
    """The primary screen scaled to ``width`` (aspect kept), with the mouse pointer drawn in: (w, h, raw BGRA)."""
    import ctypes
    from ctypes import wintypes

    global _dpi_set
    user32 = ctypes.windll.user32
    gdi32 = ctypes.windll.gdi32
    with _lock:
        if not _dpi_set:
            try:
                user32.SetProcessDPIAware()          # physical pixels, not the scaled desktop
            except Exception:  # noqa: BLE001
                pass
            _dpi_set = True
        sw, sh = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
        if sw <= 0 or sh <= 0:
            raise RuntimeError("no screen")
        w = max(MIN_WIDTH, min(MAX_WIDTH, int(width), sw))
        h = max(1, round(sh * w / sw))

        screen_dc = user32.GetDC(None)
        mem_dc = gdi32.CreateCompatibleDC(screen_dc)
        bmp = gdi32.CreateCompatibleBitmap(screen_dc, w, h)
        old = gdi32.SelectObject(mem_dc, bmp)
        try:
            gdi32.SetStretchBltMode(mem_dc, 4)        # HALFTONE: readable text when scaled down
            gdi32.SetBrushOrgEx(mem_dc, 0, 0, None)
            SRCCOPY, CAPTUREBLT = 0x00CC0020, 0x40000000
            gdi32.StretchBlt(mem_dc, 0, 0, w, h, screen_dc, 0, 0, sw, sh, SRCCOPY | CAPTUREBLT)
            _draw_cursor(user32, mem_dc, sw, sh, w, h)

            class BITMAPINFOHEADER(ctypes.Structure):
                _fields_ = [("biSize", wintypes.DWORD), ("biWidth", wintypes.LONG), ("biHeight", wintypes.LONG),
                            ("biPlanes", wintypes.WORD), ("biBitCount", wintypes.WORD),
                            ("biCompression", wintypes.DWORD), ("biSizeImage", wintypes.DWORD),
                            ("biXPelsPerMeter", wintypes.LONG), ("biYPelsPerMeter", wintypes.LONG),
                            ("biClrUsed", wintypes.DWORD), ("biClrImportant", wintypes.DWORD)]

            bih = BITMAPINFOHEADER()
            bih.biSize = ctypes.sizeof(BITMAPINFOHEADER)
            bih.biWidth, bih.biHeight = w, -h          # negative: top row first
            bih.biPlanes, bih.biBitCount, bih.biCompression = 1, 32, 0
            buf = ctypes.create_string_buffer(w * h * 4)
            got = gdi32.GetDIBits(mem_dc, bmp, 0, h, buf, ctypes.byref(bih), 0)
            if got != h:
                raise RuntimeError("screen read failed")
            raw = buf.raw
        finally:
            gdi32.SelectObject(mem_dc, old)
            gdi32.DeleteObject(bmp)
            gdi32.DeleteDC(mem_dc)
            user32.ReleaseDC(None, screen_dc)
    return w, h, raw


def capture(width: int = 1280, compress: bool = True) -> Frame:
    """One frame, now (kept for tests and one-off reads; the route uses the streamer)."""
    w, h, raw = grab(width)
    if compress:
        return Frame(w, h, zlib.compress(raw, 1), True)
    return Frame(w, h, raw, False)


def _draw_cursor(user32, dc, sw: int, sh: int, w: int, h: int) -> None:
    import ctypes
    from ctypes import wintypes

    class CURSORINFO(ctypes.Structure):
        _fields_ = [("cbSize", wintypes.DWORD), ("flags", wintypes.DWORD), ("hCursor", wintypes.HANDLE),
                    ("ptScreenPos", wintypes.POINT)]

    ci = CURSORINFO()
    ci.cbSize = ctypes.sizeof(CURSORINFO)
    try:
        if not user32.GetCursorInfo(ctypes.byref(ci)) or not (ci.flags & 1) or not ci.hCursor:
            return
        x = round(ci.ptScreenPos.x * w / sw)
        y = round(ci.ptScreenPos.y * h / sh)
        size = max(12, round(32 * w / sw * 1.6))       # a little larger than true scale, so it can be found
        user32.DrawIconEx(dc, x, y, ci.hCursor, size, size, 0, None, 3)
    except Exception:  # noqa: BLE001 - a missing pointer must never break the frame
        return


class Streamer:
    """Holds the latest compressed frame while somebody is asking for it. In memory only; never stored."""

    def __init__(self, grab_fn=None, idle_stop: float | None = None) -> None:
        self._grab = grab_fn or grab
        self._idle = IDLE_STOP_SECONDS if idle_stop is None else idle_stop
        self._cv = threading.Condition()
        self._frame: Frame | None = None
        self._seq = 0
        self._width = 1280
        self._last_ask = 0.0
        self._thread: threading.Thread | None = None
        self._error: str | None = None
        self.captured = 0          # frames grabbed (for tests)
        self.unchanged = 0         # grabs dropped because the picture had not changed

    @property
    def running(self) -> bool:
        t = self._thread
        return t is not None and t.is_alive()

    def get(self, width: int, since: int | None, wait: float) -> Frame | None:
        """The latest frame newer than ``since`` (any frame when ``since`` is None), waiting up to ``wait`` seconds.
        Returns None when nothing newer arrived (the route answers 304). Raises RuntimeError when the screen cannot be read."""
        width = max(MIN_WIDTH, min(MAX_WIDTH, int(width)))
        deadline = time.monotonic() + max(0.0, wait)
        with self._cv:
            self._last_ask = time.monotonic()
            if width != self._width:
                self._width = width
                self._frame = None                 # the old size is useless to this caller
            self._ensure_thread()
            while True:
                f = self._frame
                if f is not None and (since is None or f.seq != since):
                    return f
                if self._error is not None:
                    err, self._error = self._error, None
                    raise RuntimeError(err)
                left = deadline - time.monotonic()
                if left <= 0:
                    return None
                self._cv.wait(min(left, 0.05))
                self._last_ask = time.monotonic()

    def _ensure_thread(self) -> None:
        if self.running:
            return
        self._error = None
        self._thread = threading.Thread(target=self._run, name="screen-stream", daemon=True)
        self._thread.start()

    def _run(self) -> None:
        pool = ThreadPoolExecutor(max_workers=1, thread_name_prefix="screen-zlib")
        pending = None
        last_hash: int | None = None
        last_key = (0, 0)
        try:
            while True:
                t0 = time.monotonic()
                if t0 - self._last_ask > self._idle:
                    if pending is not None:
                        pending.result()
                    with self._cv:
                        if time.monotonic() - self._last_ask > self._idle:
                            self._frame = None     # nothing is kept once nobody is looking
                            self._thread = None
                            return
                with self._cv:
                    width = self._width
                try:
                    w, h, raw = self._grab(width)
                except Exception:  # noqa: BLE001
                    with self._cv:
                        self._error = "the screen could not be read"
                        self._cv.notify_all()
                    time.sleep(0.25)
                    continue
                self.captured += 1
                digest = zlib.adler32(raw)
                key = (w, h, width)
                if digest == last_hash and key == last_key:
                    self.unchanged += 1
                else:
                    last_hash, last_key = digest, key
                    if pending is not None:
                        pending.result()           # at most one compression in flight; this capture overlapped it
                    pending = pool.submit(self._compress_and_publish, w, h, raw, width)
                spare = MIN_FRAME_PERIOD - (time.monotonic() - t0)
                if spare > 0:
                    time.sleep(spare)
        finally:
            pool.shutdown(wait=False)

    def _compress_and_publish(self, w: int, h: int, raw: bytes, width: int) -> None:
        z = zlib.compress(raw, 1)
        with self._cv:
            if width != self._width:
                return                              # asked for another size meanwhile
            self._seq += 1
            self._frame = Frame(w, h, z, True, self._seq)
            self._cv.notify_all()


_streamer = Streamer()


def streamer() -> Streamer:
    return _streamer
