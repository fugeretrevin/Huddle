"""rc25: the window an agent-built program opens in on the PC (owner: "make an app that does X and then we can see it in
MuseDesk").

This file is OURS (trusted). It is started by ``app_launcher.AppLauncher`` as its own process, with the program's
restricted-workspace folder as the working directory, the credential-free environment the pytest runs get, and only
after every program file in the folder passed the same static check the restricted workspace applies before a run.

Two ways a program can show itself, both generic (nothing here knows what the program does):

* ``def build_ui(ui)``: the program builds its own window from the small ``ui`` toolkit below (buttons, labels, a
  drawing canvas, text boxes, timers). The program never imports tkinter: it only gets plain functions, so it cannot
  reach the Tk interpreter (which could run commands).
* otherwise ``def main(argv=None)``: a run panel (arguments field, Run key, the printed output).

The window has a Close key in its title strip (and the normal window close / Esc). The backend talks to this process
over stdin/stdout, one line each:

    in:  ``tap <u> <v>``  a tap from the headset at (u, v), fractions of the window's inside (0..1, top-left origin)
         ``type <text>``  text for the focused (or first) text field
         ``close``
    out: ``READY <hwnd>`` the window is up (the backend crops the screen panel to it)
         ``TAP <what>``   what a tap hit
         ``ERROR <text>`` a program error (also shown in the window)
"""
from __future__ import annotations

import argparse
import contextlib
import io
import queue
import sys
import threading
import traceback
import types

import tkinter as tk

BG = "#f4f1ea"
INK = "#24272b"
STRIP = "#2c3e50"
ACCENT = "#1f8a70"
CLOSE_RED = "#c0392b"
FONT = ("Segoe UI", 13)
FONT_BIG = ("Segoe UI", 16, "bold")


def _ns(**fns) -> types.SimpleNamespace:
    """A handle the program gets: plain functions only (no widget, no Tk object reachable through it)."""
    return types.SimpleNamespace(**fns)


