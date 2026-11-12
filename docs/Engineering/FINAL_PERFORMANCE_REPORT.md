# MuseDesk final performance report

Date: 2026-11-12 (evening).

## Headline

**There are no on-device performance measurements.** The attempt to profile the Quest build was blocked by the headset's lock screen (details in `FINAL_XR_VALIDATION.md` section 1). This report therefore contains no FPS, frame-time, draw-call or memory figures, because none were obtained. It records what was changed, what is known from static analysis, and the exact procedure to take the measurements.

The 81 `VrApi` lines captured during the blocked attempt came from the lock screen compositor, not from MuseDesk. They are not reported.

## 1. What was changed without measurement, and why that was judged safe

The instruction for this pass was to measure before optimizing. These changes were made anyway because each one removes work with no visual or behavioural change, so there is nothing to trade off:

| Change | Reason | Risk |
|---|---|---|
| MRUK world lock off | The device log from an earlier build showed a warning with a stack trace logged every frame (241 in about a minute). Off-desk placement never uses scene anchors | None expected; unconfirmed until a new device log shows zero occurrences |
| Per-frame reflection reads replaced by a typed delegate (`GrabState`) in four scripts | Each call boxed an integer per object per frame | None |
| Task-card status tint through a property block | `renderer.material` created a material instance per card | None |
| Debug readout (`GrabReadout`) disabled, grab-path logging removed | It searched the scene by name and logged twice a second | None |
| Project-owned URP asset for the Quest quality level (`Assets/Settings/URP_Quest.asset`) with main-light shadows, additional lights, light cookies and LOD cross-fade off | The scene has no lights; the level previously pointed at an asset inside `Library/PackageCache` | Low. If any object looks different it will be an unlit-looking surface; none is expected because there was no light to remove |
| Physics fixed step 1/72 s, maximum step 0.1 s, interpolation on pushable props | 50 Hz physics against a 72 Hz display judders | Slightly more physics work per second (72 versus 50 steps) |

Nothing was merged, combined, or stripped on a guess.

## 2. Changes in this pass that add cost

Stated so they can be checked first if the frame rate is not stable:

| Addition | Cost |
|---|---|
| 3D keys: about 6 renderers per key, 20 keys | About 120 renderers, two shared materials plus one per cap colour, GPU instancing on |
| Thirteen pushable rigid bodies, four static furniture colliders, against about 40 hand capsules | Physics broad phase and contacts while hands are near the desk |
| `AgentGaze` and `AgentIdleLife` on three agents | Two `LateUpdate` calls per agent; targets re-chosen every 0.1 s; no allocations |
| Animator now actually plays a clip per state on three 26-bone rigs | The same three Animators were already running the idle clip |
| `GrabHoverHighlight` on the paper, two stamps and each card | One state read per frame each |
| Hover frames, halos, dock pads, guide sign, archive readout | About 20 small unlit quads and discs, most inactive most of the time |

## 3. Static picture (from files, not from the device)

Unchanged from `FULL_SYSTEM_AUDIT.md` section 5 except where noted:

- Vulkan, multiview, IL2CPP ARM64, MSAA 4x, HDR off, no depth or opaque texture, no post-processing, one rendering camera, no lights, no shadows, no particle systems.
- Roughly 50 to 75 thousand triangles in the art.
- About 520 separate renderers before this pass, about 640 with the keys. Each workstation model is 53 to 60 meshes. **Renderer count is not draw-call count**: the SRP batcher is on and most renderers share a small set of materials, so the real batch count could be far lower. It has to be read from the device.
- Foveated rendering is not enabled. It was left alone: enabling it without a device check risks a rendering fault that cannot be seen from the Editor.
- All art materials are URP/Lit in a scene with no lights. Left alone for the same reason: switching shaders changes the look and needs eyes on the result.
- Another tool added "optimized" versions of the three workstation models and the Kanban frame under `Assets/MuseDesk/Art/Models/Optimized` during this pass. They are not placed in the scene. If they are merged-mesh versions of the current models, swapping them in is the most direct answer to the renderer count, and should be done as a measured before-and-after.

## 4. How to take the measurements

### Unattended (nobody wearing the headset)

The headset must be **unlocked** first; the script cannot get past the lock screen.

```powershell
.\scripts\quest_run.ps1 -Label demo -Seconds 120
```

It overrides the proximity sensor, starts the app, waits, then writes to `Logs\quest\`:

- `vrapi_demo.txt`: the runtime's own once-per-second line. The fields that matter: `FPS=` (achieved / target), `Prd=` (prediction, ms), `Tear=`, `Early=`, `Stale=` (stale frames per second; the stutter number), `App=` (app GPU time, ms), `GD=` (guardian GPU time), `CPU4/GPU=` (CPU and GPU clock levels), `Free=` (free memory).
- `unitylog_demo.txt`: MuseDesk log lines and any exception.
- `device_demo.png`: a screen capture.

and prints the process id, the count of MRUK world-lock warnings (expected 0), the count of error-level Unity lines, and a memory summary.

With nobody wearing the headset the demo starts by itself after 40 seconds and stops at the decision, so this measures: idle office, three agents working, the walk-up, and a paper waiting. It does not measure hand interaction, the hand-offs after approval, or the factory.

Afterwards, restore the sensor: `adb shell am broadcast -a com.oculus.vrpowermanager.automation_disable` (the script leaves the override on so the run is not cut short).

### Worn (the numbers that count)

Do the checklist in `FINAL_XR_VALIDATION.md` section 4 and pull the `VrApi` lines before taking the headset off (commands at the end of that section). The interesting windows are: grabbing and holding the paper, the two hand-offs with the factory animating, the board with cards while dragging one, and ten minutes of sustained use for thermals.

For draw calls and batches, use OVR Metrics Tool on the headset (or RenderDoc on a development build); `VrApi` does not report them.

### What to record

| Metric | Source | Target |
|---|---|---|
| FPS | `VrApi` `FPS=` | Equal to the display rate (72) |
| Stale frames | `VrApi` `Stale=` | 0 in steady state; brief non-zero only at scene start |
| App GPU time | `VrApi` `App=` | Under about 11 ms at 72 Hz, with headroom |
| CPU and GPU level | `VrApi` `CPU4/GPU=` | Not pinned at the maximum |
| Draw calls, batches | OVR Metrics Tool | Unknown; the number this report most needs |
| Memory | `dumpsys meminfo com.musedesk.quest` | Stable over ten minutes |

## 5. What to do with the numbers

Only if the measurements show a problem, in this order:

1. If GPU time is the limit: swap in merged workstation and board meshes (measure), then enable foveated rendering at a low level (measure and look), then move the art materials to Simple Lit (measure and look).
2. If CPU time is the limit with hands near the desk: put the hand capsules on the existing `FingerCollide` layer and trim the collision matrix; reduce the pushable set.
3. If stale frames spike at a specific moment: read the Unity log around that timestamp first. The earlier every-frame warning was found exactly this way.

Do not combine the keys' or the cards' meshes: they move.
