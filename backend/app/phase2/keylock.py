"""Per-(session, idempotency key) async lock.

An idempotent command checks the key store, runs, then saves the result. Two requests
carrying the same key that overlap (a client retry while the first is still running)
would both miss the stored result and both execute. Holding this lock across
check -> run -> save makes the second request wait and then replay the first's result.
"""
from __future__ import annotations

import asyncio
from contextlib import asynccontextmanager

_locks: dict[tuple[str, str], list] = {}   # key -> [lock, users]


@asynccontextmanager
async def idempotency_lock(session_id: str, key: str):
    k = (session_id, key)
    entry = _locks.get(k)
    if entry is None:
        entry = _locks[k] = [asyncio.Lock(), 0]
    entry[1] += 1
    try:
        async with entry[0]:
            yield
    finally:
        entry[1] -= 1
        if entry[1] == 0:
            _locks.pop(k, None)
