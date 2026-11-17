"""Deterministic /btw question classifier (pure: no I/O, no store, no model).

Splits a side question into either a READ question (with an intent that decides which real
task context to attach) or a CHANGE request (turned into a proposed action that is never
executed). Negation aware in both directions:

* ``"I'm not asking about the tests, what are you working on?"`` -> intent ``current_task``
  (the negated clause is ignored when scoring intents).
* ``"don't approve it"`` -> a change request whose proposed action is ``withhold_approval``,
  never ``approve``.
"""
from __future__ import annotations

import re
from dataclasses import dataclass

# --- read intents -----------------------------------------------------------------------
INTENTS = ("test_failure", "blocking", "file_change", "completed", "why", "current_task",
           "status")

# Which real context sections each intent attaches (bounded; see side.py for the caps).
CONTEXT_FOR_INTENT: dict[str, tuple[str, ...]] = {
    "current_task": ("task", "events"),
    "why": ("task", "events", "artifacts"),
    "completed": ("task", "completed_events", "artifacts"),
    "blocking": ("task", "blocking_events", "events"),
    "test_failure": ("task", "test_report", "failure_events"),
    "file_change": ("task", "code_diff"),
    "status": ("task", "events", "artifacts"),
}

_NEG_CLAUSE = re.compile(
    r"\b(?:not|never|without|no|\w+n'?t)\s+"
    r"(?:(?:asking|talking|wondering|interested|curious)\s+(?:about\s+)?)?"
    r"(?:the\s+|any\s+|a\s+)?\w+(?:\s+\w+)?", re.I)

_RULES: tuple[tuple[str, re.Pattern[str]], ...] = (
    ("test_failure", re.compile(
        r"\b(?:tests?|pytest|assert\w*|suite|ci)\b.*\b(?:fail\w*|error\w*|broke\w*|red|"
        r"crash\w*)\b|\b(?:fail\w*|error\w*|broke\w*)\b.*\b(?:tests?|pytest|assert\w*)\b|"
        r"\bexplain\b.*\b(?:failure|error|traceback)\b|\btest (?:failure|report|results?)\b",
        re.I)),
    ("blocking", re.compile(
        r"\bblock(?:ed|er|ers|ing)?\b|\bstuck\b|\bwaiting (?:on|for)\b|\bin (?:the|your) way\b|"
        r"\bholding you\b|\bcan'?t proceed\b", re.I)),
    ("file_change", re.compile(
        r"\bdiffs?\b|\bwhat changed\b|\bchanged (?:in|about)\b|\bfiles?\b|\bcommits?\b|"
        r"\.(?:py|cs|ts|js|json|md|cpp|java|go|rs)\b|\bpatch\b|\bmodif(?:ied|ication)s?\b",
        re.I)),
    ("completed", re.compile(
        r"\b(?:complet(?:ed|e)|finish(?:ed)?|done|accomplish\w*|achiev\w*|so far|"
        r"delivered|shipped)\b", re.I)),
    ("why", re.compile(
        r"^\s*why\b|\bwhy (?:did|are|is|this|that|do|was)\b|\breason(?:s|ing)?\b|"
        r"\brationale\b|\bhow come\b", re.I)),
    ("current_task", re.compile(
        r"\bworking on\b|\bwhat (?:are|r) you doing\b|\bcurrent(?:ly)?\b|\bright now\b|"
        r"\bwhat'?s your task\b|\byour task\b|\bbusy with\b", re.I)),
)


def _strip_negated(text: str) -> str:
    return _NEG_CLAUSE.sub(" ", text)


def classify_intent(question: str) -> str:
    """Read intent for ``question``. Falls back to ``status``. Deterministic."""
    q = _strip_negated(question.strip())
    for name, rx in _RULES:
        if rx.search(q):
            return name
    return "status"


