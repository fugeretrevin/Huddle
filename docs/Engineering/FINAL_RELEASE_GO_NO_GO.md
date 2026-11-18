# Final release go / no-go

As of 2026-11-16 23:30. The deadline is 2026-11-18 12:00 PT. **Recommendation: GO for filming on rc17 now.** The final
submission build is a release-signed rc18 = rc17 + P-1 (receipt flight guard), built after the owner's worn session.

## Gate status

| Gate | Needed for | Status | Evidence |
|---|---|---|---|
| Build compiles; EditMode | everything | **GO** | 1636/1636 |
| Backend | live film segment | **GO** | pytest 371 passed / 1 skipped |
| Play Mode regression (13 groups) | everything | **GO** | Preflight 13/13; Apply change 0 real errors after P-1 |
| Live real work (Meta model) | shots 3, 4, 8 | **GO** | 13/13: 6 cards, 40 tests, Apply change worked (32 passed after 2 real fixes), independent re-runs matched |
| rc17 installed, hash-verified | owner session | **GO** | `BD0204F4…` on the Quest |
| Worn acceptance of rc17 (B1–B8) | filming, submission | **PENDING (owner)** | `RC17_FINAL_HARDWARE_QA.md` |
| Live flow from the Quest UI (E) | shots 3, 4, 6, 8 | **PENDING (owner)** | backend side proven; Quest-side not yet |
| On-device performance rc17 + 15-min soak + restart | submission | **PENDING (owner)** | rc15 was 72.2 fps avg |
| Release signing | **upload** | **NO-GO until the owner creates the key** | `scripts/make_release_keystore.ps1`, then `RunSigned` |
| Video | submission | TO FILM | `FINAL_VIDEO_SHOT_LEDGER.md` |

## Open issues

- **P0**
  - Release signing (owner key). Nothing else is known.
- **P1**
  - None known.
  - Watch in the worn session for:
    - the rc16 desk re-layout (agents walking around the turned desks);
    - the nameplates' tilt;
    - voice on the real mic.
- **P2** (do not touch before submission)
  - The in-engine elevator preset shows a thin strip of empty Editor background (crop it in the edit).
  - The Editor's OVRPlugin warning flood.
- **Risks**
  - **Model code on the PC:** a static filter, not an OS sandbox (`FINAL_LIVE_DEMO_EVIDENCE.md`).
  - **Low PC memory:** 0.5–0.6 GB free with Unity open. Close Unity Play Mode and browsers before live filming.
  - **Live card failures:** a live card can still fail honestly. The film shows real outcomes only.

## Rollback

| If… | Then… |
|---|---|
| A rc16/rc17 change fails worn (desk layout, nameplates, voice, Apply change) | Report it. A minimal forward fix is preferred, then rc18. If that's unsafe, roll back to rc15 `047DD408…` (worn PASS, no real-work footer) or rc16 `D25583D1…` and document why |
| rc18 (signed) misbehaves | It has the same code as rc17 + P-1, so compare against rc17 first |

**Protected APKs:** rc13 `3DAF3AE6`, rc14 `3CD37ED9`, rc15 `047DD408`, rc16 `D25583D1`, rc17 `BD0204F4`. The builder refuses
their paths and any existing file.

## Final build procedure (after the owner's worn session and key)

1. Fix confirmed P0/P1 only. Run `scripts\preflight.ps1` (backend OFF) and get 13/13.
2. `unity command script run --file AgentScripts/BuildApkTo.cs --entry BuildApkTo.RunSigned --args '[\"Builds/MuseDesk_rc18_release.apk\", 2]' --timeout 2400`
   (use a versionCode above any earlier upload).
3. Hash, install (`adb install -r` needs an uninstall first, because the signature changes from debug to release: this
   wipes the app's saved panel layout and mic preference), verify the hash on the device.
4. The owner smokes B1 + B2 + leaving Wall mid-receipt on rc18. Then freeze, and the owner uploads.
