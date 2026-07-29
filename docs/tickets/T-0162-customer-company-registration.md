---
id: T-0162
title: Customer registration — "Jsem firma" checkbox with ARES autofill (name + DIČ by IČO)
status: in_progress
size: M
owner: dotnet-backend
created: 2026-07-29
updated: 2026-07-29
depends_on: [T-0032, T-0035, T-0124, T-0159]
blocks: []
user_stories: [US-customer-0025]
adrs: [0018, 0012, 0022]
phase: 7
manual_steps: [nswag-regen, ef-migration]
security_touching: true
layers: [dotnet-db, dotnet-backend, frontend, l10n]
---

# T-0162 — Customer registration: "Jsem firma" checkbox with ARES autofill

## Context

Operator directive (2026-07-29): *"Do registrace přidej ještě checkbox, jsem firma,
kde se dotáhne název a DIČ firmy z ARES dle IČO."* A customer buying on behalf of
a company ticks **Jsem firma** on `/register`, enters an IČO, and the company
name + DIČ are fetched from ARES and stored on the account as a snapshot —
groundwork for company invoicing prefill later.

This supersedes the US-customer-0001 out-of-scope cut ("B2B account fields …
are optional on the order, not on the account"): the operator explicitly wants
account-level capture at registration. Order/invoice wiring stays deferred.

The entire lookup machinery already exists (ADR 0018, T-0032/T-0124/T-0159):
`ICompanyRegistry` + keyed factory, two-layer cache, mod-11 budget guard, and
the anonymous rate-limited preview endpoint
`GET /api/v1/makers/registry-preview` with its debounced FE helper. This ticket
is a *consumer* of that seam — no extension point is created or modified.

## Scope

- **Domain (`Core.Domain/Identity/User.cs`)** — nullable company snapshot:
  `CompanyRegistrationNumber` (IČO, 8 chars), `CompanyName`, `CompanyVatId`
  (DIČ), `CompanySnapshotFetchedAt`; attached via a dedicated
  `User.AttachCompanySnapshot(...)` mutator (keeps `User.Create` signature
  stable for the password/OAuth paths that never carry a company).
- **DB** — EF migration `AddUserCompanySnapshot`: 3 nullable text columns
  and 1 nullable timestamptz on `users`; no index (no read path filters by
  IČO — uniqueness is NOT enforced: two employees of one company may both
  register).
- **AppServices (`Features/Auth/Register.cs`)** — `Command` gains
  `string? CompanyRegistrationNumber` (null = private person, exact current
  behavior). `Validator`: when non-null → `.Length(8)` + `^[0-9]+$` (mirror of
  `RegisterMaker.Validator`; checksum stays a handler concern). `Handler`
  company branch mirrors `RegisterMaker.Handler` steps: `CzechIcoValidator`
  mod-11 gate (before any registry spend, ADR 0018) → `ICompanyRegistryFactory`
  lookup → dissolved-entity reject → attach snapshot from the **server-side**
  `CompanyRecord` (client-displayed preview is UX only, never trusted).
  Stale-cache result (≤7 days) is accepted silently — customers have no admin
  verification lane, and blocking registration on ARES downtime is worse.
- **Web (`Config/Controllers/Auth/AuthController.cs`)** — register action's
  request record gains the optional field; endpoint remains `[AllowAnonymous]`
  with the auth rate bucket on all four hosts; `Role` stays hardcoded
  `Customer`.
- **Errors + i18n** — reuse registry codes surfaced by the maker flow
  (`company.notFound`, transient/permanent passthrough); the dissolved gate
  gets a customer-scoped code (the existing one is maker-scoped) + `cs-CZ` key.
- **Contract** — NSwag regen of all four generated clients in the same PR
  (the auth controller is shared via `Makables.Config`, so every host's spec
  changes).
- **Frontend (`app/(auth)/register/register-form.tsx`)** — customer tab only:
  checkbox **Jsem firma**; when checked, IČO input appears (reuses
  `normalizeIcoInput` + `isValidCzechIco` local gate) and a 400 ms-debounced
  `lookupCompanyPreview` call (T-0159 helper, reused verbatim) renders the
  company card — name + DIČ ("neplátce DPH" when ARES returns no DIČ) +
  dissolved/not-found/unavailable states. A failed preview never blocks
  submit (T-0159 principle — the server lookup is the gate). Submit sends
  `companyRegistrationNumber` iff the checkbox is checked.

## Alternatives considered

- **Capture company data on the order at checkout (original US-customer-0001
  cut)** — *superseded by the operator directive*: account-level capture was
  explicitly requested. Checkout/invoice consumption becomes a follow-up
  consumer of the account snapshot instead of its own capture UI.
- **Separate `CustomerCompany` 1:1 entity** — rejected: 4 nullable columns
  with a single writer and (today) a single reader don't justify a join +
  repository + configuration; the Maker aggregate already sets the precedent
  of a flat ARES snapshot on the owning aggregate.
- **Trust client-submitted company name/DIČ** — rejected: backend is the
  system of record; the client sends only the IČO and the handler re-fetches
  authoritatively through the cached registry seam (ADR 0018).
- **New customer-scoped lookup endpoint** — rejected: the anonymous
  `GET /api/v1/makers/registry-preview` (T-0159) already serves exactly this
  shape with the right rate limit; a cosmetic route rename would churn the
  NSwag surface for zero behavior. Revisit only if the route's `makers/`
  prefix ever confuses a real consumer.
- **Reject stale-cache (≤7d) snapshots for customers** — rejected: maker flow
  surfaces staleness because admins re-verify makers; customers have no such
  lane, and hard-failing registration during an ARES outage loses customers.
- **Enforce IČO uniqueness across customer accounts** — rejected: multiple
  employees of one company legitimately register with the same IČO (unlike
  makers, where one IČO = one selling entity).

## Defense

Strongest counter-argument: *"B2B data belongs on the order (the original BA
cut) — capturing it at registration duplicates future checkout data and adds
risk to the auth flow."* Rebuttal: the operator (product owner) explicitly
directed account-level capture, and the account snapshot is what makes future
checkout/invoice prefill possible without re-asking the user. The auth-flow
delta is strictly additive-optional — `CompanyRegistrationNumber = null`
preserves today's behavior bit-for-bit, the company branch reuses the
rate-limited + cached + budget-guarded registry seam (ADR 0018) that maker
registration has exercised since T-0033, and the registry cache writes on a
dedicated `IDbContextFactory` context (T-0032 M-1), so the mid-command lookup
cannot flush the half-built `User` aggregate.

