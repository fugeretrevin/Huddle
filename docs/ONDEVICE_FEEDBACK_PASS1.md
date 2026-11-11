# On-device feedback — pass 1 (2026-11-11)

Your headset bug list, triaged. Everything marked **✅ fixed** was verified in-Editor via Scene View
captures and live component inspection; interaction items still need an **on-head** confirm (I can't
drive hands in the Editor without the headset). Current APK built + installed to Quest `2G97C5ZHCK037K`.

| # | You said | Status | What I did |
|---|----------|--------|-----------|
| 1 | text still mirrored | ✅ fixed | TMP's readable face is local **−Z**, so `FaceUser.forward=true` showed the *back* (mirror). Set paper + name-tag `FaceUser.forward=false` and let `FaceUser` own rotation (placer `faceUser=false`). Verified: title reads correctly from the user side. |
| 2,3 | paper not grabbable / doesn't move with hands | ✅ fixed (needs on-head confirm) | Root cause found: the paper's `HandGrabInteractable` had a base-class `_pointableElement` still pointing at the **source cube's** Grabbable (copy-paste artifact), so grabs routed to the cube. Repointed to the paper's own Grabbable; made the grab collider solid to match the proven cube. |
| 7 | cup/whole-hand grab, not just pinch | ✅ already supported | Paper's `_supportedGrabTypes = All` (pinch **and** palm), and the rig has a `TouchHandGrabInteractor`. Once #2/#3 works, cupping your hand around it grabs it. |
| 8 | an actual button that presses down | ✅ already there + enlarged | The Approve/Reject/Ask buttons already have `PokeInteractableVisual` (they physically depress + recolor). They were just tiny — enlarged the buttons and labels so the press reads. |
| 9 | approve text illegible | ✅ fixed | Bumped all panel fonts + card size; button labels went from ~illegible to clear "Approve / Reject / Ask why". |
| 5 | agents shouldn't always turn to me | ✅ fixed | Removed the persistent body `FaceUser`; agents now only orient while walking up (via `AgentMovement`). Name tags still face you (un-mirrored). |
| 4 | hands look weird | ⚠️ partial | (a) The agent **arms** jutted straight out — tucked them to hang naturally (verified). (b) Likely bigger cause for *your* hands: the scene renders **two hand-visual systems** (ISDK `OculusHand_L/R` + OpenXR `OpenXRLeftHand/RightHand`) that can overlap. Fix prepared but **not baked** (see below) — disabling a hand system blind could leave you with no hands, so I want you to test it. Also bumped hand-tracking `LOW→HIGH` for smoother tracking (baked). |
| 6 | hand physics / push objects, don't glide through | ⏳ next | Needs hand physics capsules on the tracked hands + non-kinematic props. It's a rig change I don't want to bake unverified; plan below. (The paper itself is kinematic by design so it never falls — pushable props would be separate objects.) |

Plus: gave each agent a persistent **role-colored scarf** (researcher=blue, developer=green,
tester=amber) so the three are instantly distinguishable regardless of state color.

## Ready-to-run next steps (when you're back, with the headset on)

**A. Try the double-hands fix (reversible):**
```
unity command script run --file AgentScripts/FixDoubleHands.cs --entry FixDoubleHands.Disable
```
Rebuild + install, put the headset on, look at your hands.
- Hands look clean (single mesh)? Keep it.
- Hands vanished / tracking worse? Revert:
  `unity command script run --file AgentScripts/FixDoubleHands.cs --entry FixDoubleHands.Revert`

**B. Confirm grab:** reach for the decision paper and both (i) pinch and (ii) cup your whole hand
around it. It should move with your hand and expand to full detail. Tell me if either fails.

## Plan for hand-physics push (#6)

Add Meta ISDK `HandPhysicsCapsules` to each synthetic hand (kinematic capsule colliders per bone)
and drop 2–3 **non-kinematic** desk props (a mug, a couple of blocks) with Rigidbody + Collider so
your hands can bat them around and they rest on the desk (MRUK floor/desk collider). The decision
paper stays grab-based (kinematic) so it never falls. This needs an on-head pass to tune capsule
radius + mass, so it's the first thing to do live next session.

## Notes
- The orange "[BuildingBlock] Grabbable Item" cube is still active (a reliable grab test target).
  Say the word and I'll hide it for a cleaner demo — the paper no longer depends on it at runtime.
- All changes are in the committed scene + `AgentScripts/` builders; nothing here ships the Wit token
  or voice (offline-judge-clean).
