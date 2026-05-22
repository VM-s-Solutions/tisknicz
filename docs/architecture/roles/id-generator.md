---
role: IdGenerator (IIdGenerator)
kind: domain-service
status: accepted
---

# IdGenerator

## Responsibility

Hand out fresh entity identifiers (ULID strings at launch). Exists as an
abstraction so the ULID library is contained in `Infra.Common` and never
leaks into `Core.Domain` (per ADR 0001 — Core.Domain has no third-party
dependencies).

## Collaborators

- (None — leaf abstraction.)

## Knows

- The id format (26-char ULID, lexicographically sortable, encodes
  timestamp).

## Does NOT know

- Which entity is being generated for. Every entity type uses the same
  id-shape.
- Whether the id will collide (ULID's collision space is effectively
  infinite; the impl is thread-safe).
- Whether the id is "in use" — that's the repository's concern.

## Lifecycle

- **Created by:** DI container as singleton.
- **Modified by:** never (stateless).
- **Destroyed by:** never.

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Common/IIdGenerator.cs`.
Impl: `backend/src/Makables.Infra.Common/Identifiers/UlidIdGenerator.cs`.

## Why ULID over GUID

ULIDs are lexicographically sortable by creation time (the high 48 bits
are millisecond timestamp). Database B-tree indexes on the id column
get insert performance equivalent to autoincrement integers, without the
single-writer bottleneck or the global-secret leakage of sequential
integers exposed in URLs.

## Related

- ADRs: 0001 (Core.Domain free of third-party deps)
- Roles: every aggregate uses this on creation
