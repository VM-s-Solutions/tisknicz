# Gate 3 (Security) — admin-dashboard-actions (T-0118b + T-0118c)

**Branch:** `feat/admin-dashboard-actions` · 5 commits (374af2f, 28ecc59, a565d7e, 446c11e, 9f0d022)
**Scope:** the only hard-delete UI (T-0110 erase), admin command surface (refund / state-change / dispute), operator CSV (cross-maker bank PII), admin invoice PDF, country-config edit.

## Verdict: GATE3_PASS

---

## 1. DELETE-USER UI cannot escalate/bypass — PASS

The wire request is `{ confirmedEmail, reason }` only (`eraseUser`, admin-ops-client.ts:307-315). There is **no** server-trusted `confirmed:true` flag. The backend (`DeleteUserPermanently.cs`) is authoritative and independent of UI friction:
- Step 1 fail-closed session (never attributes to "system").
- Step 3 retype gate: `User.NormalizeEmail(command.ConfirmedEmail) != user.EmailNormalized` → `user.deleteConfirmationMismatch` (409). A client hardcoding a match still hits this compare against the real row.
- Step 4 in-flight interlock: `HasInFlightOrderForUserAsync([PendingPayment, Paid, Accepted, Shipped, Disputed])` → `user.cannotDeleteWithInFlightOrders` (409), short-circuits BEFORE the irreversible seam; re-guarded in the seam (patterns §A.23 rule 1).
- The client `emailMatches`/`reasonValid` predicate is explicitly presentational (delete-user-panel.tsx:159-162); the UI adds friction, the server decides.

**Delete-user-cannot-bypass-backend: CONFIRMED.** The two gates are server-enforced and the UI cannot supply a decision the backend trusts.

## 2. No client-side authority on destructive ops — PASS

Refund: amount entered, scaled display-only (`parsedWhole * 100`); the remaining-refundable cap, post-payout gate and `payment.refund.*` verdicts are backend. State-change: `ALL_ORDER_STATES` minus current is a candidate list only — the allow-list is `ManualOrderTransitionPolicy` (Core.Domain); reason min-10 is the backend Validator. Erase: gated as above. No client computes "allowed" and sends a bypass; every modal posts intent and renders `resolveErrorMessage`.

## 3. CSV PII (operator bank file) — PASS

`downloadPayoutCsv` → `apiFetch('admin', .../csv, parse:'blob')` (authed, audience cookie). Backend `PayoutBatchesController.DownloadCsv` is `[Authorize]` admin-audience, streams from the private `payouts` container (no direct browser→blob link, ADR 0011), `Cache-Control: private, no-store`. The blob URL is created client-side ONLY from the already-authed response body and revoked in `finally`. No public/unauthed link, no maker/customer surface exposes it (inverts T-0116 maker absence). **CSV-PII gating: CONFIRMED admin-only.**

## 4. Admin invoice-PDF download — PASS

`downloadAdminInvoice` → `apiFetch('admin', /admin-invoices/{id}/pdf, parse:'blob')`. Backend `AdminInvoicesController` `[Authorize]` admin-audience, Unscoped read is admin-host-only (ADR 0013), `private, no-store`, filename header-escaped. Authed blob path, admin-gated.

## 5. Reason/notes fields — PASS

Refund reason, state-change reason, erase reason, dispute description/resolutionNotes, country reason — all bound to controlled inputs and rendered as React text children (no `dangerouslySetInnerHTML` anywhere under `(admin)`). Audit-row renders `item.notes`/`targetId` as escaped children. Backend caps: erase reason ≤2000, ack ≤2000, state reason min-10 (all FluentValidation).

## 6. No secrets / no new unauthed route — PASS

No `NEXT_PUBLIC_*`, no `process.env` in the admin tree. All 4 new c-surfaces live under `(admin)/dashboard/admin/*`, covered by both the layout cookie gate (`dashboard/admin/layout.tsx`) and middleware matcher `/dashboard/admin/:path*`. Only `/admin/login` is the intentional ungated sibling (no redirect loop).

## 7. Country-config edit — PASS

Form renders only operator-entered provider codes (payment/shipping/registry/email) — not secrets. PUT is `[Authorize]` admin. The retype-modal lists the typed codes (escaped text). No key/secret field exists in the form.

## 8. Erase irreversibility — UI honesty — PASS

Banner "Data uživatele nelze obnovit"; terminal state "byly nevratně odstraněny"; no "undo" affordance. `user.notFound` re-call renders truthfully as "Uživatel již byl smazán" (no silent success — T-0110 rule). Invoice retention cited per GDPR Art. 17(3)(b) — legally accurate.

---

## Folds
None. No BLOCKER, no FOLD required.

## Noted (non-blocking, already ticket-flagged follow-ups — not security gaps)
- No user-lookup / per-user-order read → in-flight block surfaces as post-call verdict rather than pre-disable. Fail-closed; backend authoritative. Acceptable.
- No payout-batch / country-config GET → forms ship without pre-fill. No security impact.
