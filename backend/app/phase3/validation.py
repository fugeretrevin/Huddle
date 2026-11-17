"""Pure Manager-plan validation. No I/O; shared by propose, revise and commit.

Model output (or a client edit) is NEVER trusted: every plan, however it was produced,
passes through :func:`validate_items` before it is stored or committed.
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import Any

# The manager coordinates; it never owns or executes an item.
EXECUTOR_AGENTS = ("researcher", "developer", "tester")

# kind -> agents allowed to own it.
KIND_ROLES: dict[str, tuple[str, ...]] = {
    "research": ("researcher",),
    "code": ("developer",),
    "implement": ("developer",),
    "test": ("tester",),
    "verify": ("tester",),
    "doc": ("researcher", "developer"),
}

PERMISSIONS = ("none", "read_repo", "workspace_write", "run_tests")

MAX_ITEMS = 12
MAX_TITLE = 120          # TaskCardV2.title limit: an item title becomes a card title
MAX_CRITERION = 200
MAX_CRITERIA = 8

# (reason label, pattern). Matched case-insensitively against title + criteria + permission.
_DANGER_PATTERNS: list[tuple[str, re.Pattern[str]]] = [
    ("deployment", re.compile(r"\b(deploy\w*|go[- ]live|release to prod\w*|publish(ing)? to prod\w*)\b", re.I)),
    ("deletion", re.compile(r"\b(delete\w*|remov(e|ing) (all|the) (files?|data)|rm\s+-\w+|\brm\b|drop (table|database)|wipe\w*|erase\w*|destroy\w*)\b", re.I)),
    ("spending", re.compile(r"\b(payments?|spend\w*|purchas\w*|billing|buy(ing)?|charge[sd]?|invoice\w*|subscription fee)\b", re.I)),
    ("credentials", re.compile(r"\b(credentials?|secrets?|tokens?|passwords?|passwd|api[ _-]?keys?|private keys?)\b", re.I)),
    ("external network post", re.compile(r"\b(webhook\w*|send (an? )?e-?mail|post(ing)? to (an? )?(external|public|remote)|external (api|service|server)|network access|upload\w* to)\b", re.I)),
    ("push to remote", re.compile(r"\b(git push|force[- ]push|push(ing)? (to )?(the )?(remote|origin|main|master|upstream))\b", re.I)),
]


@dataclass
class Issue:
    code: str
    message: str
    item_id: str | None = None

    def as_dict(self) -> dict[str, Any]:
        d: dict[str, Any] = {"code": self.code, "message": self.message}
        if self.item_id is not None:
            d["item_id"] = self.item_id
        return d


@dataclass
class ValidationResult:
    items: list[dict[str, Any]] = field(default_factory=list)   # normalised, with danger flags
    issues: list[Issue] = field(default_factory=list)
    cycle: list[str] | None = None

    @property
    def ok(self) -> bool:
        return not self.issues

    def codes(self) -> set[str]:
        return {i.code for i in self.issues}


def detect_danger(title: str, criteria: list[str], permission: str) -> list[str]:
    haystack = " \n ".join([title, *criteria, permission])
    return [label for label, pat in _DANGER_PATTERNS if pat.search(haystack)]


def find_cycle(deps: dict[str, list[str]]) -> list[str] | None:
    """Return one dependency cycle as ``[a, b, ..., a]`` or None. Iterative DFS."""
    WHITE, GREY, BLACK = 0, 1, 2
    colour = {k: WHITE for k in deps}
    for start in sorted(deps):
        if colour[start] != WHITE:
            continue
        stack: list[tuple[str, int]] = [(start, 0)]
        path: list[str] = []
        while stack:
            node, i = stack.pop()
            if i == 0:
                colour[node] = GREY
                path.append(node)
            targets = [d for d in deps[node] if d in deps]
            if i < len(targets):
                stack.append((node, i + 1))
                nxt = targets[i]
                if colour[nxt] == GREY:
                    return path[path.index(nxt):] + [nxt]
                if colour[nxt] == WHITE:
                    stack.append((nxt, 0))
            else:
                colour[node] = BLACK
                path.pop()
    return None


def topological_order(deps: dict[str, list[str]]) -> list[str]:
    """Dependencies first; ties broken by the order ids were declared. Assumes no cycle."""
    order: list[str] = []
    seen: set[str] = set()

    def visit(n: str) -> None:
        if n in seen:
            return
        seen.add(n)
        for d in deps[n]:
            if d in deps:
                visit(d)
        order.append(n)

    for n in deps:
        visit(n)
    return order


def _str_list(raw: Any, limit: int, maxlen: int) -> list[str] | None:
    if raw is None:
        return []
    if not isinstance(raw, list) or len(raw) > limit:
        return None
    out: list[str] = []
    for v in raw:
        if not isinstance(v, str):
            return None
        out.append(v.strip()[:maxlen])
    return out


def validate_items(raw_items: Any) -> ValidationResult:
    """Validate and normalise plan items. Never raises on bad input; reports Issues."""
    res = ValidationResult()
    if not isinstance(raw_items, list) or not raw_items:
        res.issues.append(Issue("EMPTY_PLAN", "a plan needs at least one item"))
        return res
    if len(raw_items) > MAX_ITEMS:
        res.issues.append(Issue("TOO_MANY_ITEMS", f"a plan may have at most {MAX_ITEMS} items"))
        return res

    ids: list[str] = []
    for idx, raw in enumerate(raw_items):
        if not isinstance(raw, dict):
            res.issues.append(Issue("ITEM_MALFORMED", f"item #{idx + 1} is not an object"))
            continue
        cid = raw.get("client_item_id")
        if not isinstance(cid, str) or not re.fullmatch(r"[A-Za-z0-9_.\-]{1,40}", cid or ""):
            res.issues.append(Issue("BAD_ITEM_ID", f"item #{idx + 1} needs a client_item_id "
                                    "of 1-40 letters, digits, '_', '-' or '.'"))
            continue
        if cid in ids:
            res.issues.append(Issue("DUPLICATE_ITEM_ID", f"client_item_id '{cid}' is used twice", cid))
            continue
        ids.append(cid)

        title = raw.get("title")
        if not isinstance(title, str) or not title.strip() or len(title.strip()) > MAX_TITLE:
            res.issues.append(Issue("BAD_TITLE", f"title must be 1-{MAX_TITLE} characters", cid))
            title = (title if isinstance(title, str) else "").strip()[:MAX_TITLE]
        title = title.strip()

        kind = raw.get("kind")
        agent = raw.get("proposed_agent")
        perm = raw.get("required_permission", "none")
        if perm is None:
            perm = "none"
        criteria = _str_list(raw.get("acceptance_criteria"), MAX_CRITERIA, MAX_CRITERION)
        depends = _str_list(raw.get("depends_on"), MAX_ITEMS, 40)
        if criteria is None:
            res.issues.append(Issue("BAD_CRITERIA", "acceptance_criteria must be a short list of strings", cid))
            criteria = []
        if depends is None:
            res.issues.append(Issue("BAD_DEPENDS_ON", "depends_on must be a list of client_item_ids", cid))
            depends = []

        if agent == "manager":
            res.issues.append(Issue("MANAGER_CANNOT_OWN",
                                    "the manager coordinates and cannot own or execute an item", cid))
        elif agent not in EXECUTOR_AGENTS:
            res.issues.append(Issue("UNKNOWN_AGENT",
                                    f"proposed_agent must be one of {', '.join(EXECUTOR_AGENTS)}", cid))
        if kind not in KIND_ROLES:
            res.issues.append(Issue("UNKNOWN_KIND",
                                    f"kind must be one of {', '.join(sorted(KIND_ROLES))}", cid))
        elif agent in EXECUTOR_AGENTS and agent not in KIND_ROLES[kind]:
            res.issues.append(Issue("ROLE_KIND_MISMATCH",
                                    f"a '{kind}' item cannot be owned by {agent} "
                                    f"(allowed: {', '.join(KIND_ROLES[kind])})", cid))
        if perm not in PERMISSIONS:
            res.issues.append(Issue("UNKNOWN_PERMISSION",
                                    f"required_permission must be one of {', '.join(PERMISSIONS)}", cid))

        reasons = detect_danger(title, criteria, perm if isinstance(perm, str) else "")
        res.items.append({
            "client_item_id": cid, "title": title,
            "kind": kind if isinstance(kind, str) else "",
            "proposed_agent": agent if isinstance(agent, str) else "",
            "depends_on": depends, "acceptance_criteria": criteria,
            "required_permission": perm if isinstance(perm, str) else "none",
            "dangerous": bool(reasons), "danger_reasons": reasons,
        })

    known = {it["client_item_id"] for it in res.items}
    for it in res.items:
        for d in it["depends_on"]:
            if d not in known:
                res.issues.append(Issue("UNKNOWN_DEPENDENCY",
                                        f"depends_on '{d}' is not an item of this plan",
                                        it["client_item_id"]))
    graph = {it["client_item_id"]: [d for d in it["depends_on"] if d in known] for it in res.items}
    cycle = find_cycle(graph)
    if cycle:
        res.cycle = cycle
        res.issues.append(Issue("DEPENDENCY_CYCLE",
                                "dependency cycle: " + " -> ".join(cycle), cycle[0]))
    return res
