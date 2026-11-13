# Spatial workspace: on-head checklist

2026-11-13. Written by the Spatial-Art agent. This is the list a person works through with the headset on, in order, for the Floor and Ceiling modes. Each step says what correct looks like and what failure looks like. Section 9 lists, without softening, what has never been seen on a device.

Background: `SPATIAL_WORKSPACE_ARCHITECTURE.md` (placement), `SPATIAL_WORKSPACE_INTEGRATION.md` (attention, courier, summon), `docs/Art/SPATIAL_WORKSPACE_ASSET_MANIFEST.md` (the models). The art was wired in by `AgentScripts/BuildSpatialArtV1.cs`; its printed report has every measured size and position and should be kept next to this list.

Words used below: the **strip** is the row of up to three mode keys with a one-line status plate above it. **Near you** means the area about 45 cm in front of and 25 cm below your eyes.

## 0. Before putting the headset on

| Check | Correct | Failure |
|---|---|---|
| The build has the modes on | `Spatial` object, `SpatialWorkspaceDirector`: `floorModeEnabled` and `ceilingModeEnabled` ticked in the scene that was built. Both are off by default and no builder turns them on | Flags off: none of this list applies; the app must look exactly as before (section 8) |
| The builders ran in order | `BuildSpatialPlacementV1`, `BuildSpatialReachV1`, `IntegrateSpatial`, `BuildSpatialArtV1`, each report ending `scene saved=True`, no `WARNING` line left unexplained | A `WARNING` about a missing prefab, a socket that is not unit scale, a slot beyond reach, or the pod arch inside a station |
| Room | Do the list once in a room with a completed Space Setup (floor, ceiling, some furniture) and once with Space Setup deleted or scene permission denied | - |
| Sit down | Seated, desk in front, at least 1.5 m of clear floor to one side | - |

## 1. Start in Desk mode

| Step | Correct | Failure |
|---|---|---|
| 1.1 Launch, wait for the office | Office on the desk as always. Beside the Focus key: `Floor` and `Ceiling` keys, no status plate | Extra objects anywhere (a platform, a pod, a mailbox, a courier, a beacon). Keys missing with flags on |
| 1.2 Reach the furthest key of the strip without leaning | Comfortable. The builder moved the slots: its report gives the distance (target 0.545 m or less) | You lean or stretch |
| 1.3 Press `Focus`, the dock, a stamp | All work as before | Any existing control dead or moved |

## 2. Room scan present or absent

| Step | Correct | Failure |
|---|---|---|
| 2.1 With Space Setup done, press `Floor` | Status plate: "Detected floor spot 1 of N" with a reason. A teal ghost on the real floor to your side, label `READY TO PLACE` | Status "Room scan not loaded yet" in a scanned room after 15 s. Ghost in mid-air, inside furniture or in front of you |
| 2.2 Cancel. Delete Space Setup (or deny the permission), relaunch, press `Floor` | Status says the room scan is not available and offers placing by hand. The ghost label reads `NOT CHECKED`. Its height is a guess | The words claim a detected spot. Label `READY TO PLACE` on an unchecked spot |
| 2.3 Same room state, press `Ceiling` | Refused, with the reason in words. Nothing moves, no ghost | Any ghost or movement without real ceiling data |

Restore Space Setup before continuing.

## 3. Floor mode

