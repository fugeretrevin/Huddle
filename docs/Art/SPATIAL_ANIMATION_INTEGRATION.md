# MuseDesk — Spatial Animation, Audio & Visual Feedback Integration Guide

Runtime hooks authored in `Assets/MuseDesk/Scripts/Art/` provide clean, allocation-free, `MaterialPropertyBlock`-based control across desk, floor, ceiling, and wall-habitat surfaces. None of these scripts modify `MainMRScene.unity` or Claude-owned runtime orchestrators.

---

## 1. Procedural Studio Audio Cues (`Assets/MuseDesk/Art/Audio/`)

Generated deterministically by `ArtSource/Scripts/generate_art_audio.py` (Python stdlib `wave`/`math`/`struct`). Format: mono 16-bit PCM, `22,050 Hz`, normalized to `-6.0 dBFS` peak with DC removal and Hann edge tapers (`0` head/tail offset).

| Filename | Duration (ms) | Intended Trigger |
| :--- | :---: | :--- |
| `SFX_ButtonPress.wav` | 90.0 | Physical button, console key, or elevator call press |
| `SFX_PaperPickup.wav` | 145.0 | Lifting a decision paper or physical task card |
| `SFX_StampPress.wav` | 175.0 | Physical stamp contact on paper |
| `SFX_TaskAssign.wav` | 250.0 | Assigning / dispatching a task card to an agent |
| `SFX_ElevatorArrival.wav` | 350.0 | Wall Habitat elevator reaching target floor stop |
| `SFX_ManagerPrinter.wav` | 300.0 | `WallHabitat_TaskPrinter` printing a new card |
| `SFX_CourierDepart.wav` | 260.0 | Spatial Document Courier launch |
| `SFX_CourierArrive.wav` | 280.0 | Spatial Document Courier arrival at mailbox/desk |
| `SFX_PortalOpen.wav` | 380.0 | Large-agent portal opening & emergence beat |
| `SFX_PortalClose.wav` | 320.0 | Large-agent portal closing & return beat |
| `SFX_ProjectComplete.wav` | 460.0 | Cozy 3-note chord (`C5-E5-G5`) for project/deliverable completion |
| `SFX_TaskFailure.wav` | 300.0 | Muted gentle downward nudge (`E4 -> C4`) for failures |

---

## 2. Art Visual Controller APIs (`namespace MuseDesk.Art`)

### `MuseVfxPoolPlayer` (`Assets/MuseDesk/Scripts/Art/MuseVfxPoolPlayer.cs`)
- **Purpose:** Fixed ring-buffer pooling (`poolSize = 4` default) for burst `ParticleSystem` prefabs and spatial 3D `AudioSource` cues with zero runtime allocations.
- **Key API:**
  - `EnsureInitialized()`
  - `SetReducedMotion(bool reduced)`
  - `PlayEffect()` / `PlayEffect(Vector3 worldPosition)`
  - `PlayEffect(Vector3 worldPosition, AudioClip clipOverride, float volume = 1f)`
  - `PlayEffect(ParticleSystem explicitParticleSystem, Vector3 worldPosition, AudioClip clipOverride = null, float volume = 1f)`
  - `StopAndClear()`

### `MusePhysicalCardFeedback` (`Assets/MuseDesk/Scripts/Art/MusePhysicalCardFeedback.cs`)
- **Purpose:** Smooth analytical hover, pickup, snap, soft return, approval, and rejection feedback for documents, task cards, and stamps (timing range `0.08s`–`0.48s`). Uses cached `MaterialPropertyBlock` writes (`_BaseColor`, `_Color`, `_EmissionColor`); `Update()` early-exits when resting.
- **Key API:**
  - `ResolveReferences()`, `SetReducedMotion(bool reduced)`
  - `SetHovered(bool hovered)`
  - `PlayPickedUp()`, `PlayConfirmedSnap()`, `PlayRejectedSoftReturn()`
  - `PlayApprovalConfirmed()`, `PlayRejectionConfirmed()`

