# Demo deployment, competition release and submission checklist

## Must verify first

- Competition is the Meta VR Start Developer Competition 2026; verify member eligibility and applicable team requirements at https://start-developer-competition-26.devpost.com/rules.
- Deadline: Nov 18, 2026, noon Pacific / 3 PM Eastern.
- Productivity track; special Best Agentic Interaction eligibility described at https://start-developer-competition-26.devpost.com/details/special-awards.
- Hands-only usability from launch to completion; stationary/seated comfort and meaningful spatial use.

## Critical judge-access requirement

**The APK cannot depend exclusively on the developer's private Windows computer or local IP address.** An independent judge must be able to install and test it. Ship a standalone labeled demo with an actual Quest-local workflow state machine, interaction, and preset outputs; optionally add publicly accessible hosted live worker. A personal PC `192.168.x.x` endpoint will fail off your network.

A judges' demo does not need to falsely claim real code generation. The submission video can separately show true PC-connected mode and clearly explain which elements are live.

## Release process (Unity APK)

1. Register/create your app on the **Meta VR Developer Dashboard** using same package identifier and configured signing credentials as the Unity release build. Complete applicable app metadata and tests.
2. Build a release-ready **arm64 standalone Quest APK**. Ensure release build does not ship dev LAN IPs, secrets, debug overlay, or a requirement for a specific PC.
3. Upload APK to a **new release channel named exactly `Competition`**, as required in official rules. The release must be accessible to judges; follow dashboard invitation/release access steps as appropriate for competition.
4. Install the same release APK on a clean Quest and complete hands-first offline demo. Do not validate only your USB-deployed debug APK.
5. Verify the shareable release link opens and permits access for an authorized tester, ideally someone not on your team.
6. Keep app available free to test throughout judging and avoid modifying the judged build after the submission deadline.

## Video storyboard (max ~2m50s, publicly hosted)

- 0:00–0:20: real desk, three miniature stations, visible hand tracking.
- 0:20–0:45: start task; show researcher/dev/tester distinct state changes.
- 0:45–1:15: researcher physically delivers genuine decision document.
- 1:15–1:45: user grabs, expands, asks context question.
- 1:45–2:10: physically approve; show server acknowledged and task resumed.
- 2:10–2:35: show real output: changed files/test result, provenance label.
- 2:35–2:50: focus/inbox or second agent state; final one-sentence value.

Use real Quest or simulator footage; do not misrepresent scripted demo as actual live coding. Demonstrate the exact features supported by the submitted APK. The official video must be **under three minutes**, uploaded publicly to YouTube or Vimeo, and linked in Devpost form.

## Devpost text structure

- Project title and 1-sentence pitch.
- Inspiration: multiple AI workers are difficult to supervise; spatial, calm, human-in-the-loop interactions.
- How it works: 3 workstation agents; papers, questions, approvals; backend tasks and safe gating; standalone demo.
- How built: Unity 6, Meta XR SDK, Interaction SDK, MRUK, Python FastAPI, WebSockets, task workers, sample repo.
- Meta-native value: physical scene surface, hand grab, spatial characters, document gestures, readable UI.
- Technical details: what is genuine live execution; what is deterministic offline guided demonstration; safeguards.
- Challenges & learnings: hands, desk placement, live state sync, app offline availability.
- Future work and target release: voice, persistent workstations, more agent connectors, hosted sandbox, usability tests.
- Screenshot(s), build link, track/division, video URL, contact and team members as required.

## Pre-submit acceptance gate

- [ ] APK launches Quest 3 with passthrough and hands; no controllers needed.
- [ ] Never hangs waiting for PC; offline demo visibly labeled.
- [ ] One decision starts, appears, grabs, expands, responds, resolves.
- [ ] At least one output/test report shown.
- [ ] Rejection path works; network/permission failures handled.
- [ ] At least three complete no-crash test runs; frame rate checked.
- [ ] UI readable and comfortably within narrow field of view.
- [ ] No API keys, cloud credentials or PC-local addresses embedded in public build.
- [ ] No copyrighted/unlicensed character or audio assets.
- [ ] Signing, package ID and Competition release channel confirmed.
- [ ] Release invite link tested by someone outside development account.
- [ ] Public video shorter than 3 minutes and accurately represents app.
- [ ] Devpost registration/team eligibility verified and submission completed before cutoff.

## Official references

- Rules: https://start-developer-competition-26.devpost.com/rules
- FAQ: https://start-developer-competition-26.devpost.com/details/faqs
- Judging: https://start-developer-competition-26.devpost.com/details/judging
- Special awards: https://start-developer-competition-26.devpost.com/details/special-awards
