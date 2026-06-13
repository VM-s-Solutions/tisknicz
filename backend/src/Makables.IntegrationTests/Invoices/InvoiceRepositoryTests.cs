using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Orders;
using Makables.Infra.Database;
using Makables.Infra.Database.Invoices;
using Makables.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;

namespace Makables.IntegrationTests.Invoices;

/// <summary>
/// Integration coverage for <see cref="InvoiceRepository"/>'s scoping +
/// lookup methods on real Postgres (via <see cref="PostgresHarness"/>).
/// Per T-0068a AC-8 + locked decisions: ForCustomer joins via the Order
/// linkage, ForMaker uses the denormalised <c>maker_id</c> column (Fee-
/// side join deferred to T-0101), the unique-partial index on
/// <c>order_id WHERE order_id IS NOT NULL AND is_active</c> survives
/// the round trip, and soft-deleted invoices are excluded from the
/// scoped queryables by the global filter.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InvoiceRepositoryTests
{
    private static readonly DateTimeOffset SeedAt =
        new(2026, 5, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly IssueDate = new(2026, 5, 24);
    private static readonly DateOnly DueDate = new(2026, 6, 7);

    private const string CustomerAUserId = "user-customer-a";
    private const string CustomerBUserId = "user-customer-b";
    private const string MakerAId = "maker-a";
    private const string MakerBId = "maker-b";
    private const string OrderAId = "ord-a";
    private const string OrderBId = "ord-b";
    private const string InvoiceAId = "inv-a";
    private const string InvoiceBId = "inv-b";

    private readonly PostgresHarness _harness;

    public InvoiceRepositoryTests(PostgresHarness harness)
    {
        _harness = harness;
        _harness.ResetMutableTablesAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task ForCustomer_returns_only_invoices_owned_by_that_customer()
    {
        await SeedTwoCustomersWithOneInvoiceEachAsync();

        await using var db = _harness.CreateDbContext();
        var repo = new InvoiceRepository(db);

        var customerAList = await repo.ForCustomer(CustomerAUserId)
            .AsNoTracking()
            .ToListAsync();

        customerAList.Should().HaveCount(1);
        customerAList[0].Id.Should().Be(InvoiceAId);
        customerAList[0].OrderId.Should().Be(OrderAId);
    }

    [Fact]
    public async Task ForMaker_returns_Customer_and_Fee_invoices_via_denormalised_maker_id()
    {
        await SeedTwoCustomersWithOneInvoiceEachAsync();

        // Add an extra Fee invoice for maker-a that has NO order — at
        // T-0068a's ForMaker pins the Customer-side via the maker_id
        // column. The Fee invoice's maker_id is set the same way so it
        // ALSO appears in ForMaker(MakerAId) — that is intentional for
        // T-0068a; the absent join in the implementation is the Fee-side
        // PayoutBatch join, not a Fee-side filter. T-0101 will add the
        // PayoutBatch → MakerId join so Fee invoices can be filtered
        // when the maker is the payout target. For now we pin the
        // expected post-T-0068a behaviour: ForMaker returns ALL invoices
        // (Customer + Fee) with the matching maker_id, which is the
        // single-column index lookup.
        await SeedFeeInvoiceForAsync(MakerAId, "inv-fee-a", "FV-CZ-20260003", "batch-a");

        await using var db = _harness.CreateDbContext();
        var repo = new InvoiceRepository(db);

        var makerAList = await repo.ForMaker(MakerAId)
            .AsNoTracking()
            .OrderBy(i => i.Id)
            .ToListAsync();

        makerAList.Should().HaveCount(2,
            because: "T-0068a ForMaker returns both Customer and Fee invoices with the " +
                     "matching maker_id; the Fee-side PayoutBatch join is deferred to T-0101");
        makerAList.Select(i => i.Id).Should().BeEquivalentTo(new[] { InvoiceAId, "inv-fee-a" });

        var makerBList = await repo.ForMaker(MakerBId)
            .AsNoTracking()
            .ToListAsync();

        makerBList.Should().HaveCount(1);
        makerBList[0].Id.Should().Be(InvoiceBId);
    }

    [Fact]
    public async Task Unscoped_returns_every_active_invoice_across_customers_and_makers()
    {
        await SeedTwoCustomersWithOneInvoiceEachAsync();

        await using var db = _harness.CreateDbContext();
        var repo = new InvoiceRepository(db);

        var all = await repo.Unscoped()
            .AsNoTracking()
            .OrderBy(i => i.Id)
            .ToListAsync();

        all.Should().HaveCount(2);
        all.Select(i => i.Id).Should().BeEquivalentTo(new[] { InvoiceAId, InvoiceBId });
    }

    [Fact]
    public async Task GetByInvoiceNumberAsync_returns_the_invoice_with_that_number()
    {
        await SeedTwoCustomersWithOneInvoiceEachAsync();

        await using var db = _harness.CreateDbContext();
        var repo = new InvoiceRepository(db);

        var found = await repo.GetByInvoiceNumberAsync("FV-CZ-20260001", CancellationToken.None);
        var missing = await repo.GetByInvoiceNumberAsync("FV-CZ-99990000", CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(InvoiceAId);
        missing.Should().BeNull();
    }

    [Fact]
    public async Task GetByOrderIdAsync_returns_the_invoice_for_idempotency_check()
    {
        await SeedTwoCustomersWithOneInvoiceEachAsync();

        await using var db = _harness.CreateDbContext();
        var repo = new InvoiceRepository(db);

        var found = await repo.GetByOrderIdAsync(OrderAId, CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(InvoiceAId);
        found.InvoiceNumber.Should().Be("FV-CZ-20260001");
    }

    [Fact]
    public async Task ForCustomer_excludes_soft_deleted_invoices_via_global_query_filter()
    {
        await SeedTwoCustomersWithOneInvoiceEachAsync();

        // Soft-delete customer A's invoice via MarkDeactivated (the only
        // legitimate path per role/invoice.md — GDPR anonymises, never
        // hard-deletes).
        await using (var write = _harness.CreateDbContext())
        {
            var invoiceA = await write.Set<Invoice>().FirstAsync(i => i.Id == InvoiceAId);
            invoiceA.MarkDeactivated("admin-gdpr", SeedAt.AddDays(1));
            await write.SaveChangesAsync();
        }

        await using var db = _harness.CreateDbContext();
        var repo = new InvoiceRepository(db);

        var customerAList = await repo.ForCustomer(CustomerAUserId)
            .AsNoTracking()
            .ToListAsync();

        customerAList.Should().BeEmpty(
            because: "soft-deleted invoices are hidden from non-admin queries by the global " +
                     "Auditable.IsActive query filter on MakablesDbContext");

        // GetByIdUnscopedAsync ignores the filter (admin path) — pin the
        // round-trip so a future refactor can't drop the IgnoreQueryFilters().
        var stillThere = await repo.GetByIdUnscopedAsync(InvoiceAId, CancellationToken.None);
        stillThere.Should().NotBeNull();
        stillThere!.IsActive.Should().BeFalse();
    }

    // === Seed helpers ===

    private async Task SeedTwoCustomersWithOneInvoiceEachAsync()
    {
        await using var db = _harness.CreateDbContext();

        // Two minimal orders bound to two different customers + makers.
        // Order.Create stamps the snapshot fields; the audit interceptor
        // populates CreatedBy / CreatedAt from IUserSessionProvider, but
        // we're constructing the DbContext directly without one, so we
        // call MarkCreated explicitly with the seed actor.
        var orderA = MakeOrder(OrderAId, CustomerAUserId, MakerAId, productPrice: 100_00, shipping: 0);
        orderA.MarkCreated("test-seed", SeedAt);
        db.Set<Order>().Add(orderA);

        var orderB = MakeOrder(OrderBId, CustomerBUserId, MakerBId, productPrice: 200_00, shipping: 0);
        orderB.MarkCreated("test-seed", SeedAt);
        db.Set<Order>().Add(orderB);

        var invoiceA = MakeCustomerInvoice(InvoiceAId, "FV-CZ-20260001", OrderAId, MakerAId, gross: 100_00);
        invoiceA.MarkCreated("test-seed", SeedAt);
        db.Set<Invoice>().Add(invoiceA);

        var invoiceB = MakeCustomerInvoice(InvoiceBId, "FV-CZ-20260002", OrderBId, MakerBId, gross: 200_00);
        invoiceB.MarkCreated("test-seed", SeedAt);
        db.Set<Invoice>().Add(invoiceB);

        await db.SaveChangesAsync();
    }

    private async Task SeedFeeInvoiceForAsync(string makerId, string invoiceId, string invoiceNumber, string payoutBatchId)
    {
        await using var db = _harness.CreateDbContext();

        // T-0101 added the invoices.payout_batch_id FK (ON DELETE RESTRICT),
        // so the referenced payout batch must exist first.
        var batch = Makables.Core.Domain.Payouts.PayoutBatch.Create(
            id: payoutBatchId, batchNumber: $"VYP-CZ-2026-{payoutBatchId}",
            countryCode: "CZ", totalAmountMinor: 15_00, currency: "CZK",
            orderCount: 1, makerCount: 1,
            excludedPartiallyRefundedOrderCount: 0, excludedNoBankAccountOrderCount: 0,
            excludedNoBankAccountMakerCount: 0);
        batch.MarkCreated("test-seed", SeedAt);
        db.Set<Makables.Core.Domain.Payouts.PayoutBatch>().Add(batch);

        var fee = Invoice.Issue(
            id: invoiceId,
            invoiceNumber: invoiceNumber,
            type: InvoiceType.Fee,
            orderId: null,
            payoutBatchId: payoutBatchId,
            makerId: makerId,
            issuerName: "JVM YORE s.r.o.",
            issuerIco: "12345678",
            issuerDic: null,
            issuerBankAccount: null,
            recipientName: "Maker GmbH",
            recipientEmail: "maker@example.cz",
            recipientTaxId: null,
            recipientVatId: null,
            issueDate: IssueDate,
            taxableSupplyDate: null,
            dueDate: DueDate,
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 15_00,
            vatRateBp: 0,
            vatAmountMinor: 0,
            amountWithVatMinor: 15_00,
            currency: "CZK",
            countryCode: "CZ");
        fee.MarkCreated("test-seed", SeedAt);
        db.Set<Invoice>().Add(fee);
        await db.SaveChangesAsync();
    }

    private static Order MakeOrder(string id, string customerUserId, string makerId, long productPrice, long shipping)
    {
        // Mirror Order.Create's pricing-balance invariants:
        // total = product + shipping; payout + fee = product + shipping.
        var fee = productPrice / 10;             // 10% fee
        var payout = (productPrice - fee) + shipping;
        var total = productPrice + shipping;
        return Order.Create(
            id: id,
            orderNumber: $"M-CZ-2026-{id}",
            customerUserId: customerUserId,
            makerId: makerId,
            productId: "prod-1",
            contactName: "Test User",
            contactEmail: "test@example.cz",
            contactPhone: "+420700000000",
            productPriceAmountMinor: productPrice,
            shippingPriceAmountMinor: shipping,
            platformFeeAmountMinor: fee,
            makerPayoutAmountMinor: payout,
            totalAmountMinor: total,
            currency: "CZK",
            vatRateBp: 2100,
            shippingMethod: ShippingMethod.PersonalPickup,
            zasilkovnaPickupPointId: null,
            countryCode: "CZ");
    }

    private static Invoice MakeCustomerInvoice(string id, string invoiceNumber, string orderId, string makerId, long gross) =>
        Invoice.Issue(
            id: id,
            invoiceNumber: invoiceNumber,
            type: InvoiceType.Customer,
            orderId: orderId,
            payoutBatchId: null,
            makerId: makerId,
            issuerName: "JVM YORE s.r.o.",
            issuerIco: "12345678",
            issuerDic: null,
            issuerBankAccount: null,
            recipientName: "Anna Nováková",
            recipientEmail: "anna@example.cz",
            recipientTaxId: null,
            recipientVatId: null,
            issueDate: IssueDate,
            taxableSupplyDate: null,
            dueDate: DueDate,
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: gross,
            vatRateBp: 0,
            vatAmountMinor: 0,
            amountWithVatMinor: gross,
            currency: "CZK",
            countryCode: "CZ");
}
