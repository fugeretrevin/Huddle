# RC13 (lively polish, 2026-11-16 afternoon) — answers the owner's rc11 worn report

Candidate: `Builds/MuseDesk_rc13_lively_polish.apk` (label `rc13-lively-polish`), SHA-256
`3DAF3AE6CB45B4DFA24863FC51BA92E743F95A8ABBF62787359AFBA329BBF6AD`, installed on the Quest (installed hash verified).
Code `42efeff` on `feature/rc8-feedback-1`. Rollback: rc11 `AF34CBE5...`, then rc10, rc9, rc8.
(`MuseDesk_rc12_portal_polish.apk` exists but was built while scripts were being edited: never installed, superseded by rc13.)
EditMode **1619 / 1619**.

| Owner rc11 finding | Change | Level | Evidence |
|---|---|---|---|
| Wall placement too strict, only far spots | up to 0.85 m above the eye (clears desk/screen), 0.5 m from the head, straight ahead allowed above the screen, finer search; a refused drop snaps to the nearest fit | AUTO + PLAY | 5 WallPlacementTests (desk against wall, close wall, above screen, nearest fit, snap on release); PortalFxDrive Point: refused -> "moved to the nearest spot that fits" |
| Use the Floor placement's beam / dragging / reasons | per-hand arc (Floor material), ring cursor on the wall, chip "Pinch to move it here" / "Can't: covers a door"; ghost label above the ghost, coloured status word | PLAY | captures: beam + red ghost + chip; label "Fits here / Checked against your room scan" |
| Portal not animated ("growing from nothing") | Antigravity floor portal (opens into an arch, motes) + light column; agent pops in inside it, steps out (Portal_Emerge_Step), leaves with Portal_Return_Step into the column; smooth validity ring in preview; wider large-spot search | AUTO + PLAY | stage captures (opening, emerge, out, leaving); MeetDrive 15/15 |
| Visitor should be lively and talkable | PortalVisitorLife: faces you, looks at you / a near hand / the finger that touched it, breathes, leans and slides from a touch, poke hop, waves back, thinks while asked, gestures while answering. The small desk visitor (Select agents) gets the same on its bones: dangles while carried, perch idle, explains | PLAY | simulated touch -> React_Poke_Surprise, look=finger, push; Ask -> converse / DeskVisit_Explain; AgentSelectGrabDrive 12/12 |
| Buttons / cards after Manager or Meet look poor | Wall cards in the task dock's unlit dark style (plan = cream paper), keys measured from the real key (were overlapping into one bar), balanced rows, one-line footer, card left of the visitor and off the task dock; perch beside the dock, round pads; plan page mark beside the stamp zone; desk paper More / Revise labels readable | PLAY | card captures; LivingOfficeDrive (stamp) all steps; OfferHoldDrive PASS (0 retracts) |
| (hint) | "Wave or poke them. Ask on the card." once, when a visitor first comes out | AUTO | 2 coach tests |

WORN: NOT RUN for every row. Device frame rate for the new portal art: not yet measured.

---

# RC11 (polish, 2026-11-16 morning) — rc10 plus open audit items

Candidate: `Builds/MuseDesk_rc11_polish.apk` (label `rc11-polish`), SHA-256
`AF34CBE53D559DA44CAD10E42B3422C18FC3480CB9C8486087D39BF83A6284CD`, installed on the Quest (installed hash verified).
Code: `5c5750f` + `f6a2927` on `feature/rc8-feedback-1`. Rollback: rc10 `1B43F95A...`, then rc9, rc8.
EditMode **1612 / 1612**.

| Change | Level | Evidence |
|---|---|---|
| Dock status names the real keys: desk "Office at your desk" (was "Spaces"); Floor "Place, Next spot, or Cancel"; Ceiling one short line "Ceiling dock on your right. Place, Next spot, or Cancel." | AUTO + PLAY | UnifiedDockDrive 17/17 with the new words on every step |
| Wall ghost / portal caveat shortened ("Checked against your room scan.") | PLAY | MeetDrive 15/15 |
| Reset layout is discoverable: after the first panel move, "Pinch a panel's bar and hold still to reset." (ends on Reset layout) | AUTO + PLAY | coach test; PolishHintDrive: shown after a move, cleared by reset |
| "Hands lost" recovery hint now fires (tracking drops while a paper is in hand) | AUTO | coach test; device behaviour NOT RUN |
| Device perf log labels Wall scenarios (`wall_preview/active/visit/portal`) | PLAY | MeetDrive run logs `wall_portal` then `wall_active` |
| Docs: `RC8_PERFORMANCE_RESULTS.md` (rc9 device: 72 fps on Desk and with the Wall placed), `RC8_SUBMISSION_CHECKLIST.md`, `RC8_DEMO_CAPTURE_LEDGER.md` | - | - |

