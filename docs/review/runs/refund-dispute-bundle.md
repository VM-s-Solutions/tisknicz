# Refund-dispute bundle (T-0105 + T-0106 + T-0107) — Final review

> Branch `feat/refund-dispute-bundle` @ `dfb731e` (6 commits: d2817b8 grooming, f9eb0cb red, 9085897/bcedce4/ad9e862 impl, dfb731e NSwag). Reviewed against the preliminary draft (`refund-dispute-bundle-draft.md`), tickets, ADR 0013/0014/0015/0016, patterns §A.22. Run date 2026-06-12.

## Verdict

**REQUEST CHANGES** — one blocker (B-1: the AC-9 end-to-end leg is untested) plus three small folds. Everything else passes, including all five armed tripwires from the draft. No re-review of unchanged code needed once the blocker + folds land.

## BLOCKER

- **B-1 — AC-9 e2e leg missing: no integration test for the ResolveDispute → Refunded outcome.** T-0106 AC-9: "…the order ends in `State = Refunded` **end-to-end**." T-0106 §C Risk 3: "**integration test AC-9 is the guard**" for the shared-DbContext mid-request commit ordering. `ResolveDisputeIntegrationTests.cs` has only the Resumed e2e (:231) and the loud-409 (:272); `grep DisputeResolutionOutcome.Refunded` over `Makables.IntegrationTests/` returns zero hits. The only Refunded-outcome coverage is `ResolveDisputeHandlerTests.cs:133` with a **mocked `ISender`** (`:36`) — it never exercises the real nested pipeline (inner UoW flush of the shared DbContext, dual audit rows, outer-commit no-op), which is the single riskiest mechanism in this bundle. **Fix expected:** add two e2e cases to `ResolveDisputeIntegrationTests`: (a) happy leg — Disputed order + `FakeComgatePaymentProvider` → resolve Refunded → DB ends `Refunded`, dispute row resolved, exactly one refund provider call with the full remaining amount, **both** audit rows (`order.dispute.resolve` + `order.refund`); (b) inner-failure leg — scripted Permanent provider failure → 4xx surfaced, dispute stays OPEN, order still Disputed, zero outbox/audit rows (Risk §4 / draft HIGH-3 leg 2).

## Folds (same PR, small)

- **F-1** `ResolveDispute.cs:142` — bare `order.PreDisputeState!.Value` on a data-representable NULL (`pre_dispute_state SMALLINT NULL`). Draft open-item 4 asked for a defensive guard; the handler already has the exact pattern one step earlier (the step-4 missing-dispute-row guard, :129-138). Mirror it: null → Critical log + Conflict, not a potential 500.
- **F-2** RDD parity (ADR 0015): no role files for the new `Dispute` entity + `IDisputeRepository` and `ManualOrderTransitionPolicy`. `docs/architecture/roles/order-message.md` is the precedent for child entities; the content already written inline in `roles/order.md` can be extracted.
- **F-3** Gate 7: `docs/deployment/env-vars.md` has no `ADMIN_NOTIFICATION_EMAIL` row — the ticket's one manual step is routed exactly there and the bundle diff does not touch `docs/deployment/`.
- **F-4 (Architect, not implementer)** patterns.md §A.22 rule 5 (line 1083) still says re-resolve is Silent-Success; the implementation is the user-locked loud `409 order.dispute.notOpen` (T-0106 §C.4 / Alternatives G; pinned at `Dispute.Resolve` and `ResolveDispute` step 3). Align rule 5 to "open = Silent-Success idempotent; resolve = loud conflict", cite T-0106. (Draft MEDIUM-1; reviewer cannot edit process docs.)

## HIGH — accepted by lock (no action this PR)

**Double-refund on partial-refund retry is real.** Full refunds are gateway-safe (Comgate caps cumulative refunds at the capture, so a retried full refund is rejected). Partial refunds are NOT intent-safe: after refunded-but-unrecorded (provider success → commit failure at `RefundOrder.cs` step 6) **or** a transport timeout that actually succeeded at the gateway, a retry re-issues the same partial and Comgate accepts (cumulative still ≤ capture). The adapter sends no idempotency handle (`ComgatePaymentProvider.RefundAsync` form fields: merchant/transId/amount/curr/[test]/secret — no `refId`). This is locked-deferred per T-0105 Alternatives G with the compensating controls all implemented: remaining-cap pre-flight before the provider call (`RefundOrder.cs:144`), step-6 Critical log carrying AmountMinor + Currency + OrderId + TransId (`RefundOrder.cs:174-179`), adapter Information log with transId + amount, T-0118 confirm UI pending. **Recommendation:** open a follow-up Q-item/ticket to pass Comgate `refId` as an idempotency handle when T-0118 ships.

## Defect found (pre-existing) — recommend Q-item

