---
role: <RoleName>
kind: aggregate | value-object | domain-service | repository | adapter | application-service
status: draft | accepted
---

# <RoleName>

## Responsibility

One or two sentences. Complete the sentence: "This role exists to ..."

## Collaborators

- **<OtherRole>** — what we ask of it
- **<OtherRole>** — what we ask of it

## Knows

- <state or authoritative reference owned by this role>

## Does NOT know

- <explicit anti-responsibility>
- <another anti-responsibility>

## Lifecycle

- **Created by:** <factory, command, or registration>
- **Modified by:** <which commands / state transitions>
- **Persisted by:** <repository, if applicable>
- **Destroyed by:** <command or "never (soft delete only)">

## Invariants

- <rule that must always be true about this role>
- <another invariant>

## Implementation pointer

Code path(s) once implemented: `backend/src/Makables.Core.Domain/<area>/<RoleName>.cs`

## Related

- ADRs: <list>
- Stories: <list>
- Other roles: <list>
