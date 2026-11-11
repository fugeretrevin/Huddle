# Voice (TASK-06) — how it's wired and how to build it

Voice is **implemented and compiling**, using the Meta **Voice SDK (Wit.ai, 85.0.1)**. Because Wit is
a cloud recognizer, voice needs network + a token, so it is a **live/connected enhancement** and the
**judged offline APK is built voice-free**. This doc is the playbook.

## What voice does

- Poke the **"Voice"** button on the decision paper → the mic activates (first use asks for the
  RECORD_AUDIO permission on-headset).
- Say a command. It drives off the **dictation transcription** (works with an untrained Wit app):
  - "**approve / yes / ship it / confirm**" → approve
  - "**reject / no / deny / cancel**" → reject
  - "**why / explain / tell me**" → asks the agent the contextual question
- The recognized command is routed through the **same** `SessionController.ApprovePending/RejectPending/
  AskPending` the poke buttons use — so the **backend still authorizes** every action. A spoken
  "approve" is never itself an approval (AGENTS invariant #2).

## Architecture (all in `Assets/MuseDesk/Scripts/Voice/`)

- `IVoiceCommandSource` — provider-agnostic intent events (Approve / Reject / AskWhy / ListeningChanged).
- `VoiceDecisionController` — routes those to the backend; gated by `voiceEnabled` and (optionally)
  `requireLiveMode`; only acts when a decision is actually pending. Inert with no provider → hands-first intact.
- `WitVoiceSource` — the Meta Voice SDK bridge (`AppVoiceExperience` → `VoiceEvents.OnResponse` →
  transcription/intent). Requests the mic permission on first use.

## Secrets / token hygiene (important)

- Wit **Server Access Token** lives in `ProjectSettings/wit.config` — **gitignored, never committed**.
- The linked `WitConfiguration` asset (holds the runtime **client** token) is at
  `Assets/MuseDesk/Voice/WitConfig_MuseDesk.asset` — **gitignored**.
- To (re)link after a fresh clone: put the server token in a local file and run
  `unity command script run --file AgentScripts/SetupWit.cs --entry SetupWit.Go` (reads the token
  from a scratchpad path; edit `TokenPath` in that file).

## Make a VOICE build (live/dev testing)

The committed scene + manifest are **voice-free** on purpose. To produce a voice build:

1. `unity command script run --file AgentScripts/WireVoice.cs --entry WireVoice.Build`
   (adds the VoiceRig + VoiceDecisionController + the "Voice" button; sets `requireLiveMode=false`
   so you can test over **wifi with no PC** — voice drives the labeled demo decision).
2. Add the mic permission to `Assets/Plugins/Android/AndroidManifest.xml` (local only):
   `<uses-permission android:name="android.permission.RECORD_AUDIO" />` (+ the microphone feature).
3. Build + install: `unity command script run --file AgentScripts/BuildApk.cs --entry BuildApk.Run`
   then `adb install -r Builds/MuseDesk.apk`.
4. On-headset: be on **wifi**; grant the mic prompt on first Voice press.

## Make the CLEAN offline (judge) build

Revert the two local changes so no mic permission / token ships:

```
git checkout -- Assets/MuseDesk/Scenes/MainMRScene.unity
git checkout -- Assets/Plugins/Android/AndroidManifest.xml
```

Then build as normal. (The `WitConfig` asset is gitignored, so it isn't referenced by the clean scene.)

## Known limitations

- Needs network (Wit cloud). No offline recognition (Quest's system offline dictation isn't exposed to apps).
- Untrained Wit app → keyword matching on the transcription (robust for approve/reject/why, not arbitrary NLU).
- Not yet tested on-headset end-to-end (mic + recognition) — the first voice build is for that.
