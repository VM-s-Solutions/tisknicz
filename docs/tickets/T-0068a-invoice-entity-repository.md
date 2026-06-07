---
id: T-0068a
title: Invoice entity + IInvoiceRepository + InvoiceNumberGenerator TZ-aware-year migration
status: ready
size: M
owner: dotnet-db
created: 2026-06-06
updated: 2026-06-06
depends_on: [T-0011, T-0042, T-0060, T-0061, T-0062]
blocks: [T-0068b, T-0069, T-0101, T-0102]
user_stories: [US-customer-0010, US-customer-0017, US-admin-0012]
adrs: [0003, 0009, 0011, 0013, 0014]
phase: 4
manual_steps: [ef-migration]
security_touching: false
layers: [domain, infra, database]
---

# T-0068a — Invoice entity + IInvoiceRepository + InvoiceNumberGenerator TZ-aware-year migration

## Context

T-0068 (Invoice aggregate + QuestPDF renderer + service orchestration) was the first ticket to hit the new Definition-of-Ready L-split rule (`docs/process/ticket-lifecycle.md` §DoR item 3). It splits along the seam T-0060 and T-0062 already established for orders: entity + repository + number-generator-with-race-safety lands first, the service orchestrator + renderer + outbox enqueue lands second. T-0068a is the first half — a pure-domain + database PR that ships the Invoice aggregate, its EF mapping + migration, the scoped repository per ADR 0013, and migrates `IInvoiceNumberGenerator` to the TZ-aware-year pattern (the migration ADR 0009 explicitly deferred from T-0062 and named T-0068 as the migration ticket). No QuestPDF, no blob, no `IInvoiceService`, no `MarkOrderPaid` enqueue — those are T-0068b. This keeps slice (a) free of third-party licensing risk and lets TDD-with-commit-order apply cleanly against the `PostgresHarness` T-0062 already stood up.

Per the role doc (`docs/architecture/roles/invoice.md`): an Invoice knows its number (immutable, gap-free), type (Customer | Fee), issuer + recipient snapshots (immutable), line totals + VAT breakdown, currency, issue date, due date, and the blob path of its rendered PDF (set by T-0068b after upload — nullable at row creation, written once). An Invoice is never modified after issuance; errata flow through credit-notes (post-MVP). GDPR anonymises but does not delete. T-0068a honours this boundary by exposing zero state-machine methods and zero `UpdateAsync` repository entry point.

## Locked design decisions (from `/feature` deliberation)

Captured per `docs/process/deliberation.md`. The user answered four blocking AskUserQuestion items before this ticket transitioned to ready. These are non-negotiable for the implementing agent; revisiting requires a new ADR + a follow-up ticket.

1. **Legal seller on customer invoices = JVM YORE s.r.o. (Makables).** The platform is the legal issuer; makers appear as the product/service supplier in line items but are not invoice issuers. **Schema implication:** `issuer_name`, `issuer_ico`, `issuer_dic` columns hold static platform values (seeded via `CountryConfiguration`, NOT per-row varying). Numbering scope `FV-CZ-YYYY` is platform-wide (single global sequence per country per year), matching current ADR 0009. **Rejected alternative:** per-maker issuance with per-maker numbering — would have required `NumberingScope = (Invoice, MakerId)`, full ARES + per-maker DIČ + per-maker VAT status. Too much legal lift for MVP.
2. **JVM YORE s.r.o. is NOT VAT-registered (not plátce DPH).** **Implication:** the platform default `InvoicingMode` is `InvoicingMode.None` (Doklad o prodeji), not `StandardVat`. `issuer_dic` column is **nullable** at the schema level (so a future "JVM YORE just crossed 2M CZK threshold → registered for VAT" pivot can populate it without re-migration). `CountryConfiguration` for CZ seeds `DefaultInvoicingMode = None` and `IssuerVatId = NULL`. T-0068b's `IInvoiceService.IssueAsync` branches on this and renders the non-VAT-payer footer ("Nejsem plátce DPH") in the `None` template. **Rejected alternative:** assume VAT-registered → would have forced StandardVat template default + non-null `issuer_dic`. Too aggressive for a non-registered s.r.o.
3. **DUZP (datum uskutečnění zdanitelného plnění) = `Order.PaidAt`.** Payment is the marketplace's confirmation event; invoice issued immediately after `MarkOrderPaid` lands. **Implication:** `Invoice.TaxableSupplyDate` column type is `DateOnly?` (nullable for `InvoicingMode.None` rows where Czech law does not require a DUZP), populated by T-0068b's service from `order.PaidAt.DateOfDayInCountryTz(countryConfiguration.TimeZoneId)`. In the common case DUZP = `IssueDate`. **Rejected alternatives:** `Order.ShippedAt` (would defer invoice issuance to dispatch — couples invoice flow to shipping state machine) and `Order.DeliveredAt` (would defer invoice by 7–14 days — bad UX, customer expects invoice at payment confirmation).
4. **Numbering: single shared `FV-CZ-YYYY` sequence covers BOTH InvoicingMode.None receipts (Doklad o prodeji) AND StandardVat invoices.** **Implication:** only one `NumberingScope.Invoice` value exists; the T-0068a `InvoiceNumberGenerator` does not branch on mode. **Rejected alternative:** separate `NumberingScope.SaleDoc` for non-tax documents. Cleaner audit defensibility but adds enum value + seed row + branch logic for marginal benefit. § 29 of zákon o DPH does not mandate separate series at this volume.

