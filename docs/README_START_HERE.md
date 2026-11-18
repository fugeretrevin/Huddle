# MuseDesk Wall Habitat — How to use this handoff

This is an **additive Phase 3 development package** for the existing MuseDesk Unity / Quest 3 project. It is not proof that these features are implemented.

## Files
- `00_MASTER_SPEC.md` — full product vision, UX, architecture, room placement, elevator, manager, `/btw` visits, portal, data safety, state machines, acceptance gates, phased rollout.
- `01_ANTIGRAVITY_ART_HANDOFF.md` — detailed build assignment for 3D modular wall/treehouse assets, manager rig, elevator and portal animations, temporary agent projection, prefabs and asset manifest.
- `02_CLAUDE_ENGINEERING_HANDOFF.md` — detailed implementation assignment for Unity XR, backend integrations, room validation, far distance grab, manager orchestration, side-conversation and portal presentation, testing, release flags.
- `03_INTEGRATION_CONTRACT.json` — proposed common socket/role/event vocabulary for both agents; NOT a claim about existing code. Claude must reconcile with actual interfaces.

## Delivery order
1. **Preserve competition build.** On Nov 14, 2026, the Meta competition deadline is Nov 18. Work in new feature branches; flags default OFF. Do not divert the final submission from hardware validation.
2. Give Antigravity `00_MASTER_SPEC.md` + `01_ANTIGRAVITY_ART_HANDOFF.md` + `03_INTEGRATION_CONTRACT.json`, and access to existing MuseDesk art and rigs. Have it produce visual proof and an asset manifest; no production scene edits.
3. Give Claude `00_MASTER_SPEC.md` + `02_CLAUDE_ENGINEERING_HANDOFF.md` + `03_INTEGRATION_CONTRACT.json`. It can implement wall placement and interaction infrastructure against proxy meshes concurrently, but should not edit Antigravity's art worktree.
4. When Antigravity's prefab set is complete and verified, freeze/copy/cherry-pick the art commits, then let Claude integrate using the manifest. Only one agent should own production Unity scene files.
5. Release only the individually Quest-verified features. Preserve Desk/Floor/Ceiling and existing backend task/approval semantics.

## Paste into Antigravity
"Read `00_MASTER_SPEC.md`, `01_ANTIGRAVITY_ART_HANDOFF.md`, and `03_INTEGRATION_CONTRACT.json` in full. Audit the existing MuseDesk art, work in an isolated branch, and implement the original modular Wall Habitat, manager mascot and office, usable elevators, select-agent projection, and large-agent portal as actual Blender/FBX/Unity-ready prefabs. Deliver an exact socket/animation manifest and inspected preview scenes; do not edit the production Unity scene or backend. Proceed through the handoff acceptance gates, report honestly what is not yet device-tested."

## Paste into Claude Code
"Read `00_MASTER_SPEC.md`, `02_CLAUDE_ENGINEERING_HANDOFF.md`, and `03_INTEGRATION_CONTRACT.json` in full. Inspect the current MuseDesk repo and latest SDK/state; preserve Desk/Floor/Ceiling and current release APK. Implement Wall mode, room-aware wall placement, modular local navigation and elevator choreography, explicit Select Agents distance-grab conversation projections, read-only `/btw` questions, Manager proposal/approval/real task delegation, and safe large-agent portal presentation. Respect the art asset manifest and implement real code/tests rather than only plans. Use feature flags OFF until headset verification; never duplicate working backend agents or let avatar movement change their task state."
