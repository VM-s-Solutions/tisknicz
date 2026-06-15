# Gate 4 — Architect sign-off — PR `feat/admin-dashboard-actions` (T-0118b + T-0118c)

> **Gate:** 4 (architect sign-off), MANDATORY per T-0118c frontmatter `manual_steps` (T-0118c fronts the control-plane mutation surface per ADR 0004 + the only irreversible op per T-0110/§A.23).
> **Architect:** solution-architect. **Date:** 2026-06-15.
> **Scope of this gate:** the FOUR read-contract gaps the implementer surfaced + the delete-user-consumes-the-T-0110-seam confirmation. This is a design-seam ruling, NOT the full code review (code-reviewer owns Gate 1; SecOps owns Gate 3).
> **Verified up front (not assumed):**
> - `UpdateCountryConfiguration.cs` Command + Validator + Handler — the PUT semantics.
> - `CountryConfigurationsController.cs` — the only action is `[HttpPut("{countryCode}")]`; no GET.
> - `Features/CountryConfigurations/` — only `UpdateCountryConfiguration.cs`; no Get feature anywhere.
> - `admin-api.v1.ts` — `countryConfigurations` (PUT only), `count`/`retry`/`acknowledge` (outbox: no list), `payoutBatches`/`count2`/`complete`/`csv` (payout: no list), `erase` (no per-user read). Grep clean for any GET on all three.
> - `country-config-form.tsx` — ships blank (`INITIAL_FORM` all-empty); docstring lines 33-35 admit "NO server pre-fill".
> - `delete-user-panel.tsx` + `admin-ops-client.ts` — `eraseUser` posts `{ confirmedEmail, reason }` via `apiFetch`.
> - `docs/questions/open.md` Q-0024/Q-0026/Q-0027 + `T-0126` ticket — what is already groomed vs. genuinely unlogged.

---

## VERDICT: **APPROVED WITH ONE BLOCKER** — Gap 1 (country-config) blocks until a guard ships; Gaps 2/3/4 ACCEPT-with-logged-follow-up.

The slice is architecturally sound: it is a pure presentation layer consuming existing seams, it reimplements no business logic, and it surfaces every gap honestly (no mocks). **One gap carries a real operational hazard that the current UI does not adequately fence: the country-config full-replace PUT with a blank form.** That is the merge blocker — and it is a thin frontend-only fence, not a backend dependency, so it does not stall the PR for long.

---

## GAP 1 — No country-config GET (the sharpest) — **PUT IS FULL-REPLACE → BLOCK (thin FE fence, not a backend read)**

### The PUT-semantics finding (the load-bearing fact)

**The PUT is a FULL-REPLACE, not a PATCH.** Verified in `UpdateCountryConfiguration.cs`:
- All four `Default*Provider` fields are non-nullable `string` on the `Command` (l.45-48) and each is `.NotEmpty()` in the `Validator` (l.137-148).
- `StandardVatRateBp`, `InvoicingMode`, `PlatformFeeRateBp`, `DefaultShippingPriceMinor` are non-nullable required scalars (l.39-44); only `ReducedVatRateBp` and `ConfirmedProviderCode` are nullable.
- The handler computes deltas by comparing **every** incoming value against the loaded row (l.189-203) and applies **all** mutators (`UpdateVatRates`/`UpdateInvoicingMode`/`UpdatePlatformFeeRate`/`UpdateDefaultShippingPrice`/`UpdateProviders`, l.256-265). There is no "field omitted ⇒ keep current value" path. Every field on the wire is authoritative.

**Consequence:** an operator editing one VAT rate must re-enter the ENTIRE row (both VAT rates, fee, shipping price, all four provider codes) from memory. A stale-but-non-empty value submitted by memory is silently committed — e.g. re-typing the wrong payment-provider code blanks the correct one out of the live config and breaks checkout for every subsequent order in the country. This is the real hazard, and it is operational, not theoretical (2 trusted admins, no second-operator review).

### What partially backstops it (and why it is not enough)

The form's `baseValid` predicate (`country-config-form.tsx` l.117-125) requires all four providers + the three scalars non-empty before the Save button enables, AND the backend `.NotEmpty()` validators reject a literally-blank provider. **So the "blank a provider ⇒ broken checkout" path (a) cannot fire from this UI and (b) is double-fenced by the backend.** That removes the *blanking* hazard the ticket worried about most.

What is NOT fenced: **the silent-overwrite-from-memory hazard.** A blank form forces the operator to reconstruct values they cannot see; a wrong reconstruction is a valid, committed write. No client gate can catch this — only showing the operator the current row can. The retype-the-code modal (A.5) confirms the code the operator *typed*, not that it *matches what was already there* — it cannot, because the form never loaded the prior value.

### Ruling — **(b) BLOCK, narrowed to a thin frontend fence (NOT a backend GET as a hard prerequisite)**

