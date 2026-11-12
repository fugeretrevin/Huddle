# MuseDesk — Demo Script & Operator Guide

MuseDesk is a Meta Quest 3 mixed-reality app for supervising real AI agents. Three mini
coworkers — **Researcher, Developer, Tester** — live on your real desk. When one needs a human
call, it walks up, raises an exclamation mark, and hands you a graspable **decision paper**. You
read it, approve or reject, and that drives a sandboxed backend workflow. The human approval gate
is the whole product: agents never act on a gated decision until a person says yes.

There are **two modes**, and the app always labels which one is live:

| Mode | What it is | Needs |
| --- | --- | --- |
| **Offline demo** (public APK) | A fully self-contained, clearly-labeled simulation. Plays the *same event shapes* as the real backend from a bundled fixture, so the on-headset experience is identical. | Nothing — no PC, no network, no keys. |
| **Live** | The headset drives the real sandboxed backend (agents write real files + run real pytest behind the approval gate). | Dev PC running the backend + `adb reverse`. |

> **Invariant:** every artifact and event carries `source_mode: live \| demo`, and the on-screen
> mode badge reflects the *resolved* gateway. The demo is never presented as live work.

---

## The ~90-second on-head walkthrough (what the judge sees and does)

1. **Put on the headset.** The room is passthrough MR. Three cute low-poly agents stand at their
   floor workstations off to the side. A mode badge reads **DEMO · Simulated work** (or **LIVE**).
2. **A task kicks off.** The agents begin working — researcher comparing options, developer
   scaffolding, tester running a baseline — with status text above each station. *(In the offline
   build this starts on its own; see DemoDirector below.)*
3. **An agent needs you.** The Researcher turns, **walks up to you**, an **exclamation mark** pops
   above its head, and it **holds out a decision paper** (white, bold, facing you).
4. **Grab the paper.** Reach out and pinch/palm-grab it. It detaches, **expands**, and shows the
   full decision: title, summary, rationale, recommendation, risk level.
5. *(Optional)* **Ask about it** — push-to-talk voice question, spoken/caption answer. *(Voice is
   token-gated; present only in the voice build — see note below.)*
6. **Decide.** Poke the big **Approve** (or **Reject**) button on the card.
7. **The world reacts to your call:**
   - The decision paper gets an **APPROVED / REJECTED stamp**.
   - The **Developer** picks up the work; an **artifact** (code change + test results) is produced
     and can be opened in the **artifact viewer**.
   - A **handoff** plays: the result is carried agent-to-agent (researcher → developer → tester).
   - The **software factory** animates to show work flowing through.
   - On completion a token lands on the **project display shelf**; enough completions reveal a
     **trophy / award plaque**.
8. **Back to calm.** The agent walks back to its station and the board returns to a ready state.

### Supporting interactions to show if time allows
- **Task board** (off-desk): cards for Backlog / Working / Needs me / Completed. **Drag a card to
  an agent** to assign it; poke a card to pause/resume or cycle priority; poke **＋ New task**.
- **Focus mode** (poke toggle): routes non-urgent updates to an inbox badge instead of walking an
  agent up — but *never* suppresses a blocking decision.
- **Explain** (poke toggle): raises an **architecture diagram** (Frontend → API → Auth → Database).

---

## Running the offline demo (public build)

The offline build is self-driving via **`DemoDirector`** (`Assets/MuseDesk/Scripts/Demo/DemoDirector.cs`):

- It runs **only in demo mode** and stands down completely in live mode.
- By default it does **not** answer decisions for you — a pending decision **waits for a real human**,
  which is exactly what a judge should experience.
- `SessionController` already auto-starts the sample task, so the flow begins a few seconds after
  launch with no input.

**Booth / kiosk option (opt-in, off by default):** set `autoResolveIfIdle = true` (and optionally
`loop = true`) on the DemoDirector component. An untouched decision is then auto-approved after a
generous idle window (`idleResolveSeconds`, default 30 s) so an unattended loop still completes. It
still goes through the normal gateway path, stays `source_mode=demo`, and no-ops the instant a human
has already answered. Leave these **off** for an attended, hands-on demo.

Install & launch:
```
adb install -r MuseDesk.apk
# then launch "MuseDesk" from the Quest app library
```

---

## Running live (dev PC)

1. Start the backend (producer = live):
   ```powershell
   cd backend
   $env:MUSEDESK_PRODUCER="live"; $env:MUSEDESK_REQUIRE_PAIRING="false"
   .venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8765
   ```
2. Bridge the headset to the PC:
   ```
   adb -s <device> reverse tcp:8765 tcp:8765
   ```
3. **Relaunch the app.** `RuntimeSettings` points at `http://127.0.0.1:8765` with
   `preferOfflineDemo = false`, so it auto-connects; `POST /v1/sessions` returns `mode: live_local`
   and the badge shows **LIVE**. DemoDirector detects live and stays inert.

---

## Notes / honesty markers

- **Voice** interaction is **token-gated** (needs a Wit.ai provider token) and ships only in a
  separate voice build via the two-build rule; the committed offline judge build is voice-free.
- All runtime interaction (grab, poke, billboards) only truly exercises on a headset — Editor Play
  Mode stalls without an HMD. Numbers like "60 FPS" and the exact feel of grab/handoff should be
  confirmed on-device, not from the Editor.
- Features described above are implemented in the project; anything a given build omits (e.g. voice)
  is called out at launch by the mode/feature labels rather than hidden.
