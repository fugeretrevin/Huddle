# UX and physical interactions specification

## Design principles

- **Spatially meaningful:** task ownership and state are represented by agents and stations; decisions arrive as manipulable documents.
- **Hands first:** no controller required from start to finish.
- **Calm by default:** routine activity stays at stations; interruptions occur only on genuine blocking decisions.
- **Progressive disclosure:** one-line summary → expanded rationale → sources/diffs.
- **Agency and trust:** every action has clear effect, status, provenance and undo/cancel when feasible.
- **Comfort:** seated, small movements, no walking across room, text not tiny, no sudden motion toward face.

## Flow A: initial entry

1. Start in passthrough. A short card: **“Meet your tiny AI team.”**
2. Request spatial-data permission only to place the workstation. If denied, offer **Place a floating workspace** and let user position a tray with their hand.
3. Show workstations with characters idle; each has readable role label.
4. Show **Connect to PC** (optional) and **Try offline demo**. Default to offline demo when no reachable configured backend.
5. Only call mode **LIVE** after authenticated health/session handshake succeeds. Offline always displays **DEMO** in a prominent but not intrusive location.
6. `Start sample task` appears on central board.

## Flow B: task and approval

| Step | User sees | Hand control | Backend effect |
|---|---|---|---|
| Task starts | Characters begin role-specific work, status strip updates | none | task created and workers scheduled |
| Need input | Researcher carries white page to delivery zone | grab/pinch | none yet |
| Inspect | Small paper → larger legible panel | grab + expand control | none |
| Ask why | One-question UI opens with current decision context | keyboard-in-Quest UI, optional push-to-talk | question request; no decision change |
| Decide | Approve / Reject / Ask for changes buttons | direct poke or pinch | decision command with idempotency key |
| Await response | “Sending…” and subtle pending state | disabled controls until reply | server validates state and persists |
| Confirm | “Approved” stamp animation + agent rejoices | no extra input | server event says resolved; dependent work released |
| Completion | Dev/test status changes; completed report arrives | grab or inspect report | artifact metadata from real or demo gateway |

## Critical document states

- `offered`: no initial clutter, clear single-line recommendation; hand-grabbable object.
- `held`: follows hand naturally, cannot be accidentally approved via back-side interactions.
- `expanded`: large stable panel; hand buttons; scrollable detail; reset button.
- `responding`: “Waiting for confirmation...” and disabled duplicate actions.
- `resolved`: visually stamped and archived; show actual backend `approved`/`rejected`.
- `error`: original choice remains available with human-readable retry message.

## Expanded document information architecture

```text
Decision #D-102 [BLOCKING] [LIVE / DEMO]
Choose authentication approach
“Use OAuth instead of storing passwords?”

Recommendation: APPROVE OAUTH
Why: fewer password-handling obligations for prototype
Options: [OAuth] [Local password]
Risks: third-party dependency, setup complexity
Impact: Developer task DEV-14 awaiting decision
Evidence: research notes / linked source / changed files

[ASK WHY] [APPROVE] [REJECT] [REQUEST CHANGES]
```

Never use a legal-signature appearance or imply the handwritten mark has legal significance. A physical ink stamp is a playful confirmation animation after actual server confirmation. A controller-free accessible button remains mandatory.

## Interruption policy

| Priority | Behavior |
|---|---|
| Routine update | Station animation and compact status text only |
| Nonblocking completion | Place report in inbox; no approach |
| Blocking decision | One agent approaches delivery zone; subtle sound only |
| Critical/unsafe request | Explicit warning + second confirm; MVP does not execute destructive actions |

Provide **Focus Mode** toggle: noncritical reports go to inbox; one blocking approval may queue a visual indicator. No sound spam. Agent may pause rather than walking until focus mode ends.

## Interaction affordances

- Pinch pickup: paper highlights when a hand is near and eligible.
- Scale: use a large **Expand** affordance (P0) and optional two-hand pinch-to-scale (P1).
- Approval: clear large button/stamp hit area; user must intentionally interact, never a background auto-approve.
- Reposition: `Move Workspace` affordance attaches tray temporarily to hand, then lets user release it.
- Accessibility: alternative UI for fine motor gesture; text question path independent of voice; show voice transcript/captions if enabled.
- Sound: quiet footsteps/notification sound localized to delivery point, never mandatory for conveying state.

## UI copy for implementation

- Launch: `Your AI team is ready.`
- Mode labels: `LIVE · Connected to desktop` and `DEMO · Simulated work`.
- No PC: `Desktop companion not found. Try an interactive offline demo.`
- Start: `Start sample task`
- Agent waiting: `Researcher needs a decision`
- Paper heading: `Approval requested`
- Before response: `Approve this proposed approach?`
- Sending: `Saving your decision…`
- Confirmed: `Approved. Developer is continuing.`
- Rejected: `Rejected. Agent will reconsider.`
- Backend error: `Couldn't reach the desktop. Your choice wasn't applied. Retry or use Demo Mode.`
- Completion: `Task finished · 8 tests passed · View results` (only when actual test data says 8 passed; never hard-code as true in live mode).

## Required visual QA

1. View every important label on Quest hardware, not just PC Editor.
2. Test with standing and seated body height but keep all mandatory actions within seated reach.
3. Ensure a paper delivered near the user's lap cannot occlude their physical keyboard or make them miss objects.
4. Test with overhead desk lights and weaker ambient light; don't assume perfectly tracked hands.
5. Verify no crucial buttons are obstructed by agent hands/props and no panel rotates out of reach when released.
