# Integration contract (as implemented today)

Date: 2026-11-15. Branch `feature/living-world-polish`. This records the shared decisions as they exist in code now. It does not change them. File paths were confirmed with Grep or Read on 2026-11-15. Items marked "unverified" were taken from docs only.

## 1. Agent identity

- **Logical role ids** are the strings `researcher`, `developer`, `tester` (`backend/tests/test_api.py:21`, `Assets/MuseDesk/Scripts/Networking/DemoAgentGateway.cs:143-145`) and `manager` (`backend/app/phase3/manager.py:70`). Unity and the backend use the same strings. Do not rename them. The contract rule is in `docs/AGENTS.md` invariant 4.
- **Presentation is not identity.** The Wall Manager reuses the Developer model with a cap and badge (`docs/Engineering/WALL_HABITAT_STATUS.md`, "The art is proxy art"). The logical id stays `manager`. Art swaps must not change the id.
- Unity side: `AgentId` enum location unverified (not grepped).

## 2. Movement ownership (one writer per transform)

Rule: `docs/Polish/MOTION_AND_INTERACTION_DESIGN_SYSTEM.md` section 1, rule 7. The workspace director owns world pose. A walker or locomotion controller owns a character root. The Animator owns bones. Gaze and IK run after it in LateUpdate.

Code evidence:
- `AgentMovement.cs:163` `TurnInPlaceDirection` (S9a). Turn-in-place is decided there, and the animator reads it at `AgentAnimatorBridge.cs:282` through `DeskTurnRule.Select`. The animator does not write the root.
- `AgentMovement.cs:189` clears a corner turn when the turn is cancelled.
- `AgentGaze.cs:160` is the single writer of the Head bone for gaze (comment says so).
- Wall walker: `Assets/MuseDesk/Scripts/Spatial/Core/Wall/HabitatWalker.cs:42` (velocity profile, turn in place, early lease).
- Held objects belong to their grab owner. Second writers are bugs (audit A, `docs/Polish/IMPLEMENTATION_AUDIT.md` M10).

S9b (`docs/Polish/S9B_CHARACTER_AUDIO_CHANGES.md`): `Turn_Left/Right` come from `TurnInPlaceChanged`. Hand reactions stay quiet while the user holds something. These do not write the root.

## 3. Hand ownership

Priority, lower number wins: `Assets/MuseDesk/Scripts/Spatial/Core/HandOwnership.cs:26` (`HandOwner`) and `:29` (`HandClaim`):

`Holding (1) > CloseInteraction (2) > AgentSelect (3) > Placement (4) > Summon (5) > Ambient (6)`

- Holding and CloseInteraction always beat any far claim: a paper, stamp or card pinch is never taken.
- `HandClaimRegistry` at `HandOwnership.cs:95` is owner-aware. Equal claims from two arbiters do not both succeed (S2, `docs/Polish/S2_INTERACTION_CHANGES.md`).
- `HandGateSignal.Cancelled` (`HandOwnership.cs:45`) is never a release. Tracking loss cancels a card drag. It does not count as a drop (`IMPLEMENTATION_AUDIT.md` I1).
- Voice gesture and summon ray check the claim before they open (I5, I6).

## 4. Task state is backend-authoritative

- Task states come from `backend/app/models.py:41` (`TaskState`). Unity shows a state only after an acknowledged backend event.
- Animations follow acknowledged state. A visual never implies backend success (`MOTION_AND_INTERACTION_DESIGN_SYSTEM.md` rule 6).
- Wall presence is derived from the state: `HabitatPresence.FromTaskState` (`IMPLEMENTATION_AUDIT.md` C1; not re-grepped).
- Each event carries `source_mode` (`backend/app/orchestrator.py`, for example lines 124, 200, 330). This is invariant 1 in `docs/AGENTS.md`.

## 5. Document lifecycle

- A paper or card is delivered by `Assets/MuseDesk/Scripts/Documents/DocumentDeliveryController.cs:14`. It fades or scales with `panelHide` (`MOTION_AND_INTERACTION_DESIGN_SYSTEM.md`, M9). It is never hidden while held.
- The decision is sent once. The paper shows "Saving" until the result comes back. A cancelled Respond clears "Saving" (S2).
- Approve and Reject go through one guard for all entry points (I3).

## 6. Provenance labels (exact strings)

| Where | String | Source |
|---|---|---|
| Plan or deliverable, template, no model | `[SCRIPTED DEMO] No model was used.` | `backend/app/phase3/runner.py:86` |
| Runner label, no model | `SCRIPTED DEMO: no model was used.` | `backend/app/phase3/runner.py:257` |
| Runner label, model provider | `Model-written, no tools: nothing was run or applied.` | `backend/app/phase3/runner.py:256` |
| Plan template note | `SCRIPTED DEMO: template plan for the offline demonstration. No model was used.` | `backend/app/phase3/planner.py:14` |
| /btw reply, demo | `[SCRIPTED DEMO] ` prefix | `backend/app/phase3/side.py:206` |
| Enum value | `SCRIPTED_DEMO` (underscore, in JSON) | `backend/tests/phase3/test_p3_side.py:167` |
| Mode on screen | `LIVE` / `SCRIPTED DEMO` on dock, plaque, paper, receipt, bubble, voice HUD | `BuildTaskDockV2.cs:447`, `BuildOfficeV2.cs:20`, `BuildPaperV3.cs:634`, `S9C_SETTINGS_SAFETY_UI_CHANGES.md` |

Rule (`docs/AGENTS.md` invariant 1): scripted or template output must not be presented as live coding or research. Whether every surface follows this rule is not fully verified.

## 7. Feature-flag policy

- One asset: `Assets/MuseDesk/Resources/ReleaseConfig.asset`, class `Assets/MuseDesk/Scripts/Config/MuseDeskReleaseConfig.cs`. Applied at start-up by `ReleaseConfigApplier.cs` (order -10000).
- Every Wall flag is OFF in the committed asset. Competition build keeps every Wall flag OFF (`docs/Engineering/WALL_HABITAT_RELEASE_FLAGS.md`).
- Dependencies are enforced in code, not only in docs (`ReleaseConfigApplier.cs:84-88`).
- Each flag is reversible: components are disabled and their state restored (`ReleaseConfigApplier.cs:13-14`).
- Backend has separate env gates (`MUSEDESK_P3_*`). Unity and backend flags do not depend on each other.
- Switching a validation build: `AgentScripts/WallValidationConfig.cs` `On()` / `Off()`. Rebuild with `Off()` before any submission.

## 8. Error and cancel semantics

- Every visual transition can be cancelled. `MotionClock.Cancel()` stays where it is and reports Cancelled. `Finish()` shows an authoritative state now (`MOTION_AND_INTERACTION_DESIGN_SYSTEM.md` rule 3).
- Hitches cannot teleport: each step is capped at `MotionSettings.MaxStep` (1/20 s) (rule 4).
- Idempotency keys: card and decision commands use a stable key per action (`LiveAgentGateway.cs:70, 81`; `WallBackendGateway.cs:215, 262, 316, 446`).
- Gap: `FocusModeController.cs:164` and `ArchitectureDiagramController.cs:109` build the key from a counter (`++_idem`) on each call. That gives a new key per call, so a retry is not deduplicated. Not covered by S2 (which named cards and decisions). Unverified whether it matters.
- Tracking loss during a drag cancels the drag. It does not assign (`IMPLEMENTATION_AUDIT.md` I1).
