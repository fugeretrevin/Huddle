# Sprint A3 - Reaction arbitration (pure core)

Status: written, NOT compiled, NOT run (no Editor access in this sprint). Based on 9a6fbc6.

## What exists
- `Assets/MuseDesk/Scripts/Agents/Core/ReactionArbiter.cs` (asmdef MuseDesk.Agents.Core, ns MuseDesk.Agents):
  `ReactionArbiter`, `ReactionKind/Class/Verdict/Request/ArbiterConfig`, `SeededRng` (xorshift32), `AmbientVariation`.
- `Assets/MuseDesk/Scripts/Agents/Core/AttentionPolicy.cs`: `AttentionPolicy` (one per agent), `AttentionContext/Decision/Config`.
- `Assets/MuseDesk/Tests/EditMode/AgentInteraction/ReactionArbiterTests.cs` (asmdef MuseDesk.Tests.AgentInteraction, same as DeskPolishLogicTests): 47 tests.
- No .meta files created; Unity generates them on import (commit them then).

## Rules implemented
- Classes: Critical (PaperTransfer, Handoff, ElevatorBoarding, Walking) > Task (CompletionNod, ErrorWorry, WaitingOffer) > Social (Poke, Wave, Glance, Selected) > Ambient (IdleVariation).
- Body slot per agent: a reaction of class <= the active one never plays; strictly higher preempts. Timed kinds free themselves (DefaultDuration); open-ended Critical kinds need `End(agent, kind)` (60 s safety expiry).
- Cooldowns: per-agent-per-kind (`KindCooldown`, CompletionNod = NodGate gap), per-agent gap 0.8 s for Social/Ambient only (a confirmed Task reaction is never gap-dropped), global cap 4 non-critical plays / 6 s. Critical is exempt and does not count.
- Focus: drops Social+Ambient unless `StatusBacked`; also drops CompletionNod (same as NodGate). Task/Critical pass.
- Hands busy: drops Social (even status-backed). Poke is Social, so poke is also silenced while hands are busy; AgentAttention already guards `busy` at its poke site.
- Queue: capacity 8; only Task and body-blocked Social are queued, Ambient/Critical never; stale (> 2.5 s since RequestedAt) dropped at request and in `Poll`; `Poll(now, out req)` plays the highest class then oldest still-allowed entry, re-checking Focus/hands.
- Determinism: all time injected, `SeededRng`, `AmbientVariation` delay clamped to [min,max] (slow multiplier clamped 1..4), no repeat of the previous variant.
- AttentionPolicy: addressed edge -> User for <= 3 s then Workstation even if still addressed (needs a new edge); 5 s user cooldown; re-address inside cooldown gets a 1.2 s acknowledgement; unprompted glance 1 s and denied in cooldown; HandoffActive -> Colleague (cuts a user look short, beats a fresh address); else Workstation (weight lower when not working). `Switched` + `BlendSeconds` tell the caller when to re-issue SetAttention.

## Adapter plan (do not apply here; line numbers at 9a6fbc6)
Per-frame, the integration owner calls `arb.SetFocus(FocusModeController.Active)` and `arb.SetHandsBusy(holding||carrying)` once in `AgentAttention.LateUpdate` (AgentAttention.cs:484-524, next to `focus` at :500), and `arb.Poll(Time.time, out r)` there to play queued reactions.

| Site | Change |
|---|---|
| AgentAttention.cs:643 (poke trigger, `OnPoke`) / :714-718 `p.Poke(streak)` | `Request(Poke)`; call Poke only on `Play` |
| AgentAttention.cs:654, :748, :767, :800 `Glance(...)` | `Request(Glance)` (Social); replaces `_glanceCd` global cooldown and the `!focus` test at :800 |
| AgentAttention.cs:726-745 `OnUserWave` (`WaveBack`) | `Request(Wave)` per agent; keep `_waveCd` or let the arbiter's 3 s kind cooldown replace it |
| AgentAttention.cs:810-855 `IdleGestures` (`Random.Range` at :817,:825,:839,:850; `LookAt` :842,:851) | use `AmbientVariation.NextDelay(slow)`/`NextVariant()` instead of `Random`; `Request(IdleVariation)`, status-backed paper/colleague looks pass `StatusBacked=true`; replaces the manual focus skip |
| AgentAnimatorBridge.cs:480-489 `PlayConfirmedNod` (uses `NodGate.Allow`) | `Request(CompletionNod)` instead of NodGate + `_lastNodAt` (keep `bodyBusy` source from `IsBusy`); AgentRegistry.cs:140 caller unchanged |
| AgentAnimatorBridge.cs:439 `PlayOffer` / :450 `EndOffer` (called from AgentRegistry.cs:77) | `Request(WaitingOffer)`; `End` on EndOffer |
| AgentAnimatorBridge.cs:373-374 Complete/Error state triggers (`TrSuccess`/`TrError`) | `Request(ErrorWorry)` for the error path (nod stays the completion reaction) |
| AgentAnimatorBridge.cs:459-460 `PlayHandoffGive/Receive`; AgentHandoffController.cs:231-232 | `Request(Handoff)`; `End(Handoff)` when the handoff ends |
| Walking/paper/elevator (AgentMovement, CompletionSequence, elevator code) | `Request(Walking/PaperTransfer/ElevatorBoarding)` on start, `End` on arrival; these also give `IsBusy` as the `bodyBusy` source |
| AgentGaze.cs:122-150 `SetAttention/SetAttentionOnUser`; AgentPresence.cs:259-266, :347, :380, :405, :433; AgentTouchSummary.cs:105; CompletionSequence.cs:339, :439; FirstUseGuide.cs:288 | per-agent `AttentionPolicy.Update(now, ctx)` once per frame (e.g. in AgentPresence); on `Switched`, map Target to a Transform (user head = `AgentGaze.UserHead` :179, colleague head, `workTarget` :44) and call `SetAttention(t, seconds, d.Weight)`. User-directed calls (SetAttentionOnUser) become `ctx.AddressedByUser` edges. AgentGaze.cs:336-346 (its own random glance and working-target logic) can then be reduced to the policy output |

## Risks / untested
- Nothing compiled or run. Tests were checked by reading only; a failing assertion most likely points at timing constants (cooldown/duration tables).
- Poll re-queues blocked entries; a request kind blocked forever (e.g. Wave behind a stuck Walking) is dropped by staleness, not by Poll.
- Defaults (gaps, caps, dwell) are untuned and should be tuned on head.
