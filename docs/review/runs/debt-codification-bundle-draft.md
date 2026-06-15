# Preliminary review — debt-codification bundle (T-0125)

> Reviewer draft, written IN PARALLEL with the implementers (T8/T9 checks + dispute-index map
> + Q-0013 sweep + Q-0019 index). Read against master before the diff exists. This is a
> **META bundle** — it codifies recurring findings #2/#3 into build-breaking gates, so the
> review focus is gate-soundness, not feature AC. Final verdict re-runs once the PR opens.

Ticket: `docs/tickets/T-0125-debt-codification.md` (status `ready`, user-locked §A.1–A.6 2026-06-15).
ADRs in frontmatter: 0009, 0013, 0022, 0023. `security_touching: false` (concurred — see §Dispute).

---

## Bundle scope (6 items + closeouts)

1. **T8** i18n-parity check in `scripts/check-consistency.mjs` (codifies finding #2). Allowlist-driven, `hard:true`, 70 seeds.
2. **T9** unique-index→translator check (codifies finding #3). NAMED-index-only, `hard:true`, 5 `// no-translator:` markers.
3. **`ux_disputes_order_open`** translator map (the latent bug) + concurrent-double-OpenDispute integration test.
4. **Q-0013** `/auth/login` → `/login` frontend sweep.
5. **Q-0019** `ix_orders_payout_unclaimed` partial index + migration.
6. **Q-0017** VERIFY-ONLY (already shipped at `20260613060609_FixEmailSubjectPlaceholders`).

Closeouts: flip recurring-findings #2/#3 → `codified-in-script`; add T8/T9 to checklist §J;
flip Q-0013/Q-0019 → `answered` on merge.

---

## HEADLINE — Are T8/T9 SOUND gates, or theater?

The check architecture on master is **per-file** (`RULES.forEach(rule => rule(file, src))` inside the
candidate-file loop) and **baseline-suppressed uniformly** (`fresh = findings.filter(f => !baseline.has(key))`,
key = `path:line:ruleId`, `readBaseline` regex accepts any `T\d+`). T8/T9 do not fit this shape. Three
soundness conditions MUST hold in the diff or the gates are theater:

### S-1 (BLOCKER-class): `hard:true` must actually bypass baseline — in BOTH directions
The current `main()` has **no `hard` concept**. Every finding flows through `baseline.has(findingKey)`
and `--update-baseline` writes **all** findings. As written, a future T8/T9 violation would be
suppressible: run `--update-baseline` once and the new unkeyed code / unmapped index lands as a
grandfathered baseline row — exactly the "real defect we never want to grandfather" the ticket forbids
(AC-2). The implementer must:
- Exclude T8/T9 findings from the `baseline.has()` filter (they always count as `fresh`).
- Exclude T8/T9 findings from `writeBaseline` (so `--update-baseline` cannot absorb them).
- Leave T1–T7 baseline flow untouched (the 145 tracked rows in `docs/audits/consistency-violations.md`
  must still exit 0).
- **Required proof:** a temporary-violation test that ALSO runs `--update-baseline` then re-runs the
  check, showing T8/T9 still exit 1. "Fails on a fresh violation" is necessary but NOT sufficient — the
  grandfather-immunity is the load-bearing half of AC-2.

### S-2 (HIGH): T8/T9 are WHOLE-TREE aggregate checks, not per-file
T8 parses the entire `BusinessErrorMessage.cs` code set against the entire `cs-CZ.ts` key set; T9 walks
all `Configurations/*.cs` against the single `UniqueConstraintTranslator.cs`. The existing `rule(file, src)`
contract is per-file and is **subject to `--paths=`**. If T8/T9 are shoehorned into the per-file loop:
- A `--paths=<subset>` run (pre-commit hooks routinely scope to staged files) would see a PARTIAL code
  set and **false-flag** every code/index not in the subset, OR silently pass by missing the source files.
- The check must scan its FIXED source files (`BusinessErrorMessage.cs`, `cs-CZ.ts`, the config dir, the
  translator) regardless of `--paths`, run ONCE per invocation, and be excluded from `IGNORED_PATH_GLOBS`
  drift. Verify the diff runs them as an aggregate phase, not inside the candidate loop.

### S-3 (HIGH): the negative-case proof must be REAL, per AC-1/AC-3
The check must FAIL on a real violation. Required in the diff (per test-plan stub): a temporary
unkeyed-unallowlisted fixture code → exactly 1 T8 finding naming the code + "add a cs-CZ key or
allowlist it"; a named `.IsUnique().HasDatabaseName("x")` with neither key nor marker → exactly 1 T9
finding. If the PR only asserts "exits 0 on the real tree," the gate is unproven — a check that always
passes is worse than no check. Demand the red proof.

**Pre-verified on master (the inputs the gates will codify are real and currently green):**
- Running the A.1 parse (`public const string (\w+) = "([^"]+)";` → group 2) against
  `BusinessErrorMessage.cs` = **148 codes**; cross-referenced against `cs-CZ.ts` keys
  (`^\s*'([^']+)'\s*:`) = **78 keyed, 70 unkeyed**. The ticket's "actual = 70" seed count is **exact**
  (architect's ~60 estimate was low). The parse approach is sound and reproducible.
- All 12 NAMED unique indexes enumerated (see §T9). 7 are translator-mapped today (incl. the dispute
  one once A.3 lands), 5 are the documented exclusions needing markers. Zero gaps → T9 exits 0 after
  the bundle.

---

## T8 allowlist soundness — is `T8_NO_KEY_REQUIRED` a rug? (NO)

The 70 unkeyed codes (the seed set) were enumerated and spot-checked against the "should this be
customer-facing?" test. Breakdown:
- **auth.*** (15) — Unauthorized/Forbidden/Conflict/Validation type-fallback; the auth UI renders its own
  bespoke keys (`auth.login.invalid_credentials`, `auth.login.account_locked`, `auth.register_maker.ico_*`),
  NOT `resolveErrorMessage(code)`. Correctly allowlisted.
- **validation.*** (11) — all collapse to `error.validation`. Field-level FluentValidation codes. Correct.
- **blob.*** (6), **company.registry*/notFound** (3), **email.*** pipeline (8: templateNotFound,
  translationMissing, providerTransient/Permanent, payloadMalformed, payloadMissingFields, eventTypeUnknown,
  + outbox.queuePublishFailed), **geocoder.*** (4) — internal / server-to-server / admin-log. Correct.
- **country.notServiced / configMissing**, **payment.gateway*** (3 legacy — live surface uses the keyed
  `payment.provider*`), **product.*** (notFound, imageLimitReached, imageNotFound, priceNegative,
  freeRequiresOnRequest, currencyMismatch, notOrderable — catalog surfaces use bespoke
  `catalog.product_detail.not_found.*` keys), **category.*** (3), **maker.*** (notFound, notActive,
  alreadyVerified, icoAlreadyRegistered, companyDissolved, slugAlreadyExists — register-maker form uses
  bespoke keys), **order.alreadyAccepted / notPayableYet** (Conflict fallback, no dedicated surface today).

**Verdict: GENUINELY the auth/validation/pipeline/internal/server-log fallback set — NOT a dumping ground.**
No customer-facing review/order error is masked. The borderline cases (`product.notFound`, the mapped
maker/category conflict codes) are defensible because their live surfaces render bespoke namespace keys,
not the dotted BusinessErrorMessage value. A code comment pointing at
`frontend/src/lib/runtime/errors.ts` (the `resolveErrorMessage` type-fallback) is required per A.1 — verify
it lands. **GREEN, with one watch:** if a future PR adds a customer-facing code AND drops it into the
allowlist to dodge T8, that defeats the gate — but that is a future-review concern, not this bundle's.

---

## T9 scope soundness — NAMED-only is correct

Enumerated every `.IsUnique()` in `Configurations/`:

**NAMED (`.IsUnique().HasDatabaseName(...)`) — IN T9 scope (12):**
| Index | Status after bundle |
|---|---|
| `ix_categories_slug` | translator-mapped ✓ |
| `ix_makers_registration_number` | translator-mapped ✓ |
| `ix_makers_slug` | translator-mapped ✓ |
| `ux_payout_batches_country_batch_number` | translator-mapped ✓ |
| `ux_payout_batches_open_per_country` | translator-mapped ✓ |
| `ux_reviews_order_active` | translator-mapped ✓ |
| `ux_disputes_order_open` | **mapped by A.3** ✓ |
| `ix_orders_order_number` | `// no-translator:` marker (A.2) |
| `ix_orders_payment_provider_ref` | `// no-translator:` marker (A.2) |
| `ix_invoices_invoice_number` | `// no-translator:` marker (A.2) |
| `ix_invoices_order_id` | `// no-translator:` marker (A.2) |
| `ix_makers_user_id` | `// no-translator:` marker (A.2) |

**EF-auto-named (`.IsUnique()` WITHOUT `HasDatabaseName`) — OUT of scope (correctly):**
`CountryConfiguration` IsoCode + CountryId, `EmailTemplate.Type`, `EmailTemplateTranslation`,
`User.EmailNormalized`, `User.GoogleSub`, `RefreshToken.TokenHash`.

These are config/seed/auth-infra — none back a user-facing uniqueness rule that should surface a typed
409, so excluding them is right.

**Limitation worth recording (NOT a blocker — A.2 scopes it out deliberately):** T9 is **one-directional**
(index → translator). It does NOT validate translator → index. Concrete instance: the translator maps
`IX_users_email_normalized`, but that index is **EF-auto-named** (`UserConfiguration.cs:25-27` has no
`HasDatabaseName`), so T9 will never see it and never validate that the mapping is still live. A future
rename/drop of an auto-named index would leave a stale translator key that T9 can't catch. Acceptable for
this bundle (the mapping is correct today and the constraint name is EF-stable), but flag for the
recurring-findings sweep as a known T9 blind spot if finding #3 recurs on an auto-named index.

**T9 marker honesty (A.2 / AC-4):** the 5 markers must carry the REAL rationale already present in the
translator's "Intentionally unmapped" prose (verified present in `UniqueConstraintTranslator.cs:87-126`),
NOT a blanket suppress:
- `ix_orders_order_number` / `ix_invoices_invoice_number` — generator-monotonic-under-FOR-UPDATE (ADR 0009);
  a 23505 means the generator broke = a bug, not a user conflict.
- `ix_orders_payment_provider_ref` / `ix_invoices_order_id` — idempotent-webhook pre-check; translating to
  Conflict would make the provider retry / outbox fail — wrong resolution.
- `ix_makers_user_id` — defence-in-depth (handler adds exactly one Maker; a 23505 = unexpected concurrent
  insert the handler couldn't produce). Must stay unmapped so the bug stays visible.

Reject any marker that reads as a bare `// no-translator: excluded` without the specific reason.

---

## Per-item AC

### AC-1/AC-2 (T8) — see HEADLINE S-1/S-3. Green on master inputs (70/148 exact); soundness gated on `hard` impl + red proof.

### AC-3/AC-4 (T9) — scope correct (12 named, EF-auto out). Markers must quote real prose. Gated on `hard` impl + red proof.

### AC-5 (dispute map) — `ux_disputes_order_open` → `Error.Conflict("orderId", OrderInvalidTransition)`
- The target code is correct: **no `DisputeAlreadyOpen`/`OrderAlreadyDisputed` code exists** (confirmed by
  enumerating `BusinessErrorMessage.cs` — the dispute codes are only `OrderDisputeCategoryNotAllowed` and
  `OrderDisputeNotOpen`, neither of which fits "already open"). Reusing `OrderInvalidTransition` is the
  right call and mints no new code, per A.3.
- **Semantic check passes:** `OpenDispute.Handler` (`OpenDispute.cs:90-101`) already returns
  `OrderInvalidTransition` for the already-Disputed invariant branch. The race-loser (insert-active-then-
  dispatch → 23505 on `ux_disputes_order_open`) getting the identical typed 409 is the correct
  dispute-already-open semantic — NOT a random conflict.
- **Field-name note (not a defect):** the handler's branch uses `Error.Conflict("state", ...)`; A.3/AC-5
  specify `Error.Conflict("orderId", ...)` for the translator entry. The `orderId` field matches the
  translator convention (key the field to the constraint's column — same as `ux_reviews_order_active` →
  `"orderId"`). Both resolve to the `order.invalidTransition` key (exists at `cs-CZ.ts:455`) and HTTP 409.
  Intentional and consistent — confirm the implementer uses `"orderId"` (translator convention), not a
  copy-paste of the handler's `"state"`.
- **Test must exercise the 23505 path, not the app pre-check:** AC-5 requires inserting an ACTIVE dispute
  row DIRECTLY (bypassing the handler's Step-3 Silent-Success pre-check), THEN dispatching `OpenDispute` so
  the loser hits the unique index. Verify the test does NOT just trip the `order.State == Disputed`
  pre-check at line 90 — that path returns the conflict WITHOUT touching the index and would prove nothing
  about the translator. The order must be in a NON-Disputed state with an orphan active dispute row to
  force the insert→23505. Mirror the `ux_reviews_order_active` concurrent-loser test.

### AC-6 (Q-0013) — sweep correctness: TICKET FILE LIST IS STALE ON 2 OF 8 ENTRIES
Grepped `/auth/login` across `frontend/src`. The **actual live 404 source refs = 7**, all
`<Link href="/auth/login">`:
- `(auth)/verify/verify-client.tsx:63`
- `(auth)/reset/reset-client.tsx:56` + `:116`
- `(auth)/register/register-form.tsx:52` + `:99`
- `(auth)/register/maker/register-maker-form.tsx:62`
- `pro-makery/page.tsx:159`

The ticket §4 / Files list names **8** refs including `middleware.ts:24` and `profile-client.tsx:53` —
but **both are ALREADY `/login` on master**:
- `middleware.ts:24` → `loginUrl.pathname = '/login'` (already correct; comment at :6 confirms).
- `(customer)/dashboard/zakaznik/profile/profile-client.tsx:53` → `router.push('/login')` (already correct).
- (the maker profile-client has no login ref at all.)

So the sweep should edit the **7** live `<Link>` refs and VERIFY (not edit) middleware + profile-client.
Net: the ticket's "exactly 8 live source refs" is off by the two already-correct entries. Not a defect in
the codification, but the implementer should reconcile the count and NOT introduce a spurious edit to the
two already-fixed files. **Correctly excluded:** the 4 api-client `/api/v1/auth/login` refs
(`public/maker/customer/admin-api.v1.ts`) + the `login-form.tsx:14` prose doc-comment (describes the
`/api/v1/auth/login` POST endpoint) — leave untouched per A.4. The `/login` route resolves
(`(auth)/login/page.tsx` exists, renders `LoginForm`). `next build` assertion required per AC-6.

### AC-7 (Q-0019) — partial-index predicate must match the eligibility scan EXACTLY
The eligibility scan filters `state == Delivered AND payout_batch_id IS NULL` and (per house convention)
active rows. The new index `ix_orders_payout_unclaimed` must use
`HasFilter("state = 'Delivered' AND payout_batch_id IS NULL AND is_active")`. Verify:
- The literal `'Delivered'` matches the string-stored state column (`OrderConfiguration.cs:68-72` stores
  `State` via `HasConversion<string>()` — so `state = 'Delivered'` is the correct SQL, matching the
  `ix_orders_state` partial precedent at `:203-205` and `ux_payout_batches_open_per_country`'s
  `state = 'Processing'`).
- Index target column: Q-0019's proposal is `ON orders(country_code) WHERE ...`. Confirm the leading
  column matches what the scan seeks (country-scoped). Additive only — no data change, no query change,
  no translator entry (not unique → T9 N/A, per A.5). Auto-generated migration + Designer (Designer is
  `IGNORED_PATH_GLOBS`, won't trip checks).

### AC-8 (Q-0017) — VERIFY-ONLY confirmed
`20260613060609_FixEmailSubjectPlaceholders.cs` exists, is idempotent (`WHERE subject LIKE '%{order_number}%'
AND subject NOT LIKE '%{{order_number}}%'`), schema-free (snapshot unchanged), with a matching Down. No code
diff in this bundle. Record verified-resolved in the ledger. ✓

### AC-9 (exit 0) — must hold after the bundle
On master the check exits 0 (145 tracked, 0 fresh). After the bundle: T8 (70 seeded) + T9 (12 named all
mapped/markered) + dispute-map + Q-0019 index (not unique, T9 N/A) = zero NEW T1–T9 findings. The risk is
the codification introducing its OWN violation (e.g. the new migration file tripping T6, or a config edit
tripping T9 against itself). Re-run `node scripts/check-consistency.mjs` on the branch and confirm exit 0
at final review.

### AC-10 (docs) — flips
recurring-findings #2/#3 → `codified-in-script` (link the T8/T9 check ids); checklist §J gets T8 + T9 rows
(quote, don't paraphrase). Q-0013/Q-0019 → `answered` on merge. Note: per recurring-findings workflow, only
the ARCHITECT flips prior rows — the reviewer/implementer flipping #2/#3 is sanctioned here ONLY because the
ticket explicitly scopes it as a closeout task; confirm the architect is in the loop on the status flip.

### AC-11 (build/tests) — backend build clean; new Testcontainers concurrency test passes; `next build`
clean; NSwag parity unaffected (no contract change — no new BusinessErrorMessage code minted, A.3 reuses
`OrderInvalidTransition`). Confirm no `.spec-hashes.json` / api-client churn in the diff.

---

## Recurring-findings codification correctness

- **#2 (i18n parity)** count=3 (order-cleanup, order-dashboards, payout-core) — correctly at the ≥3
  codify threshold. T8 codifies the "every BusinessErrorMessage ships a parallel cs-CZ key" rule
  mechanically. Allowlist is the honest realization of the "type-fallback codes are exempt" reality
  (errors.ts). Sound promotion.
- **#3 (unique-index → translator)** count=2 on the log (payout-core ×2 constraints counted as one row,
  reviews-loop ×1). The ticket calls it "one strike from codification" / "third strike fired" — the
  dispute bug surfaced during T9 design IS effectively the third instance, justifying codify-now rather
  than wait. Defensible: codifying at the moment a latent third instance is found is better than shipping
  the bug. T9 codifies the index→translator pairing. Sound.
- Both rows flip to `codified-in-script` — correct status per the log legend.

---

## Preliminary verdict: REQUEST CHANGES (pending diff) — 1 BLOCKER-class soundness gate + 4 HIGH verifications

This bundle is well-specced and the inputs are green on master (70/148 T8 exact; 12 named indexes all
accounted for; dispute target code correct; Q-0017 verified; Q-0019 predicate derivable). Approval is
**gated on proving the gates are real**, not on feature correctness:

1. **[BLOCKER-class] S-1 `hard:true` grandfather-immunity** — T8/T9 must bypass `baseline.has()` AND be
   excluded from `--update-baseline`. Proof required: temporary violation + `--update-baseline` + re-run
   still exits 1. Without this, AC-2/AC-6-hard is unmet and the gates are theater.
2. **[HIGH] S-2 whole-tree aggregate** — T8/T9 must scan fixed source files once, immune to `--paths=`
   scoping, outside the per-file loop.
3. **[HIGH] S-3 red proof** — negative-case fixtures showing exactly 1 finding each (AC-1/AC-3). A
   check that only proves "exits 0" is unproven.
4. **[HIGH] AC-5 dispute test exercises the 23505 path** — insert orphan active dispute on a NON-Disputed
   order, force the index violation; not the Step-3 pre-check. Field name `"orderId"` per translator
   convention.
5. **[HIGH] AC-6 Q-0013 stale file list** — sweep the 7 live `<Link>` refs; do NOT spuriously edit the
   already-correct `middleware.ts:24` and `profile-client.tsx:53`; leave api-client + login-form prose.

Plus the green checks to re-confirm at final review: T8 allowlist remains the fallback set (no
customer-facing leak), T9 markers quote real prose, Q-0019 predicate matches `state = 'Delivered' AND
payout_batch_id IS NULL AND is_active`, full-tree exit 0, doc flips.

No SecOps ping (security_touching:false concurred — the dispute fix converts a race-loser 500 into the
same typed 409 the winner's pre-check already returns; no new auth surface, endpoint, or data exposure).
No Architect design ping needed, but the architect should be in the loop on the recurring-findings #2/#3
status flip (workflow rule: only architect edits prior log rows) and may want the T9 one-directional
blind-spot (auto-named indexes) recorded.
