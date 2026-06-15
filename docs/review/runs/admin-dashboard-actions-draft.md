# Preliminary review — PR 2 (T-0118b + T-0118c) — admin order actions + ops/control-plane

> **Status:** DRAFT / preliminary. Written in parallel with the implementer, before the diff exists.
> **Reviewer:** code-reviewer. **Date:** 2026-06-15.
> **Scope:** the second admin-frontend PR — **T-0118b** (order detail + refund/state/dispute action surfaces) then **T-0118c** (outbox / country-config / payout / **delete-user**, the most destructive UI in the product), implemented risk-ascending b → c.
> **Inputs read:** T-0118b, T-0118c, T-0087b (action precedent), T-0110 (delete-user backend, the gate the UI mirrors), T-0105/0106/0107/0108/0109/0103 (command semantics + error codes), patterns.md §B, CLAUDE.md frontend self-check, quality-gates.md, checklist.md, recurring-findings.md (#2 i18n CODIFIED as T8/`ruleT8`).
> This is not approval. It is the row-by-row trap list the final review will hold the diff against, plus the contract facts I verified up front so impl-time surprises are pre-empted.

---

## 0. Required final gates (call out NOW — both MANDATORY, do not approve without them)

- **§secops Gate 3 — REQUIRED.** Both slices are `security_touching: true`. T-0118c carries the T-0110 control-plane + the only hard-delete UI; T-0118b moves money + drives the state machine via admin commands. SecOps must sign off (quality-gates.md Gate 3). My approval is blocked until SecOps signs.
- **§architect sign-off — REQUIRED.** T-0118c fronts the control-plane mutation surface (country-config drives VAT/fee/provider selection per ADR 0004; delete-user fronts the only irreversible op). Architect must sign off on the PR. Both gates are named in T-0118c frontmatter `manual_steps`. I will not approve until both are green.

---

## 1. Contract facts I verified up front (so impl doesn't discover them late)

All against `frontend/src/lib/api-client/admin-api.v1.ts` (read-only consumer — **no regen, no hand-edit; pre-commit hook + Gate 6 enforce**):

- **All command + read methods exist.** `refund` (l.60), `dispute` (l.65), `resolve` (l.70), `state` (l.75), `auditLog` (l.50), `countryConfigurations` (l.55), `retry` (l.85), `acknowledge` (l.90), `complete` (l.106), `csv` (l.111), `erase` (l.116). The "read-only consumer, no NSwag regen" claim in both tickets is TRUE. **Any diff under `lib/api-client/` is an automatic request-changes** (AC-11 / AC-15 / Gate 6).
- **Request DTO shapes match the tickets exactly:** `RefundOrderRequest { amountMinor, reason?, acknowledgePostPayout }` (l.3756), `ChangeOrderStateRequest { targetState, reason }` (l.2407), `ResolveDisputeRequest { outcome, resolutionNotes }` (l.4032), `MarkPayoutBatchCompletedRequest { bankReference, paymentDate? }` (l.3254), `EraseUserRequest { confirmedEmail }` (l.2822 — `reason` rides the request too per T-0110; verify the generated body carries it). No shape surprises.
- **The CSV body-discard gap is REAL and load-bearing.** `csv(id): Promise<void>` at **admin-api.v1.ts:1286** (`processCsv` does `response.status` handling then `return;` — body discarded), exactly as T-0118c §A.4 / Option F asserts. **HARD trap:** if the diff calls the generated `csv()` method anywhere, request changes — the CSV MUST go through `apiFetch parse:'blob'` + `triggerBlobDownload` (T-0118c AC-9). This is the same gap as T-0087b `label()` and T-0116 fee-invoice; precedent is locked.
- **No single admin order-detail read exists.** Only `adminOrders(page, pageSize, state, country, makerId, customerEmail)` (l.26) — a list, **no id filter**. The T-0118b §C.2 composition gap is genuine: the detail header must degrade to list-row/nav-state fields, and `auditLog(..., targetEntity:"order", ...)` filtered to this order is the load-bearing read. **Trap:** the page must NOT invent or mock a `GetAdminOrderDetail` call (no-mocks rule, CLAUDE.md). Verify the `docs/questions/open.md` follow-up is logged.
- **No user-for-erase read exists.** There is no `getUser` / `usersGET` / single-user method in the admin client (grep clean). The delete-user screen needs the target user's **email** (to gate type-to-confirm) and the **in-flight-order summary** (to pre-disable per AC-12). **This is the single biggest impl risk in the PR** — see §3 HIGH-1. The email + in-flight set must be sourced from an existing read (e.g. `adminOrders(customerEmail=...)` composition) or a logged follow-up; it must NOT be mocked.

---

## 2. i18n / T8 status (recurring-finding #2, now CODIFIED — `ruleT8`, `hard:true`)

**Good news, pin it:** every BACKEND error code both slices surface **already has a `cs-CZ.ts` key** (the backend tickets T-0105/0106/0107/0108/0109/0103/0110 shipped them). Verified present: `payment.refund.{invalidState,amountExceedsRemaining,postPayoutAckRequired,noProviderRef}`, all six `order.manualTransition.*`, `order.dispute.{categoryNotAllowed,notOpen}`, `order.invalidTransition`, `outbox.{alreadyProcessed,rowNotFound}`, `country.{providerConfirmationMismatch,providerNotRegistered}`, `countryConfiguration.notFound`, `payoutBatch.{notProcessing,notFound}`, `user.{notFound,deleteConfirmationMismatch,cannotDeleteWithInFlightOrders}`. So `ruleT8` should stay green for error-code parity — **but confirm `check-consistency.mjs` exit 0 on the actual diff** (AC-12 / AC-16).

**The real i18n trap is what T8 does NOT catch.** `ruleT8` only pairs `BusinessErrorMessage` codes ↔ `cs-CZ` keys — it does **not** catch hardcoded Czech UI strings in JSX. The two NEW namespaces (`dashboard.admin.orderActions.*` and `dashboard.admin.ops.*`) **do not exist yet** (grep clean). Every new section heading, modal copy, the irreversibility banner, the in-flight reason, dropdown option labels, button labels, the "already deleted" string MUST be keyed (CLAUDE.md i18n self-check; T-0118b §C.7 / T-0118c §i18n). **Final-review action:** grep the new JSX for any non-ASCII / Czech literal outside `cs-CZ.ts`. This is the failure mode that slips past CI and lands on me.

---

## 3. HIGH findings (the delete-user screen dominates c)

### HIGH-1 — delete-user data source (the screen has no read to drive its gates) — BLOCKER unless resolved cleanly
The delete-user panel needs the target user's **exact email** (AC-11 type-to-confirm) and the **in-flight-order set** (AC-12 pre-disable). The admin client exposes **neither** a single-user read nor a per-user in-flight count. The impl must either compose from `adminOrders(customerEmail=...)` (the email comes from the list row the operator navigated from; the in-flight set from filtering that order list by `state ∈ {PendingPayment,Paid,Accepted,Shipped}`) OR log a thin follow-up and render from whatever T-0118a surfaced. **Verify at final review:**
- The email used for the type-to-confirm comparison is the **real displayed user email**, not a mocked/placeholder value (a wrong email source silently weakens the strongest gate in the product).
- The in-flight pre-disable is computed from **backend-supplied order state**, not a hardcoded list (T-0118c §B "any in-flight order ⇒ disable delete" is presentation over backend data — acceptable; inventing the order set client-side is not).
- **No mock.** If the read isn't there, the screen reads what exists or the follow-up is logged — it does not fabricate.

### HIGH-2 — the UI must MIRROR, never REPLACE/WEAKEN, the backend gates
T-0110 is authoritative for BOTH gates (`user.deleteConfirmationMismatch` retype + `user.cannotDeleteWithInFlightOrders` interlock; AC-7/AC-8). The client-side email compare and in-flight pre-disable are **UX conveniences**; the backend rejects regardless. **Request changes if:**
- the client-side email comparison is the ONLY check and the request never carries `confirmedEmail` to the backend (the backend MUST re-validate — the body must include `confirmedEmail` so `user.deleteConfirmationMismatch` can fire server-side; AC-11 "if the backend is reached, surfaces the mismatch error").
- the in-flight block is implemented as a client-side allow-list that would let a click through when the backend would refuse (the UI disables for clarity; the server is the gate — T-0118c "Technical notes / Why both gates before the call").
- the email compare is normalized differently from the backend (T-0110 normalizes case/NFC/whitespace via `User.NormalizeEmail`). A client compare that is **stricter** than the backend is fine (fails closed); one that is **looser** is fine for UX but must still send `confirmedEmail` for the authoritative check. A client compare that **bypasses** typing (e.g. pre-fills the field) is a hard reject.

### HIGH-3 — re-call → `user.notFound` rendered as "already deleted" (no Silent-Success confusion)
T-0110 §C / Option E / AC-10/AC-14: erasure is NOT idempotent; a re-call returns `user.notFound`. The UI must render this as **"uživatel již byl smazán"** (already deleted), NOT a silent success and NOT a generic error. **Trap:** a generic catch-all that maps every error to "něco se pokazilo" would swallow the honest "already gone" signal. Verify `user.notFound` has a dedicated, contextual rendering on the erase path (T-0118c AC-14).

### HIGH-4 — type-to-confirm RESERVED for delete-user ONLY (grep-provable exclusivity)
T-0118c §A.1 + AC-15: type-to-confirm (retype-the-exact-value-to-enable-a-destructive-button) must appear in `delete-user-panel.tsx` and **nowhere else**. Refunds + state changes (T-0118b) use a modal + disabled-while-pending; provider-change uses **retype-the-new-provider-code** (a related but distinct idiom, T-0108 — that is allowed and required, §A.5). **Final-review action:** grep the diff for the type-to-confirm pattern; assert the *email* retype is exclusive to delete-user and the *provider-code* retype is exclusive to country-config. Friction inflation devalues the one gate that must mean something (T-0118c "Why type-to-confirm is reserved").

### HIGH-5 — irreversibility banner always-visible (not behind a tooltip/expander)
T-0118c §A.2a / AC-10: the banner ("Nevratné smazání … faktury zůstávají zachovány dle GDPR čl. 17 odst. 3 písm. b)") must be prominent and ALWAYS visible on the delete-user screen. **Reject** if it is hidden behind a tooltip, a hover, or an accordion (T-0118c Option, "hide the banner behind a tooltip" rejected). The GDPR-retention fact must be unmissable. Banner copy must be a `cs-CZ` key (§2).

### HIGH-6 — refund idempotency lock is the front-line money guard (no double-submit)
T-0118b §A.1 / AC-4: the partial-refund path has **no backend idempotency key at MVP** (T-0105 Risk / Q-0018) — the **disabled-button-while-pending lock is the only guard against moving money twice**. **Reject** if: the submit button is not disabled during the in-flight POST; a second click can fire a second POST; or any optimistic state flip occurs (T-0105 AC-5 leaves a `Permanent`-rejected order byte-identical — `router.refresh()` is the only truthful reconciliation). Network-tab single-POST proof is in the QA plan; assert it at Gate 5.

### HIGH-7 — `acknowledgePostPayout` checkbox renders ONLY when `state == Completed`
T-0118b §A.1 / AC-3: the post-payout-ack checkbox appears **only** when the order is `Completed` (maker already paid out — T-0105 AC-4 requires the flag, else `payment.refund.postPayoutAckRequired`). **Reject** if the checkbox is always-on, or absent when `Completed` (the refund would bounce on `postPayoutAckRequired` with no way to acknowledge), or if the UI tries to compute "is the maker paid out" itself beyond reading `state == Completed` (the gate is backend; the UI reads one state field).

---

## 4. MED findings (state-change, country-config, payout, outbox)

### MED-1 — state-change allow-list is BACKEND-enforced; UI must NOT implement its own
T-0118b §A.2 / Option B / AC-6/AC-7: the `targetState` dropdown offers candidate (non-current) states and lets `ManualOrderTransitionPolicy` refuse. **Reject** any client-side allow-list / state-machine in TypeScript (forbidden business logic, CLAUDE.md; drifts on the first backend rule change). The blocked-transition `Alert` must render the **named-command** Czech string (e.g. `useRefundOrder` → "Použijte refundaci objednávky"; the keys already exist). Mandatory `reason` ≥ 10 chars — the UI mirrors the length hint only; the backend (T-0107 Validator `MinimumLength(10)`) re-validates. The UI must not submit with an empty/short reason (disabled), but must not be the authority.

### MED-2 — provider-change confirmation = retype-the-new-provider-code MODAL (NOT a boolean checkbox)
T-0118c §A.5 / Option G / AC-6: a `Default*Provider` change opens a modal requiring retyping the **new** provider code; mismatch surfaces `country.providerConfirmationMismatch`; an unregistered code surfaces `country.providerNotRegistered`; on success the `inFlightOrderCount` advisory renders (informational, NEVER blocking — Option H). **Reject** a boolean "I confirm" checkbox (one mis-click commits a catastrophic provider swap, T-0108 Option A). A VAT/fee-only edit saves WITHOUT the modal (AC-5) — verify the modal triggers iff a provider field actually changed.

### MED-3 — payout: VIEW + complete + CSV; NO manual create-batch button
T-0118c §A.3 / Option D / AC-7/AC-8: the payout surface is view + mark-completed + CSV only. **Reject** any "create batch now" affordance (the T-0104 timer + its HTTP escape-hatch owns creation). Mark-completed modal captures `bankReference` (required) + `paymentDate` (optional); disabled-while-pending; `Completed` batches show no complete action (forward-only). CSV is **operator-only and the admin MAY download it** here (inverts the T-0116 maker absence — A.4) via the blob helper (§1 CSV trap). State badges keyed (`Processing → "Zpracovává se"`, `Completed → "Vyplaceno"`). `?page=` URL-state, clamped ≥ 1, `page=1` dropped from canonical (patterns §B.8).

### MED-4 — outbox retry/ack asymmetry surfaced correctly
T-0118c §C / AC-2/AC-3: retry-on-processed is a **hard 409 `outbox.alreadyProcessed`** surfaced as a clear "already ran — nothing to retry" alert (NOT a silent success); re-acknowledge is **benign 200**. Acknowledge requires a non-empty reason (≤ 2000); empty keeps the action disabled. **Reject** if `alreadyProcessed` is swallowed silently or if retry/ack disabled-while-pending is missing.

### MED-5 — dispute resolve = INLINE form (not a modal); open = secondary inline form; mutually exclusive by state
T-0118b §A.3 / Options D & F / AC-8/AC-9: resolve renders inline when `state == Disputed` (outcome dropdown Refunded/Resumed/Cancelled + required `resolutionNotes`, labelled customer-visible); the open form (category + description) shows when not Disputed — **never both**. Surfaces `order.dispute.{categoryNotAllowed,notOpen}` / `order.invalidTransition`. Reject a modal-for-resolve or a folded open+resolve toggle.

---

## 5. Cross-cutting checklist (walk EVERY row at final review — quote, don't paraphrase)

CLAUDE.md frontend self-check + checklist.md §A–J + quality-gates Gate 1 (frontend), applied to the diff:

- **§A hygiene:** zero `any`, zero unsafe `!`, zero `console.*`, no dead/commented code, no TODO without owner (AC-12 / AC-16).
- **§B architecture:** every `page.tsx` a **Server Component** with `dynamic = 'force-dynamic'`; `'use client'` ONLY on the modals/forms/islands (order-actions, dispute-form, outbox-actions, country-config-form, complete-batch-modal, payout-csv-download, delete-user-panel) — justification is "interactivity", acceptable. **No `useEffect` data fetching anywhere** (AC-1 / AC-16). All calls via `apiFetch`-wrapped `Result<T, ApiError>` helpers (`admin-orders.ts`, `admin-ops-client.ts`); no raw `fetch` except the locked CSV blob path (which still goes through `apiFetch parse:'blob'`). No DB SDK imports. **No `lib/api-client/` edits** (Gate 6 / pre-commit hook).
- **§B.14 + ADR 0024:** SSR admin-audience cookie forwarding on every read; a customer/maker JWT 401s at the admin host (ADR 0013 — backend-side; the FE adds no parallel auth and must not).
- **§B.10 `formatCzk`:** every money figure (order total, refund amount display, payout totals, `DefaultShippingPriceMinor`) via `formatCzk`. **No money math client-side** — the remaining-refundable cap is backend (T-0105); VAT/fee bp display is `/100` presentation only, not a pricing rule (T-0118c §B).
- **§E UI/UX:** loading.tsx + error.tsx present for each c-route folder (T-0118c files-touched lists them); responsive 375/768/1280 with modals usable on mobile and rows-as-cards `< md` / grid `≥ md`; no inline `style={}` for layout; primitives from `components/ui/`.
- **§F AC traceability:** every AC (T-0118b AC-1..12, T-0118c AC-1..16) maps to a diff change + a QA proof. PR description must list them.
- **§G/Gate 5 tests:** frontend MVP has **no automated suite** (T-0087b precedent) — manual QA on the Vercel preview per `docs/test-plans/T-0118c.md`. **No pure-logic TDD trap here** (this is presentation-only; the button-enable predicates are presentation over backend data, not pure domain logic in the must-cover categories — Gate 5 pure-logic-TDD does not bite a frontend presentation slice). If the impl extracts any genuinely pure helper (e.g. an email-normalization mirror), that WOULD need test-first — flag if it appears.
- **§J mechanical:** `node scripts/check-consistency.mjs` exit 0 (T8 included) on both slices (AC-12 / AC-16).

---

## 6. RDD parity (per ADR 0015)

This is a **frontend presentation slice** — no new aggregate / value object / domain service / repository interface / adapter interface in the diff. **No `docs/architecture/roles/` file is required** for T-0118b/c. (The role files that matter here — `user.md` erasure matrix, `maker.md` tombstone — were the T-0110 backend ticket's deliverable, already merged.) If the diff unexpectedly touches backend (it must not — both tickets are `layers: [frontend]`), re-open RDD parity. No handler-collaborator-count concern (frontend).

---

## 7. Harvest watch (recurring-findings.md)

- **#2 (i18n parity) is CODIFIED** (`ruleT8`, T-0125, `hard:true`) — I will NOT re-log it; a missing error-code key is now a CI fail, surfaced as "violates ruleT8" not a new row. The UI-string-keying gap (§2) is a CLAUDE.md self-check item, not #2 — if hardcoded-Czech-in-JSX recurs across PRs I will open a NEW row (count starts at 2 on the first repeat), candidate for a future check.
- Nothing else at a 3rd-strike threshold is anticipated for a presentation slice. Re-scan the log at final review for any finding I raise twice.

---

## 8. Optimizer ping decision

**No optimizer ping required.** These are presentation islands, not hot paths — no handler touching >5 entities, no multi-step pipeline, no algorithmic surface (the backend owns the money math, the allow-list, the erasure matrix). The payout list is paginated server-side (URL-state); the audit-trail panel paginates server-side. If the delete-user in-flight composition ends up iterating a large unbounded order list client-side (HIGH-1), that becomes an optimizer concern — flag it then. Otherwise Gate 8 is N/A.

---

## 9. Final-review entry conditions (what I require before I even start the row-walk)

1. Diff touches `frontend/**` ONLY (no backend, no `lib/api-client/` — both are auto-reject).
2. `npm run lint` + `npm run build` clean; `check-consistency.mjs` exit 0 (screenshots/log in PR).
3. PR description lists every AC (b AC-1..12, c AC-1..16) with a QA proof link.
4. **SecOps Gate 3 sign-off present.** **Architect sign-off present.** (Both mandatory; §0.)
5. The `docs/questions/open.md` follow-ups (GetAdminOrderDetail; any delete-user read gap) are logged, not mocked.
6. Grep proofs attached: type-to-confirm exclusivity (HIGH-4); no `csv()` generated-method call (§1); no hardcoded Czech in new JSX (§2).

---

### Bottom line (preliminary)
The contract is ready (all methods + DTOs exist; no regen). The two slices are well-specified and the locked decisions are defensible. **The review will live or die on the delete-user screen (c):** the data source for its gates (HIGH-1), mirror-not-replace discipline (HIGH-2), the honest re-call rendering (HIGH-3), type-to-confirm exclusivity (HIGH-4), and the always-visible banner (HIGH-5). On the money side (b): the idempotency lock (HIGH-6) and the conditional post-payout checkbox (HIGH-7). Plus the CSV-blob trap (§1) and the hardcoded-Czech trap T8 can't see (§2). **SecOps Gate 3 + Architect sign-off are non-negotiable final gates.** No approval until every row above passes.