# --- change requests --------------------------------------------------------------------
_VERBS = ("cancel|stop|abort|halt|switch|change|modify|redo|rewrite|delete|remove|drop|deploy|"
          "publish|push|merge|revert|undo|restart|rerun|re-run|replace|rename|add|install|run|"
          "execute|fix|update|use|make|set|skip|pause|resume|reassign|approve|commit|create|"
          "send|write|implement|ship|accept|reject|bypass|disable|ignore")
# Lead-ins before the verb, each optionally followed by a comma. rc14 (E2E journey D6): spoken requests start with a
# softener or the agent's name ("Actually, change the password rule to 12 characters", "Developer, change ...");
# before, the comma broke the match and such a request was answered as a status question (still read-only, but no
# proposed action was offered for confirmation).
_LEAD = (r"(?:(?:please|can you|could you|would you|will you|just|now|then|also|let'?s|go ahead and|"
         r"i want you to|i'd like you to|you should|you need to|you must|i need you to|"
         r"actually|ok(?:ay)?|oh|so|wait|hey|hmm|"
         r"researcher|developer|tester|manager|musedesk)\s*,?)")
_NEG = r"(?:don'?t|do not|never|no need to|shouldn'?t|should not|must not|mustn'?t)"
_AMEND_RE = re.compile(
    rf"(?:^|[.!?;]\s+)(?:{_LEAD}\s+)*(?:{_VERBS})\b"
    r"|\binstead\b|\b(?:don'?t|do not|stop|quit)\s+(?:use|using|do|doing)\b|\bswitch to\b",
    re.I)
_NEG_DIRECTIVE_RE = re.compile(
    rf"(?:^|[.!?;]\s+)(?:{_LEAD}\s+)*(?:i\s+)?(?:please\s+)?{_NEG}"
    rf"(?:\s+(?:want|need) you to)?\s+(?:\w+\s+){{0,2}}?({_VERBS})\b", re.I)
_LEAD_VERB_RE = re.compile(
    rf"(?:^|[.!?;]\s+)(?:{_LEAD}\s+)*({_VERBS})\b", re.I)

_STOPPERS = {"stop", "cancel", "abort", "halt", "pause"}
_RUNNERS = {"run", "execute", "restart", "rerun", "re-run", "resume"}
_APPROVERS = {"approve", "accept", "commit", "merge", "ship", "deploy", "publish", "push"}


@dataclass(frozen=True)
class Classification:
    is_change: bool
    intent: str                 # read intent, or "change_request"
    action_kind: str | None = None
    negated: bool = False


def _kind_for(verb: str, text: str, negated: bool) -> str:
    v = verb.lower()
    tests = bool(re.search(r"\btests?\b", text, re.I))
    if v in ("approve", "accept", "commit", "merge", "ship"):
        base = "approve"
    elif v in ("deploy", "publish", "push"):
        base = "deploy"
    elif v in _STOPPERS:
        base = "stop"
    elif v in ("skip", "bypass", "disable", "ignore") and tests:
        base = "skip_tests"
    elif v in _RUNNERS:
        base = "run"
    else:
        base = "change"
    if not negated:
        return base
    return {"approve": "withhold_approval", "deploy": "withhold_approval",
            "stop": "keep_running", "skip_tests": "keep_tests",
            "run": "hold", "change": "keep_as_is"}[base]


def classify(question: str) -> Classification:
    q = question.strip()
    if re.match(rf"\s*(?:{_LEAD}\s+)*(?:don'?t|do not|stop|quit)\s+(?:use|using)\b", q, re.I):
        return Classification(True, "change_request", "change", False)
    m = _NEG_DIRECTIVE_RE.search(q)
    if m:
        return Classification(True, "change_request", _kind_for(m.group(1), q, True), True)
    if _AMEND_RE.search(q):
        lead = _LEAD_VERB_RE.search(q)
        verb = lead.group(1) if lead else ("use" if re.search(r"\buse\b|\busing\b", q, re.I)
                                           else "change")
        return Classification(True, "change_request", _kind_for(verb, q, False), False)
    return Classification(False, classify_intent(q))
