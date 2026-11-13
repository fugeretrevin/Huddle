# Voice: how it works, how to build it, how to test it

Rewritten 2026-11-13 for Voice V2. Nothing here has been heard on the headset yet; the grammar is
covered by EditMode tests and every command can be driven in Play Mode without a microphone.

## What it is

- A **microphone key** (`VoiceHud/MicKey`, in `ModeConsole/MicSlot` when the mode console exists,
  otherwise beside the task dock). Press to talk, press again to send early. The microphone closes
  by itself after about 1.4 s of silence or 6 s in total. No wake word.
- The **Meta Voice SDK (Wit.ai)** turns speech into text. It needs internet and a Wit client token.
  It does not need the desktop backend, so voice also works in the scripted demo.
- The **intent is matched locally** (`VoiceIntentParser`), so no Wit intents have to be trained and
  any app token works.
- A **readout plate** says what voice is doing: Listening (with the words so far), Thinking,
  "Heard: ..." and what it did (3 s), "Not understood" with what it heard and two phrases to try,
  or "Voice unavailable" with the reason (no speech service, no token, no internet, permission
  denied). The halo behind the key swells with the microphone level.
- The microphone permission is asked for on the first press, not at launch.

## Rules

- A decision is spoken only as **approve / reject / request changes**. "yes", "sure", "sounds good"
  never approve. It is accepted only while a decision is pending and the paper has been taken and
  has opened; then the readout shows "Approving... Say cancel to stop." for 1.5 s (with a Cancel
  key) before the paper's own key method is called. The backend still decides; the readout says
  "sent", never "approved".
- **stop** halts any move at once, also from a partial transcript. Nothing else acts on partials
  (except "cancel" while a question is open).
- What an agent "says" is read from the run state; the readout adds "(scripted demo)" in the demo.
- A new task is created only on the live server and only after the heard title was confirmed.

## Files (`Assets/MuseDesk/Scripts/Voice/`)

| File | Job |
|---|---|
| `VoiceIntentParser.cs` | Transcript to intent. Plain C#. Tests: `Tests/EditMode/VoiceIntentParserTests.cs` |
| `VoiceAnswers.cs` | Status sentences and the phrases that fit the moment. Plain C#. Tests: `VoiceAnswersTests.cs` |
| `VoiceCommandCenter.cs` | State, gates, undo window, confirmation, carries intents out |
| `VoiceOffice.cs` | Finds the office's components and calls the keys' own public methods |
| `VoiceHud.cs` | Microphone key word, halo, readout, Confirm / Cancel keys |
| `WitVoiceSource.cs` | Voice SDK bridge: transcripts, level, problems. No intent matching |
| `VoiceGestureActivator.cs` | Optional pinch in front of the mouth for 0.5 s. Off by default |
| `VoiceDecisionController.cs`, `IVoiceCommandSource.cs` | Kept for the paper's Voice chip and the old router; they no longer act |

Scene: `AgentScripts/BuildVoiceV2.cs` (`BuildVoiceV2.Apply`, idempotent).
Driver: `AgentScripts/VoiceDrive.cs` (`Say`, `Partial`, `State`, `Mic`, `Confirm`, `Cancel`, `Parse`, `Gesture`).

## Secrets

- `ProjectSettings/wit.config` and `Assets/MuseDesk/Voice/WitConfig_MuseDesk.asset` hold Wit tokens.
  Both are gitignored. Never print, copy or commit them. The code only checks whether a client
  token is present, and scrubs anything token-shaped from speech-service error logs.
- After a fresh clone there is no configuration asset: `BuildVoiceV2` then adds no speech service,
  the key shows "Voice unavailable: this build has no speech service", and everything else works.

## Building

A build with voice needs: the speech service on `AppServices/VoiceRig` (the builder keeps an
existing one, or adds one when the local configuration asset exists), `RECORD_AUDIO` in
`Assets/Plugins/Android/AndroidManifest.xml`, and wifi on the headset.

A build without voice needs nothing: with no speech service the key reports why and the keys,
stamps and hands do everything.

## Testing without a microphone

In Play Mode, `VoiceDrive.Say("...")` injects a final transcript where the recogniser's would
arrive. Example: `PlayDrive.Start`, wait for the paper, `VoiceDrive.Say("show me the paper")`,
`VoiceDrive.Say("approve")`, wait 2 s, `VoiceDrive.State`.

## Known limits

- Needs internet. No offline recognition.
- Not yet tried on the headset: recognition quality, the readout's position, the gesture.
- `AgentScripts/RuntimeSmokeTests.cs` still tests the first voice layer's classifier
  (`WitVoiceSource.Classify`), which no longer exists; its `Voice()` section is superseded by the
  EditMode tests.