WORN: NOT RUN. Frame rate for rc10/rc11 forest art and the portal: NOT RUN.

---

# RC10 (overnight, 2026-11-16) — answers the owner's rc9 worn report

Candidate: `Builds/MuseDesk_rc10_overnight.apk` (label `rc10-overnight`), SHA-256
`1B43F95AF86A2437B491437B810F9A97688DDE9393B204EA93A54117F3DD89E6`, installed on the Quest (installed hash verified).
Rollbacks: rc9 `97AA4094...`, rc8 `788D4A04...`, rc7, rc6_2. EditMode **1609 / 1609** (plus new fixtures run by name).

| Owner rc9 finding | Fix | Level | Evidence |
|---|---|---|---|
| Wall legs walk for a second then slide | View feeds the controller's MoveSpeed / IsCarryingPaper; re-asserts the wanted clip | PLAY | walking avatars sampled in Walk_Loop / Walk_Carry_Paper every sample, MoveSpeed=1 |
| Rider never left the elevator, teleported home | Riders hold in the open cabin then walk out (claims, rider priority), no corridor stops, soft transfer only after 60 s stuck; device log on any soft transfer | AUTO + PLAY | WalkerReliabilityTests 19/19 (30-min seeded stress, both layouts, owner scenario), claim tests 7/7; Living Office 13/13, softTransfers=0 |
| Portal never used, no way to reach it | Meet key on the Wall dock -> agent chooser -> portal preview; voice "Tester, come here" / "meet the developer"; status line + hint name Meet | AUTO + PLAY | MeetDrive 15/15 (leave/return exactly once, keep-out guard clear, cancel paths); MeetPortalEntryTests 19, PortalVoiceRequestTests 39 |
| Mode panel / placement flow gone | Desk-first launch (no auto-restore into a mode); stranded-panel seat in every mode; guided Wall card (Look at a wall -> Point and pinch, then Place) | AUTO + PLAY | WallGuideDrive PASS (steps, no duplicate keys, card hides on Place; dock 0.42-0.54 m after a stranded start); UnifiedDockDrive 17/17 |
| Antigravity forest/treehouse Wall | merged (46/46, 44/44 fit) | PLAY | one agent per role at 0.170 m, art car follows the cabin (gap 0.000) |
| Audit polish | duplicate push-to-talk key hidden; Mic + Calm motion inside the dock; Wall hint after the core loop; stale copy; duplicate guide keys hidden; greyed Wall says why; voice "wall mode"; elevator VFX flag wired | AUTO + PLAY | dock captures; OnboardingCoach/SpacesDock/VoiceIntentParser tests |

WORN: NOT RUN for every row. Frame rate on device: still to measure with `scripts\quest_perf_watch.ps1` (rc9 logs showed 72 fps on Desk; Wall/portal not yet measured).

---

# RC8 → RC9 release gate matrix

Candidate: `Builds/MuseDesk_rc9_feedback1.apk` (build label `rc9-feedback-1`), SHA-256 `97AA4094DAC81EE3AD6BFD9E737BA2A927DE47914492739FBDF525B709E2D2A9`, from
`feature/rc8-feedback-1` at `4180895 (code) / 2c7aadd (label + docs)`. Rollbacks: `MuseDesk_rc8.apk` (788D4A04...E1428), then `MuseDesk_rc7.apk`, then `MuseDesk_rc6_2.apk`.

Evidence levels: **AUTO** (EditMode tests), **PLAY** (Editor Play Mode driver, synthetic room, simulated hands, no
headset), **WORN** (the owner wearing the Quest and reporting the specific interaction). Nothing below is WORN yet:
the only WORN evidence is the owner's round-1 report on rc8 (`RC8_HARDWARE_EVIDENCE.md`, W1-W10), which this build answers.

Tester for every AUTO/PLAY row: Claude (coordinator), 2026-11-16, shared Unity Editor 6000.6.8f1.
EditMode suite: **1500 / 1500 passed** (full run after the last change).

