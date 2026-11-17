"""rc20 (owner, worn rc19): "we need a way for the computer screen to be a panel, rather than strictly through
passthrough". The PC's primary screen, scaled down, as raw BGRA pixels for the headset's screen panel.

Windows only, standard library only (ctypes + GDI): no new packages. The frame is read on request and never stored.
It is served ONLY when the backend was started with MUSEDESK_SCREEN_SHARE=true (the owner's own launcher sets it), only
to a paired session (bearer token), and only on 127.0.0.1 (the Quest reaches it over USB with adb reverse).
"""
from __future__ import annotations

import os
import sys
import threading
import zlib
from dataclasses import dataclass

ENV_FLAG = "MUSEDESK_SCREEN_SHARE"
MAX_WIDTH = 1600
MIN_WIDTH = 320


def enabled() -> bool:
    return os.environ.get(ENV_FLAG, "").strip().lower() in ("1", "true", "yes", "on") and sys.platform == "win32"


@dataclass
class Frame:
    width: int
    height: int
    data: bytes          # BGRA, top row first; zlib-compressed when ``compressed``
    compressed: bool


_lock = threading.Lock()
_dpi_set = False


def capture(width: int = 1280, compress: bool = True) -> Frame:
    """The primary screen scaled to ``width`` (aspect kept), with the mouse pointer drawn in."""
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