| Step | Correct | Failure |
|---|---|---|
| 3.1 `Floor`: look at the preview | The ghost is the office's platform outline: about 0.95 x 0.65 m (exact size in the builder report), lying flat ON the floor, its label toward you. The office has not moved | Ghost floating above or sunk below the floor by more than about 2 cm. Ghost much smaller or larger than the platform that arrives in 3.4. Label mirrored or unreadable |
| 3.2 `Next` | The ghost jumps to another spot, the status counts up, never onto furniture or within 0.6 m of your feet | Same spot again. A spot under a table or through a wall |
| 3.3 Nudge is not on a key. Skip unless a driver is attached | - | - |
| 3.4 `Confirm` | Ghost disappears. Agents finish any walk first (up to 6 s), then the office glides to the spot in about 1 to 2.5 s and never passes through your head. On arrival the platform grows in under it (about a third of a second) with the courier station at its front left edge. The task dock, stamps, mode plaque, Focus key and receipt stay at the desk | The platform is visible during the flight. The office arrives with no platform. The office passes through you. Your desk controls fly away with it. An agent trails behind the office |
| 3.5 Look at the office on the floor | Desks, factory and trays stand ON the platform deck, inside its edge. "MUSEDESK LAB" plaque readable at the front. Known compromise: the deck top is at floor height, so the platform body is about 3 cm INTO the floor (report line "floor contact") | Desk legs sunk into the deck or floating above it. Stations or trays hanging over the edge. The courier station overlapping the inbox tray or the factory. Platform visibly hovering |
| 3.6 Near you | The strip shows `Desk` only, at your right. `Bring it here` and `Status` keys at your left. A small mailbox upper left reading "Nothing waiting" with 0 on it | Keys overlapping each other or the mailbox. Any of them more than a comfortable reach away |
| 3.7 Start a run (Start key or the dock) | Agents work on the floor. The platform's strip stays teal. No beacon while nobody needs you | A beacon with no cause. Sounds repeating |
| 3.8 Wait for the decision | Above the Researcher: the beacon model (lamp with a document icon, amber) standing on an amber plate "Needs you", large enough to read from your seat (about 2 degrees tall). A small "Needs you" plate upper right near you. ONE attention sound. The tiny "!" over the Researcher is gone. Platform strip amber. Mailbox: "1 to decide" | No words (icon or colour only). Two sounds. Both the beacon and the tiny "!". The beacon model facing away, upside down, or 100 times too big or small |
| 3.9 Watch the paper | The Researcher walks to the front left, offers the paper; the paper moves onto the courier at the station; the courier (small winged tray) lifts off and flies an arc to you in about a second, the paper upright just above it, then hovers in front of you | Paper teleports. Paper and courier separated by more than a centimetre or two, or the paper inside the courier's body. Courier flying backwards (its front should face you). Courier or paper passing through your head |
| 3.10 Take the paper (grab it) | It comes away in your hand and unfolds as at the desk. The courier flies back to the station and disappears there. The strip status changes only if you ask to move the office: it must refuse while you hold the paper | Paper cannot be grabbed while hovering. Paper snaps back to the office. Courier stays hovering |
| 3.11 Decide by key | Approve or Reject key on the paper. Outcome shows only after the confirming event. Paper glides to the out-tray at the far office in about a second. Beacon and mirror go; mailbox "Nothing waiting"; platform strip teal | Paper darts or vanishes. Beacon stays after the decision |
| 3.12 Next decision: decide by stamp | The stamps are still on their pad at the DESK in front of you (they do not go to the floor). Take the paper from the courier, hold it over the desk, stamp it | Stamps missing from the desk. Stamp does not register on a paper that came by courier |
| 3.13 Run completes | Beacon: green lamp with a check badge on a green plate "Done" for 20 s, then gone. Mailbox "1 done". Receipt appears at the desk as before | "Done" for work that was not delivered. Receipt at the far office |
| 3.14 Press `Status` | A small perch (folding platform) with a dark plate on it, upper right: agent name, LIVE or SCRIPTED DEMO, activity, progress. Closes after 10 s. Mailbox count clears (you looked) | Plate hidden behind the perch model's parts. Plate overlapping the strip or the mirror cue. No LIVE / SCRIPTED DEMO words |
| 3.15 Press `Bring it here` | The platform vanishes at once, then the office glides back to exactly where it was on the desk. Beside the Focus key the first key now reads `Send back`, the status plate says the office is at your desk and Send back returns it | No way back to the floor spot (key reads `Floor` and opens a new preview). Platform visible during the flight or left on the desk |
| 3.16 Press `Send back` | The office returns to the same floor spot without a new preview; platform grows in on arrival. (If the spot no longer passes the room checks, the floor preview opens instead) | Office goes somewhere else. Nothing happens and no words say why |
| 3.17 Press `Desk` | Office back on the desk, platform gone, near-you keys and mailbox gone, key reads `Floor` again | Anything from Floor mode left visible |