Rule options (a)/(b)/(c) from the gate:
- **(c) is FALSE** — the PUT is full-replace, and the form does NOT pre-fill from anywhere. Rejected on the verified facts.
- **(a) "ship as-is with a loud warning"** is INSUFFICIENT for a control-plane mutation that drives VAT math + provider selection for every order. A passive warning does not stop a confident operator re-typing a stale code.
- **(b) BLOCK** — but I scope the blocker to the cheapest fence that closes the hazard, because the gap is a presentation gap and the fix is frontend-only:

  **BLOCKER B1 (frontend, must land in THIS PR before merge):** the form must NOT present an editable blank-field full-replace surface for a control-plane row the operator cannot see. The acceptable minimum is a **prominent, always-visible "full-replace — every field overwrites the live config; re-enter ALL current values" banner (keyed `cs-CZ`)** ON the form, AND the irreversibility-of-effect must be made unmissable the same way the delete-user banner is (A.2a precedent in `delete-user-panel.tsx`). A passive hint line is not sufficient; it must be a banner at the top of the form, not a per-field hint.

  **FOLLOW-UP F1 (backend, the real fix — logged, NOT a merge prerequisite):** a thin `GetCountryConfiguration` read (`GET /api/v1/country-configurations/{countryCode}` → the editable set, AsNoTracking, `Unscoped` admin-host per ADR 0013) so the form pre-fills and the full-replace PUT becomes a true edit-in-place. **This gap is currently UNLOGGED** — Q-0024 (admin order-detail), Q-0026 (invoice PDF), Q-0027 (count endpoints) and the groomed **T-0126** bundle do NOT cover it. T-0126 is invoice-PDF + two count endpoints only. **Log a new open.md question (Q-0029-class) and groom it; the country-config edit surface is degraded-and-fenced until it ships, at which point B1's banner downgrades to a normal edit form.** Until F1 lands, B1's banner is the standing mitigation.

**Why BLOCK and not ACCEPT:** every other gap in this PR is a *read-richness* gap (the operator sees less). Gap 1 is a *write-safety* gap (the operator can silently corrupt the control plane). A blank full-replace form over an invisible row is the one place where "ship degraded" crosses from "less convenient" into "operationally hazardous." The fence is cheap (one banner) and frontend-local, so the blocker is light — but it IS a blocker.

---

## GAP 2 — No payout-batch LIST read — **ACCEPT, with logged follow-up**

The payout surface is count (`count2(state)`) + complete-by-id + CSV-by-id; no browsable list. For MVP this is acceptable: the T-0104 timer creates **one** weekly batch (low cardinality — the operator is not hunting through dozens), the `count2(state=Processing)` tile (T-0126) surfaces how many are outstanding, and the maker-facing T-0116 list covers the maker's own visibility. The operator-without-an-id case is the gap, but at one-batch-per-week volume it is bounded, not blocking.

