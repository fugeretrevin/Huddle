# A4b: stable idempotency keys

Pattern reused from S2 (`CommandLedger`: in-flight lock plus one key per logical action until the server answers).

- Focus (`FocusModeController`): new pure `LatestWinsToggle` (Spatial/Core) over `CommandLedger`. One request in flight, key per on/off value, retry reuses it, failure frees the toggle, newest wish wins (a late answer for an older value is acknowledged but the pump then sends the newest). Backend echoes call `Observe` and never clobber an in-flight wish.
- Diagram (`ArchitectureDiagramController`): `CommandLedger` lock and key per template; a failure keeps the key and rethrows (caller falls back to the example); `_gen` bumped by `Clear`/`Dismiss` so a late answer cannot rebuild a dismissed diagram. A second poke while in flight returns true (no example fallback).
- Other `++counter` keys (not changed, outside ownership): `BoardPresenter.cs:687` and `SessionController.cs:500` use counters; both already sit behind S2 `CommandLedger` keys for the actions that matter, the counter is only the mint function.
- Tests: `Assets/MuseDesk/Tests/EditMode/LatestWinsToggleTests.cs`. Nothing compiled or run.
