"""MuseDesk Phase 2 backend package (additive to Phase 1).

Everything in this package is strictly additive: new ``schema_version=2`` DTOs,
a separate v2 event store/bus, v2 services and v2 (``/v2/...``) routes. No Phase 1
module, route, field, event enum or table is modified. v1 subscribers never see
v2-only events because v2 events live in their own store (``events_v2``).
"""
from __future__ import annotations

SCHEMA_VERSION_2 = 2
