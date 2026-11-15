# Motion and interaction design system

One motion language and one interaction contract for every MuseDesk surface: Desk, Floor, Ceiling, Wall, Manager, visits, portal. Code: `Assets/MuseDesk/Scripts/Spatial/Core/Motion/` (namespace `MuseDesk.Motion`).

## 1. Motion rules

1. **Sample time against fixed endpoints.** A transition stores its start and end and evaluates `MotionToken.Evaluate(elapsed / duration)`. Never `Lerp(current, target, k * deltaTime)` for anything that must finish: it never ends and its speed depends on the frame rate. `MotionClock` does this and always ends exactly at 1.
2. **Bounded physical motion uses a profile.** Moving things (elevator cabin, walkers, workspace travel) use `ProfiledMover`: accelerate, cruise and brake, landing exactly on the target with zero velocity. Turns use `Damped.FollowAngle`, which is critically damped, never overshoots and settles exactly.
3. **Every transition can be cancelled.** `MotionClock.Cancel()` (stays where it is, reports Cancelled) and `Finish()` (an authoritative state arrived: show it now). Starting a new transition on the same object starts from the current pose, never from a stale one.
4. **Hitches and resumes cannot teleport things.** Each step consumes at most `MotionSettings.MaxStep` (1/20 s). NaN and negative steps are ignored, and zero or negative durations complete on the first step.
5. **Reduced Motion** (`MotionSettings.ReducedMotion`) halves durations and replaces overshoot with OutCubic. It does not make starts and stops harsher, because accelerations are unchanged.
6. **Visual completion never gates backend execution, and backend success is never implied by a visual.** Animations follow acknowledged state.
7. **One writer per transform.** The workspace director owns world pose; a walker or locomotion controller owns a character root; the Animator owns bones, and gaze/IK layers run after it in LateUpdate (`BonePoseLayer` order 300–430). Held objects belong to their grab owner. A second writer is a bug.

## 2. Tokens (`MuseMotionProfile`, starting values from polish spec 4)

| Token | Duration | Shape | Use |
|---|---:|---|---|
| hover | 0.11 s | OutQuad | emphasis only, no layout shift |
| pressDown / pressReturn | 0.09 / 0.13 s | OutQuad / OutCubic | keys and buttons |
| cardPickup | 0.16 s | OutCubic | lift to show ownership |
| cardRelease | 0.28 s | InOutCubic | to slot, back after a refusal, lane changes |
| panelAppear / panelHide | 0.26 / 0.18 s | OutCubic / InOutCubic | inspectors, menus, cards appearing |
| paperUnfold | 0.40 s | InOutCubic | paper expand |
| detailsSheet | 0.33 s | InOutCubic | details slide under the folio |
| turnSmoothTime | 0.16 s | critically damped | body and head turns |
| startSeconds / stopSeconds | 0.30 / 0.28 s | profile | walkers |
| walkTurnLimit | 50° | | larger heading changes stop and turn in place |
| acknowledge | 0.48 s | SoftBack (≈4 % overshoot) | one nod or acknowledgement |
| gazeBlend | 0.35 s | InOutSine | look-at weight in and out |
| elevatorDoor | 0.45 s | InOutCubic | doors |
| elevatorAccel / Brake | 0.6 / 0.7 s | profile | cabin |
| portalSpawn / portalReturn | 0.65 / 0.50 s | InOutSine | portal in and out |
| visitLand | 0.35 s | OutCubic | desk-visit landing |
| fadeSwap | 0.30 s | InOutSine | soft transfer |
| completionSeconds | 2.0 s | staggered | one coordinated completion moment |

These are hypotheses to tune from headset video. Change them in one asset (`Resources/MuseMotionProfile`, menu MuseDesk ▸ Motion Profile); code falls back to the same built-in values when the asset is absent.

## 3. Characters

- Presentation is separate from task state, but **derived only from it**. In Wall, `HabitatPresence.FromTaskState` maps state to activity: running → role work clip; awaiting approval, input or paused → `Waiting_For_Input`; blocked → `Thinking`; failed → `Worried`; none, queued or finished → `Idle_Breathing`. The Manager works only while a plan is drafted or committed.
- Movement: reserve the corridor, then turn in place if needed (`Turn_Left`/`Turn_Right`), accelerate, walk the validated polyline, brake to stop at the next stop or a sharp corner, then arrive and present. The walk cycle rate follows ground speed (stride matching), so feet do not skate.
- Acknowledgement: one `Nodding` for a confirmed `task.completed`, non-blocking, at the desk only. No looping celebration.
- No path means no walking through anything: wait off-corridor, then a soft transfer (fade, re-home, fade).

## 4. Interaction contract (every consequential control)

Each control has four stages. **Affordance**: what can be acted on is visible. **Hover / aim**: stable target ID and no layout shift. **Engaged**: held or pressed feedback. **Result**: *pending* (sent, not confirmed), then *committed* (backend acknowledged) or *refused* (rollback animation plus a reason).

- One in-flight request per object and action, with a stable idempotency key per logical action. Double pokes, jitter and reconnects never send twice.
- Losing hand tracking **cancels** an uncommitted action. It never counts as a release, drop or confirm.
- Gaze alone never commits.

**Hand precedence:** active held object > near grab/poke > explicit agent-selection drag > explicit workspace placement > ceiling summon > ambient gestures. Near interactions win by construction (far gestures yield through `HandInteractionProbe`). Far gestures arbitrate through `HandOwnershipArbiter` claims.

## 5. Colour semantics

| Meaning | Token | Pair with |
|---|---|---|
| select / active / running | Teal | outline ring or check |
| needs you | Amber | "!" or a document |
| completed | Green | ✓ |
| problem | Coral | ✕ or triangle |
| optional / Manager plan | Lavender | mailbox / plan glyph |
| neutral | Grey | — |

Colour is always paired with a symbol or text. Role accents (Researcher, Developer, Tester) must not reuse a state colour. The audit found Tester = amber and Developer = green, which still needs fixing: see `IMPLEMENTATION_AUDIT.md` U1.

## 6. Sound

Short, quiet, spatialised one-shots, never loops (the ceiling travel hum is the only loop and stops on arrival). Planned cues: card tick, key click, page rustle, elevator door and ding, portal whoosh, one completion chime. Focus Quiet mutes non-essential cues.
