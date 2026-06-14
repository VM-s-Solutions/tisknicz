# Admin-ops bundle — Gate 9 consistency + QA plan authoring

Branch: `feat/admin-ops-bundle` (the git-status snapshot label
`feat/order-cleanup-bundle` is stale). Date: 2026-06-14. Author: QA.
Tickets: T-0108 (config update), T-0109 (outbox retry/ack), T-0110 (GDPR
hard-delete), T-0111 (admin read queries). One PR, risk-ascending order.

## Task 1 — Gate 9 consistency

```
node scripts/check-consistency.mjs  →  exit 0, "clean (145 tracked)"
```

**Verdict: PASS.** Exit 0 at 145. Every tracked entry is a known-benign
baseline class (T1 wrapper-shape false-positives, T3 SaveChangesAsync in the
UoW behavior / outbox interfaces, T4 `dynamic` in `AdminAuditLogWriter`, T5
inline-string Error calls). **No new real (T-class) violation introduced.**

### Baseline audit — vs master (`f0a07e2`)

The branch baseline grew **125 → 145 (+20)**, NOT the naive +7. This is
**correct and benign**, because `feat/admin-ops-bundle` sits atop two
merged-but-not-yet-on-master bundles:
- PR #50 `feat/payout-settlement-bundle` (+ payout-core PR #49) — 8 Payouts
  feature files (T-0102a/b, T-0103, T-0112).
- PR #51 `feat/reviews-loop-bundle` — 5 Reviews feature files (T-0100).

So vs `master` the +20 = **7 admin-ops-owned** + 8 payouts + 5 reviews
(the latter 13 are prior-bundle source files already reviewed upstream, all
benign T1 wrapper-shape false-positives).

### The admin-ops-specific +7 — verified exactly as claimed

The bundle's own re-key commit (`2bc3274`) added the 7 claimed admin feature
files (and back-filled 5 Reviews lines the prior merge's baseline had not yet
captured — see finding below):

| File | Ticket | Rule |
|---|---|---|
| `Features/Admin/GetAllOrders.cs` | T-0111 | T1 wrapper-shape FP |
| `Features/Admin/GetAllInvoices.cs` | T-0111 | T1 wrapper-shape FP |
| `Features/Admin/GetAdminAuditLog.cs` | T-0111 | T1 wrapper-shape FP |
| `Features/Outbox/RetryOutboxEvent.cs` | T-0109 | T1 wrapper-shape FP |
| `Features/Outbox/AcknowledgeOutboxEvent.cs` | T-0109 | T1 wrapper-shape FP |
| `Features/CountryConfigurations/UpdateCountryConfiguration.cs` | T-0108 | T1 wrapper-shape FP |
| `Features/Users/DeleteUserPermanently.cs` | T-0110 | T1 wrapper-shape FP |

All 7 are the "feature file must declare a public static class wrapper" T1
false-positive (the one-file `Feature.Command/Response/Validator/Handler`
shape the checker doesn't model) — identical class to the 100+ existing T1
baseline entries. Nothing else admin-ops-owned appears; no T2/T3/T4/T5/T6/T7
real violation was added by this bundle.

**Finding (LOW, not a blocker):** commit `2bc3274` also added 5 `Reviews/*`
T1 lines to the baseline that the reviews-loop merge (`7f6b85e`) had not yet
recorded. These correspond to genuine source files merged upstream in PR #51
(not new code) and are the same benign T1 wrapper-shape class. They are a
baseline back-fill, not a regression. Net real-violation delta introduced by
the admin-ops bundle: **0.** When this branch merges to master, master's
baseline jumps to 145 in one step (folding all three bundles' benign entries);
reviewers comparing the eventual PR against master should expect +20, of
which only +7 are admin-ops-authored.

## Task 2 — QA plans authored (committed: NOTHING)

Four plans written to `docs/test-plans/`, T-0105.md format (scope,
preconditions w/ admin account + seeded multi-tenant + reduced-parallelism
note, manual cases table, automated must-cover matrix, edge cases,
regression spot-checks, defects):

| Plan | Manual TCs | Automated must-cover rows | Notes |
|---|---|---|---|
| `T-0108.md` | 9 | 6 + Validator rows | retype gate, unregistered-code ordering, in-flight advisory, no-op Q-0021 |
| `T-0109.md` | 10 | 5 | ladder-NOT-reset (A.1), retry-409 vs ack-Silent-Success asymmetry |
| `T-0110.md` | 11 | 6 + GDPR checklist (16 rows) | the big one — full matrix, dual-role interlock, atomicity, no-Silent-Success |
| `T-0111.md` | 12 | 6 | Unscoped cross-tenant, soft-deleted visibility, privileged DTO |

Error codes confirmed present in `BusinessErrorMessage.cs`:
`country.providerConfirmationMismatch` (273), `country.providerNotRegistered`
(267, reused), `outbox.alreadyProcessed` (638), `user.notFound` (593),
`user.deleteConfirmationMismatch` (596), `user.cannotDeleteWithInFlightOrders`
(590, reused). Routes confirmed: `POST /users/{id}/erase`,
`POST /outbox-events/{id}/{retry|acknowledge}` (`[Authorize]` admin host).

### T-0110 GDPR-completeness coverage (the highest-risk surface)

The plan ships a 16-row PII-field disposition checklist covering the full
matrix (User/RefreshToken/unreferenced-Address hard-delete; Order contact /
Review author / Maker PII anonymize; IČO+bank retain + `IsRetainedForLegal`;
Invoice byte-identical; audit row survives). It does NOT stop at the ticket's
enumerated entities — it adds an explicit **independent-schema-grep** step
(challenge the matrix for any un-listed `*_email`/`*_name`/`*_phone`/`ip` or
user-FK column — e.g. `OrderMessage` author, payout/label contact snapshots),
recording any column without a disposition as a HIGH defect. It also raises
the `Disputed`-order question (money in escrow but NOT in the in-flight
interlock set) as a deliberate challenge rather than a silent pass, and
covers atomicity via fault-injection at multiple seam points.

## Gaps / risks surfaced
- **T-0110 matrix-completeness is the residual risk.** The plan's
  schema-grep step is manual and depends on QA executing it against the live
  schema at PR time; an un-enumerated PII column would pass the listed
  automated integration test yet leak data. This is flagged as the bundle's
  top QA risk — recommend the Reviewer + architect explicitly sign the
  16-row checklist against the actual `users`-referencing schema before merge.
- **No staging admin UI** — every manual case runs via Swagger/curl with an
  admin JWT (T-0118 owns the admin views downstream). Manual execution is
  blocked until a deploy with an admin token is available; the automated
  suites carry the AC weight in the interim.
- **`Disputed` not in the interlock set** — open question for BA/architect
  (T-0110 plan edge cases): a disputed order has escrowed money; anonymizing
  its contact snapshot mid-dispute may be unsafe. Ticket lists only the 4
  states by design; flagging for confirmation, not asserting a defect.
- Casing interaction in T-0108 (registry membership is `OrdinalIgnoreCase`
  but the retype-equality gate may be case-sensitive) — flagged as an edge
  case to verify; a leniently-cased registered code could pass
  `providerNotRegistered` yet fail the mismatch.

Nothing here blocks Gate 9. The 4 plans + this note are the deliverables;
no code or baseline file was committed.