**Latent seed-subject bug confirmed.** Four prior migrations store email **subjects** with single braces because `{{key}}` inside a `$@"…"` interpolated string collapses to `{key}`: `20260606155359_SeedOrderEmailTemplates.cs:60-63`, `20260608120000_ShippingPipelineBundle.cs:86-89`, `20260609075803_DeliveryCloseBundle.cs:63-64`, `20260609174208_OrderCleanupBundle.cs:191-194` — 14 translation rows whose subjects render the literal `#{order_number}` (the `{{key}}` substitution never fires; bodies are unaffected, they are non-interpolated consts). The new bundle migrations correctly use quadruple braces (`{{{{order_number}}}}`). Recommend: defect Q-item + a fix-up migration re-writing the 14 subjects.

## Tripwire and mandatory-check results

| # | Check | Result |
|---|---|---|
| 1 | Gate 5 red-first commit order | **PASS.** `git show f9eb0cb --stat`: tests-only (7 files — OrderRefundTests, OrderDisputeTests, ManualOrderTransitionPolicyTests w/ `Enum.GetValues<OrderState>()` exhaustive matrix, OrderRevertAcceptanceTests, DisputeEnumsTests, OutboxEventTypesTests, OrderTests reshape), precedes 9085897/bcedce4/ad9e862. All three pure-logic surfaces pinned red. Handler/integration tests alongside impl = compliant. |
| 2 | Provider-first ordering + Critical-log content | **PASS.** Steps 1–4 read-only; provider at `RefundOrder.cs:157`; mutation at :171; Critical log carries the full reconciliation tuple. Pre-flight uses the same `ValidateRefund` predicate the mutator calls — one source of truth. Retry intent-safety: see HIGH above. |
| 3 | Partial arithmetic | **PASS.** `long` minor units end-to-end (adapter: `amountMinor.ToString(InvariantCulture)`); cumulative `+=`; `RemainingRefundableMinor` computed, EF-ignored (no phantom column); over-refund 409 before the provider, pinned with `FakeComgatePaymentProvider.RefundCalls` no-second-call assertion; state flips only at cumulative == total; migration `BIGINT NOT NULL DEFAULT 0`. |
| 4 | Nested dispatch UoW boundary | **PASS in code, UNDER-TESTED (B-1).** Same scope (injected `ISender`), inner failure propagated verbatim (:199-202), Cancelled-edge failure rolls back the whole resolution (:211-214), no manual transaction, no scope factory. Resolution staged before dispatch → atomic with the inner commit; outer audit rides a second commit (known residual, draft HIGH-3.1). |
| 5 | `Order.Cancel` default-param trap | **PASS.** Both call sites explicit `OrderCancellationSource.Admin` (`ResolveDispute.cs:210`, `ChangeOrderStateManually.cs:114`); source asserted in tests (`ResolveDisputeHandlerTests.cs:191`, `ChangeOrderStateManuallyHandlerTests.cs:78`). |
| 6 | A.22 rule 5 vs loud 409 | Implementation follows the ticket (loud). **→ F-4.** |
| 7 | T-0107 allow-list | **PASS.** Table-driven policy w/ deterministic precedence; 5 pairs route to semantic domain methods only (no generic setter exists); manual Shipped→Delivered stamps `DeliveredAt` + `OrderDeliverySource.AdminManual` (pinned at integration :235); blocked codes name sanctioned commands; `Accepted→Paid` requires providerRef; exhaustive matrix fails on unclassified future enum values. |
| 8 | Dispute restore | **PASS except F-1.** PreDisputeState stamped before flip, restored + cleared on resolve, `DisputedAt` kept; allow-list Paid/Accepted/Shipped/Delivered (Completed out, T-0060 pins rewritten red-first); double-open backstopped by `ux_disputes_order_open UNIQUE (order_id) WHERE resolved_at IS NULL`; re-open Silent-Success returning existing id; re-resolve loud at both entity and handler. |
| 9 | Admin audit + fail-closed | **PASS.** All four admin commands `IAdminAuditableCommand`; all four handlers have explicit fail-closed session checks (incl. the two T-0106 admin commands the ticket omitted — draft HIGH-6 closed); the `?? "system"` fallback is unreachable for this bundle. No fold needed. Notes: refund folds the ack marker; resolve = ResolutionNotes; manual change = Reason. |
| 10 | i18n parity tripwire | **PASS — third strike NOT fired.** 13 new `BusinessErrorMessage` codes ↔ 13 new cs-CZ keys (4 refund + 2 dispute + 1 email config + 6 manualTransition). No harvest row. Recurring-finding count stays 2/3. |
| 11 | Deviations | All judged sound. Double-brace subjects = correct fix-forward (exposes the pre-existing defect above). `OrderDisputedCarrierSourcedPayload` deletion clean — repo-wide grep shows zero remaining code consumers (event was never routed; any queued rows stay visibly unrouted, pre-launch zero risk). 40-char id caps in Validators match the ULID column — clean 400 instead of a DB error. Send-time admin recipient, `RefundProviderRef = transId` echo, `test` flag only on sandbox, canned carrier description, nested `Reason = ResolutionNotes` + `AcknowledgePostPayout: false` — all per ticket §C. |
| 12 | Open questions | Seed bug → real, Q-item recommended (above). Two-emails-on-resolve-Refunded → by design (draft HIGH-3.6), QA flagged for T-0118. Same-state NoOp precedence → implemented as MEDIUM-2 resolved (diagonal always Silent-Success, incl. Refunded/Disputed), pinned. |
| 13 | Verification re-run (by reviewer) | **PASS.** `dotnet build` 0 warnings / 0 errors; `Makables.Tests` 1510/1510; `Makables.IntegrationTests` 192/192; `tsc --noEmit` exit 0; `check-consistency` exit 0 at 125 tracked. Baseline diff = exactly +7: 6 T1 one-file wrappers + 1 T5 on `ChangeOrderStateManually.cs:99` (verified genuine false positive — every `Decision.Blocked` call site passes a `BusinessErrorMessage` constant). |
| 14 | NSwag | **PASS.** 3 hosts regenerated + `.spec-hashes.json` updated (public untouched); zero bare `export class Response` across all clients; admin client diff matches exactly the 4 new endpoints (refund / dispute / dispute/resolve / state). |

