# TASK-03 — Three miniature agents and workstation visuals

**Owner:** character/scene asset agent. **Priority:** P0 with P1 polish. **Dependencies:** TASK-01 station anchors, event enums. **Never edit:** MainMRScene root rig/packages, REST schemas.

## Goal

Three distinct small characters occupy miniature desk workstations. Each shows truthful task progress and the researcher visibly delivers a paper when a blocking decision exists.

## Implement

1. Choose cohesive, legally licensed/simple low-poly 3D character meshes. Character silhouettes should be distinctive without high poly counts.
2. Create `AgentResearcher`, `AgentDeveloper`, `AgentTester` prefabs, each with idle, working, thinking/waiting, holding paper, offering, happy, error animations (reuse rig where efficient).
3. `ResearchStation`: mini bookshelf, notebook, reading prop; `DevStation`: miniature laptop, abstract code monitor, keyboard; `QAStation`: mini console, clipboard and pass/fail LEDs.
4. `AgentAnimator`: project `agent.status_changed` and task states to animations; do not advance task state locally.
5. `AgentMovement`: short station-to-delivery waypoint path; use 1–3 s animation with turn and offer; handle relocation of entire `WorkspaceRoot`.
6. `DeliveryZone` reservation: one approaching agent maximum; while occupied, later papers queue into inbox.
7. Subtle optional audio; ensure no flashing or intrusive sound. Optimize mesh/material allocations for device.

## Acceptance

- [ ] All three roles distinguishable at small Quest scale.
- [ ] Animations reflect event values from fake gateway, not timers independent of task.
- [ ] Researcher walks to reachable delivery position with paper and returns.
- [ ] Stations remain aligned when WorkspaceRoot is moved.
- [ ] No collisions/obstruction with user's physical table or decision buttons.
- [ ] Quest frame rate remains at or above target baseline with characters active.

## Visual direction

Cute toy-diorama coworkers, warm neutral materials, readable silhouettes, deliberately small actions. No copyrighted preexisting mascots, unlicensed music, or proprietary avatar references.