**Open decisions absorbed by PM at draft→ready transition (not user-facing):**

- **`IInvoiceRepository.ForMaker` Fee-side join:** OMIT at T-0068a; XML-doc TODO referencing T-0101 (which migrates `payout_batches` table and widens the query). Avoids `NotImplementedException` stub per CLAUDE.md "no mocks during build phase".
- **§ 35 10-year retention policy:** comment-only TODO in `InvoiceConfiguration.cs` cross-referencing the Azure Blob lifecycle deploy ticket (TBD). Infrastructure-as-code is out of scope for T-0068a.
- **Czech translation keys for `BusinessErrorMessage.InvoiceBlobPathAlreadySet`:** defer to l10n during T-0068b batch (code surface in T-0068a is admin/logs only, not customer-facing).
- **`ix_invoices_order_id` partial unique `WHERE is_active`:** keep current shape. Soft-deleted invoices (GDPR anonymisation) free the slot for re-issuance; numbering stays gap-free because the new invoice gets a new number from the sequence.
- **`Invoice.AttachPdfBlobPath` set-once semantic:** idempotent same-value succeed, different-value fail. T-0068b's renderer must be deterministic enough that retry produces the same blob path (same invoice number + same blob layout → same path).

## Scope

### Domain

- **Invoice entity** at `backend/src/Makables.Core.Domain/Invoices/Invoice.cs`. Sealed; Auditable base. All properties `private set;`; mutation only through `Invoice.Issue(...)` static factory. Carries Identity (Id ULID, InvoiceNumber immutable allocated by IInvoiceNumberGenerator inside the issuing command's transaction per ADR 0009), Type (InvoiceType enum), Aggregate link (OrderId string? for Customer invoices XOR PayoutBatchId string? for Fee invoices — XOR enforced in factory; PayoutBatchId column ships here, FK added in T-0101), Issuer snapshot (IssuerName, IssuerTaxId IČO, IssuerVatId DIČ nullable, IssuerBankAccount nullable), Recipient snapshot (RecipientName, RecipientEmail, RecipientTaxId nullable, RecipientVatId nullable), Dates (IssueDate DateOnly country-local, TaxableSupplyDate DUZP DateOnly? nullable for InvoicingMode.None, DueDate DateOnly), Mode (InvoicingMode snapshot at issuance), Money per ADR 0003 (AmountWithoutVatMinor long, VatRateBp int, VatAmountMinor long, AmountWithVatMinor long, Currency CHAR(3); factory asserts breakdown balances; None mode requires zero VAT), PdfBlobPath string? nullable at row creation (T-0068b sets via Invoice.AttachPdfBlobPath set-once).
- **InvoiceType enum** at `backend/src/Makables.Core.Domain/Invoices/InvoiceType.cs`: Customer = 0, Fee = 1.
- **Invoice.Issue(...) static factory** — throws ArgumentException for impossible inputs; pattern mirror Order.Create (T-0060).
- **Invoice.AttachPdfBlobPath(string)** instance method — set-once with belt-and-braces guard matching Order.PaymentProviderRef pattern; returns BusinessResult.
- **IInvoiceRepository** at `backend/src/Makables.Core.Domain/Invoices/IInvoiceRepository.cs`: ForCustomer (joins via OrderId), ForMaker (joins via Order.MakerId; Fee-side join documented TODO with T-0101 cross-ref), Unscoped, GetByIdForCustomerAsync, GetByIdForMakerAsync, GetByIdUnscopedAsync (IgnoreQueryFilters), GetByInvoiceNumberAsync (Unscoped), GetByOrderIdAsync (Unscoped; supports T-0068b idempotency check), AddAsync. No UpdateAsync or DeleteAsync. IDOR-warning XML doc on every GetByIdFor* matching IOrderRepository pattern.

### Numbering — IInvoiceNumberGenerator TZ-aware migration

New signature mirrors T-0062's IOrderNumberGenerator: `Task<string> NextAsync(string countryCode, CancellationToken ct)`. InvoiceNumberGenerator impl at `backend/src/Makables.Infra.Database/Numbering/InvoiceNumberGenerator.cs` loads CountryConfiguration via existing ICountryConfigurationRepository.GetByCodeAsync, computes `nowInCountryTz = TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow.UtcDateTime, TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId))`, passes nowInCountryTz.Year to NumberingSequenceAllocator.AllocateAsync(NumberingScope.Invoice, year, ct), returns formatted `FV-{CC}-{YYYY}{NNNN}` per ADR 0009. Zero callers exist yet so signature change is risk-free.

### Infrastructure

- **InvoiceRepository** at `backend/src/Makables.Infra.Database/Invoices/InvoiceRepository.cs` — primary-ctor DI of MakablesDbContext. Soft-delete query filter automatic.
- **InvoiceConfiguration** at `backend/src/Makables.Infra.Database/Configurations/InvoiceConfiguration.cs`. Table `invoices`. Snake-case columns. InvoiceType + InvoicingMode as string via HasConversion. Indexes: unique on invoice_number partial WHERE is_active (`ix_invoices_invoice_number`); unique-partial on order_id WHERE order_id IS NOT NULL AND is_active (`ix_invoices_order_id`); composite (maker_id, created_at DESC) (`ix_invoices_maker_created`); single-column on type (`ix_invoices_type`). FK order_id → orders(id) ON DELETE RESTRICT. payout_batch_id column exists, FK added in T-0101.
- **EF migration** `<timestamp>_Invoices.cs`. Applies cleanly against both SQLite test harness AND postgres:16-alpine via PostgresHarness.MigrateAsync().
- **DI wiring** in AddMakablesInfrastructure.cs — IInvoiceRepository → InvoiceRepository (Scoped).
- **ADR 0009 amendment** noting invoice generator is now TZ-aware too.

### Tests

- `Makables.Tests/Domain/Invoices/InvoiceTests.cs` (~12 tests): Issue invariants (money balance, XOR aggregate link, currency length, blank inputs, None+zero-VAT, StandardVat happy path); AttachPdfBlobPath idempotency + set-once enforcement.
- `Makables.Tests/Domain/Numbering/InvoiceNumberGeneratorDelegationTests.cs` (1 test): forwards NumberingScope.Invoice and country-local year.
- `Makables.IntegrationTests/Numbering/InvoiceNumberGeneratorRaceSafetyTests.cs` (3 tests, reuses PostgresHarness): steady-state FOR UPDATE serialisation (pre-seeds FV-CZ-20260001), rollback safety, first-allocation race.
- `Makables.IntegrationTests/Numbering/InvoiceNumberGeneratorYearContractTests.cs` (2 tests): 23:30 Prague Dec-31 edge case; clean error when country missing.
- `Makables.IntegrationTests/Invoices/InvoiceRepositoryTests.cs` (6 tests): ForCustomer/ForMaker/Unscoped scoping, GetByInvoiceNumber, GetByOrderId, soft-delete exclusion.

### Docs

- `docs/architecture/roles/invoice.md` — add Implementation pointer section with shipped file paths; note PDF rendering + service in T-0068b.
- `docs/adr/0009-numbering.md` — amendment paragraph noting IInvoiceNumberGenerator is now TZ-aware.
- `docs/tickets/INDEX.md` — split T-0068 row into T-0068a + T-0068b; rewire T-0069 depends_on to T-0068b.

### NSwag regen

No public contract changes. No controllers ship in T-0068a.

## Alternatives Considered

- **Option A — Keep T-0068 as a single L ticket.** *Rejected because* the new DoR rule blocks any L ticket from transitioning to ready without a split, and T-0068 is the first ticket to hit this rule. The split also reduces blast radius — a failed merge of slice (a) does not pull QuestPDF licensing baggage with it.
- **Option B — Split into three slices (entity/numbering, QuestPDF + SPAYD renderer alone, service + outbox enqueue).** *Rejected because* slice 2 would ship a renderer with no consumer — testable only through a stub orchestrator that gets thrown away in slice 3. Violates no mocks during build phase rule and produces a slice that contributes no user-observable change.
- **Option C — Keep int year on IInvoiceNumberGenerator and defer TZ-aware migration.** *Rejected because* ADR 0009's T-0062 amendment explicitly names T-0068 as the migration ticket, and gap-free invoice numbering is a legal compliance issue. Zero callers exist yet so the interface migration is risk-free now; it gets harder once T-0068b wires the first caller.
- **Option D — Ship Fee-invoice repository methods (Fee-side join in ForMaker) now and have T-0101 reuse them.** *Rejected because* the payout_batches table does not exist yet and the FK cannot be created until T-0101 migrates it. Documenting the Fee-side as a TODO in ForMaker XML doc with T-0101 cross-ref is the honest seam.

## Out of scope

- IInvoiceService.IssueAsync orchestration — T-0068b.
- IPdfRenderer + QuestPdfInvoiceRenderer — T-0068b.
- Czech-glyph font embedding — T-0068b.
- SPAYD QR encoding — T-0068b.
- Blob upload (invoices/{cc}/orders/{orderId}/{invoiceNumber}.pdf) — T-0068b.
- OutboxEventTypes.InvoiceGenerate + InvoiceGenerateOutboxPayload — T-0068b.
- MarkOrderPaid.Handler third outbox enqueue — T-0068b.
- GenerateInvoice Function + dispatcher routing — T-0069.
- Customer / maker / admin PDF download endpoints — T-0068b (admin) and downstream.
- ReverseCharge + StrictFiscalReporting rendering — post-MVP.
- Fee invoices (InvoiceType.Fee rendering + PayoutBatch FK + Fee-side ForMaker join) — T-0101.
- Credit notes — post-MVP.
- Czech i18n keys for new BusinessErrorMessage codes — l10n during T-0068b.

## Acceptance criteria

- **AC-1** Given a call to Invoice.Issue(...) with money breakdown that does not balance (AmountWithoutVatMinor + VatAmountMinor != AmountWithVatMinor), when the factory runs, then it throws ArgumentException naming the failing invariant.
- **AC-2** Given a call to Invoice.Issue(...) with both OrderId and PayoutBatchId non-null, OR both null, when the factory runs, then it throws ArgumentException.
- **AC-3** Given an invoice with PdfBlobPath == null, when AttachPdfBlobPath("a") is called once, then it returns BusinessResult.Success and the property is set. A second call with the same value returns Success idempotently. A second call with a different value returns BusinessResult.Failure(InvoiceBlobPathAlreadySet).
- **AC-4** Given CZ country_configuration and a pre-seeded first allocation that consumed FV-CZ-20260001, when two InvoiceNumberGenerator.NextAsync("CZ", ct) calls run on independent IServiceScopes with Task.WhenAll, then the two returned numbers are contiguous ({FV-CZ-20260002, FV-CZ-20260003} modulo order) with zero duplicates.
- **AC-5** Given a transaction that calls NextAsync then throws, when the transaction rolls back, then the row in numbering_sequence for (CZ, Invoice, 2026) either does not exist or has last_used_value equal to its pre-call value.
- **AC-6** Given IClock returns 2026-12-31T22:30:00Z (23:30 Europe/Prague, still local 2026), when NextAsync("CZ", ct) runs, then the returned number contains 2026. Given IClock returns 2026-12-31T23:30:00Z (00:30 local 2027), then it contains 2027.
- **AC-7** Given the codebase, when it builds, then IInvoiceNumberGenerator.NextAsync(string, CancellationToken) is the only public method (int year parameter removed).
- **AC-8** Given two orders (one for customer A, one for customer B) with one invoice each, when IInvoiceRepository.ForCustomer(customerA.UserId) is materialised, then exactly one invoice (customer A's) is returned.
- **AC-9** Given an existing invoice with invoice_number = FV-CZ-20260001, when a second invoice with the same number is added and saved, then Postgres rejects with unique-constraint violation.
- **AC-10** Given an order with an existing active invoice, when a second invoice with the same order_id is added and saved, then Postgres rejects via the partial unique index ix_invoices_order_id.
- **AC-11** Given the EF migration Invoices is applied to an empty postgres:16-alpine container, when MigrateAsync() completes, then the invoices table exists with all columns + indexes per InvoiceConfiguration, and MigrateAsync() is idempotent on re-run.
- **AC-12** Build clean. Unit tests: baseline + 13 new methods (~19 effective xunit cases after Theory expansion). Integration tests: baseline + 11 new.
- **AC-13** Role doc, ADR 0009 amendment, and INDEX.md row split are all committed in the same PR.

## Technical notes

### Why the entity carries the issuer snapshot inline (not a value object)

Matches the Order pattern from T-0060 (ContactName/Email/Phone inline). Inlining keeps EF mapping flat, migration simple, Postgres query plan obvious.

### Why InvoicingMode is captured per-row

Invoices are legal documents; mode at issuance time is the authoritative record. If CountryConfiguration.DefaultInvoicingMode changes post-launch, an audit query must still surface what mode the document was issued under. Snapshotting follows the same logic the pricing snapshot uses in Order (T-0060).

### Why no UpdateAsync on IInvoiceRepository

Per role doc: invoices never modified post-issuance. The only mutation is AttachPdfBlobPath (set-once), which EF change-tracking picks up automatically. Omitting prevents accidental mass-update use cases.

### Why the payout_batch_id column ships here with no FK

Deferring would mean two migrations touching the same table. Adding the column now with a documented FK-added-in-T-0101 comment is the cleanest seam. The XOR factory invariant prevents payout_batch_id from being set in any T-0068a or T-0068b code path.

### Why PostgresHarness reuse

T-0062 stood up PostgresHarness + ICollectionFixture so race-sensitive tests would not pay container-startup cost N times. T-0068a's race tests join the same xunit collection. ResetMutableTablesAsync already TRUNCATEs numbering_sequence + orders; it needs an append of invoices. Per T-0062 L-2 the method name signposts this extension expectation.

### Why dotnet-db is the owner

Pure-domain + DB slice: entity, EF configuration, migration, repository, numbering interface migration. No commands, no controllers, no AppServices. Matches routing rules in docs/process/ticket-lifecycle.md §Workflow step 4.

## Manual deployment steps

One EF migration (Invoices) — applied automatically by the dev startup flow per Program.cs migration hook. No manual data-fix step. **Rollback:** `dotnet ef database update <PreviousMigration>` reverses cleanly because no data has been written yet (T-0068b is the first writer).

## Files touched (expected)

- `backend/src/Makables.Core.Domain/Invoices/Invoice.cs` (new)
- `backend/src/Makables.Core.Domain/Invoices/InvoiceType.cs` (new)
- `backend/src/Makables.Core.Domain/Invoices/IInvoiceRepository.cs` (new)
- `backend/src/Makables.Core.Domain/Numbering/IInvoiceNumberGenerator.cs` (signature change)
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` (add InvoiceBlobPathAlreadySet)
- `backend/src/Makables.Infra.Database/Invoices/InvoiceRepository.cs` (new)
- `backend/src/Makables.Infra.Database/Configurations/InvoiceConfiguration.cs` (new)
- `backend/src/Makables.Infra.Database/Numbering/InvoiceNumberGenerator.cs` (impl change)
- `backend/src/Makables.Infra.Database/Migrations/<timestamp>_Invoices.cs` (new, auto-generated)
- `backend/src/Makables.Infra.Database/MakablesDbContext.cs` (add DbSet<Invoice>)
- `backend/src/Makables.Config/Extensions/AddMakablesInfrastructure.cs` (DI registration)
- `backend/src/Makables.IntegrationTests/Common/PostgresHarness.cs` (extend ResetMutableTablesAsync)
- `backend/src/Makables.Tests/Domain/Invoices/InvoiceTests.cs` (new, ~12 tests)
- `backend/src/Makables.Tests/Domain/Numbering/InvoiceNumberGeneratorDelegationTests.cs` (new, 1 test)
- `backend/src/Makables.IntegrationTests/Numbering/InvoiceNumberGeneratorRaceSafetyTests.cs` (new, 3 tests)
- `backend/src/Makables.IntegrationTests/Numbering/InvoiceNumberGeneratorYearContractTests.cs` (new, 2 tests)
- `backend/src/Makables.IntegrationTests/Invoices/InvoiceRepositoryTests.cs` (new, 6 tests)
- `docs/architecture/roles/invoice.md` (Implementation pointer section)
- `docs/adr/0009-numbering.md` (amendment paragraph)
- `docs/tickets/INDEX.md` (split T-0068 row; rewire T-0069 dep)

## Test plan reference

Inline above (see Scope > Tests). Reuses PostgresHarness from T-0062; no separate docs/test-plans/T-0068a.md file.

## Status log

- 2026-06-06 `draft` by PM. Created as part of T-0068 L-split (the first ticket to hit the new DoR L-split rule per docs/process/ticket-lifecycle.md §DoR item 3). Sister ticket: T-0068b. Open user decisions documented in the splitting PM's report.
- 2026-06-06 `draft → ready` by PM. User answered 4 blocking decisions via AskUserQuestion per `/feature` workflow step 3 (JVM YORE as legal seller; not VAT-registered → InvoicingMode.None default; DUZP = Order.PaidAt; single shared FV-CZ-YYYY sequence). Decisions captured in `## Locked design decisions` section. Five non-user-facing open decisions absorbed by PM with documented kill reasons. Ready for dotnet-db.
- 2026-06-06 `ready → in_progress` by PM. Branch `feat/T-0068a-invoice-entity-repository`. dotnet-db owner; reviewer parallel-draft per `docs/process/routing.md`.
- 2026-06-06 `in_progress → in_review` by dotnet-db. 8 commits landed (1 ticket + 7 feat/test/docs). TDD commit order verified: `0f2fdf1 test:(red)` precedes `2cba200 feat:(green)` for Invoice entity per `docs/process/tdd-policy.md`. Build clean (0/0). Unit tests: 1082 → 1102 (+20). Integration tests: 133 → 144 (+11). `node scripts/check-consistency.mjs` exit 0 (clean, 100 tracked).
- 2026-06-06 Reviewer final-pass verdict: **APPROVE_WITH_NITS**. Gate 8 (Optimizer): GATE8_NA (no controllers); 6 perf checks PASS + 4 advisory nits for T-0068b. Gate 9 (Mechanical): GATE9_PASS. Folded inline before PR: (1) `docs/process/must-cover-tests.md` §11 set-once table row added for `Invoice.PdfBlobPath`; (2) `InvoiceRepositoryTests.ForMaker_returns_*` test method renamed to match its body; (3) `UniqueConstraintTranslator` intentionally-unmapped comment block extended with ix_invoices_invoice_number + ix_invoices_order_id rationale; (4) § 35 retention TODO sharpened with named owner (blob-storage role doc maintainer) per CLAUDE.md "no TODO without owner"; (5) AC-12 amended to "13 methods (~19 effective xunit cases)".
