# Final review — PR 2 (T-0118b + T-0118c) — admin order actions + ops/control-plane

> **Reviewer:** code-reviewer. **Date:** 2026-06-15. **Branch:** `feat/admin-dashboard-actions` (28ecc59..9f0d022, 5 commits).
> **Verdict: REQUEST CHANGES — 1 BLOCKER (country-config AC-4 + AC-5).** Everything else passes; the delete-user screen and the money paths are clean. Re-approve on the single country-config fix.

---

## (a) Verdict

**REQUEST CHANGES.** The headline delete-user screen is correctly built (mirror-not-replace, honest re-call, exclusive type-to-confirm, always-visible banner). The money paths (refund / state-change / payout-complete) all carry the disabled-while-pending idempotency lock and run no client business logic. The CSV uses the blob helper (not the `csv()` `Promise<void>` gap). Zero hardcoded Czech in rendered JSX. Gates 1–7 green.

**The one BLOCKER is the country-config form: it misses AC-4 and AC-5.** `T-0118c AC-4` (ticket l.146) requires *"the form pre-fills from the current `CountryConfiguration`; `countryConfiguration.notFound` renders `notFound()`"*. `AC-5` (l.147) requires *"a VAT/fee-only edit (no provider field changed)… is called **without the provider modal**."* The implementation does neither, and the backend PUT is **full-replace** — making the gap dangerous, not cosmetic.

---

## (b) Delete-user gate-mirroring disposition — ACCEPT

- **(a) Type-to-confirm is client-UX-only; backend authoritative.** `delete-user-panel.tsx:160` `emailMatches = confirmEmail === userEmail` only gates the button; the POST at `:171-174` ALWAYS sends `confirmedEmail: confirmEmail`, and `eraseUser` (`admin-ops-client.ts:307-315`) forwards it so `user.deleteConfirmationMismatch` re-fires server-side. The `userEmail` is operator-entered (no read), but it is *also* the value sent as `confirmedEmail`, so a wrong entry simply fails the backend compare against the real stored email — **fails closed**. Correct.
- **(b) Re-call → `user.notFound` rendered as "už byl smazán".** `:191-192` maps `user.notFound` to `t('…erase.alreadyDeleted')` — a dedicated render, NOT a generic catch-all, NOT a refresh-into-404 (terminal `deleted` phase, `:65-67`/`:277`). No Silent-Success swallow. Correct.
- **(c) In-flight interlock degrades to the post-submit backend verdict** (`:188-190` → inline `inFlightReason` alert). **RULING: ACCEPT for MVP.** It is the SAME backend gate (`user.cannotDeleteWithInFlightOrders`), surfaced reactively not proactively. Proactive pre-disable (AC-12) rides the per-user-order-read follow-up — see (c) gap #1.
- **Exclusivity:** the `=== userEmail` retype is grep-EXCLUSIVE to `delete-user-panel.tsx:160` (HIGH-4). Refunds/state-changes use modal + disabled-while-pending; the provider-code retype is its own modal `typed` state in `country-config-form.tsx`. Confirmed.
- **Banner always-visible:** `IrreversibilityBanner` renders unconditionally at top of `EraseConfirmation` (`:202-203`), an `Alert variant="error"`, not behind a tooltip/accordion. Correct.

---

## (c) The 4 read-contract gap rulings

1. **No per-user-order read → in-flight surfaced reactively.** **SHIP-AS-IS + follow-up.** Same backend gate, just reactive; fails closed. Log the per-user-order read as a backend follow-up Q.
2. **No outbox LIST read → by-id retry/ack.** **SHIP-AS-IS + follow-up.** By-id is usable: the operator gets the stalled *count* (red-banner signal, T-0126) and pastes an event id from logs/alerting to retry/ack. Not browsable, but the triage loop (count → investigate → act-by-id) is operable for a 2-person ops team. Log a thin list endpoint as a follow-up.
3. **No payout-batch LIST read → count + by-id complete/CSV.** **SHIP-AS-IS + follow-up.** Same shape as #2; `getProcessingPayoutsCount` + by-id complete/CSV. No pagination, but no list to paginate. Operable. Log a list endpoint follow-up.
4. **No country-config GET → no server pre-fill.** **BLOCK.** See (d). This is the one gap the ticket AC does NOT permit to ship reactively.

---

## (d) BLOCKER

**BLOCKER-1 — country-config form ships with no pre-fill against a full-replace PUT (AC-4 + AC-5).**
`country-config-form.tsx` starts every field blank (`INITIAL_FORM`, `:73-84`; page note `:50` "noPrefillNote") and `baseValid` (`:117-125`) requires ALL of VAT / reduced-VAT / fee / shipping / four-providers to be re-entered. The backend `UpdateCountryConfiguration.Handler` is **full-replace** — `:256-265` applies `UpdateVatRates / UpdateInvoicingMode / UpdatePlatformFeeRate / UpdateDefaultShippingPrice / UpdateProviders` from the command verbatim; there is no patch path. Consequences:

- **AC-4 fail.** No GET, no pre-fill (ticket l.146 + Technical-notes l.82 "GETs the current `CountryConfiguration` (pre-fill)"; `notFound()` path absent). An operator intending to change only the VAT rate must retype the four provider codes + fee + shipping + reduced-VAT from memory; one mis-keyed digit silently overwrites VAT/fee/provider for **every subsequent order in the country** (ADR 0004 blast radius). The provider retype modal guards only the provider fields — VAT/fee/shipping sail through unconfirmed.
- **AC-5 fail.** Because all four providers are required by `baseValid`, `anyProviderSet` (`:115`) is ALWAYS true, so `handlePrimaryClick` (`:183-186`) opens the retype modal on **every** save — including a VAT/fee-only intent. AC-5 explicitly requires VAT/fee-only edits to save **without** the modal.

**Required fix (implementer):** the country-config form needs a server pre-fill. The clean fix is the backend country-config GET (the ticket presumes it; it is a real contract gap). Until that exists, this surface cannot ship — a no-prefill full-replace form on the VAT/fee/provider control plane is a foot-gun that the AC was written to prevent. Do NOT mock a GET (CLAUDE.md no-mocks). Either (i) land the thin GET follow-up first and pre-fill, or (ii) descope the country-config route from this PR and ship the other three control-plane surfaces (outbox / payout / delete-user) now. The provider retype modal alone does not mitigate the unconfirmed VAT/fee overwrite.

> Pinging **@architect**: the country-config GET is a missing read on a control-plane mutation surface (ADR 0004); please confirm the descope-vs-block-on-GET path. Pinging **@secops**: the full-replace-no-prefill foot-gun on VAT/fee/provider is a control-plane integrity concern, not just UX.

---

## (e) Fold list (ship now, non-blocking follow-ups)

- **F1 — log the 4 read-contract gaps as open questions.** The implementer flagged all four in code comments + the draft, but they are NOT yet in `docs/questions/open.md` (tail ends at the T-0126 forensic Q). Per the workflow, log: (1) per-user-order read for proactive in-flight pre-disable; (2) outbox LIST read; (3) payout-batch LIST read; (4) **country-config GET** (this one is the BLOCKER's fix, link it). Q-0019 (GetAdminOrderDetail) already covers the order-detail read.
- **F2 — provider retype-on-every-save is annoying-but-safe friction** once a GET exists (the modal would then fire only on an actual provider delta vs the pre-filled value). Resolves naturally with the AC-4 fix; no separate action.
- **No new recurring-findings.md row.** No finding here hit a 3rd strike (the i18n parity finding #2 is codified as `ruleT8`; this PR is green on it). No harvest append, no Architect harvest-ping.

---

## (f) Checks (Gates 1–7)

- **Gate 1 (build/lint/types):** `tsc --noEmit` exit 0; `eslint` on the new admin TSX + helpers exit 0; routes resolve (lead-pre-confirmed; all b+c folders carry `loading.tsx`/`error.tsx`, delete-user/orders carry `not-found.tsx`). PASS.
- **Gate (i18n / T8):** `check-consistency.mjs` exit 0 (147 tracked, "clean"). Manual grep for Czech-diacritic literals in the new TSX: all hits are in **comments**, zero in rendered JSX — every string via `t(...)`. PASS.
- **Gate (Server-Components-first):** every `page.tsx` is a Server Component with `dynamic = 'force-dynamic'`; `'use client'` only on modals/forms/islands; the `useEffect`s (order-actions ModalShell, country-config + complete-batch modals) are focus-trap/scroll-lock, NOT data fetching. PASS.
- **Gate (contract / NSwag):** read-only consumer; no `lib/api-client/` hand-edit in the scoped diff; helpers wrap `apiFetch` returning `Result<T, ApiError>`. Response field names (`inFlightOrderCount`, `providerChanged`, `alreadyCompleted`) match the generated DTOs. PASS.
- **Gate 3 (SecOps) + Architect sign-off:** the PR is `security_touching: true`; both are mandatory per ticket `manual_steps`. Confirm both green before merge (independent of the BLOCKER). The country-config foot-gun is now also flagged to both above.
- **Gate 5 (tests):** frontend MVP has no automated suite (T-0087b precedent); manual QA per `docs/test-plans/T-0118c.md`. No pure-logic-TDD trap — these are presentation islands over backend verdicts; the button-enable predicates are presentation, not domain logic in the must-cover categories. PASS.
- **Gate 8 (optimizer):** N/A — presentation islands, no hot path; backend owns the money math / allow-list / erasure matrix.
- **RDD parity:** frontend-only slice; no new aggregate/VO/service/repo/adapter; no `roles/` file required.

**Re-approval condition:** land the country-config GET + pre-fill (or descope the country-config route from this PR) so AC-4 and AC-5 pass; log F1's four follow-up Qs. The other six headline checks and the entire delete-user + money surface are approved.
