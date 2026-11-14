"""Read-only ``/btw`` side conversation.

HARD INVARIANTS (tested): this module performs no writes to the task or event stores,
publishes no events, calls no mutating service, never touches an agent transcript, and
gives a model NO tools. The only state it owns is ephemeral per-visit memory in process
memory. A request that asks to change work is turned into a *proposed amendment* that is
explicitly NOT applied.
"""
from __future__ import annotations

import asyncio
import logging
import re
from collections import OrderedDict, deque
from dataclasses import dataclass, field
from typing import Any, Callable

from ..phase2.models import TERMINAL_STATES, TaskStateV2
from ..phase2.sanitize import sanitize_text
from ..phase2.store import Phase2Store
from .provider import ModelProvider
from .store import Phase3Store

log = logging.getLogger("musedesk.phase3.side")

VALID_AGENTS = ("researcher", "developer", "tester", "manager")
MAX_REPLY = 1200
MAX_VISITS = 256
RECENT_EVENTS = 5

_VERBS = ("cancel|stop|abort|halt|switch|change|modify|redo|rewrite|delete|remove|drop|deploy|"
          "publish|push|merge|revert|undo|restart|rerun|re-run|replace|rename|add|install|run|"
          "execute|fix|update|use|make|set|skip|pause|resume|reassign|approve|commit|create|"
          "send|write|implement")
_LEAD = (r"(?:please|can you|could you|would you|will you|just|now|then|also|let'?s|go ahead and|"
         r"i want you to|i'd like you to|you should|you need to|you must|i need you to)")
_AMEND_RE = re.compile(
    rf"(?:^|[.!?;]\s+)(?:{_LEAD}\s+)*(?:{_VERBS})\b"
    r"|\binstead\b|\b(?:don'?t|do not|stop|quit)\s+(?:use|using|do|doing)\b|\bswitch to\b",
    re.I)

SYSTEM_PROMPT = (
    "You answer a user's side question about a software task. You are strictly READ-ONLY: you "
    "cannot change, start, stop, cancel, approve or deploy anything, and you have no tools. "
    "Answer ONLY from the context block provided. If the context does not contain the answer, "
    "say you do not have that information. Never invent progress, results or evidence. "
    "Keep answers under 120 words. If asked to change work, say it needs approval through the "
    "normal task flow and that you did not do it."
)


def is_change_request(question: str) -> bool:
    return bool(_AMEND_RE.search(question.strip()))


@dataclass
class Ctx:
    agent_id: str
    task: dict[str, Any] | None
    events: list[dict[str, Any]] = field(default_factory=list)
    artifacts: list[str] = field(default_factory=list)
    board: list[dict[str, Any]] = field(default_factory=list)
    plan: dict[str, Any] | None = None
    revision: int = 0


def _clean(text: Any, n: int) -> str:
    return sanitize_text(str(text))[:n] if text is not None else ""


