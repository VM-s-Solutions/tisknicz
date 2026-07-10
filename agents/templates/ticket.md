---
id: T-NNNN
title: <short imperative title>
status: draft            # draft | ready | in_progress | in_review | qa | done | blocked
size: M                  # S | M | L  (L must be split before going ready)
owner: <charter>         # the agent currently working it (pm sets this)
created: YYYY-MM-DD
updated: YYYY-MM-DD
depends_on: []           # ticket ids that must be done first
blocks: []               # tickets waiting on this one
stories: []              # US-<persona>-NNNN ids this satisfies (persona: customer | maker | admin)
adrs: []                 # ADR numbers in force (docs/adr/NNNN-*.md)
layers: []               # any of: ba, architect, dotnet-db, dotnet-backend, frontend, l10n
security_touching: false # true → SecOps gate mandatory
manual_steps: []         # owner-only: ef-migration, nswag-regen, db-seed, vendor-account, secret-rotation, deploy-trigger
phase: N                 # 1 | 2 | 3 | 4  (build phase, per docs/tickets/INDEX.md)
---

## Context
Why this ticket exists; the problem it solves; links to the audit finding / user story / owner request. If this ticket is part of a bundle (3–6 tightly-coupled tickets shipping as one PR per [ticket-lifecycle](../../docs/process/ticket-lifecycle.md) §"Bundle DoR"), name the bundle and its ordering here.

## Acceptance criteria
- [ ] **AC1** — Given <state>, When <action>, Then <observable outcome>.
- [ ] **AC2** — ...
(Every AC is an observable outcome with verifiable evidence at review time.)

## Out of scope
- What this ticket deliberately does NOT do (prevents scope creep).

## Implementation notes
Contract details (DTO shape, `BusinessErrorMessage` codes, entity fields), the sequence of layers, any ADR to read. Point at [patterns](../../docs/architecture/patterns.md) for the canonical backend + frontend patterns; the frontend consumes the NSwag-generated client only — never talks to a DB or the provider adapters (Comgate, Packeta, ARES, SendGrid, Mapbox) directly. Money is `long` minor units + `string Currency`; VAT rates in basis points. If the API contract changes, the NSwag client is regenerated for **every** affected host (`Web.Customer` :5001, `Web.Maker` :5002, `Web.Admin` :5003, `Web.Public` :5104) in the same PR.

## Status log
- YYYY-MM-DD HH:MM — draft (created by pm)
- YYYY-MM-DD HH:MM — ready (deps satisfied)
- ...one line per transition...

## Review
<!-- reviewer / secops / optimizer write verdicts here; PM reconciles before advancing state -->
