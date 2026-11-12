# Self-Stamp Approval — design & how-to

Let the user **physically stamp** the decision paper (pick up a stamp, press it on) instead of poking a
button. The headline human-in-the-loop gesture for the "Best Agentic Interaction" story.

## Files
- `Assets/MuseDesk/Scripts/Documents/StampTool.cs` — runtime (ns `MuseDesk.Documents`).
- `AgentScripts/BuildSelfStamp.cs` — Editor builder (`BuildSelfStamp.Run`).
- Reuses the existing `ApprovalStampController` for the authoritative APPROVED/REJECTED mark.

## Hard invariant (product thesis)
StampTool's ONLY state change is calling the real path: `SessionController.ApprovePending()` /
`RejectPending()` (verified public; both no-op unless `PendingDecisionId` is set, and the gateway stamps
`source_mode` demo/live). It **never fabricates a result**, never bypasses the backend, fires **at most
once per decision** (a `static` guard shared across both stamps), and shows only a **neutral** press cue.
The APPROVED/REJECTED word is still revealed by `ApprovalStampController` on the backend's
`decision.resolved` — so no outcome is shown before it is confirmed.

## Interaction
- Two grabbable desk stamps flanking the delivery spot: green **Approve** (WorkspaceRoot-local `(-0.22,0.02,-0.32)`),
  red **Reject** (`(0.22,0.02,-0.32)`) → world ≈ `(±0.22, 1.02, 0.18)`, on the user side of the desk, in reach.
- Grab rig copied from the proven `[BuildingBlock] Grabbable Item` cube (trigger BoxCollider, `_rigidbody` +
  base-class `_pointableElement` repointed to self, `ReRegisterInteractable` so late-activated ISDK registers).
- **StampZone**: child of `DecisionDocument` at local `(0,-0.06,-0.015)` — lower-centre of the paper's front
  (readable) face (−Z), with a subtle flat disc marker. It tracks the paper as it is handed over / expanded.

## Press detection (why it won't false-fire)
Manual proximity + dwell, **not** physics triggers (both stamp and paper are kinematic → `OnTriggerEnter`
is unreliable between them). A stamp fires only when ALL hold:
1. the stamp **is held** (reflection `Grabbable.SelectingPointsCount > 0`);
2. its **ink head is within `contactRadius` (0.06 m)** of the StampZone, and the zone is active in hierarchy;
3. contact is **sustained `dwellSeconds` (0.12 s)** — debounces a brush-past;
4. there **is** a pending decision and it hasn't already been handled (`s_handledDecision` guard);
5. not in `cooldownSeconds` (1 s).
Pressing with **nothing pending** plays a soft "nope" and makes no backend call. The held-gate means a
random nearby object can't trigger it; the dwell + cooldown + per-decision guard prevent repeats and
cross-stamp double-fire (and the gateway is idempotent per decision as a backstop).

## Feel (bare-hand → no haptics; audio + visual only)
Self-contained procedural cues (FeedbackController's `Play` is private/event-bound): a low "thunk" on a
successful stamp + a head **scale-pop**; a soft descending "nope" when nothing's pending. Stamps **glide
back to their rest pose** when released (`returnHome`) so the desk stays tidy.

## How to apply
1. Recompile (StampTool must be in Assembly-CSharp): `unity command script recompile` → poll `recompile_status`.
2. `unity command script run --file AgentScripts/BuildSelfStamp.cs --entry BuildSelfStamp.Run` (dry-run first).
3. Rebuild + install the APK.

## Verify
- **In-editor (safe):** Scene-View that two stamps sit on the desk, the StampZone disc is on the paper's
  front-lower area, and `StampTool` fields are wired (grabbable/inkHead/stampZone/controller/action/head).
- **On-head (required — Play Mode stalls without HMD):** grab a stamp; press it on the paper during a
  pending decision → the real approve/reject fires, thunk + pop play, and `ApprovalStampController` reveals
  the confirmed mark. Press with nothing pending → soft "nope", no change. Confirm it never fires on its own.

## Tuning knobs / risks
- `contactRadius` / `dwellSeconds` / stamp rest positions likely need a device nudge for comfortable reach.
- The paper's front face is assumed −Z (FaceUser forward=false convention); if the paper's readable side
  differs, flip the StampZone's local `z` sign. The zone marker can be gated to pending-only if it reads as
  clutter (small change: toggle `ZoneMarker` active on `DecisionRequested`/`DecisionResolved`).
- Poke approve/reject buttons on the paper are left intact as an accessible fallback.
