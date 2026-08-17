---
id: T-0164
title: ARES mapper hardening — nameless rows rejected, oversized company names capped
status: done
size: S
owner: dotnet-backend
created: 2026-07-29
updated: 2026-08-17
depends_on: [T-0032]
blocks: []
user_stories: [US-maker-0001, US-customer-0025]
adrs: [0018]
phase: 7
manual_steps: []
security_touching: true
layers: [dotnet-backend]
---

# T-0164 — ARES mapper hardening

> **Numbering note.** This ticket was spun off the T-0162 secops pass as
> "T-0163" (see the T-0162 status log). The T-0163 slot was independently
> reused on 2026-08-01 by the maker-proposed-categories ticket, which two
> user stories already reference; the ARES hardening therefore moved to
> T-0164 and the INDEX row was corrected in the same commit.

## Context

`AresResponseMapper.TryMap` is the single seam both registration paths cross —
`RegisterMaker` (T-0033) and the customer company snapshot (T-0162). The
T-0162 secops gate raised two LOW parity findings against it, both about
`obchodniJmeno`, both pre-existing since T-0032:

- **F-1 (oversized name).** `obchodniJmeno` is free registry text with no
  documented bound. `makers.company_name` and `users.company_name` are both
  `varchar(300)`. A longer name reached Postgres as a `22001` — a 500 on a
  user-triggered registration.
- **F-2 (empty name).** An absent name mapped to `string.Empty`, which then
  tripped `Maker.Create`'s `ArgumentException("CompanyName is required.")` —
  again a 500, on a path where every other structural defect in the ARES row
  (missing IČO, incomplete sídlo) already returns a clean Permanent
  `company.registryPermanent`.

Neither is exploitable, and neither is reachable through user-supplied text —
ARES is the source. They are wrong-error-class defects: the operator sees a
500 and an exception in App Insights where the maker should have seen "IČO
nelze ověřit". Both are fixed at the shared seam, so both register paths get
the fix in one change.

## Scope

- `MapFailure.MissingCompanyName` — a nameless subject is a structural map
  failure like `MissingIco` / `IncompleteSidlo`. `AresCompanyRegistry` already
  maps *any* map failure to `Error.Permanent(CompanyRegistryPermanent)`, so no
  adapter change and no new `BusinessErrorMessage` code (and therefore no new
  `cs-CZ` key) is needed.
- `AresResponseMapper.MaxCompanyNameLength = 300` + a `Cap` helper: trim → cut
  → `TrimEnd`. The second trim matters because a cut can land mid-word and
  leave trailing whitespace, and the snapshot is display copy that prints on
  invoices and shipping labels.
- `AresResponseMapperTests` — the mapper had no test file of its own; the
  behaviour was only covered incidentally through `AresCompanyRegistryTests`.

## Out of scope

- Truncation *telemetry*. A capped name is silent by design: it is a
  once-in-a-registry pathology and the maker sees the ARES card before
  confirming, so a log line would be noise. Revisit if it ever fires.
- Any change to how `Maker.Create` validates its own inputs — the aggregate's
  `ArgumentException` is correct as a programmer-error guard; the fix is to
  stop feeding it garbage.

## Acceptance criteria

- **AC-1** Given an ARES payload whose `obchodniJmeno` is null, empty or
  whitespace, when it is mapped, then the result is null with
  `MapFailure.MissingCompanyName` and the adapter surfaces
  `CompanyRegistryPermanent` — not a 500.
- **AC-2** Given an ARES payload whose `obchodniJmeno` exceeds 300 characters,
  when it is mapped, then `CompanyRecord.CompanyName` is exactly 300
  characters and does not end in whitespace.
- **AC-3** Given a name at exactly 300 characters, when it is mapped, then it
  is stored unchanged.
- **AC-4** Given every other T-0032 mapping behaviour (street fallback to the
  city name, dissolved-company flag, unparseable incorporation date, blank
  DIČ → null, missing IČO / city / ZIP / street), when the hardening lands,
  then each is pinned by a characterization test and unchanged.

## Test plan

20 cases in `Makables.Tests/Infra/Clients/Ares/AresResponseMapperTests.cs`,
written before the implementation: 8 for the two new behaviours (AC-1–AC-3),
12 characterizing AC-4.

## Status log

- 2026-07-29 spun off the T-0162 secops gate as a LOW-severity parity pair
  (F-1/F-2), `draft`.
- 2026-08-17 `draft → done` by dotnet-backend. Red: the 8 new cases failed to
  compile against the missing `MapFailure.MissingCompanyName` /
  `MaxCompanyNameLength`; green after the mapper change. Evidence:
  `Makables.Tests` 2003/2003 (1983 before), `Makables.IntegrationTests`
  272/272 against Postgres 16. Renumbered from T-0163 (slot collision, see
  the note above).
