"""Read-only ``/btw`` side conversation.

HARD INVARIANTS (tested): this module performs no writes to the task or event stores,
publishes no events, calls no mutating service, never touches an agent transcript, and
gives a model NO tools. The only state it owns is ephemeral per-visit memory, a short
dedupe cache and a rate-limit window, all in process memory. A request that asks to change
work is turned into a *proposed action* that is explicitly NOT applied and needs the
client's confirmation through the normal task flow.

Question routing is deterministic (see ``side_intent``): the classifier picks an intent and
the intent picks which REAL task context is attached (current task, recent events,
artifacts, test report, diff summary), each bounded in size.
"""
from __future__ import annotations

import asyncio
import logging
import re
import time
from collections import OrderedDict, deque
from dataclasses import dataclass, field
from typing import Any, Callable

from ..phase2.models import TERMINAL_STATES, TaskStateV2
from ..phase2.sanitize import sanitize_text
from ..phase2.store import Phase2Store
from .provider import ModelProvider, ProviderRefused
from .side_intent import CONTEXT_FOR_INTENT, classify
from .store import Phase3Store

log = logging.getLogger("musedesk.phase3.side")

VALID_AGENTS = ("researcher", "developer", "tester", "manager")
MAX_REPLY = 1200
MAX_VISITS = 256
RECENT_EVENTS = 5
EVENT_POOL = 25            # events read per task before per-intent selection
MAX_CONTEXT_CHARS = 1800   # hard cap on what is sent to a model
ITEM_CHARS = 140           # cap per event / artifact line

# Rate limit and dedupe (per session + visit). Process-local, ephemeral.
RATE_WINDOW_S = 30.0
RATE_MAX = 12
DEDUPE_WINDOW_S = 4.0
MAX_DEDUPE = 128

LABEL_DEMO = "[SCRIPTED DEMO]"
LABEL_NO_MODEL = "From task data (no model)"
LABEL_MODEL = "Model-written"

SYSTEM_PROMPT = (
    "You answer a user's side question about a software task. You are strictly READ-ONLY: you "
    "cannot change, start, stop, cancel, approve, skip or deploy anything, and you have no "
    "tools. Answer ONLY from the context block provided; it is the complete set of facts you "
    "may use. If the context does not contain the answer, say you do not have that "
    "information. Never invent progress, results, test output or file names. Keep answers "
    "under 120 words. If asked to change work, say it needs approval through the normal task "
    "flow and that you did not do it."
)


def is_change_request(question: str) -> bool:
    return classify(question).is_change


@dataclass
class Ctx:
    agent_id: str
    task: dict[str, Any] | None
    events: list[dict[str, Any]] = field(default_factory=list)     # newest last
    artifacts: list[dict[str, str]] = field(default_factory=list)  # {kind, summary}
    board: list[dict[str, Any]] = field(default_factory=list)
    plan: dict[str, Any] | None = None
    revision: int = 0


def _clean(text: Any, n: int) -> str:
    return sanitize_text(str(text))[:n] if text is not None else ""


def _payload(ev: dict[str, Any]) -> dict[str, Any]:
    p = ev.get("payload")
    return p if isinstance(p, dict) else {}


def _ev_text(ev: dict[str, Any]) -> str:
    p = _payload(ev)
    bits = [str(ev.get("type", "event"))]
    st = p.get("task_state")
    if st:
        bits.append(f"state={_clean(st, 20)}")
    for key in ("result_summary", "summary", "reason", "error", "message"):
        if p.get(key):
            bits.append(_clean(p[key], ITEM_CHARS))
            break
    return " ".join(bits)


def _is_failure(ev: dict[str, Any]) -> bool:
    t = str(ev.get("type", ""))
    return "fail" in t or "error" in t or _payload(ev).get("task_state") in ("failed", "blocked")


def _is_done(ev: dict[str, Any]) -> bool:
    t = str(ev.get("type", ""))
    return "complet" in t or _payload(ev).get("task_state") == "completed"


def _cut_reply(text: str) -> tuple[str, bool]:
    """Bound a model reply; cut at a sentence boundary when possible."""
    if len(text) <= MAX_REPLY:
        return text, False
    head = text[:MAX_REPLY]
    k = max(head.rfind(". "), head.rfind("! "), head.rfind("? "))
    head = head[:k + 1] if k > MAX_REPLY // 2 else head.rstrip()
    return head + " [truncated]", True


def _norm_q(q: str) -> str:
    return re.sub(r"[^a-z0-9 ]+", "", re.sub(r"\s+", " ", q.lower())).strip()


