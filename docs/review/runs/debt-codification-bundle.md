# Final review — debt-codification bundle (T-0125)

**Branch:** `chore/debt-codification-bundle` (6 commits, 2c26e61..5975bc4)
**Verdict: APPROVE.** Every checklist row + every mandatory check passes. Zero BLOCKERs, zero
request-changes items. Gates 1–9 green; gate-soundness proven by execution (not assertion).

The bundle diffed against its grooming parent (`2c26e61^..HEAD`) is 20 files / +3244 −19 — the
master diff is inflated by intervening merged feature bundles; the bundle scope is exactly the 6
items + closeouts.

---

## (b) T9 hard-fail proof — RAN, PASSES (mirror of the pre-confirmed T8 proof)

Injected a NAMED unmapped+unmarked unique index `ix_temp_t9` into `MakerConfiguration.cs`:

1. `node scripts/check-consistency.mjs` → **exit 1**, exactly 1 T9 finding naming `ix_temp_t9`
   ("neither mapped … nor carries a `// no-translator:` marker — map it or mark it").
2. `--update-baseline --baseline=<temp>` → wrote **145** findings; `grep -c ix_temp_t9 <temp>` = **0**.
   The hard finding never enters the baseline file (`writeBaseline` filters `!f.hard`,
   check-consistency.mjs:620).
3. Re-run against that baseline → **still exit 1**, same T9 finding. No grandfathering
   (`fresh = findings.filter(f => f.hard || !baseline.has(key))`, :712).
4. Reverted via `git checkout`; file clean (no diff).

**EF-auto-named negative case:** injected `.IsUnique()` WITHOUT `HasDatabaseName` → **exit 0, clean,
zero T9 findings.** Correctly out of scope (`nameM` null → `continue`, :560). Reverted; clean.

T9 has the identical grandfather-immunity the lead verified for T8. Both run in the `AGGREGATE_RULES`
phase gated by `if (!args.paths)` (:690) — whole-tree, once-per-invocation, immune to `--paths`
scoping (S-2 satisfied). Designer/snapshot files are in `IGNORED_PATH_GLOBS` so they never self-trip.

## (c) Allowlist-not-a-rug — VERDICT: NOT A RUG

Read all 70 `T8_NO_KEY_REQUIRED` codes (check-consistency.mjs:393–478). They are
auth.* (15) / validation.* (11) / blob.* (6) / company.registry* (3) / email-pipeline (8) /
outbox (1) / geocoder.* (4) / country (2) / payment.gateway* (3, legacy — live surface uses keyed
`payment.provider*` which are NOT allowlisted) / product.* / maker.* / category.* (live surfaces
render bespoke namespace keys, not the dotted value) / `order.alreadyAccepted` + `order.notPayableYet`
(Conflict fallback, no dedicated surface). `errors.ts:38` confirms allowlisted codes render the
generic `error.<type>` copy via `resolveErrorMessage`. **NONE is a customer-facing review.*/order.*/
payment.* user-error masquerading as fallback.** No leak.

## (d) BLOCKERs — NONE

The draft's S-1 BLOCKER (hard:true grandfather-immunity) was a pre-implementation concern; the lead
pre-confirmed T8 and I independently confirmed T9 above. Resolved in code.

## (e) Fold list — EMPTY (nothing to fix)

One advisory only (not a fold, not a gate): the recurring-findings legend (line 3) says the
**architect** marks rows codified; these flips landed in the implementer commit. Sanctioned by the
ticket's explicit closeout scope — architect should be looped on the #2/#3 status flip and may want
the T9 one-directional blind-spot (auto-named indexes invisible to T9) recorded for the next sweep.

---

## (f) Per-item checks

| # | Check | Result |
|---|---|---|
| 1 | T9 hard-fail proof (3-step) + EF-auto zero-findings | **PASS** (ran — see (b)) |
| 2 | T8 allowlist not a rug | **PASS** (70 reviewed; no customer-facing leak) |
| 3 | T9 markers carry real rationale (5 markers) | **PASS** — generator-monotonic (order_number, invoice_number), idempotent-pre-check (payment_provider_ref, invoice→order_id), defence-in-depth (maker user_id); quote translator prose at UniqueConstraintTranslator.cs:100–139 |
| 4 | Dispute map + 23505 test | **PASS** — `ux_disputes_order_open` → `Error.Conflict("orderId", OrderInvalidTransition)`; handler's already-Disputed branch uses same code (OpenDispute.cs:90,100, field "state"); `"orderId"` is translator convention (matches ux_reviews_order_active). Test inserts winner OPEN dispute in own ctx leaving order Delivered → loser passes Step-3 gate, hits 23505; asserts 409 + atomic rollback (state stays Delivered, 1 dispute). **Ran on real Postgres: 1 passed.** |
| 5 | Q-0013 sweep complete | **PASS** — `git grep` shows ZERO frontend-nav `/auth/login`; only 4 api-client `/api/v1/auth/login` + 1 doc-comment remain. 7 `<Link>` swept + middleware.ts:24 + profile-client.tsx:53 (these WERE `/auth/login`, draft's master snapshot was wrong — end state correct). `/login` route resolves (page.tsx exists). tsc clean. |
| 6 | Q-0019 index | **PASS** — composite `(State, PayoutBatchId)`, filter `state = 'Delivered' AND payout_batch_id IS NULL AND is_active` matches scan (OrderRepository.cs:253–255 + global IsActive filter). `ix_orders_state` PRESERVED (snapshot:1860 + :1868 both present). Additive only; not unique → T9 N/A. |
| 7 | recurring #2/#3 → codified-in-script + checklist §J | **PASS** — #2→ruleT8, #3→ruleT9 with links; §J adds T8/T9 rows (quoted). |
| 8 | T1–T7 flow intact | **PASS** — exit 0, 145 tracked unchanged, 0 fresh; aggregate phase skipped under --paths (acceptable, CI runs full tree). |
| 9 | Build + tests | **PASS** — dotnet build 0W/0E; unit 1719/0; dispute concurrency 1/1 on real PG (maxParallelThreads=2 confirmed); frontend tsc exit 0; no api-client/spec-hash churn (no contract change — OrderInvalidTransition reused). |

## Gates summary
Gate 1 (arch) ✓ · Gate 2 (AC traceability) ✓ all 11 ACs proven · Gate 3 (security) ✓ no ping —
security_touching:false concurred (race-loser 500→same typed 409; no new surface) · Gate 4 (RDD) ✓
no new aggregates/VOs (translator entry + index only) · Gate 5 (TDD) ✓ red predicate commit 18f8401
precedes impl; dispute test ships with the fix · Gate 8 (mechanical) ✓ exit 0 · Gate 9 (build/QA) ✓.

**APPROVED for merge.**
