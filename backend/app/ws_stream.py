"""Shared replay-then-live pump for the v1 and v2 event WebSockets.

The caller subscribes its queue BEFORE calling this, so nothing published between the
replay read and the live loop is lost; ``sent`` de-duplicates the overlap, so the client
sees each seq exactly once, in order.

The pump also watches the socket for a client disconnect. Without that, a handler whose
client went away sat in ``queue.get()`` until the next event for that session happened
to be published (possibly never), keeping its subscriber queue registered on the bus.
"""
from __future__ import annotations

import asyncio
from typing import Any, Callable, Iterable

from fastapi import WebSocket, WebSocketDisconnect


async def _until_disconnect(ws: WebSocket) -> None:
    while True:
        msg = await ws.receive()
        if msg.get("type") == "websocket.disconnect":
            return


async def pump_events(ws: WebSocket, queue: asyncio.Queue, replay: Iterable[dict[str, Any]],
                      after_seq: int, dump: Callable[[Any], dict[str, Any]]) -> None:
    gone = asyncio.create_task(_until_disconnect(ws))
    nxt: asyncio.Task | None = None
    try:
        sent = after_seq
        for ev in replay:
            if ev["seq"] > sent:
                await ws.send_json(ev)
                sent = ev["seq"]
        while True:
            nxt = asyncio.create_task(queue.get())
            await asyncio.wait({nxt, gone}, return_when=asyncio.FIRST_COMPLETED)
            if not nxt.done():
                return  # client disconnected while idle
            env = nxt.result()
            if env.seq > sent:
                await ws.send_json(dump(env))
                sent = env.seq
    except WebSocketDisconnect:
        pass
    finally:
        for t in (gone, nxt):
            if t is not None and not t.done():
                t.cancel()
