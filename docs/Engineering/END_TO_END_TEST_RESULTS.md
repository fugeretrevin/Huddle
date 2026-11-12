# MuseDesk end-to-end test results

Date: 2026-11-12. Branch `integration/audit-repair`.

Every result names how it was obtained. Four levels are used and they are not interchangeable:

| Level | Meaning |
|---|---|
| **Automated** | A test or script ran and passed on this machine today |
| **Live server** | A real backend process was started and driven over HTTP and WebSocket from the PC, the way the Quest client drives it |
| **Device log** | Read from `adb logcat` while the app ran on the Quest |
| **On head** | A person wearing the headset saw it and said so |

Nothing below is "on head" unless that word appears. Editor captures show static appearance only and are not counted as interaction evidence.

## 1. Automated results

| Suite | Command | Result |
|---|---|---|
| Backend | `backend\.venv\Scripts\python.exe -m pytest -p no:cacheprovider` | **110 passed**, 3 warnings (deprecation and a collection notice), about 10 s |
| Unity real-type checks | `unity command script run --file AgentScripts/RuntimeSmokeTests.cs --entry RuntimeSmokeTests.Run` | **54 checks, 0 failed** |
| Unity NUnit contract tests | `unity command test run --mode editor` | **33 passed, 0 failed** (constants and mappings only; see the note below) |
| Unity compile | `unity command script recompile_status` | `failed: false`, 0 errors |
| Scene health | `AgentScripts/SceneHealth.cs` | 0 missing scripts |
| Quest build | `AgentScripts/BuildApk.cs` | `RESULT=Succeeded`, 0 errors |

Note on the NUnit suite: it tests mirrors of runtime constants, because a test assembly cannot reference `Assembly-CSharp`. The checks with real regression value are in `RuntimeSmokeTests.cs`.

## 2. Live server run

A real `uvicorn` process with `MUSEDESK_PRODUCER=live`, a fresh database and a fresh sandbox directory, driven by a script over HTTP and the event WebSocket. Two full runs, one approved and one rejected. **36 of 36 checks passed.**

| Check | Approve run | Reject run |
|---|---|---|
| Session created; task accepted | pass | pass |
| A second task is refused while one is running (409 `RUN_IN_PROGRESS`) | pass | pass |
| The tester ran real baseline tests before the decision ("3 passed / 0 failed") | pass | pass |
| No artifact exists before the decision | pass | pass |
| Every event before the decision is labelled live; sequence numbers are dense from 1 | pass | pass |
| The snapshot shows the pending decision | pass | pass |
| The response is applied; the same response replayed applies nothing new; a conflicting second answer returns 409 | pass | pass |
| Resuming the event stream from the last sequence number yields no duplicates | pass | pass |
| Developer then tester worked; the artifact carries real counts (6 passed, 0 failed; `oauth_login.py`, `test_oauth_login.py`) | pass | n/a |
| Completed with an artifact reference and `task_state: completed`; the artifact is retrievable; diff and test report captured as live v2 artifacts | pass | n/a |
| No artifact and nothing written; the branch is reported `task_state: cancelled` with no artifact reference | n/a | pass |
| Another session's token is refused (401) | pass | pass |

The client was not in this loop. It shows that the backend half of scenarios C and D is real; it does not show what the user sees.

## 3. Device results

Quest 3 `2G97C5ZHCK037K`, package `com.musedesk.quest`.

| Observation | Level | Detail |
|---|---|---|
| The build installs and the app launches and keeps running | Device log | Process alive across a 20-minute session |
| Hand tracking active while the app ran | Device log | System tracking reported hands present |
| A physical stamp approval reached the approval path and the demo backend accepted it | Device log | `self-stamp Approve -> ... demo-auth-approval-1` followed by `decision demo-auth-approval-1 -> approved (applied=True)` at 14:44:30 |
| MRUK logged a warning with a stack trace every frame (241 times in about a minute) | Device log | Fixed: world lock turned off. To be confirmed in the next log |
| The font has no check-mark glyph (`✓`) | Device log | Fixed: the three symbol glyphs were replaced with words |
| Card text overflowed the cards; buttons were flat squares; hands passed through objects | On head | Reported by the developer during the 14:42 session. All three were addressed afterwards and are **not yet re-checked on head** |

