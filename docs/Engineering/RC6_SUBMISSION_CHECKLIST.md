# RC6 submission checklist

Deadline 2026-11-18, 12:00 PM Pacific. Nothing is submitted or published without the owner's explicit go-ahead.

## Build

| Item | State |
|---|---|
| Release candidate | `rc6-1` (`b469a6a`), `Builds/MuseDesk_rc6.apk`, 85.3 MB |
| Release flags chosen from the gates | ☐ waiting on worn pass (`RC6_RELEASE_DECISIONS.md`) |
| `buildLabel` in `ReleaseConfig.asset` set to the final name | ☐ |
| Version | ☐ `bundleVersion` is `1.0` and `AndroidBundleVersionCode` is `1`. Each upload to a release channel needs a higher version code. |
| Signing | ☐ **Owner action.** No custom keystore is configured (`androidUseCustomKeystore: 0`), so builds are debug-signed. A store upload needs a release keystore. Create it outside the repo and enter its passwords in the Editor only. Never commit the keystore or its passwords. |
| Package id `com.musedesk.quest` | ☐ confirm it matches the app created in the developer dashboard |
| Wit.ai config never in the submission notes or repo history beyond what is already there | ☐ |
| Final build manifest (commit, tag, APK SHA-256, flags, date) appended to this file | ☐ |

## Judge path (standalone)

| Item | State |
|---|---|
| Cold start with no PC, no network: office on desk, DEMO label, first paper within ~10 s | ☐ worn |
| Hands-only walkthrough start to finish (deliver, grab, approve, handoffs, factory, receipt) | ☐ worn |
| Reject path | ☐ worn |
| No room scan needed for Desk; with no room data, Floor and Ceiling refuse with a reason and Desk stays | ☐ worn |
| 15-minute soak, no crash (`perf_rc6_worn` also counts exceptions) | ☐ |

## Release channel (owner, after the go-ahead)

- ☐ Upload the signed APK to the competition release channel named in the competition rules.
- ☐ Install from the channel on a clean headset (or after uninstalling) and repeat the cold start.
- ☐ Project description: what it is, what is live and what is scripted (see below), hands-first, standalone demo.
- ☐ Video under 3 minutes (`RC6_DEMO_SHOT_LIST.md`).
- ☐ Screenshot set: office on desk; paper delivery; stamp; factory and receipt; board; Floor office; ceiling pod (only if Gate C passes).

## Provenance statement (for the description and judge notes)

- **Standalone build:** the agents' work is a labelled scripted demo ("DEMO · Simulated work"). No model runs on the headset.
- **Live mode (PC companion):** a local FastAPI backend runs real sandboxed code changes and real pytest runs behind the approval gate. The research step uses templates and is labelled as such; it is not autonomous model-backed research.
- **The backend decides:** approval is recorded by the backend; the headset animation alone does not approve anything.
- **Voice:** optional, and needs internet (Wit.ai). It is not needed for anything.
