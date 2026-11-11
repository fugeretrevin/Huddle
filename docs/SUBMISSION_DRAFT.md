# MuseDesk — submission draft (TASK-08)

Working draft for the Meta VR Start Developer Competition 2026 (Productivity / Best Agentic
Interaction). **Replace the working title before submission.** Deadline: **Nov 18 2026, noon Pacific.**

> Honesty rule (AGENTS invariant #1): the video and copy must never present scripted/offline
> demo output as live agent execution. We show both modes and label which is which.

## 1. One-line pitch

**MuseDesk puts three tiny AI coworkers on your desk in mixed reality — they do real sandboxed
work and physically hand you a paper whenever they need your decision.**

## 2. Inspiration

Supervising multiple AI agents today means juggling terminals, logs, and chat threads — you
constantly poll for "who's blocked?" and "does this need me?". MuseDesk makes the *state of
autonomous work* spatial and calm: agents work quietly at their stations, and only a genuine
blocking decision walks up to you as a graspable document.

## 3. How it works (demo journey)

1. Put on the Quest 3 — passthrough shows your real room; three mascots are at a workstation.
2. A task starts ("build an authentication flow"). The researcher, developer, and tester each do
   independent work; the UI clearly shows **LIVE** or **DEMO** mode.
3. The researcher hits a decision it can't make alone, raises an **exclamation point**, and walks
   up to you holding a **decision paper**.
4. You grab the paper, expand it, read the recommendation/rationale/risks, and **ask "why?"**.
5. You **approve / reject** with your hand. The UI shows *Saving…* and only confirms once the
   backend acknowledges — a hand gesture is never treated as approval on its own.
6. In **live mode** the approval makes the developer **actually write code and the tester actually
   run pytest** in an isolated sandbox; the result paper shows **real test counts**. In **offline
   demo mode** the same flow runs from a bundled script, clearly labeled "simulated."

## 4. How it's built (real architecture)

- **Quest client:** Unity 6 (6000.6.8f1) + Meta XR All-in-One SDK, OpenXR, URP; passthrough,
  hand tracking + Interaction SDK (poke/grab), MR Utility Kit. One `IAgentGateway` with a
  `LiveAgentGateway` (REST + WebSocket) and a `DemoAgentGateway` (bundled fixture) behind a single
  event reducer, so the experience is identical online and offline.
- **Windows companion backend:** Python FastAPI + WebSocket, SQLite durable events (persist before
  broadcast, monotonic per-session `seq`, snapshot + gap-free replay), per-session bearer auth,
  idempotent decision responses with HTTP 409 on conflict.
- **Real approval gate + sandboxed workers (live mode):** each session gets an isolated copy of a
  disposable sample repo; a `WorkspaceGuard` canonicalizes paths (rejects `..`/symlink escape) and
  the only process ever spawned is pinned `pytest`. On approve, the developer writes real files and
  the tester runs real tests; artifacts are stamped `source_mode=live`. Reject writes nothing.
- **Safety:** localhost-only cleartext (adb reverse), no provider keys in the APK, short-lived
  session tokens, secret redaction + capped diffs in artifacts.

## 5. What is genuinely live vs. demo

- **Live:** real file writes + real pytest in a sandbox, driven by your hand approval over the
  network. (Shown connected to the dev PC via USB.)
- **Demo:** a deterministic, clearly-labeled script bundled in the APK so judges can play the full
  interaction with no PC. The research *text* is deterministic (disclosed as `provenance`), not
  passed off as model reasoning.

## 6. Meta-native value

Physical desk surface placement, hand grab + poke on a real document, spatial characters that walk
and react, progressive-disclosure paper you expand to read — interactions that only make sense in MR.

## 7. Challenges & learnings

- Getting a single, correct Meta interaction rig (avoiding duplicate hands) and hand-pokeable UI.
- Making state legible in passthrough: white+outlined text that faces the user, readable backdrops.
- Keeping the backend the source of truth so a gesture never fakes an approval; idempotent, replayable.
- A real end-to-end test caught a sandbox-isolation bug (pytest inheriting a parent config) that
  unit tests missed — the kind of thing only a real HTTP+WS round trip surfaces.

## 8. Future work

- Cute rigged character models + animations (currently stylized primitives with a procedural
  arm-swing walk cycle).
- Voice (implemented, live-mode only): push-to-talk "approve / reject / why?" via the Meta Voice
  SDK (Wit.ai). Because Wit is a cloud recognizer, voice can't run in the standalone offline demo,
  so it's a connected-mode enhancement and the judged offline APK stays hands-only. The client
  token ships in a voice build, so the offline competition APK is built voice-free.
- A hosted live sandbox so judges can try live mode without the developer's PC.
- More agent connectors and a task board; usability study vs. terminal-based agent supervision.

## 9. Video storyboard (≤ 2:50, public YouTube/Vimeo)

| Time | Shot |
|---|---|
| 0:00–0:20 | Real desk in passthrough; three mascots at their station; hands visible (no controllers). |
| 0:20–0:45 | Start the task; agents change state at their stations (typing/reading/examining). |
| 0:45–1:15 | Exclamation point; researcher walks up and offers the paper. |
| 1:15–1:45 | Grab, expand, read; ask "why?" and get the contextual answer. |
| 1:45–2:10 | Approve by hand; "Saving… → Approved" only after the server confirms. |
| 2:10–2:35 | **Live:** cut to the PC — real files written, real pytest passing — then back to the result paper with real counts. Label "LIVE". |
| 2:35–2:50 | One line of value; show the labeled offline demo running with no PC. |

## 10. Pre-submit checklist (see DEPLOYMENT_AND_SUBMISSION.md)

- [ ] Replace working title; final package id (not the `com.musedesk.quest` placeholder).
- [ ] Build signed arm64 release; upload to a release channel named exactly `Competition`.
- [ ] Offline demo works on a clean Quest with no PC; mode clearly labeled.
- [ ] Live mode demonstrated truthfully in the video; no scripted-as-live claims.
- [ ] Public video < 3:00; Devpost form complete before the deadline.
- [ ] No API keys / PC-local IPs / unlicensed assets in the public build.
