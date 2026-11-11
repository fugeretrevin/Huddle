# MuseDesk — build handoff package

**What this is:** An implementation specification and agent-ready task pack for a Meta Quest 3 mixed-reality AI coworker workspace. This pack is **not a completed application**. It contains enough architecture, interface, setup, security and acceptance guidance to implement work in parallel.

## Read in this order

1. **Human/team lead:** [`MASTER_IMPLEMENTATION_PLAN.md`](MASTER_IMPLEMENTATION_PLAN.md)
2. **Windows/Quest developer:** [`docs/QUEST3_AND_WINDOWS_SETUP.md`](docs/QUEST3_AND_WINDOWS_SETUP.md)
3. **All coding agents:** [`AGENTS.md`](AGENTS.md)
4. **Frontend and backend together:** [`docs/API_AND_EVENT_CONTRACT.md`](docs/API_AND_EVENT_CONTRACT.md) + [`contracts/`](contracts/)
5. **Specific implementer:** one [`tasks/TASK-XX`](tasks/) file and copy-paste [`docs/AGENT_LAUNCH_PROMPTS.md`](docs/AGENT_LAUNCH_PROMPTS.md).
6. **Integration/submission:** [`docs/SECURITY_TESTING_AND_EVALUATION.md`](docs/SECURITY_TESTING_AND_EVALUATION.md), [`docs/DEPLOYMENT_AND_SUBMISSION.md`](docs/DEPLOYMENT_AND_SUBMISSION.md).

## First 90 minutes

- Confirm Meta developer account, Unity Android tools, Meta VR Developer Hub and `adb devices`.
- Use MVDH's **Unity XR Projects → Create project** with Unity OpenXR and Meta XR All-in-One SDK.
- Build a blank Quest passthrough + hand-tracking scene; run it directly on Quest 3.
- Clone this pack into project `/planning` or repo root and lock architecture contracts.
- Create separate tasks/branches for XR, interaction, character, backend and orchestration.
- If using AI coding agents, install Meta VR CLI and `metavr init` for device/documentation access.

## Integration sequence

1. Freeze contracts, run a fake gateway in Unity; backend serves a real health endpoint.
2. Quest receives one WebSocket event and updates one character.
3. Quest displays one `decision.requested` document and sends a `respond` command.
4. Backend persists approval, resumes a sandboxed sample task, and creates an artifact.
5. Quest renders real results; standalone demo follows the same gesture workflow offline.
6. Add the remaining animations, voice, task board, and competition polish only afterward.

## Project layout *to implement*

```text
repo/
├── quest/                      # Unity project (after creating via MVDH)
├── backend/                    # Python package (TASK-04 and TASK-05)
├── samples/                    # disposable sample app and fixed tests
├── contracts/                  # initial JSON schema files are supplied
├── docs/                       # human-readable engineering specs
├── tasks/                      # independent AI coding agent tickets
├── scripts/windows/            # preflight and helper scripts
├── AGENTS.md                   # machine/team invariants
└── MASTER_IMPLEMENTATION_PLAN.md
```

**Important:** Quest judges will not have your Windows PC. The public APK must work in an explicit **OFFLINE DEMO** mode even if live workstation mode requires your local backend.
