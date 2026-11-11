# MuseDesk — Release Candidate checklist & build evidence

_Living QA document (TASK-07 item 7). Updated 2026-11-11. Competition deadline: 2026-11-18, noon Pacific._

This records the verified build configuration and the exact steps to produce, install, and validate
a judge-ready standalone APK. Facts marked ✅ were verified in-session on this machine; items marked
⏳ require the Quest on-head and are called out honestly (AGENTS invariant #9: Editor checks are not
proof of passthrough/hands/perf).

---

## 1. Build environment (exact versions)

| Component | Version | Source |
|---|---|---|
| Unity Editor | **6000.6.8f1** (237b6593792b) | `ProjectSettings/ProjectVersion.txt` |
| Meta XR SDK (all/core/interaction/MRUK/platform/audio/haptics) | **209.0.0** | `Packages/packages-lock.json` |
| Meta XR Voice SDK | **85.0.1** | installed — voice is token-gated, not package-gated |
| URP (+ config) | **17.6.0** | packages-lock |
| OpenXR | **1.18.0** | packages-lock |
| XR Hands | **1.9.0** | packages-lock |
| XR Management | **4.7.0** | packages-lock |
| Input System | **1.20.1** | packages-lock |

Do not bump these outside a coordinated integration PR (AGENTS invariant #6).

## 2. Quest build configuration ✅ (verified via PlayerSettings)

| Setting | Value | Why it matters |
|---|---|---|
| Scripting backend | **IL2CPP** | required for Quest store/sideload |
| Target architecture | **ARM64** | required for Quest 3 |
| Graphics API | **Vulkan** | Meta-recommended for Quest 3 |
| Color space | **Linear** | correct URP rendering |
| Application ID | **com.musedesk.quest** | proper id (not DefaultCompany) |
| Min / Target SDK | **API 32 / 34** | Quest 3 compatible |
| Product name | **MuseDesk** | — |
| bundleVersion / code | **1.0 / 1** | bump code per uploaded build |

> Company name is still `DefaultCompany` — cosmetic only (app id is already correct); optionally set before final upload.

## 3. Pre-build verification ✅

- **C# compile:** 0 console errors, `compilationFailed: false` (unity console status).
- **Backend tests:** `pytest -q` → **15/15 passing** in `backend/.venv` (test_api.py + test_live_workers.py).
  One benign StarletteDeprecationWarning (httpx), not a failure.
- **Scene integrity:** one XR rig (no duplicate OVRCameraRig / Main Camera) — AGENTS invariant #5.
- **Mode honesty:** `SessionController.AdoptGateway` sets the on-screen mode badge from the *resolved*
  gateway after the live-connect attempt; a failed live connect falls through to the offline demo and
  the badge reads DEMO. The per-decision badge also carries `[LIVE|DEMO]`. No path shows LIVE without a
  real backend connection (AGENTS invariants #1/#2). ✅
- **Demo fixture labeled:** `source_mode="demo"`, `mode="demo"` (DemoFixture.cs / offline_demo_fixture.json). ✅

## 4. Build & install (USB-tethered Quest)

```powershell
# Build + deploy via unity-pipeline (preferred; ~15 min clean, faster incremental)
unity command editor set_auto_tick --enabled true
unity command script build run        # build + install to the connected Quest

# Live mode (optional — demo works with no PC):
#   start backend on 127.0.0.1:8765 with MUSEDESK_PRODUCER=live
#   adb reverse tcp:8765 tcp:8765      # route device localhost -> PC backend
```

Cleartext is scoped to 127.0.0.1/localhost only (Android network-security-config); no API keys in the
APK (AGENTS invariants #3, and acceptance "no private credentials embedded"). ✅

## 5. Clean-install validation (do on-head) ⏳

1. `adb uninstall com.musedesk.quest` then install the fresh APK (simulates a judge's first launch).
2. Launch with **no PC / no network**, no `adb reverse`:
   - [ ] App opens into passthrough; workspace places in front of the user.
   - [ ] Mode badge reads **DEMO · Simulated work** (never LIVE). 
   - [ ] Three agents visible at their stations; each walks up on a decision (arm-swing walk cycle).
   - [ ] Exclamation point appears; decision paper delivered within reach.
   - [ ] **Grab the paper** → it expands to full detail; release → stays put, re-faces you.
   - [ ] Poke **Approve / Reject / Ask why** → status updates only after the (demo) backend confirms.
   - [ ] One artifact/result appears; task completes. Audio cues fire.
   - [ ] Grabbable cube renders **orange** (not magenta).
3. Repeat 3 uninterrupted runs; watch for double-spawned papers or out-of-reach delivery.
4. **Live run** (with backend + `adb reverse`): approval drives real sample work; artifact `source_mode=live`.
5. Performance: capture FPS via MVDH; target **≥60** baseline (acceptance criterion).

## 6. Known limitations / still needs on-device sign-off ⏳

- **Grab gesture** (grabbable paper, arm-swing walk) verified in Editor (compiles, runs, 0 errors, correct
  geometry) but the actual hand-grab needs real hand tracking — untested on-head as of this doc.
- **Agent art** is stylized primitives + procedural walk (cute, but placeholder); real rigged models are a
  later swap (blocked on licensed assets).
- **Voice (TASK-06)** is optional and not wired: Voice SDK 85.0.1 is installed but needs a Wit.ai app token.
  All P0 flows are hands-only (AGENTS invariant #7), so this does not block submission.
- FPS / passthrough / hand-reach ergonomics are **not** proven until tested on-head.

## 7. Acceptance criteria status (TASK-07)

- [x] Offline demo path exists, labeled, hands-only, no PC — _code-complete; ⏳ final on-head pass._
- [x] Live mode receives real events + real approval (confirmed on-device earlier over adb reverse).
- [x] Reconnect/duplicate events safe (persist-before-broadcast, monotonic seq, snapshot replay; backend tests cover dedup/idempotency/409).
- [ ] Three uninterrupted runs + clean install + FPS ≥60 — ⏳ on-head.
- [x] No embedded credentials / local-only hardcoded dependency in the APK.
