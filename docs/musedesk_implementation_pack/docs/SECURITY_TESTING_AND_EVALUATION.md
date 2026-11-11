# Security model, test matrix and product evaluation

## Threat model and guardrails

The highest-risk boundary is a network-connected headset sending a command to a Windows machine that can read and write files. Treat the backend as a permission enforcement service, not a remote shell.

**Never trust:** any user speech transcript, generated model text, a Unity gesture animation, remote network messages, a file path in a decision paper, or task IDs provided without session authorization.

**Controls:**

1. Per-session bearer authentication and short expirations; no permanent privileged token embedded in Quest APK.
2. Workspace root canonicalization (`Path.resolve`) and rejection of path traversal/symlink escape. Use dedicated disposable repository copy or worktrees.
3. Hardcoded tool allowlist for MVP. Reject arbitrary shell commands; tests run through predetermined project-specific command(s), with timeouts and resource caps.
4. All consequential actions checked again by backend approval gate; UI approval cannot directly invoke arbitrary shell.
5. Append audit events and record terminal decision response transactionally before dispatching a tool.
6. Run potentially untrusted code under restricted user/container when possible; assume subprocess scripts can still be dangerous without isolation.
7. Secret redaction from LLM prompts, tool output, events, screenshots and logs. Do not store raw microphone audio unless explicitly enabled.
8. Cloud workers, if any, must be isolated across sessions; no public endpoints exposing file operations without auth, quotas and cleanup.
9. No automatically approving destructive actions, deployments, payments, password or credential changes. These are MVP forbidden, not “medium risk”.
10. Always distinguish live output from demo/simulation in both internal data and UI.

## Automated test plan

**Unit (backend):** task transition table; decision state machine; `response_key` idempotency; path containment; allowlisted commands; event sequence monotonicity; artifact redaction; mode labeling.

**Integration (backend):** create session, issue template task, await approval, query, approve/reject, verify different downstream states, confirm artifact, reconnect and replay. Use fake worker process in CI to make test deterministic.

**Contract:** validate all example events and REST models against JSON schemas; roundtrip Unity serialization against `contracts/`; reject missing `schema_version`, wrong event type, mismatched session IDs.

**XR play mode:** fallback placement; input focus; expand/reset; document state machine; fake gateway; edge-of-reach, double event/restart, duplicate paper and disposal.

**On Quest:** hands only for complete demo, failure with spatial permission denied, wireless disconnected, 2 consecutive approval gestures, switching between modes, headset sleep/resume, frame rate ≥60, voice denied, text fallback. Test second desk if possible.

## Matrix of mandatory acceptance cases

| ID | Case | Expected |
|---|---|---|
| T01 | Fresh Quest APK, no PC/network | Offline demo playable and labeled |
| T02 | Quest hand-tracking only | Start, grab, enlarge, approve, inspect report |
| T03 | Live PC mode, valid token | Authenticated session and realtime state |
| T04 | Duplicate WS `decision.requested` | Exactly one paper visible |
| T05 | Duplicate approval POST same key | Exactly one backend execution effect |
| T06 | Opposite second decision request | HTTP 409/canonical state; no change |
| T07 | Reject choice | Approved branch not run; visible result correct |
| T08 | Lost network mid-approval | No false “approved”; retry same key; resync |
| T09 | No spatial permission | Manual workspace placement still possible |
| T10 | Missing microphone permission | Text question works; no crash |
| T11 | Tool tries `../` escape | Denied; audit logged |
| T12 | Test command times out | Failed task, clear report, no frozen agent |
| T13 | No backend service | Choice to use Demo Mode within timeout |
| T14 | Other Quest room/desk | Functional spatial placement/reposition |
| T15 | Headset sleep/resume | Snapshot/recovery; no double task |
| T16 | 3 uninterrupted demo runs | No crash, no major visual defects |

## Performance test procedure

Run a standalone Quest build with all three characters and a document. Use MVDH frame timing/performance capture. Record FPS distribution, worst spikes while approaching/expanding, GPU frame time, draw calls and memory. Competition baseline is 60 fps; target 72Hz where practical for Quest passthrough smoothness. Profile with thermal load after several minutes, not just first few seconds.

## Productivity validation (after hackathon)

Compare with conventional desktop multi-agent monitoring. Small pilot: ~5–10 developers with counterbalanced task order; measure time to identify blocked task, time to act on decision, error rate, interruptions per task, subjective workload and preference. Clearly distinguish measured usability improvements from novelty enjoyment. Do not claim proven productivity gains without comparative data.

## Failure visibility

- Fail closed: unavailable backend must **not** silently record an approval locally and pretend it applied.
- Agent animation reflects failure and returns to rest; diagnostic UI states error code.
- User can retry or switch to explicit offline demo after warning about mode difference.
- Export a small redacted diagnostic bundle: build hash, schema version, event IDs, task states, network error, and performance metadata. No raw home path, credentials, or microphone recordings.