The final build of the day (with the layout rearrangement, pushable props, 3D keys and card text layout) was installed after that session. See the last section for its launch check.

## 4. Scenario matrix

| Scenario | Backend | Client logic | On device |
|---|---|---|---|
| **A. Fresh launch**: XR initialises, passthrough, workspace placed, three agents, mode labelled, tasks shown | Not applicable (demo) or live server (session creation) | Automated: demo session and board load in the smoke tests; placement logic compiled, not exercised | Device log: launches and runs. Placement in front of the user and the mode label were not confirmed by a person today |
| **B. Task assignment**: grab a card, drop on the Researcher, backend accepts, Researcher starts, board updates | Automated: assign, stale-revision rejection, session scoping | Automated: an assign reaches the card; a stale assign is rejected and changes nothing | Not run. **Known limit:** no agent starts working on a card (`REMAINING_ISSUES.md` section 2) |
| **C. Human approval**: agent requests, delivers paper, user grabs, expands, reviews, decides, backend receives one valid decision | Live server: all checks pass | Automated: decision lifecycle, second-cycle decision ids, failure recovery compiled | Device log: a stamp approval was applied. Grab and expand were confirmed on head on 2026-11-11, not re-checked today |
| **D. Complete pipeline**: research, blueprint handoff, real development, artifact handoff, real tests, factory, shelf, board Completed | Live server: develop, test, artifact, completion all real | Automated: demo order is develop, artifact, test, complete; only an artifact-backed completion counts as delivered work | Not run. The handoff walks, factory stages and shelf token have not been seen since they were changed. The board does not show v1 pipeline tasks |
| **E. Failure and revision**: tests fail, failure visuals, developer gets details, revision, retest | Automated: a failed run emits `task.failed`, agents settle to error, messages are sanitized | Compiled: red lamp and reject chute on failure | Not run. **Revision does not exist** in the backend |
| **F. Backend disconnection**: disconnect, graceful handling, no fake completion, reconnect, restore state | Automated: restart with a pending decision fails the run and expires the decision; WebSocket replay and disconnect handling | Compiled: status sign shows reconnecting; resumes from the last sequence number; snapshot projected on reload | Not run |
| **G. Focus mode**: enable, work continues, updates queue, blocking decisions preserved, disable, review | Automated: focus policy and inbox | Compiled: updates collect with their real summaries and remain readable after focus is turned off | Not run |
| **H. App restart**: save settings, reopen, restore safely, reconcile, no duplicates | Automated: snapshot | Automated: the pending decision is found in a snapshot and events at or below the snapshot sequence are ignored. Layout save on pause compiled | Not run |

## 5. What would change these results

To move the "not run" cells to "on head", one seated pass of about ten minutes covers A, C, D and G in the standalone demo, and a second pass with the backend running (`RUN_AND_DEPLOY.md` section 8) covers B, D live and F. The checklist is in `REMAINING_ISSUES.md` section 1.

## 6. Launch check of the final build

The last build of 2026-11-12 (`RESULT=Succeeded`, 0 errors, 81.9 MB APK, commit `51169b8`) was installed on the Quest at 15:07 device time.

**It has not been launched or checked.** The headset was being worn and another app was in the foreground, so launching it would have interrupted that session. In particular these are still unconfirmed on this build:

- that the every-frame MRUK warning is gone from the log,
- that the app starts cleanly with the thirteen pushable props and the rebuilt keys,
- everything in `REMAINING_ISSUES.md` section 1.

To check it:

```powershell
$adb = "C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
& $adb -s 2G97C5ZHCK037K logcat -c
# launch MuseDesk in the headset, use it for a minute, then:
& $adb -s 2G97C5ZHCK037K shell "logcat -d -s Unity:V | grep -c EnableWorldLock"                     # expect 0
& $adb -s 2G97C5ZHCK037K shell "logcat -d -v time -s Unity:V | grep -E '\[MuseDesk|Exception'"      # session and placement lines, no exceptions
```