## 4. Ceiling mode

| Step | Correct | Failure |
|---|---|---|
| 4.1 Press `Ceiling`: look at the preview | A pod-shaped teal ghost BESIDE you (where the office will come down, not on the ceiling), label `COMES DOWN HERE`, status names the ceiling dock. Not closer than about half a metre to your head | Ghost on top of you or in front of your face. Label reading `READY TO PLACE` |
| 4.2 `Confirm` | The pod model appears under the office (deck under the desks, a rear arch rising to a connector), a mount appears on the real ceiling to your side, a thin three-section mast joins mount and pod, and the office rises along a straight line to the dock. The mast shortens as it goes. Nothing passes through your head | Mast pointing somewhere else, detached at either end, or passing through you. Pod arch cutting through an agent or a desk (the builder warns when it measures this). Office overhanging the deck. Mount below or above the real ceiling by more than a few cm |
| 4.3 Docked: look up | Office in its pod just under the mount, a few cm of mast showing, wings folded, shelf closed, pod and mount still. Beacon and courier behave as in Floor mode, read from below | Pod intersecting the mount. Constant motion. Text upside down from below |
| 4.4 Near you | Strip: `Desk`, `Lower`. `Bring it here` and `Status` at the left, mailbox upper left | - |
| 4.5 Press `Lower` (or `Bring it here`) | Latches open (short unlock), the pod comes down the same straight line beside you, the mount's spool turns, the mast lengthens; on arrival a short settle, then the inspection shelf slides out at the front and the wings open. The strip moves back beside the Focus key and reads `Desk`, `Raise` | Descent toward your head. Pod animation moving the desks relative to the deck by more than a centimetre. Shelf never opens. Two shelves |
| 4.6 Interact | Touch an agent card, the factory, the trays as at the desk: the office is within reach. The paper is handed over by the Researcher directly (no courier while it is down) | Hands collide with invisible pod parts (all art colliders are disabled) |
| 4.7 Hold the paper and press `Raise` | The office does NOT move. Status: "Waiting to go up: put the paper down first". Key reads `Waiting` | The office leaves while you hold the paper |
| 4.8 Decide or let go, then `Raise` if it is not already waiting | Shelf slides in and wings fold during the short wind-up BEFORE the pod starts up; then it rises, locks under the mount with a short settle | Pod starts up with the shelf out. No lock at the top |
| 4.9 While docked, cause a decision (start a run) | Beacon above the Researcher sized for the distance, readable from below; the courier flies down from the pod to you with the paper; take and decide as in Floor mode | Courier starts from empty air far from the pod. Paper unreachable |
| 4.10 Press `Desk` | Pod, mount and mast disappear; the office glides back to the desk | Mount or mast left on the ceiling |

## 5. Saved placement

| Step | Correct | Failure |
|---|---|---|
| 5.1 Confirm Floor, quit the app fully, relaunch in the same room | After the desk placement and the room scan (up to 15 s) the office moves to the same floor spot; platform grows in | Office in a different spot. Office on the floor in a room it was not placed in |
| 5.2 Same for Ceiling | Docked at the same place; mount on the ceiling | - |
| 5.3 Relaunch in a different room, or after moving furniture onto the spot | Office stays at the desk; log line "Saved workspace placement not used" with the reason | Office placed into furniture |
| 5.4 A hand-placed floor spot | Not restored: it is never saved | Restored |