class SideConversationService:
    def __init__(self, store2: Phase2Store, store3: Phase3Store, settings: Any,
                 provider_getter: Callable[[], ModelProvider | None],
                 clock: Callable[[], float] = time.monotonic) -> None:
        self._s2 = store2
        self._s3 = store3
        self._settings = settings
        self._provider = provider_getter
        self._clock = clock
        self._sem: asyncio.Semaphore | None = None
        self._memory: OrderedDict[tuple[str, str], deque] = OrderedDict()
        self._rate: dict[tuple[str, str], deque] = {}
        self._dedupe: OrderedDict[tuple, tuple[float, dict[str, Any]]] = OrderedDict()

    # ----------------------------------------------------- ephemeral visit memory
    def _mem(self, session_id: str, visit: str) -> deque:
        key = (session_id, visit)
        dq = self._memory.get(key)
        if dq is None:
            dq = deque(maxlen=max(1, int(self._settings.p3_side_history_exchanges)))
            self._memory[key] = dq
            while len(self._memory) > MAX_VISITS:
                self._memory.popitem(last=False)
        else:
            self._memory.move_to_end(key)
        return dq

    def history_len(self, session_id: str, visit: str) -> int:
        return len(self._memory.get((session_id, visit), ()))

    def clear_visit(self, session_id: str, visit: str) -> int:
        self._rate.pop((session_id, visit), None)
        for k in [k for k in self._dedupe if k[0] == session_id and k[1] == visit]:
            self._dedupe.pop(k, None)
        dq = self._memory.pop((session_id, visit), None)
        return len(dq) if dq else 0

    def _remember(self, session_id: str, req: dict[str, Any], q: str, a: str) -> None:
        self._mem(session_id, req["visit_session_id"]).append((_clean(q, 500), _clean(a, MAX_REPLY)))

    # ----------------------------------------------------- rate limit + dedupe
    def _rate_limited(self, session_id: str, visit: str) -> bool:
        now = self._clock()
        dq = self._rate.setdefault((session_id, visit), deque())
        while dq and now - dq[0] > RATE_WINDOW_S:
            dq.popleft()
        if len(dq) >= RATE_MAX:
            return True
        dq.append(now)
        return False

    def _dedupe_key(self, session_id: str, req: dict[str, Any], ctx: Ctx) -> tuple:
        t = ctx.task["id"] if ctx.task else None
        return (session_id, req["visit_session_id"], req["agent_id"], t, ctx.revision,
                _norm_q(req["question"]))

    # ----------------------------------------------------- context (reads only)
    def _gather(self, session_id: str, agent_id: str, task_id: str | None) -> Ctx | None:
        """Real context for the asked agent. A task id from another session, or owned by a
        different (non-manager) agent, is rejected (returns None)."""
        s2 = self._s2
        if agent_id == "manager" and not task_id:
            board = s2.list_tasks_v2(session_id)
            return Ctx(agent_id, None, board=board, plan=self._s3.latest_plan(session_id),
                       revision=s2.last_seq_v2(session_id))
        task: dict[str, Any] | None
        if task_id:
            task = s2.get_task_v2(task_id)
            if task is None or task["session_id"] != session_id:
                return None
            if agent_id != "manager" and task["agent_id"] not in (agent_id, None):
                return None
        else:
            mine = [t for t in s2.list_tasks_v2(session_id) if t["agent_id"] == agent_id]
            if not mine:
                return None
            live = [t for t in mine if TaskStateV2(t["task_state"]) not in TERMINAL_STATES]
            task = (live or mine)[-1]
        tid = task["id"]
        events = [e for e in s2.events_after_v2(session_id, 0) if e.get("task_id") == tid]
        arts = [{"kind": str(a["kind"]), "summary": _clean(a["summary"], ITEM_CHARS)}
                for a in s2.list_artifacts_v2(session_id) if a["task_id"] == tid]
        return Ctx(agent_id, task, events=events[-EVENT_POOL:], artifacts=arts[-8:],
                   revision=task["revision"])

    @staticmethod
    def _select(c: Ctx, intent: str) -> dict[str, list[str]]:
        """Pick bounded sections of REAL data for ``intent``. Returns name -> text lines."""
        out: dict[str, list[str]] = {}
        for sec in CONTEXT_FOR_INTENT.get(intent, CONTEXT_FOR_INTENT["status"]):
            if sec == "events":
                out[sec] = [_ev_text(e) for e in c.events[-RECENT_EVENTS:]]
            elif sec == "completed_events":
                out[sec] = [_ev_text(e) for e in c.events if _is_done(e)][-RECENT_EVENTS:]
            elif sec in ("blocking_events", "failure_events"):
                out[sec] = [_ev_text(e) for e in c.events if _is_failure(e)][-RECENT_EVENTS:]
            elif sec == "artifacts":
                out[sec] = [f"{a['kind']}: {a['summary']}" for a in c.artifacts[-5:]]
            elif sec in ("test_report", "code_diff"):
                kind = sec
                out[sec] = [a["summary"] for a in c.artifacts if a["kind"] == kind][-3:]
        return out

    @staticmethod
    def _last_update(ev: dict[str, Any] | None) -> str:
        if ev is None:
            return "no recorded events for this task yet"
        p = _payload(ev)
        bits = [ev.get("type", "event")]
        summary = p.get("result_summary") or p.get("summary")
        if summary:
            bits.append(_clean(summary, 120))
        return f"{' - '.join(bits)} at {ev.get('occurred_at', '?')}"

    def _snapshot_text(self, c: Ctx) -> str:
        if c.task is None:
            tally: dict[str, int] = {}
            for t in c.board:
                tally[t["task_state"]] = tally.get(t["task_state"], 0) + 1
            board = (", ".join(f"{n} {s}" for s, n in sorted(tally.items()))
                     if tally else "no tasks")
            plan = (f" Latest plan: '{_clean(c.plan['title'], 60)}' is {c.plan['status']} "
                    f"(revision {c.plan['revision']})." if c.plan else " No manager plan yet.")
            return f"The board has {len(c.board)} task(s): {board}.{plan}"
        t = c.task
        text = (f"Task '{_clean(t['title'], 80)}' is {t['task_state']}, revision {t['revision']}. "
                f"Last update: {self._last_update(c.events[-1] if c.events else None)}.")
        if c.artifacts:
            text += " Artifacts: " + "; ".join(
                f"{a['kind']}: {a['summary'][:100]}" for a in c.artifacts[-5:]) + "."
        return text

    def _factual_answer(self, c: Ctx, intent: str, sel: dict[str, list[str]]) -> str:
        """Intent-specific template built ONLY from recorded data. Says so when empty."""
        if c.task is None:
            return self._snapshot_text(c)
        t = c.task
        title = _clean(t["title"], 80)
        state = t["task_state"]
        if intent == "current_task":
            last = self._last_update(c.events[-1] if c.events else None)
            return f"I'm on '{title}' ({state}). Latest recorded progress: {last}."
        if intent == "completed":
            done = sel.get("completed_events", [])
            arts = [a for a in sel.get("artifacts", []) if a.startswith("completion")]
            if not done and not arts and state != "completed":
                return f"Nothing is recorded as completed yet for '{title}' (it is {state})."
            bits = done + arts
            tail = ("; ".join(bits)) if bits else "no further detail recorded"
            return f"'{title}' is {state}. Recorded completions: {tail}."
        if intent == "blocking":
            fails = sel.get("blocking_events", [])
            if state == "blocked":
                return (f"'{title}' is blocked. Recorded: "
                        f"{'; '.join(fails) or 'no reason was recorded'}.")
            if fails:
                return f"'{title}' is {state}. Recorded problems: {'; '.join(fails)}."
            return f"Nothing is recorded as blocking '{title}'; it is {state}."
        if intent == "test_failure":
            reports = sel.get("test_report", [])
            fails = sel.get("failure_events", [])
            if not reports and not fails:
                return f"No test report or failure is recorded for '{title}'."
            parts = []
            if reports:
                parts.append("Test report: " + "; ".join(reports))
            if fails:
                parts.append("Failures recorded: " + "; ".join(fails))
            return " ".join(parts)
        if intent == "file_change":
            diffs = sel.get("code_diff", [])
            if not diffs:
                return f"No diff summary is recorded for '{title}', so I cannot say what changed."
            return "Recorded diff summary: " + "; ".join(diffs) + "."
        if intent == "why":
            arts = sel.get("artifacts", [])
            plan = [a for a in arts if a.startswith(("implementation_plan", "research_report"))]
            if plan:
                return f"Recorded reasoning for '{title}': " + "; ".join(plan) + "."
            return (f"No rationale is recorded for '{title}'. What I have: "
                    + self._snapshot_text(c))
        return self._snapshot_text(c)

    def _model_context(self, c: Ctx, intent: str, sel: dict[str, list[str]]) -> str:
        if c.task is None:
            lines = [f"- {_clean(t['title'], 70)} [{t['task_state']}] owner={t['agent_id'] or 'none'}"
                     for t in c.board[:15]]
            plan = (f"Latest plan: {_clean(c.plan['title'], 60)} status={c.plan['status']} "
                    f"revision={c.plan['revision']}" if c.plan else "No manager plan.")
            text = "Board:\n" + ("\n".join(lines) or "(empty)") + "\n" + plan
        else:
            t = c.task
            parts = [f"Intent: {intent}", f"Task: {_clean(t['title'], 100)}",
                     f"State: {t['task_state']}", f"Owner: {t['agent_id'] or 'none'}",
                     f"Revision: {t['revision']}"]
            for name, lines in sel.items():
                parts.append(f"{name}:\n" + ("\n".join(f"- {x}" for x in lines) or "(none recorded)"))
            text = "\n".join(parts)
        if len(text) > MAX_CONTEXT_CHARS:
            text = text[:MAX_CONTEXT_CHARS].rstrip() + "\n[context truncated]"
        return text

    # ----------------------------------------------------- answer
    def _reply(self, req: dict[str, Any], *, reply: str, provenance: str, status: str,
               ctx: Ctx | None, revision: int, label: str | None = None,
               intent: str | None = None, context_used: list[str] | None = None,
               action: dict[str, Any] | None = None, truncated: bool = False,
               deduped: bool = False) -> dict[str, Any]:
        t = ctx.task if ctx else None
        stale = (req.get("context_revision") is not None and ctx is not None
                 and req["context_revision"] < revision)
        amendment = ({"summary": action["summary"], "requires_approval": True}
                     if action else None)
        return {
            "request_id": req["request_id"], "agent_id": req["agent_id"],
            "task_id": t["id"] if t else req.get("task_id"),
            "visit_session_id": req["visit_session_id"], "reply": reply,
            "provenance": provenance, "status": status, "context_revision": revision,
            "stale_context": bool(stale),
            "task_snapshot": ({"task_id": t["id"], "title": t["title"], "state": t["task_state"],
                               "agent_id": t["agent_id"], "revision": t["revision"]} if t else None),
            "label": label, "intent": intent, "context_used": context_used or [],
            "truncated": truncated, "deduped": deduped,
            "proposed_action": action,
            "proposed_amendment": amendment,   # legacy alias of proposed_action
        }

    @staticmethod
    def _action(req: dict[str, Any], ctx: Ctx, kind: str, negated: bool) -> dict[str, Any]:
        text = _clean(req["question"].strip(), 140)
        t = ctx.task
        return {
            "kind": kind, "negated": negated,
            "target_task_id": t["id"] if t else None,
            "target_agent": req["agent_id"], "text": text,
            "requires_confirmation": True, "applied": False,
            "summary": "Requested change (not applied): " + text,
        }

    async def ask(self, session_id: str, req: dict[str, Any]) -> dict[str, Any]:
        """``req``: request_id, agent_id, task_id?, visit_session_id, question,
        context_revision?. Returns the SideReply dict. Never raises for domain outcomes."""
        demo = self._settings.state_source_mode == "demo"
        base_prov = "SCRIPTED_DEMO" if demo else "TASK_SNAPSHOT"
        log.info("side question agent=%s qlen=%d", req["agent_id"], len(req["question"]))

        if req["agent_id"] not in VALID_AGENTS:
            return self._reply(req, reply=f"Unknown agent '{_clean(req['agent_id'], 30)}'. "
                               f"Valid agents: {', '.join(VALID_AGENTS)}.",
                               provenance="NONE", status="unavailable", ctx=None, revision=0)
        ctx = self._gather(session_id, req["agent_id"], req.get("task_id"))
        if ctx is None:
            what = "that task" if req.get("task_id") else f"the {req['agent_id']}"
            return self._reply(req, reply=f"I have no recorded task data for {what}, so I "
                               "cannot answer from real state.",
                               provenance="NONE", status="unavailable", ctx=None, revision=0)

        question = req["question"].strip()
        visit = req["visit_session_id"]

        dkey = self._dedupe_key(session_id, req, ctx)
        hit = self._dedupe.get(dkey)
        if hit is not None and self._clock() - hit[0] <= DEDUPE_WINDOW_S:
            out = dict(hit[1])
            out.update(request_id=req["request_id"], deduped=True,
                       stale_context=bool(req.get("context_revision") is not None
                                          and req["context_revision"] < ctx.revision))
            return out
        if self._rate_limited(session_id, visit):
            return self._reply(req, reply="You are asking too quickly. Wait a few seconds and "
                               "ask again.", provenance="NONE", status="rate_limited",
                               ctx=ctx, revision=ctx.revision)

        out = await self._answer(session_id, req, ctx, question, demo, base_prov)
        if out["status"] in ("answered", "amendment_suggested"):
            self._dedupe[dkey] = (self._clock(), out)
            while len(self._dedupe) > MAX_DEDUPE:
                self._dedupe.popitem(last=False)
        return out

    async def _answer(self, session_id: str, req: dict[str, Any], ctx: Ctx, question: str,
                      demo: bool, base_prov: str) -> dict[str, Any]:
        cls = classify(question)
        prefix = LABEL_DEMO + " " if demo else ""
        snapshot = prefix + self._snapshot_text(ctx)

        if cls.is_change:
            action = self._action(req, ctx, cls.action_kind or "change", cls.negated)
            reply = ("I did not apply that. /btw is read-only, so changing work needs your "
                     "approval through the normal task flow. " + snapshot)
            self._remember(session_id, req, question, reply)
            return self._reply(req, reply=reply, provenance=base_prov,
                               status="amendment_suggested", ctx=ctx, revision=ctx.revision,
                               label=LABEL_DEMO if demo else LABEL_NO_MODEL,
                               intent="change_request", action=action)

        intent = cls.intent
        sel = self._select(ctx, intent)
        used = ["task"] + [k for k, v in sel.items() if v and k != "task"] if ctx.task else ["board"]
        provider = None if demo else self._provider()
        if provider is None:
            body = self._factual_answer(ctx, intent, sel)
            reply = (LABEL_DEMO if demo else LABEL_NO_MODEL + ":") + " " + body
            self._remember(session_id, req, question, reply)
            return self._reply(req, reply=reply, provenance=base_prov, status="answered",
                               ctx=ctx, revision=ctx.revision,
                               label=LABEL_DEMO if demo else LABEL_NO_MODEL,
                               intent=intent, context_used=used)

        fallback_label = LABEL_NO_MODEL
        if self._sem is None:
            self._sem = asyncio.Semaphore(max(1, int(self._settings.p3_side_max_concurrency)))
        if self._sem.locked():
            return self._reply(req, reply="Another side conversation is busy. Try again in a moment.",
                               provenance="NONE", status="busy", ctx=ctx, revision=ctx.revision)
        async with self._sem:
            messages: list[dict[str, str]] = []
            for q, a in self._mem(session_id, req["visit_session_id"]):
                messages += [{"role": "user", "content": q}, {"role": "assistant", "content": a}]
            messages.append({"role": "user", "content":
                             f"Context:\n{self._model_context(ctx, intent, sel)}"
                             f"\n\nQuestion: {question}"})
            try:
                text = await asyncio.wait_for(
                    provider.complete(system=SYSTEM_PROMPT, messages=messages, max_tokens=400,
                                      purpose="side"),
                    timeout=float(self._settings.p3_side_timeout_seconds))
            except asyncio.TimeoutError:
                return self._reply(req, reply="The model did not answer in time. Recorded state: "
                                   + snapshot, provenance="TASK_SNAPSHOT", status="timeout",
                                   ctx=ctx, revision=ctx.revision, label=fallback_label,
                                   intent=intent, context_used=used)
            except ProviderRefused:
                return self._reply(req, reply="The model declined to answer that, so I will not "
                                   "guess. Recorded state: " + snapshot,
                                   provenance="TASK_SNAPSHOT", status="model_declined",
                                   ctx=ctx, revision=ctx.revision, label=fallback_label,
                                   intent=intent, context_used=used)
            except Exception:  # noqa: BLE001
                log.warning("side provider failed")
                reply = "The model is unavailable. Recorded state: " + snapshot
                return self._reply(req, reply=reply, provenance="TASK_SNAPSHOT",
                                   status="answered", ctx=ctx, revision=ctx.revision,
                                   label=fallback_label, intent=intent, context_used=used)
        text = _clean(text, MAX_REPLY * 4).strip()
        if not text:
            return self._reply(req, reply="The model returned nothing. Recorded state: " + snapshot,
                               provenance="TASK_SNAPSHOT", status="answered",
                               ctx=ctx, revision=ctx.revision, label=fallback_label,
                               intent=intent, context_used=used)
        text, cut = _cut_reply(text)
        self._remember(session_id, req, question, text)
        return self._reply(req, reply=text, provenance="LIVE_MODEL", status="answered",
                           ctx=ctx, revision=ctx.revision, label=LABEL_MODEL, intent=intent,
                           context_used=used, truncated=cut)
