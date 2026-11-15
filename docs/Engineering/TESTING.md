# Testing

## Unity EditMode (one fixture at a time)

The Editor is shared and a single full-suite run hangs / eats RAM (last sweep stopped at 37/59 fixtures). So the
suite is run **per fixture** by `scripts\tests_batch.ps1`, which uses the same CLI call as `scripts\tests.ps1`
(`unity command --project-path ... test run --mode editor --timeout T --filter <Fixture>`).

```
scripts\tests_batch.ps1 -List                    # fixtures + groups, runs nothing
scripts\tests_batch.ps1 -Group board             # one group
scripts\tests_batch.ps1 -Only MotionTests,PresenceTests
scripts\tests_batch.ps1 -From FloorAimDirectorTests   # resume the sorted full list from a fixture
scripts\tests_batch.ps1                          # everything except the dormant group
scripts\tests_batch.ps1 -BatchSize 8 -FixtureTimeout 120 -MinFreeMemMB 1500 -StopOnTimeout
scripts\tests_batch.ps1 -UpdateCatalog           # regenerate fixture lists in scripts\test_catalog.json
```

Requires a running Editor with the project open (this script never starts or stops it). Compile first with
`scripts\compile.ps1`.

### Groups
Defined by regex rules in `scripts\test_catalog.json` (file path OR class name; first match wins; unmatched = `core`).
Fixtures are re-discovered from `Assets/MuseDesk/Tests/EditMode/**/*.cs` on every run (any class containing
`[Test]`/`[TestCase]`/`[UnityTest]`; top-level classes only); the `fixtures` lists in the catalog are a snapshot
for reading. Groups: `real`, `voice`, `board`, `wall`, `motion`, `interaction`, `spatial`, `core` (fallback),
and `dormant` (fixtures inside `#if MUSEDESK_TESTS_RUNTIME_REF`, compiled out; only run by `-Group dormant`).
Edit the rules, then run `-UpdateCatalog`.

### Timeouts and memory
- `-FixtureTimeout` (default 150 s) is the Unity test timeout; the CLI client is hard-killed at +60 s and the
  fixture is recorded as `timeout` (the Editor is left running; check `unity command editor status`).
- `-MinFreeMemMB N`: before each fixture wait up to 5 min for free RAM >= N; otherwise record `lowmem`, stop, and
  print the `-From` command to resume.
- `-PauseBetweenBatches` (default 5 s) and `-BetweenBatches { ... }` run after each batch (e.g. a reminder or your own
  restart step). There is no scripted Editor restart: the `unity` CLI usage in this repo has no restart command, so
  restart the Editor by hand between batches if memory climbs.

### Reading results
`TestResults\editmode_<timestamp>.json` (saved after every fixture, so a crash loses nothing) plus a `.txt` summary line.
Per fixture: `fixture, group, passed, failed, skipped, duration, status, failures[], note`.
Status: `ok`, `failed`, `timeout`, `error` (no result lines at all: filter matched nothing, compile error, or the CLI
could not reach the Editor; see `note`), `lowmem`. Final line: `SUMMARY: fixtures=.. passed=.. failed=.. timeouts=..`.
Exit code 0 only if every fixture is `ok`. Quick view:
`(Get-Content TestResults\editmode_X.json -Raw | ConvertFrom-Json) | ? status -ne ok`.
Filtering is by bare class name (as tests.ps1 does); use `-FullNames` for `Namespace.Class` if a name is ambiguous
(e.g. a short name that is a substring of another fixture would also run that one).

### Known hangs / limits
- Whole-suite runs hang and exhaust memory: do not run unfiltered (`scripts\tests.ps1` with no filter).
- `error` with no lines usually means the Editor is compiling or domain-reloading; run `scripts\compile.ps1` and retry with `-From`.
- Pass/fail counts are parsed from CLI lines matching `Passed` / `Failed`, same as tests.ps1.

## Mirrored "Spec" fixtures vs real types
`Phase2ContractSpecTests.cs` (`Phase2WireContractSpecTests`, `BoardPresenterLogicSpecTests`,
`DemoGatewayDecisionSpecTests`) test copies of runtime logic, so they cannot catch drift. The real runtime
(`Phase2Constants`, `BoardPresenter`, `DemoAgentGateway`) is in the predefined `Assembly-CSharp`, which an asmdef
test assembly cannot reference. `Assets/MuseDesk/Tests/EditMode/Real/RealTypeDriftTests.cs` (group `real`) asserts
the same contract against the REAL types, resolved by reflection from the loaded `Assembly-CSharp`:
`Phase2ConstantsRealTests` (wire strings, IsTerminal, IsSafeAssignCheckpoint, IsExecutable),
`BoardPresenterRealTests` (RegionFor, NextIdemKey), `DemoGatewayRealTests` (StateFor, private; NonPublic reflection).
A rename/removal fails with a clear message. Not covered by reflection: the priority-cycle and pause/resume poke
handlers (private, need views).

Permanent fix (not done; touches runtime layout): create `Assets/MuseDesk/MuseDesk.Runtime.asmdef`
(name `MuseDesk.Runtime`), add it to `references` of `MuseDesk.Tests.EditMode.asmdef` plus `Newtonsoft.Json.dll`
to `precompiledReferences`, and define `MUSEDESK_TESTS_RUNTIME_REF`; this activates the dormant
`Phase2RuntimeTests.cs` and makes the mirrors redundant. Note that other runtime code outside any asmdef
(everything in Assets/MuseDesk except Spatial/Core and Agents/Core) would then have to compile inside it.

## Backend
```
scripts\backend_tests.ps1                 # pytest, junit XML -> TestResults\backend_<timestamp>.xml
scripts\backend_tests.ps1 -K board -Timeout 300 -Extra "-x"
```
Uses `backend\.venv` (falls back to the main checkout's venv when run from a git worktree), sets `PYTHONPATH` to this
checkout's `backend`, kills pytest after `-Timeout` (default 600 s, exit 2). Per-test timeout uses `pytest-timeout`
(`--timeout=-PerTestTimeout`) only if that plugin is installed; it is not in `pyproject.toml` dev extras. Last run: 207 passed in ~36 s.