class Host:
    def __init__(self, title: str) -> None:
        self.root = tk.Tk()
        self.root.title("MuseDesk app: " + title)
        self.root.configure(bg=BG)
        self.root.attributes("-topmost", True)
        self.root.protocol("WM_DELETE_WINDOW", self.close)
        self.root.bind("<Escape>", lambda _e: self.close())
        self._inbox: "queue.Queue[str]" = queue.Queue()
        self._canvas_click: dict[str, object] = {}      # canvas widget path -> program handler(x, y)
        self._inputs: list[tk.Entry] = []
        self._closing = False

        strip = tk.Frame(self.root, bg=STRIP)
        strip.pack(side="top", fill="x")
        tk.Label(strip, text="  " + title, bg=STRIP, fg="white", font=FONT_BIG, anchor="w").pack(side="left", pady=6)
        tk.Button(strip, text="Close app", command=self.close, bg=CLOSE_RED, fg="white", activebackground="#e74c3c",
                  font=FONT, relief="flat", padx=14, pady=4).pack(side="right", padx=8, pady=6)
        self.body = tk.Frame(self.root, bg=BG, padx=16, pady=12)
        self.body.pack(side="top", fill="both", expand=True)
        self.status = tk.Label(self.root, text="", bg=BG, fg=CLOSE_RED, font=("Segoe UI", 11), anchor="w",
                               justify="left", wraplength=760)
        self.status.pack(side="bottom", fill="x", padx=12, pady=(0, 6))

    # ───────────────────────── program errors ─────────────────────────
    def error(self, where: str, exc: BaseException) -> None:
        line = f"{where}: {type(exc).__name__}: {exc}"
        self.status.configure(text=line[:400])
        _say("ERROR " + line.replace("\n", " ")[:400])

    def guard(self, where: str, fn, *args):
        try:
            return fn(*args)
        except Exception as exc:  # noqa: BLE001 - a program bug shows in the window, the window stays up
            self.error(where, exc)
            return None

    # ───────────────────────── the ui toolkit ─────────────────────────
    def toolkit(self, parent: tk.Widget) -> types.SimpleNamespace:
        host = self

        def pack(w: tk.Widget) -> None:
            if parent is host.body:
                w.pack(side="top", anchor="w", pady=4, fill="x" if isinstance(w, (tk.Text, tk.Entry)) else None)
            else:
                w.pack(side="left", padx=4)

        def title(text):
            host.root.title("MuseDesk app: " + str(text))

        def label(text="", size=14, bold=False, color=INK):
            w = tk.Label(parent, text=str(text), bg=BG, fg=str(color), justify="left",
                         font=("Segoe UI", int(size), "bold" if bold else "normal"))
            pack(w)
            return _ns(set=lambda t: w.configure(text=str(t)), get=lambda: w.cget("text"))

        def button(text, on_click=None, color=ACCENT):
            w = tk.Button(parent, text=str(text), bg=str(color), fg="white", activebackground=str(color), font=FONT,
                          relief="flat", padx=16, pady=6,
                          command=(lambda: host.guard("button '" + str(text) + "'", on_click)) if on_click else None)
            pack(w)
            return _ns(set=lambda t: w.configure(text=str(t)))

        def text_input(placeholder="", on_enter=None, width=30):
            w = tk.Entry(parent, font=FONT, width=int(width))
            if placeholder:
                w.insert(0, str(placeholder))
            if on_enter:
                w.bind("<Return>", lambda _e: host.guard("text input", on_enter, w.get()))
            host._inputs.append(w)
            pack(w)

            def set_(t):
                w.delete(0, "end")
                w.insert(0, str(t))
            return _ns(get=lambda: w.get(), set=set_)

        def text_box(height=8, width=60):
            w = tk.Text(parent, height=int(height), width=int(width), font=("Consolas", 12), bg="white", fg=INK,
                        relief="flat")
            pack(w)

            def write(line=""):
                w.insert("end", str(line) + "\n")
                w.see("end")

            def set_(t):
                w.delete("1.0", "end")
                w.insert("end", str(t))
            return _ns(write=write, clear=lambda: w.delete("1.0", "end"), set=set_)

        def canvas(width=600, height=400, background="white"):
            w = tk.Canvas(parent, width=int(width), height=int(height), bg=str(background), highlightthickness=0)
            pack(w)
            n = lambda v: float(v)  # noqa: E731

            def on_click(fn):
                host._canvas_click[str(w)] = fn
                w.bind("<Button-1>", lambda e: host.guard("canvas click", fn, e.x, e.y))

            return _ns(
                width=int(width), height=int(height),
                clear=lambda: w.delete("all"),
                circle=lambda x, y, r, fill="red", outline="black": w.create_oval(
                    n(x) - n(r), n(y) - n(r), n(x) + n(r), n(y) + n(r), fill=str(fill), outline=str(outline)),
                oval=lambda x0, y0, x1, y1, fill="red", outline="black": w.create_oval(
                    n(x0), n(y0), n(x1), n(y1), fill=str(fill), outline=str(outline)),
                rect=lambda x0, y0, x1, y1, fill="gray", outline="black": w.create_rectangle(
                    n(x0), n(y0), n(x1), n(y1), fill=str(fill), outline=str(outline)),
                line=lambda x0, y0, x1, y1, color="black", width=2: w.create_line(
                    n(x0), n(y0), n(x1), n(y1), fill=str(color), width=n(width)),
                arc=lambda x0, y0, x1, y1, start=0, extent=90, fill="red", outline="black": w.create_arc(
                    n(x0), n(y0), n(x1), n(y1), start=n(start), extent=n(extent), fill=str(fill),
                    outline=str(outline)),
                polygon=lambda points, fill="gray", outline="black": w.create_polygon(
                    *[n(p) for xy in points for p in xy], fill=str(fill), outline=str(outline)),
                text=lambda x, y, text, color="black", size=14: w.create_text(
                    n(x), n(y), text=str(text), fill=str(color), font=("Segoe UI", int(size))),
                on_click=on_click,
            )

        def row():
            f = tk.Frame(parent, bg=BG)
            pack(f)
            return host.toolkit(f)

        def every(ms, fn):
            ms = max(15, int(ms))

            def tick():
                if host._closing:
                    return
                ok = True
                try:
                    fn()
                except Exception as exc:  # noqa: BLE001 - a broken timer stops (it would only repeat the error)
                    host.error("timer", exc)
                    ok = False
                if ok:
                    host.root.after(ms, tick)
            host.root.after(ms, tick)

        def after(ms, fn):
            host.root.after(max(0, int(ms)), lambda: host.guard("after", fn))

        return _ns(title=title, label=label, button=button, text_input=text_input, text_box=text_box,
                   canvas=canvas, row=row, every=every, after=after)

    # ───────────────────────── a program without build_ui ─────────────────────────
    def run_panel(self, module, name: str) -> None:
        doc = (getattr(module, "__doc__", None) or "").strip().splitlines()
        ui = self.toolkit(self.body)
        ui.label(doc[0] if doc else name, size=15, bold=True)
        r = ui.row()
        r.label("Arguments:")
        args = r.text_input("", width=40)
        out = ui.text_box(height=16, width=80)

        def run():
            argv = [a for a in args.get().split() if a]
            buf = io.StringIO()
            try:
                with contextlib.redirect_stdout(buf):
                    rc = module.main(argv)
            except SystemExit as exc:
                rc = exc.code
            except Exception as exc:  # noqa: BLE001
                buf.write("\n" + "".join(traceback.format_exception_only(type(exc), exc)))
                rc = "error"
            out.set(buf.getvalue() + ("\n[exit " + str(rc) + "]" if rc not in (None, 0) else ""))
        r.button("Run", run)
        run()

    # ───────────────────────── headset input ─────────────────────────
    def start_stdin(self) -> None:
        def pump():
            for line in sys.stdin:
                self._inbox.put(line.strip())
            self._inbox.put("close")            # the backend went away: close with it
        threading.Thread(target=pump, name="stdin", daemon=True).start()
        self.root.after(30, self._drain)

    def _drain(self) -> None:
        if self._closing:
            return
        try:
            while True:
                self._handle(self._inbox.get_nowait())
        except queue.Empty:
            pass
        self.root.after(30, self._drain)

    def _handle(self, line: str) -> None:
        if not line:
            return
        cmd, _, rest = line.partition(" ")
        if cmd == "close":
            self.close()
        elif cmd == "tap":
            try:
                u, v = (float(t) for t in rest.split()[:2])
            except ValueError:
                return
            self.tap(u, v)
        elif cmd == "type":
            target = self.root.focus_get()
            if not isinstance(target, tk.Entry):
                target = self._inputs[0] if self._inputs else None
            if target is not None:
                target.insert("end", rest)
                _say("TAP typed")

    def tap(self, u: float, v: float) -> None:
        u, v = min(1.0, max(0.0, u)), min(1.0, max(0.0, v))
        rx = self.root.winfo_rootx() + int(u * max(1, self.root.winfo_width() - 1))
        ry = self.root.winfo_rooty() + int(v * max(1, self.root.winfo_height() - 1))
        w = self.root.winfo_containing(rx, ry)
        if w is None:
            _say("TAP nothing")
            return
        if isinstance(w, tk.Button):
            w.configure(relief="sunken")
            self.root.after(120, lambda: w.configure(relief="flat"))
            _say("TAP button " + str(w.cget("text"))[:40])
            w.invoke()
        elif isinstance(w, tk.Canvas):
            fn = self._canvas_click.get(str(w))
            _say("TAP canvas" + ("" if fn else " (no click handler)"))
            if fn is not None:
                self.guard("canvas click", fn, rx - w.winfo_rootx(), ry - w.winfo_rooty())
        elif isinstance(w, tk.Entry):
            w.focus_set()
            _say("TAP text field")
        else:
            _say("TAP " + w.winfo_class())

    def close(self) -> None:
        if self._closing:
            return
        self._closing = True
        _say("CLOSED")
        self.root.after(10, self.root.destroy)

    def show(self) -> None:
        self.root.update_idletasks()
        w = max(self.root.winfo_reqwidth(), 520)
        h = max(self.root.winfo_reqheight(), 320)
        sw, sh = self.root.winfo_screenwidth(), self.root.winfo_screenheight()
        self.root.geometry(f"{w}x{h}+{max(0, (sw - w) // 2)}+{max(0, (sh - h) // 3)}")
        self.root.deiconify()
        self.root.lift()
        self.root.update()
        try:
            hwnd = int(self.root.wm_frame(), 16)
        except (ValueError, tk.TclError):
            hwnd = 0
        _say(f"READY {hwnd}")


