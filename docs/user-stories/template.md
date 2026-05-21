---
id: US-<persona>-NNNN
persona: customer | maker | admin
title: <short imperative>
status: draft | accepted
priority: P0 | P1 | P2
---

# US-<persona>-NNNN — <Title>

## Narrative

As a <persona>, I want <capability>, so that <value>.

## Background / context

Anything the implementer needs to understand about the user's situation.

## Roles in play

Per [ADR 0015](../../adr/0015-responsibility-driven-design.md), list the domain roles this story uses or extends. New roles must have a file under `docs/architecture/roles/`.

- **<RoleName>**
  - Responsibility: <one line; link to role file>
  - This story extends it by: <what new behavior, if any>
  - Collaborators used here: <subset of the role's collaborators relevant to this story>

- **<RoleName>**
  - Responsibility: <one line>
  - This story uses it as-is.

## Acceptance criteria

Given / When / Then format. Each AC item is verifiable.

- **AC-1** Given <context>, when <action>, then <observable outcome>
- **AC-2** ...

## Out of scope

- <something a reader might assume is in scope, but isn't>
- <another exclusion>

## Open questions

Link to `docs/questions/open.md` entries if any.

## Related

- ADR: NNNN
- Ticket: T-NNNN
- Roles: <links>
