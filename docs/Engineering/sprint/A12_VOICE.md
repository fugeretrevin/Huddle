# A12 Voice intents and agent targeting

Status: written, NOT compiled, NOT run (the Editor was not available).

## New pure logic (assembly `MuseDesk.Voice.Core`, no Unity types, autoReferenced)
`Assets/MuseDesk/Scripts/Voice/Core/`
- `VoiceIntentClassifier.Classify(transcript, focused = None)` returns `VoiceClassification { Kind, Target, Confidence, RequiresConfirmation, Question, Negated, Hedged }`.
  - `VoiceAction`: Unknown, AskAgent, Summon, StartWork, Approve, Reject, Pause, Resume, ManagerRequest, Cancel.
  - `VoiceAgentTarget`: None, Researcher, Developer, Tester, Manager (by name or role word: dev, coder, qa, ...). "you" resolves to `focused` only when the sentence is question-shaped.
  - Approve/Reject: only short sentences made of the decision word plus filler; negated ("don't approve", "do not reject") or hedged ("maybe approve?", "should I approve", any "?") gives Unknown with Negated/Hedged set. Approve/Reject ALWAYS have `RequiresConfirmation = true`. "never mind" is Cancel.
  - Helpers: `ResolveTarget(string)`, `FromId("Tester")`, `AgentId(target)`, `Tokenize`.
- `VoiceAddressing` (pure, time passed in): `MicOpened(now)`, `MicClosed(now)`, `Hear(text, now)`, `SetGaze(target, now)`, `Tick(now)`; `AddressedAgent`, `MicOpen`; events `ListeningStarted(VoiceAgentTarget)` / `ListeningEnded(VoiceAgentTarget)`. A name mention beats gaze; both time out after `TimeoutSeconds` (default 8) unless refreshed; mic close ends it. No event fires while nobody is addressed. `VoiceAddressing.Shared` is the process-wide instance.

Tests: `Assets/MuseDesk/Tests/EditMode/Voice/` (own asmdef `MuseDesk.Tests.Voice`, references `MuseDesk.Voice.Core`), about 75 cases.

## Wiring (minimal edits)
- `VoiceOffice.AgentsListen(on)`: now also calls `VoiceAddressing.Shared.MicOpened/MicClosed`. Everyone tilts when nobody is addressed (unchanged); once an agent is addressed only that agent's `AgentGaze.SetListening(true)` stays on.
- `VoiceOffice.HearForAddressing(text)`: new; feeds partial transcripts. Called from `VoiceCommandCenter.OnPartial`.
- `VoiceCommandCenter.Update`: `VoiceAddressing.Shared.Tick(now)`.
- Unknown/noise: the existing "Not understood / Heard: ..." path in `VoiceCommandCenter.HandleFinal` is untouched and still used. The new classifier is NOT yet used to execute anything; the existing `VoiceIntentParser` still drives actions. The classifier is available for the next step (e.g. routing AskAgent by Target).
- Gaze/selection is not fed yet: whoever owns it calls `VoiceAddressing.Shared.SetGaze(VoiceAgentTarget.X, Time.unscaledTime)` each frame while the user looks at an agent (null gaze: pass None).

## How AgentAnimatorBridge should subscribe (not edited)
```csharp
void OnEnable()  { VoiceAddressing.Shared.ListeningStarted += OnListenStart; VoiceAddressing.Shared.ListeningEnded += OnListenEnd; }
void OnDisable() { VoiceAddressing.Shared.ListeningStarted -= OnListenStart; VoiceAddressing.Shared.ListeningEnded -= OnListenEnd; }
void OnListenStart(VoiceAgentTarget t) { if (t == MyTarget) PlayListening(); }   // MyTarget = VoiceIntentClassifier.FromId(role)
void OnListenEnd(VoiceAgentTarget t)   { if (t == MyTarget) StopListening(); }
```
Events fire on the main thread from the voice runtime. Manager target may be ignored by agent characters.

## Integration owner: compile and check
1. Compile. New asmdef `MuseDesk.Voice.Core` is carved out of Assembly-CSharp; it has no dependencies. Verify nothing else in `Scripts/Voice/Core` is needed from Assembly-CSharp.
2. Run EditMode tests `MuseDesk.Tests.Voice`. Phrase lists are hand-tuned; a failing edge case is a tuning issue, not a design one.
3. Play Mode: open mic, say "developer ..." and confirm only the Developer tilts; others restore on mic close.
4. Risks: `Tick` timeout can end addressing mid-mic without re-tilting all agents (they stay untilted until mic close). `VoiceIntentParser` (existing) and the new classifier are separate grammars and may disagree; only the parser executes.