def _say(line: str) -> None:
    try:
        sys.stdout.write(line + "\n")
        sys.stdout.flush()
    except (OSError, ValueError):
        pass


def _dpi_aware() -> None:
    if sys.platform != "win32":
        return
    try:
        import ctypes
        ctypes.windll.shcore.SetProcessDpiAwareness(1)   # crisp pixels for the headset's screen panel
    except Exception:  # noqa: BLE001
        pass


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--folder", required=True)
    ap.add_argument("--module", required=True)
    ap.add_argument("--title", default="")
    a = ap.parse_args()
    _dpi_aware()
    sys.path.insert(0, a.folder)
    host = Host(a.title or a.module)
    host.root.withdraw()
    try:
        import importlib
        module = importlib.import_module(a.module)
    except Exception as exc:  # noqa: BLE001
        host.toolkit(host.body).label("The program could not be loaded.", bold=True)
        host.error("import", exc)
        module = None
    if module is not None:
        build = getattr(module, "build_ui", None)
        if callable(build):
            host.guard("build_ui", build, host.toolkit(host.body))
        elif callable(getattr(module, "main", None)):
            host.run_panel(module, a.module)
        else:
            host.toolkit(host.body).label("This program has no build_ui(ui) or main(argv) to open.", bold=True)
    host.show()
    host.start_stdin()
    host.root.mainloop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