| Gate | Owner finding | Method | Level | Result | Evidence | Next action |
|---|---|---|---|---|---|---|
| Wall: one agent per role, one scale | W1 doubled agents | `WallSingleAgentDrive Setup/Place/Count` | PLAY | PASS | 4 roles x 1 root, all 0.170 m, 4 baked characters hidden, 0 desk roots visible | WORN: look at the wall for doubles |
| Manager paper: keys fit, stamp zone | W2, W7 | `PlanPaperGeometryTests`, `LivingOfficeDrive RunOfflineStamp` | AUTO + PLAY | PASS | smallest text 4.4 mm (floor 4.3 mm at 0.45 m); REJECTED / APPROVED stamps land only after confirmation | WORN: read the paper, stamp it |
| Dedicated Manager model | owner decision | `WallManagerModelV1`, Avatar probe, `LivingOfficeDrive` | PLAY | PASS | `Agent_Manager_Body` on `MuseDesk_WallAgent`, no cap, 0 scripts/colliders; full plan loop 13/13, rides 8/8, 0 console errors | WORN: Manager looks right walking and in the lift |
| Panels near at start-up (office stays) | W3 | `UnifiedDockDrive` reach steps, `SpacesDockTests` | AUTO + PLAY | PASS | every dock key 0.42-0.55 m from the seated eye; re-seat moves panels only | WORN: first launch, is the dock in reach? |
| Factory belt removed | W4 | `RemoveFactoryV1`, `NoFactoryDrive Check`, `FactoryRemovedTests` | AUTO + PLAY | PASS | 0 visible factory/belt renderers; test evidence still reaches the receipt | WORN: none needed beyond a look |
| Desk / Floor / Ceiling / Wall from one dock | W5 | `UnifiedDockDrive Run` | PLAY | PASS 17/17 | Floor place, Ceiling place/lower/raise, Wall place with Move/Select/Manager, back to Desk each time | WORN: switch through all four |
| Select-agents grab, put back | W6 | `AgentSelectGrabDrive Run`, 19 tests | AUTO + PLAY | PASS 12/12 | grab with a 6 deg dip keeps the target, perch, /btw, regrab, put back by stretching to the wall, Return key, refusal while holding a paper; never >1 away | **WORN is the real test** (hands): grab, carry, put back |
| Offer: hold until taken | W8 | `OfferHoldDrive` (probe), `OfferHoldPolicyTests` | AUTO + PLAY | PASS | 0 retracts over 20 s, frozen pose 4537 frames, paper steady (x drift 2 cm) | WORN: wait 20 s without taking the paper |
| Paper keys: More + Revise only | W9 | `PaperKeysTrimV1` | PLAY | PASS | Approve/Reject/Ask why/Voice hidden by scale; More/Revise 1.5x, labels fit | WORN: press More and Revise |
| Hands-free mic + wake word | W10, owner decision | `MicToggleDrive`, `WakeWordTests` (21), `HandsFreeMicTests` (14) | AUTO + PLAY | PASS | key flips and saves; "approve" / "I would approve that" ignored (Unaddressed); "Researcher, approve" acts; "stop" always heard | WORN: mic on, talk normally, then "Researcher, why?" (needs Wit.ai / network) |
| Large portal keep-out (flag ON) | handoff P0 | `PortalKeepOutDrive` + `WallDrive`, 24 tests | AUTO + PLAY | PASS | portal opened at a clear spot 1.34 m to the side, 44 valid checks; box on the spot -> closed "Furniture is in the way"; 1.8 m room -> no large spot (small visitor) | WORN: open the portal in the real room; FPS during the portal |
| Paper hold point centred | art handoff P0 | `PaperHoldCenterV1` | PLAY | PASS | x 0.055 -> 0, y/z kept; offer re-validated after | WORN: paper centred between the hands |
| Grounded feet, loop flags | art handoff | Antigravity `2d5be2f` (merged) | AUTO (art) | PASS | min foot Y >= -2 mm on walk/turn/carry clips; 34 Manager loops serialised | WORN: no sinking feet |
| Frame rate >= 60 fps | competition rule | `scripts\quest_perf_watch.ps1` | WORN | **NOT RUN** | - | owner wears the build with the script running (desk, wall, portal) |
| Live model agents | handoff | backend live run | AUTO (backend) | PASS | see `RC8_LIVE_AGENT_EVIDENCE.md` | WORN with the backend live |