**Ruling: ACCEPT for MVP.** Log a thin **admin payout-batch LIST read** follow-up (Processing-first, paged, `Unscoped`) so the operator can browse-then-act once weekly volume or multi-country splits the batch count. Note in the PR description that the operator currently reaches a batch via its id (from the count tile's deep-link / the CSV filename), not a browse. Not a merge blocker — the mutation seams (complete/CSV) are id-addressable and the count gives the signal.

---

## GAP 3 — No outbox-event LIST read — **ACCEPT, with logged follow-up (the weakest of the three "accepts")**

Triage is count (`count()`) + retry-by-id + ack-by-id; the operator sees the stalled COUNT but cannot browse the stalled set to get the ids to retry/ack. **This is the gap where by-id triage is genuinely awkward** — unlike a payout batch (one per week, id reachable from the CSV/tile), a stalled outbox event has no obvious id source for the operator: the count says "3 stalled" but offers no path to the three ids. The retry/ack-by-id surface is therefore only usable if the operator already has ids from App Insights / the DB / a log — i.e. out-of-band.

**Ruling: ACCEPT for MVP, but log the follow-up at HIGHER priority than Gap 2.** The count + banner (US-admin-0002 AC-2, driven by T-0126's `CountStalledAsync`) is a legitimate *alert* — "something is stalled, investigate." For MVP a stalled outbox is a rare, ops-escalation event (2 trusted operators, App Insights available for the ids), so the by-id retry/ack surface is a usable-if-clunky remediation tool once the id is in hand. But the **admin stalled-outbox LIST read** is the natural next read and should be the first of the three list follow-ups to ship — the triage loop (see → pick → retry) is incomplete without it. Note T-0126 §Out-of-scope already explicitly defers the stalled-outbox LIST ("the count here drives the overview tile + banner; the list is separate") — so this gap is *acknowledged in grooming* but **not yet logged as its own ticket**. Log it. Not a merge blocker (the count + the by-id seam + App Insights close the loop for MVP volume).

---

## GAP 4 — No per-user-order read (delete-user in-flight pre-disable) — **ACCEPT**

The in-flight-order block is the same backend gate (`user.cannotDeleteWithInFlightOrders`, T-0110 §A.23 invariant) surfaced **reactively** (post-submit verdict, rendered as the inline "resolve in-flight orders first" reason at `delete-user-panel.tsx` l.188-189/214-218) rather than **proactively** (pre-disabled button). The authoritative gate is identical in both cases — the backend refuses regardless. A proactive pre-disable is a pure UX nicety; its absence weakens nothing security-relevant.

**Ruling: ACCEPT.** This is the textbook "friction-only client gate + authoritative backend" pattern done correctly — the reactive surfacing is honest (no fabricated client-side order set, no mock; the implementer explicitly chose the backend verdict over inventing the in-flight set client-side). Log a thin follow-up for the proactive pre-disable (rides whatever per-user read lands), but it is cosmetic. Not a blocker. (Note: the same `GetCountryConfiguration`-class read-gap logic does NOT apply here — the missing read would only improve UX, never write-safety, because the gate is read-only and server-authoritative.)

---

## Delete-user UI consumes the T-0110 seam correctly — **CONFIRMED**

Per §A.23 (orchestrated multi-entity GDPR erasure in one UoW) + extension-points.md §14, the erasure matrix is owned by `IUserDataDeletionService` invoked by `DeleteUserPermanently` (T-0110), inside the single pipeline UoW. **The UI consumes this seam; it never reimplements the erasure logic.** Verified:

- **No erasure logic on the client.** `eraseUser(userId, { confirmedEmail, reason })` (`admin-ops-client.ts` l.307-311) posts to the `erase` endpoint via `apiFetch` and renders the `Result<T, ApiError>`. The disposition matrix (hard-delete / anonymize / retain), the in-flight guard, and the legal-retention rules all stay server-side. The UI knows nothing of `Review.CustomerUserId`, the maker tombstone, or the invoice-retention carve-out.
- **Both gates are MIRRORED, never REPLACED (§A.23 #5 + T-0110 AC-7/AC-8).** The client email-match (`emailMatches`, l.160) and reason-non-empty (`reasonValid`, l.161) are explicitly documented as *presentational only* (l.159 "the server re-checks both gates"); `confirmedEmail` rides the request body so `user.deleteConfirmationMismatch` can fire server-side. The client compare is `===` (stricter than the backend's `User.NormalizeEmail` — fails closed, acceptable).
- **The irreversible / no-Silent-Success rule is honored (§A.23 #5).** A re-call returns `user.notFound`, rendered as the dedicated "uživatel již byl smazán" (l.191-192), NOT a silent success and NOT a generic error. The terminal phase is a "deleted" confirmation, not a `router.refresh()` into a 404 (l.65-67, 277-304).
- **Type-to-confirm friction (A.1) is present and reserved** to this screen (the email retype); the always-visible irreversibility banner (A.2a) renders unconditionally on the confirm surface (l.202-203). The in-flight block surfaces as the backend verdict (Gap 4), correctly.

The delete-user UI architecture (friction-only client gates + authoritative backend) **matches the T-0110 seam intent (§14 / §A.23).** No reimplementation; the UI is a faithful consumer.

---

## BLOCKER (single)

**B1 — country-config full-replace fence (frontend, this PR).** The country-config form presents an editable blank full-replace surface over a control-plane row the operator cannot see; a stale re-typed value silently overwrites VAT/fee/provider selection for every subsequent order in the country. Before merge, the form MUST carry a prominent, always-visible, `cs-CZ`-keyed "full-replace — every field overwrites the live config; re-enter ALL current values" banner (A.2a-banner precedent). **Backend follow-up F1 (thin `GetCountryConfiguration` GET — currently UNLOGGED, not in T-0126) must be logged in `docs/questions/open.md` and groomed**; it is the real fix but NOT a merge prerequisite. B1's banner is the standing mitigation until F1 ships.

## Logged follow-ups (NOT blockers)

- **F1 (BLOCKER-paired):** thin `GetCountryConfiguration` read — log + groom (gap is unlogged today).
- **F2:** admin payout-batch LIST read (Gap 2) — MVP-deferred.
- **F3:** admin stalled-outbox LIST read (Gap 3) — ship FIRST of the three list reads; acknowledged in T-0126 §Out-of-scope but not yet ticketed.
- **F4:** per-user in-flight pre-disable (Gap 4) — cosmetic.

## Sign-off

**Architect Gate 4: APPROVED conditional on B1 landing in this PR.** Clear B1 (the banner) and the architect gate is green. SecOps Gate 3 + code-reviewer Gate 1 remain independently required (not in this gate's scope).