## Out of scope

- Order/invoice wiring of the buyer company snapshot (follow-up ticket;
  US-customer-0010's "B2B invoice fields on the order form" deferral stands).
- Profile UI to add/edit/remove company data after registration (follow-up —
  registrace-time capture only).
- Company registered-address capture on the customer account.
- Maker registration flow, admin surfaces, snapshot refresh jobs.
- T-0161 checksum demotion (in flight on its own branch) — this ticket keeps
  the `CzechIcoValidator.IsValid` hard gate shaped identically to
  `RegisterMaker.Handler` so T-0161's rewire sweep covers both call sites.

## Acceptance criteria

- **AC-1** Given the customer tab of `/register`, when "Jsem firma" is
  unchecked (default), then no IČO field renders, the POST carries no
  `companyRegistrationNumber`, and the created `users` row has all four
  company columns NULL (regression: today's flow unchanged).
- **AC-2** Given the checkbox is checked, when the customer types a valid
  IČO (shape + mod-11), then at most one debounced preview call hits
  `GET /api/v1/makers/registry-preview` and the card renders the ARES
  company name and DIČ (or the "neplátce DPH" note when DIČ is absent).
- **AC-3** Given a valid IČO of an active company, when registration
  submits, then the handler re-fetches ARES server-side and the `users`
  row persists IČO + company name + DIČ + `company_snapshot_fetched_at`
  (DB-state proof); welcome-email flow unchanged.
- **AC-4** Given an IČO failing shape or checksum, when the form is used,
  then the local mirror blocks submit with inline Czech copy; when the POST
  is forced anyway, then the backend returns 400 with the validation code
  and no registry call is made (budget guard, ADR 0018).
- **AC-5** Given an IČO not present in ARES, when registration submits,
  then `company.notFound` returns and renders as Czech copy under the IČO
  field; no user row is created.
- **AC-6** Given a dissolved company's IČO, when registration submits, then
  the customer-scoped dissolved code returns as `Permanent` (422, mirroring
  the `MakerCompanyDissolved` precedent) with Czech copy; no user row is
  created.
- **AC-7** Given ARES is unreachable, when a DB-cached record ≤7 days old
  exists, then registration succeeds with the stale snapshot; when no cache
  exists, then the transient error surfaces and no user row is created.
- **AC-8** Given the contract changed, when the PR is opened, then all four
  NSwag clients are regenerated in the same PR and the CI parity check is
  green.
- **AC-9** Given new `BusinessErrorMessage` codes, when the i18n parity
  check (T8) runs, then every new code has a `cs-CZ` key.

## Technical notes

- Mirror `RegisterMaker.Handler` for the company branch ordering; keep the
  existing `Register` behavior byte-identical when the field is null.
- The registry lookup inside the command is safe re: unit-of-work — cache
  persistence is isolated on `IDbContextFactory` (ADR 0018, T-0032 M-1).
- GDPR: an OSVČ DIČ is birth-number-derived → treat the snapshot columns as
  PII. GDPR erasure (extension-points §14) HARD-DELETEs the `users` row, so
  the snapshot dies with it — no erasure-matrix change needed.
- The register endpoint exists on all four hosts (shared Config controller);
  the new field is optional everywhere and `Role` stays hardcoded, so no new
  audience surface opens.
- NSwag: nullable string on the request record — verify the generated TS
  accepts omission (same shape as `zasilkovnaPickupPointId` on CreateOrder).

## Files touched (expected)

- `backend/src/Makables.Core.Domain/Identity/User.cs`
- `backend/src/Makables.Infra.Database/**` (User configuration + migration)
- `backend/src/Makables.Core.AppServices/Features/Auth/Register.cs`
- `backend/src/Makables.Core.AppServices/Common/BusinessErrorMessage.cs`
- `backend/src/Makables.Config/Controllers/Auth/AuthController.cs`
- `backend/tests/**` (Register validator + handler + integration)
- `frontend/src/app/(auth)/register/register-form.tsx`
- `frontend/src/lib/api-client-helpers/auth.ts` (request type only)
- `frontend/src/lib/i18n/cs-CZ.ts`
- `frontend/src/lib/api-client/*.v1.ts` (NSwag regen)
- `docs/user-stories/customer/README.md`, `docs/tickets/INDEX.md`

## Test plan reference

`docs/test-plans/T-0162.md`

## Status log

- 2026-07-29 `draft → ready` by PM — DoR: not-duplicate (INDEX checked; T-0159
  is the maker-side sibling), G/W/T AC, sized M, deps all done, manual steps
  listed, security_touching: true (auth flow + new PII columns), layers set.
- 2026-07-29 `ready → in_progress`, owner dotnet-backend.
- 2026-07-29 `in_progress → in_review` — TDD red→green (17 new unit tests:
  9 handler + 5 validator shape + 4 domain snapshot; suite 1927/1927), EF
  migration `AddUserCompanySnapshot`, NSwag regen all four hosts, FE form +
  6 vitest tests (93/93), eslint + next build clean, check-consistency
  parity with master (27 pre-existing findings, 0 new). Postgres-harness
  integration tests compile-verified; execute in CI (no local Docker).
  AC-6 clarified during implementation: dissolved = `Permanent` (422),
  mirroring the maker precedent, not 409.
- 2026-07-29 gate fan-out results (PR #112): **optimizer PASS 5/5**;
  **secops PASS** (2 LOW pre-existing parity findings on the shared ARES
  mapper — spun off as T-0163; T-0161 merge-ordering note recorded below);
  **qa PASS** (5 minor); **reviewer REQUEST CHANGES** (1 MAJOR: role-file
  parity). Fold commit closes: reviewer MAJOR (`docs/architecture/roles/
  user.md` updated — company snapshot in Knows, Does-NOT-know qualified,
  `Register.Command` naming + implementation-pointer drift fixed), QA F-1
  (debounce two-keystroke pin), F-2 (server-error mapping tests ×2), F-3
  (dissolved preview test), F-4 (test-plan V-3 wording). Accepted as-is
  with rationale: QA F-5 (empty-IČO shows checksum copy — blocks correctly,
  copy nuance) and the optimizer/reviewer debounce-unmount NIT (byte-for-
  byte the accepted T-0159 shape; fold both forms together in a future FE
  hygiene pass). **T-0161 sequencing (secops F-3):** whichever of PR #112 /
  T-0161 lands second must include `Register.cs` mod-11 gate in the
  checksum-demotion rewire — the gate is deliberately shaped like
  `RegisterMaker.Handler`'s so one sweep covers both.
- 2026-07-29 CI note: GitHub Actions org billing broken (all jobs die at
  start: "recent account payments have failed…" — also kills Deploy → dev
  since 2026-07-27). Local substitute recorded on the PR; Testcontainers
  suite + NSwag parity job + manual M-1..M-4 (dev preview is also down)
  remain OWED once billing is fixed.