### `WallHabitatElevatorVisualController` (`Assets/MuseDesk/Scripts/Art/WallHabitatElevatorVisualController.cs`)
- **Purpose:** Drives cab vertical travel between `ElevatorStop_Lower`, `ElevatorStop_Mid`, and `ElevatorStop_Manager`, synchronized landing/cab door clips, floor indicator gem highlighting via `MaterialPropertyBlock`, and optional arrival `ParticleSystem` + audio hooks.
- **Existing Preserved API:** `MoveCabToStop(string stopSocketName, bool instant = false)`, `SetDoorsOpen(bool open, string stopSocketName = null)`, `ResolveReferences()`
- **Added Living-World API:**
  - `ActiveFloorIndex` (`int`), `SetReducedMotion(bool reduced)`
  - `PlayElevatorCall()` / `PlayElevatorCall(int floorIndex)`
  - `PlayElevatorArrival(int floorIndex)`
  - `PlayDoorsOpen(string stopId)`, `PlayDoorsClose()`
  - `HighlightFloorIndicator(int floorIndex)`

### `WallHabitatManagerDelegationVisualController` (`Assets/MuseDesk/Scripts/Art/WallHabitatManagerDelegationVisualController.cs`)
- **Purpose:** Attached directly to `WallHabitat_TaskPrinter.prefab` with `optionalPrintParticles` (`VFX_Blueprint_Print`) bound; drives `Printer_Idle`, `Printer_Queue_Pulse`, `Printer_Print_TaskCard`, `Printer_Dispatch_Card`, optional `Agent_Manager` review/delegate states, MPB status orb tint, and optional audio cues. Non-looping idle return (`autoReturnToIdleSeconds = 1.4f`).
- **Key API:** `ResolveReferences()`, `SetReducedMotion(bool reduced)`, `PlayManagerReview()`, `PlayTaskCardPrinted()`, `PlayDelegationComplete()`, `ResetIdle()`.

### `WallHabitatAmbientLifeVisualController` (`Assets/MuseDesk/Scripts/Art/WallHabitatAmbientLifeVisualController.cs`)
- **Purpose:** Calm cosine MPB emission modulation (`pulsePeriodSeconds = 3.2s`) on workstation `LampBulb*` / `CabCeilingLamp*` and `MonitorScreen*` / `DisplayPanelScreen*` / `MiniMon_Screen*` renderers ONLY while `workActive == true` and `reducedMotion == false`.
- **Key API:** `ResolveReferences()`, `SetWorkActive(bool active)`, `SetReducedMotion(bool reduced)`.

### `SpatialQualityPresetAdapter` (`Assets/MuseDesk/Scripts/Art/SpatialQualityPresetAdapter.cs`)
- **Purpose:** Unified quality tier controller (`ArtEffectQualityTier { High, Medium, Low }`) bridging `CeilingDeploymentVFXController`, `WallHabitatLargePortalVisualController`, `DocumentCourierController`, `MuseVfxPoolPlayer`, and standalone `ParticleSystem` arrays.
- **Key API:** `ResolveReferences(bool includeScene = false)`, `ApplyQualityTier(ArtEffectQualityTier tier)`, `SetQualityTier(ArtEffectQualityTier tier)`, `SetParticlesEnabled(bool enabled)`.

---

## 3. Agent Animator Bridge Compatibility Notes

Preserved untouched in `Assets/MuseDesk/Scripts/Agents/AgentAnimatorBridge.cs`:
- `MuseDesk_Agent.controller` and `MuseDesk_WallAgent.controller` retain exact Base Layer parameter `MoveSpeed` (`float`) and all state names (`Idle_Breathing`, `Walk_Loop`, `Offer_Document_Present`, `Offer_Document`, `Offer_Document_Release`, `React_Approve`, `React_Reject`, `Handoff_Give`, `Handoff_Receive`, `React_Completion_Cheer`, `Turn_Left`, `Turn_Right`, etc.).
- New layers `UpperBody` (index `1`) and `HeadGaze` (index `2`) default to `weight = 0.0f` and sit on empty pass-through defaults until runtime raises layer weights.