## 6. Recenter and taking the headset off

| Step | Correct | Failure |
|---|---|---|
| 6.1 In Floor mode, hold the Meta button to recenter | The office and its platform stay on the same real floor spot. The near-you keys, mailbox and strip move to your new front | Office jumps with you. Platform left behind |
| 6.2 In Ceiling mode, docked, recenter | Office at the dock, mount still on the ceiling above it, mast intact | Mount and pod separated |
| 6.3 Ceiling, down beside you, recenter | Office beside you, shelf open | Office at the dock while the strip says `Raise` |
| 6.4 Take the headset off mid-descent, put it on | The office is at its destination, not in mid-air | Frozen half way |
| 6.5 Watch the log during all of the above | No "Another script moved WorkspaceRoot" warning | That warning |

## 7. Comfort and legibility (judge, do not measure)

- Looking down at the floor office for a minute: neck comfortable? Text on nameplates readable at 1.5 m? Is scale 1 too small on the floor?
- The beacon at 1.5 to 3 m and from below: words readable at a glance? Is the lamp-on-a-plate one object or two?
- Courier: does it read as a delivery? Is the hover point easy to grab from?
- Near you: with paper, keys, strip, perch, mirror cue and mailbox all present, is it crowded? (Positions are in the builder report; each is one public field.)
- Ceiling descent: any discomfort as the pod comes down beside your head?
- Frame rate with the floor office and a decision pending (the art adds about 12k triangles for the platform, 9k for the pod, small amounts for the rest; models near you are switched off while hidden).

## 8. Flags off: nothing changed

With both flags unticked: no strip keys, no ghost, no platform, no pod, no mount, no beacon, no courier, no mailbox, no perch, at any time, including during a decision. The tiny "!" over the Researcher shows as before. If anything from this document appears, that is a failure.

## 9. What has NOT been verified, honestly

Run only in the Editor, in Play Mode, with a **synthetic 4 x 4 m room** and no headset (by the coordinator, before the art pass): floor candidate search and ranking, the preview / confirm / cancel flow, the office's travel and the agent wait, the pod state machine including the hold while a paper is held, the courier's path and the fallback snap, the beacon levels, the near-user kit, the return to the exact desk pose. 48 pure-logic tests pass.

Never run anywhere, not even in the Editor, at the time of writing: **everything in the art pass.** `BuildSpatialArtV1` and the changed scripts were written without compiling. Until the coordinator's run, every "correct" above that mentions a model is the intended result, not an observed one. In particular:

- Whether each model faces the user. The art faces +Z; cues that turn their -Z to the head carry the model turned by 180 degrees. Not seen.
- Whether the office fits the platform and the pod. Sizes come from mesh measurements in the builder; the pod was designed for workstations at about half size and is scaled up (about 1.4x), so its rear arch may meet the desks or the agents.
- The platform sinking about 3 cm into the floor plane (the office root is at floor height and the Core placement may not be edited in this pass).
- The mast between the mount's lower socket and the pod's connector, and whether a few cm of it show when docked.
- Animator clips started on objects that were switched on in the same frame (beacon, courier, perch, pod).
- The pod's own clips moving its deck a few millimetres under a rigid office.

Never run with real room data, on any device: MRUK floor and ceiling outlines and furniture volumes as the placement code assumes them; scene permission denied; no Space Setup; the anchor UUID and floor anchor pose across sessions (saved placement); recenter with real anchors.

Never seen on a headset at all: any of the spatial modes. Reach distances, text sizes at distance, the hover point for grabbing, poke keys that are moved at run time, hand grab of a paper that is re-positioned every frame, one sound or two, and all of section 7.

Known gaps that are not bugs in this list: stamps do not travel to the user (they stay at the desk, which works while the user sits at the desk); the filed paper glides to the far out-tray without the courier; voice summon is wired but not practically usable; an agent is never moved to the perch.
