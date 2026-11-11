# TASK-06 — Contextual speech and answering (optional P1)

**Owner:** voice agent. **Priority:** P1, cut if P0 slips. **Dependencies:** Quest UI and question route from TASK-02/TASK-04.

## Goal

A user can ask a blocked agent “Why do you recommend this?” via push-to-talk and get a decision-aware response, with captions and a fully usable text fallback.

## Implement

1. Use a **push-to-talk** button with visual recording state and maximum clip duration, no invisible always-listening mic.
2. Request microphone runtime permission only after explanation; on denial retain text question UI.
3. Encode microphone samples in a server-accepted format, send to a configured speech transcription adapter with limits; never ship provider keys in APK.
4. Attach transcribed question to `agent_id`, current `decision_id`, task context, evidence/rationale, and call question API. Preserve brief conversation within decision scope.
5. Return short contextual text, source references if any, and optional TTS audio. Show captions always.
6. Handle silence/failed recognition/unavailable provider explicitly; do not make a decision on behalf of user from speech transcript.
7. Cache or pre-record the scripted demo question/answer *only in labeled offline mode*.

## Acceptance

- [ ] Text fallback always works when mic permission or provider is missing.
- [ ] Spoken follow-up answered from pending decision context, not generic hallucinated claim.
- [ ] User sees transcript and can correct/retry.
- [ ] No secrets/audio recorded by default; no accidental voice-triggered approval.
- [ ] Does not block main Unity rendering thread.
