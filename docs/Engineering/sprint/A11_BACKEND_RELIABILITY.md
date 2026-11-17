# Sprint A11 - Backend reliability and security

Base: feature/living-world-polish 9a6fbc6. Tests: `backend/tests/test_a11_reliability.py`.
pytest before: 207 passed. After: 247 passed, 1 skipped (symlink test where symlinks are unavailable).

Method note: the first pass was written against an older base. After rebasing onto 9a6fbc6 many defects
turned out already fixed by earlier audit work; those tests now serve as verified-OK proofs.

## Fixed in this sprint (test failed first, then fix)

| # | Finding | Test | Fix |
|---|---|---|---|
| 1 | `tasks` terminal states were reopenable: `set_task_status` / `upsert_task` could flip failed/cancelled/completed back to running or completed | `test_terminal_task_status_is_immutable` | `storage.py`: writes are no-ops once status is completed/failed/cancelled |
| 2 | Demo run left `...-dev` task `running` forever under a completed parent | `test_approve_completes_everything`, `test_event_replay_...`, `test_every_v1_event_payload_carries_source_mode` | `orchestrator.py`: dev task settled with the run outcome |
| 3 | Parent task reported `completed` when the gated work was rejected/cancelled | `test_reject_leaves_no_task_completed_over_cancelled_work`, `test_request_changes_...` | parent mirrors outcome (`cancelled`) in demo and live; live already set `failed` on failed tests (`test_live_failed_tests_do_not_complete_parent`, verified OK) |
| 4 | Unanswered decision waited forever (run stuck `awaiting_approval`) | `test_decision_timeout_expires_instead_of_waiting_forever`, `test_demo_decision_timeout_expires` | new `decision_wait.py` + `Settings.decision_timeout_seconds` (default 3600, 0 = off); on timeout decision -> `expired`, tasks -> `failed`, `system.warning DECISION_TIMEOUT` via existing `fail_run` |
| 5 | `create_workspace(".")` / `""` resolved to the root and `rmtree`d every session workspace | `test_create_workspace_never_wipes_root_or_escapes` | `workspace_guard.py`: refuse ws == root |
| 6 | Unhandled exceptions returned a bare non-JSON 500 | `test_unhandled_error_returns_sanitised_json` | `main.py`: generic handler -> `{error_code: INTERNAL_ERROR, user_message}`, no exception text or paths (logged server-side by type and path only) |
| 7 | v1 snapshot and task list carried no `source_mode` | `test_v1_snapshot_and_task_lists_carry_source_mode` | additive optional `source_mode` on `Snapshot` and task list |

## Verified OK (already correct on 9a6fbc6)

- Idempotency key reused on another decision -> 409 `IDEMPOTENCY_KEY_REUSE`, other decision untouched: `test_idempotency_key_cannot_be_replayed_across_decisions`.
- Same key + same action replays once (`applied` false): `test_same_key_same_action_replays_once`.
- Same key + different action reports the canonical stored state, never the requested one: `test_same_key_different_action_reports_canonical_state_not_requested_one`.
- Approve after reject -> 409, state stays rejected: `test_approve_after_reject_is_409_and_state_stays_rejected`.
- Different key, same action after resolution is idempotent: `test_different_key_same_action_after_resolution_is_idempotent`.
- Decision of another session -> 404/401 and untouched: `test_decision_of_other_session_is_404_and_untouched`.
- Expired decision cannot be approved: `test_expired_decision_cannot_be_approved`.
- Session isolation for v2 task commands, v2 dependencies, v2 diagrams, handoff artifacts, v1 artifacts: `test_v2_commands_cannot_touch_other_sessions_task`, `test_v2_task_cannot_depend_on_other_sessions_task`, `test_v2_diagram_cannot_reference_other_sessions_artifact`, `test_handoff_cannot_reference_other_sessions_artifact`, `test_v1_artifact_and_decision_isolated_between_sessions`.
- Sandbox env has no credentials (ANTHROPIC_API_KEY, MUSEDESK_*, OPENAI, AWS, GITHUB, WIT): `test_sandbox_subprocess_env_has_no_host_credentials`. Timeout returns `timed_out`, no TypeError: `test_sandbox_timeout_returns_timed_out_not_exception`. Traversal / absolute / UNC / drive paths and symlink escape denied: `test_sandbox_path_escapes_all_raise_workspace_error`, `test_sandbox_symlink_escape_denied`. Only pinned `-m pytest` command, no shell: `test_sandbox_command_is_pinned_to_pytest_only`.
- Replay deterministic, gapless, unique event ids: `test_event_replay_is_gapless_deterministic_and_ids_unique`, `test_duplicate_event_id_is_rejected_by_storage`.
- Restart keeps sessions, continues seq; orphaned runs fail and decisions expire: `test_restart_recovers_sessions_and_continues_seq`, `test_restart_marks_orphaned_work_failed_and_decisions_expired`.
- Internal run errors fail the run instead of hanging: `test_live_internal_error_fails_tasks_instead_of_hanging_running`.
- Provenance on every v1 event, v1 artifact, v2 snapshot: `test_every_v1_event_payload_carries_source_mode`, `test_v1_demo_artifact_is_demo_and_live_artifact_is_live`, `test_v2_snapshot_and_artifact_source_mode`.

## Contract changes needed

Additive only, no enum/event changes: `Snapshot.source_mode` (optional, "live"|"demo") and `source_mode` on the v1 task list response. New setting `MUSEDESK_DECISION_TIMEOUT_SECONDS`. Task-create response left unchanged (pinned by `test_board_bridge`). The `/v1` timeout failure reuses `system.warning` code `DECISION_TIMEOUT`; the Unity client should treat it like `RUN_INTERRUPTED`.

## Remaining / not done

- v2 task create accepts unknown dependency ids (task stays blocked indefinitely); left for contract owner.
- Decision race: an answer arriving between the timeout check and `fail_run` can still be discarded (run fails, decision shows resolved); narrow, acceptable.
- Phase 3 code was out of scope.