class SideConversationService:
    def __init__(self, store2: Phase2Store, store3: Phase3Store, settings: Any,
                 provider_getter: Callable[[], ModelProvider | None]) -> None:
        self._s2 = store2
        self._s3 = store3
        self._settings = settings
        self._provider = provider_getter
        self._sem: asyncio.Semaphore | None = None
        self._memory: OrderedDict[tuple[str, str], deque] = OrderedDict()

    # ─────────────────────── ephemeral visit memory ───────────────────────
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
        dq = self._memory.pop((session_id, visit), None)
        return len(dq) if dq else 0

    # ─────────────────────── context (reads only) ───────────────────────
    def _gather(self, session_id: str, agent_id: str, task_id: str | None) -> Ctx | None:
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
        else:
            mine = [t for t in s2.list_tasks_v2(session_id) if t["agent_id"] == agent_id]
            if not mine:
                return None
            live = [t for t in mine if TaskStateV2(t["task_state"]) not in TERMINAL_STATES]
            task = (live or mine)[-1]
        tid = task["id"]
        events = [e for e in s2.events_after_v2(session_id, 0) if e.get("task_id") == tid]
        arts = [f"{a['kind']}: {_clean(a['summary'], 100)}"
                for a in s2.list_artifacts_v2(session_id) if a["task_id"] == tid][:5]
        return Ctx(agent_id, task, events=events[-RECENT_EVENTS:], artifacts=arts,
                   revision=task["revision"])

    @staticmethod
    def _last_update(ev: dict[str, Any] | None) -> str:
        if ev is None:
            return "no recorded events for this task yet"
        p = ev.get("payload") or {}
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
            text += " Artifacts: " + "; ".join(c.artifacts) + "."
        return text

    def _model_context(self, c: Ctx) -> str:
        if c.task is None:
            lines = [f"- {_clean(t['title'], 70)} [{t['task_state']}] owner={t['agent_id'] or 'none'}"
                     for t in c.board[:15]]
            plan = (f"Latest plan: {_clean(c.plan['title'], 60)} status={c.plan['status']} "
                    f"revision={c.plan['revision']}" if c.plan else "No manager plan.")
            return "Board:\n" + ("\n".join(lines) or "(empty)") + "\n" + plan
        t = c.task
        evs = [f"- {e.get('type')} state={(e.get('payload') or {}).get('task_state', '?')} "
               f"{_clean((e.get('payload') or {}).get('result_summary', ''), 100)}".rstrip()
               for e in c.events]
        return (f"Task: {_clean(t['title'], 100)}\nState: {t['task_state']}\nOwner: "
                f"{t['agent_id'] or 'none'}\nRevision: {t['revision']}\nRecent events:\n"
                + ("\n".join(evs) or "(none)") + "\nArtifacts:\n"
                + ("\n".join(f"- {a}" for a in c.artifacts) or "(none)"))

    # ─────────────────────── answer ───────────────────────
    def _reply(self, req: dict[str, Any], *, reply: str, provenance: str, status: str,
               ctx: Ctx | None, revision: int, amendment: dict[str, Any] | None = None) -> dict[str, Any]:
        t = ctx.task if ctx else None
        stale = (req.get("context_revision") is not None and ctx is not None
                 and req["context_revision"] < revision)
        return {
            "request_id": req["request_id"], "agent_id": req["agent_id"],
            "task_id": t["id"] if t else req.get("task_id"),
            "visit_session_id": req["visit_session_id"], "reply": reply,
            "provenance": provenance, "status": status, "context_revision": revision,
            "stale_context": bool(stale),
            "task_snapshot": ({"task_id": t["id"], "title": t["title"], "state": t["task_state"],
                               "agent_id": t["agent_id"], "revision": t["revision"]} if t else None),
            "proposed_amendment": amendment,
        }

    async def ask(self, session_id: str, req: dict[str, Any]) -> dict[str, Any]:
        """``req``: request_id, agent_id, task_id?, visit_session_id, question,
        context_revision?. Returns the SideReply dict. Never raises for domain outcomes."""
        mode = self._settings.state_source_mode
        demo = mode == "demo"
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

        prefix = "[SCRIPTED DEMO] " if demo else ""
        snapshot = prefix + self._snapshot_text(ctx)
        question = req["question"].strip()

        if is_change_request(question):
            summary = "Requested change (not applied): " + _clean(question, 140)
            reply = (f"I did not apply that. /btw is read-only, so changing work needs your "
                     f"approval through the normal task flow. {snapshot}")
            self._remember(session_id, req, question, reply)
            return self._reply(req, reply=reply, provenance=base_prov, status="amendment_suggested",
                               ctx=ctx, revision=ctx.revision,
                               amendment={"summary": summary, "requires_approval": True})

        provider = None if demo else self._provider()
        if provider is None:
            self._remember(session_id, req, question, snapshot)
            return self._reply(req, reply=snapshot, provenance=base_prov, status="answered",
                               ctx=ctx, revision=ctx.revision)

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
                             f"Context:\n{self._model_context(ctx)}\n\nQuestion: {question}"})
            try:
                text = await asyncio.wait_for(
                    provider.complete(system=SYSTEM_PROMPT, messages=messages, max_tokens=400),
                    timeout=float(self._settings.p3_side_timeout_seconds))
            except asyncio.TimeoutError:
                return self._reply(req, reply="The model did not answer in time. Recorded state: "
                                   + snapshot, provenance="TASK_SNAPSHOT", status="timeout",
                                   ctx=ctx, revision=ctx.revision)
            except Exception:  # noqa: BLE001
                log.warning("side provider failed")
                reply = "The model is unavailable. Recorded state: " + snapshot
                return self._reply(req, reply=reply, provenance="TASK_SNAPSHOT",
                                   status="answered", ctx=ctx, revision=ctx.revision)
        text = _clean(text, MAX_REPLY).strip()
        if not text:
            return self._reply(req, reply="The model returned nothing. Recorded state: " + snapshot,
                               provenance="TASK_SNAPSHOT", status="answered",
                               ctx=ctx, revision=ctx.revision)
        self._remember(session_id, req, question, text)
        return self._reply(req, reply=text, provenance="LIVE_MODEL", status="answered",
                           ctx=ctx, revision=ctx.revision)

    def _remember(self, session_id: str, req: dict[str, Any], q: str, a: str) -> None:
        self._mem(session_id, req["visit_session_id"]).append((_clean(q, 500), _clean(a, MAX_REPLY)))