## AC traceability (34 ACs)

- **T-0105 (12):** AC-1→`POST_full_refund_as_admin_flips_state_writes_outbox_and_audit_row`; AC-2→OrderRefundTests accumulation pins + handler; AC-3→`Partial_then_over_refund_accumulates_then_blocks_without_second_provider_call`; AC-4→ack-gate domain pin + audit-notes marker; AC-5→handler Permanent-failure test (no mutation/outbox/audit); AC-6→Silent-Success handler test; AC-7→noProviderRef + invalidState pins; AC-8→outbox row + `IsEmailSend` + cs/en seeds; AC-9→audit assertions in e2e; AC-10→`Customer_JWT_and_anonymous_are_rejected_on_the_admin_host`; AC-11→migration `BIGINT NOT NULL DEFAULT 0`; AC-12→re-run green. **12/12.**
- **T-0106 (13):** AC-1→`Customer_POST_dispute_e2e…`; AC-2→OpenMakerDispute handler tests; AC-3→scoped-repo 404 pins; AC-4→rewritten state pins (red-first); AC-5→re-open Silent-Success + partial unique index; AC-6→`DisputeShipment_e2e_transitions_to_Disputed…` + carrierSourced grep clean; AC-7→category-gate validator pins; AC-8→`Admin_resolve_resumed_e2e…`; **AC-9→loud-409 leg covered; Refunded e2e leg MISSING (B-1)**; AC-10→Cancel-source pins both branches; AC-11→`Disputed_order_not_claimed_by_auto_deliver_or_carrier_sweeps`; AC-12→`Message_post_on_disputed_order_succeeds…`; AC-13→re-run green. **12/13.**
- **T-0107 (9):** AC-1→`POST_shipped_to_delivered_succeeds_and_audits` (+ AdminManual pin); AC-2→Cancel-source handler pin; AC-3→policy + RevertAcceptance pins; AC-4→providerRef branch pins; AC-5→blocked-code pins + `POST_paid_to_refunded_blocked_409_names_RefundOrder`; AC-6→diagonal NoOp pins; AC-7→reason validator + audience tests; AC-8→audit assertions; AC-9→re-run green. **9/9.**

## Gates

| Gate | Status |
|---|---|
| 1 — Ticket/DoR | PASS |
| 2 — Architecture/patterns | PASS (F-4 doc misalignment is the doc's defect, not the code's) |
| 3 — SecOps | **PENDING — mandatory** (all 3 tickets `security_touching`). Reviewer notes for SecOps: IDOR scoping verified; per-host audiences pinned; secret last-field/never-logged; dispute Description in outbox is locked §C.8 and not logged at Information. |
| 4 — Extension points | PASS (provider via `IPaymentProviderFactory.ResolveAsync`; no country branching; HTTP only in `Infra.Clients/Comgate`) |
| 5 — Tests/TDD | PASS (red commit f9eb0cb verified) |
| 6 — Contract parity | PASS |
| 7 — Docs | **FAIL → F-2, F-3** |
| 8 — Optimizer | **PENDING** — hot paths qualify (RefundOrder blocking external HTTP on request path; ResolveDispute multi-step nested pipeline). No gate-8 artifact exists for this bundle yet; policy matrix itself is pure, no concern. |
| 9 — Mechanical | PASS (re-run by reviewer; +7 baseline fully accounted) |

## Observations (no action required)

- **Handler collaborator counts** vs ADR 0015 "~5": `ResolveDispute.Handler` = 10, `RefundOrder.Handler` = 9, open variants = 9 (merged precedent `CancelExpiredOrder` = 7). The enrichment-at-enqueue block (users + languageResolver + publicAppUrls + outbox) recurs in every email-emitting handler — Architect may want to either exempt ambient deps (clock/logger/options) from the count or extract a notification-enqueue collaborator. Tracking as potential recurring finding (count 1 formally raised here).
- en-US dispute-resolved template uses `{{outcome}}` (raw enum) while cs-CZ uses `{{outcome_label}}` (Czech label); both keys are provided in the substitution map, so both render — cosmetic inconsistency only.
