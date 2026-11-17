# Guards, stale docs, state colours, lerp audit (2026-11-15)

Branch `worktree-agent-ab305aec3ce84ccbc`, based on `8c2ca85`. Nothing was compiled or run (no Editor access); every API used was read in source.

## 1. Root-motion guard

- Test: `Assets/MuseDesk/Tests/EditMode/AgentInteraction/AgentRootMotionGuardTests.cs` (assembly `MuseDesk.Tests.AgentInteraction`). Loads the three desk agent prefabs the scene instantiates (`Prefabs/Agents/Agent_*`), the three art-pipeline desk prefabs (`Art/Prefabs/Characters/Agent_*`) and the wall `Agent_Manager` prefab, and asserts every `Animator` (inactive children included) has `applyRootMotion == false`. A missing prefab is ignored, not failed.
- Runtime: `AgentMovement.Awake` now sets `applyRootMotion = false` on every Animator under the agent root. Wall avatars already force it off (`WallHabitatView`, `WallHabitatController.Interaction`), so no wall edit was needed (those files are not mine).
- Risk: the desk prefab YAML shows no `m_ApplyRootMotion` line for the `Prefabs/Agents` files, so the Animator comes from the FBX/nested prefab. If the test fails on one of those, the runtime guard still covers it; the failing prefab then needs its override rebuilt.

## 2. Stale docs

Updated to the V2 verdict (all 7 routes pass in Desk, Floor and Ceiling, min 0.017 m at r 0.080; Editor/PLAY evidence only; outer-desk deck overhang still open): `RELEASE_POLISH_CHANGELOG.md` (S9c row and remaining-defects row, plus a new commits table 363bf84..8c2ca85, 71 rows), `S9C_SETTINGS_SAFETY_UI_CHANGES.md` section 2 (history note above the findings), `IMPLEMENTATION_AUDIT.md` row C6, `FINAL_EXPERIENCE_QUALITY_CHECKLIST.md` row M-5.

## 3. State colours

| File | Finding | Action |
|---|---|---|
| `Phase2/Runtime/Board/TaskCardView.cs:567` | Card tint uses `OfficeTokens.StateColor(Data.TaskState)`. | none |
| `TaskCardView.cs:56-59` | Edge colours (`edgeIdle/Active/Pending/Refused`) are serialized public fields whose defaults are hex copies of Housing, Teal, Amber, Coral (values equal the tokens today). | report only (not mine); could default to `OfficeTokens` |
| `TaskCardView.cs:135-139` | `NoticePending/Ok/Error` are darker text-background shades, not the token values; `RoleTextOnDark/Light` copy OnPlate/Ink. | report only; deliberate darker shades for contrast |
| `Scripts/Agents/AgentView.cs:191-202` | `Style()` already uses `OfficeTokens` (Teal, Amber, Success, Coral, Neutral). | none, resolved by 2b07752 |
| `StationDropZone.cs:30-32` | Halo valid/invalid were raw RGB (0.30,0.80,0.45) and (0.85,0.35,0.30). | fixed: defaults now `OfficeTokens.Success` and `OfficeTokens.Coral` with the old alphas (0.24, 0.26). Scene-serialized overrides on existing instances are untouched. Role accents not touched. |

## 4. Lerp-by-dt audit

Scope: `Assets/MuseDesk/Scripts` and `Assets/MuseDesk/Phase2/Runtime`. Pattern: `Lerp/Slerp/MoveTowards(..., dt*k)` with a fixed endpoint.

Converted:

| File:line (before) | Before | After |
|---|---|---|
| `Scripts/Agents/RubberBandReturn.cs:116, 124` | `_offset = Lerp(_offset, zero, deltaTime*springback)` (never exactly home; position written each frame) | `SpringBack()`: same decay, factor clamped to 0..1, snaps to exact zero below 0.1 mm |

Left, with reasons:

| File:line | Pattern | Why left |
|---|---|---|
| `AgentArmNudge.cs:60,98,106`, `AgentTilt.cs:98,135,142` | Lerp by dt*k toward hand-driven target / zero | follow-the-hand; spring back already ends exactly (`mag < 1e-4` releases the layer / clears) |
| `RubberBandReturn.cs:59,114` | Lerp toward push target | follow-the-hand |
| `AgentView.cs:128-129,162,164` | Slerp arms to home / sinusoid | forbidden file; arms follow an oscillating target |
| `AgentAnimatorBridge.cs:202`, `AgentAttention.cs:897` | MoveTowards / exp lerp | forbidden files |
| `FaceUserWhileDelivering.cs:47`, `Workspace/FaceUser.cs:32` | Slerp toward the user's head | tracking a moving target, not a timed move |
| `AgentPresence.cs:544` | exp lerp of body yaw | tracking the user's head; weight (`_faceW`) uses exact `MoveTowards` and zeroes `_faceYaw` |
| `AgentGaze.cs:442-452`, `AgentIdleLife.cs:190-192` | frame-rate-independent exp smoothing | tracking / continuous modulation; residual is sub-visible and gated by weights |
| `MovablePanel.cs:404` | colour exp lerp | already snaps when within 1e-5 |
| `PlacementGuide.cs:449,453,465` | exp follow of anchor | follows the user's gaze/hand |
| `StampTool.cs:225-227` | exp return when `returnSeconds <= 0` | legacy fallback; the default timed path (`returnSeconds > 0`) is an exact eased move |
| `GrabReadout.cs:30` | fps average | not motion |
| All `MoveTowards(x, goal, dt/duration)` (`AgentSpeechBubble`, `AgentStatusChip`, `AgentPresence` 533/711, `DecisionArchive`, `DecisionPanel`, `NearUserKit`, `BoardExpander`, `TaskCardView` 293-308, `OfficeUi.ScaleEase`, `CeilingDeploymentVFXController`, `CeilingPodAudio`, `VoiceHud`, `HabitatWalker`, elevator visual) | linear to endpoint over a duration or speed | already exact (reach the endpoint in finite time) |

Nothing was moved to `MuseDesk.Motion` `MotionClock`/`Damped.Follow`: the only clear presentation-only case needed a Vector3 snap, and `Damped.Follow` is float only.
