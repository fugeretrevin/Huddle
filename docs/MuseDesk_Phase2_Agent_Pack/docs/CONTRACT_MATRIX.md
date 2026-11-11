# Phase 2 contract and visual mapping matrix

Phase 1 v1 strict event schema and routes remain unchanged. See original `docs/API_AND_EVENT_CONTRACT.md` and `contracts/event.schema.json`. The following is a *proposed v2 surface*, to be frozen by P2-00 after an audit of actual code.

| v2 input | Canonical server work | Unity presentation | On failure |
|---|---|---|---|
| `POST /v2/.../tasks/{id}/assign` | Validate task role/revision, persist assign event | Card pending → snap to station only on ack | Bounce + show canonical task |
| `POST /v2/.../tasks/{id}/priority` | Versioned update; queue reorder only | Priority badge changes after ack | Restore original priority |
| `POST /v2/.../tasks/{id}/pause` | Mark pause requested; pause at checkpoint | `pause requested`, then `paused` | Show still-running status |
| v1 `POST .../decisions/{id}/respond` | Existing approval gate | Existing paper resolved on ack | Preserve paper, show 409 |
| internal `HandoffService.create` | Artifact/dependency/role checks, persist handoff | Courier animates once per ID | History error, no false delivery |
| `GET /v2/.../artifacts/{id}?page=n` | Sanitize/paginate by stable hash | Expand paper to diff/report | Error card or text fallback |
| `POST /v2/.../focus` | Persist policy + event | Inhibit nonurgent walkups; open inbox | Reconcile canonical focus state |
| `POST /v2/.../diagrams` | Validate fixed-template graph | Render pooled nodes/edges | Show plain text explanation |

## Event/animation projection

| v2 event | Required refs | Effect | If event replayed |
|---|---|---|---|
| `task.assigned` | `task_id`, `agent_id`, `seq` | card position/status | update same card |
| `task.pause_requested` | `task_id` | pending pause badge | update same badge |
| `task.paused` | `task_id` | paused pose | no extra tool action |
| `handoff.created` | `handoff_id`, `artifact_id`, `task_id` | queue courier animation | dedup by ID |
| `handoff.received` | `handoff_id` | archive courier cue | dedup |
| `artifact.page_ready` | `artifact_id`, `page` | update expanded panel | replace same page |
| `focus.changed` | `revision` | mode/status/inbox | keep newer revision |
| `diagram.created` | `diagram_id` | spawn typed graph | replace if same ID |
| `station.milestone` | `artifact_id`, station | add milestone decoration | dedup by artifact ID |

## Source mode / auth / privacy

Every task, artifact, handoff and diagram in the Quest UI must include and display `source_mode`. All v2 commands require same session authorization rules as v1, idempotency keys, and optimistic revision controls where mutations can conflict. Layout/anchors are saved locally and never sent over network; raw logs/credentials/absolute paths never appear in artifact output.

## Required contract tests

- v1 schema fixture is unchanged and still accepted; v1 viewer never sees unknown v2 enum values.
- v2 fixture validates and a v2 event projects to correct reducer state.
- Device reconnect sees no old animation repeated and no new backend mutation.
- Contradictory task versions return 409; correct revision succeeds once.
- Invalid graph edge or overlong artifact is rejected at boundary.
