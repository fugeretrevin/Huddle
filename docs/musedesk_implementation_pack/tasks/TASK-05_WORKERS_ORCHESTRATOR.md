# TASK-05 — Multi-agent orchestration, real tasks and sandbox

**Owner:** backend orchestration agent. **Priority:** P0. **Dependencies:** documented API, TASK-04 event/storage interface. **Do not edit:** Quest scene/characters.

## Goal

Produce genuine multiple-task execution, a human approval gate, and real artifact/test outputs within a safe sample repository. Agents must not bypass approval or overwrite each other's files.

## Implement

1. Create `TaskOrchestrator` with parent task `auth_demo` and three child roles: researcher, developer and tester.
2. Assign separate independent tasks initially: researcher evaluates authentication choice; developer scaffolds an independent login UI; tester runs baseline test or builds isolated tests. Explicitly mark dependent work as `blocked` until decision resolved.
3. Implement research output as structured evidence/alternatives/risk with `decision.requested` event. If using LLM provider, add deterministic fallback fixture and provenance flags.
4. Implement backend `ApprovalGate`: persist decision → pause matching task → accept validated approve/reject/request_changes → resume correct branch exactly once. Approval must be checked when worker attempts gated operation.
5. Implement safe `WorkspaceGuard`: make a copy/worktree of sample repo; canonicalize file paths; reject traversal/symlink escape; do not allow shell beyond specific static command allowlist.
6. Implement worker adapters `ResearchWorker`, `DeveloperWorker`, `TesterWorker`, with bounded timeouts, failure reporting, cancellation and artifact records. Adapt existing coding-agent CLI only behind interface; don't assume agent tool availability or power.
7. Integration test: user approval produces a real modified file and tests (or explicit failure). Reject produces no approved branch modifications. Task statuses/animations derive from truth.
8. Add artifact serialization that removes secrets and caps diff length before events cross to Quest.

## Deliverables

Orchestrator/worker modules and disposable sample project, fixtures, pytest integration tests, real diffs and test report examples.

## Acceptance

- [ ] At least two nonconflicting tasks actually run concurrently.
- [ ] Decision emission pauses dependent branch.
- [ ] Approve resumes one legitimate branch and produces verifiable artifact.
- [ ] Reject does not run approved branch; requests for changes produce a revised proposal or explicitly supported fallback.
- [ ] Duplicate decision response cannot rerun tool.
- [ ] Workspace escape attempts denied and recorded.
- [ ] Timeout/failure shows in task state, not hidden loop.
- [ ] `source_mode=live` only for actually executed work; deterministic fixture marked `demo`.
