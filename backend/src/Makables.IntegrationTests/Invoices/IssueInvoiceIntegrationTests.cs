using FluentAssertions;
using Makables.Core.AppServices.Features.Invoices;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Storage;
using Makables.Infra.Common.Identifiers;
using Makables.Infra.Common.Time;
using Makables.Infra.Database;
using Makables.Infra.Database.Interceptors;
using Makables.Infra.Database.Invoices;
using Makables.Infra.Database.Numbering;
using Makables.Infra.Database.Orders;
using Makables.Infra.Database.Repositories;
using Makables.Infra.PdfRendering;
using Makables.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.IntegrationTests.Invoices;

/// <summary>
/// End-to-end coverage for T-0068b's <see cref="IssueInvoice.Handler"/>
/// against real Postgres (<see cref="PostgresHarness"/>) +
/// <see cref="FakeBlobStorageClient"/> + the real QuestPDF renderer.
/// Exercises the full happy path (invoice row persisted, blob uploaded,
/// blob path attached), the idempotent re-issue (no double-upload),
/// and the blob-upload-failure rollback shape.
///
/// <para>
/// Unit-level coverage of the handler is in
/// <c>IssueInvoiceHandlerTests</c> (NSubstitute mocks). This file pins
/// the wiring between the handler and the Postgres-backed
/// <see cref="InvoiceRepository"/> + <c>InvoiceNumberGenerator</c>.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IssueInvoiceIntegrationTests
{
    private const string CountryCode = "CZ";
    private const string OrderId = "ord-issueinvoice-1";
    private const string MakerId = "maker-issueinvoice-1";
    private const string CustomerUserId = "user-issueinvoice-1";

    private static readonly DateTimeOffset Now =
        new(2026, 6, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PaidAt =
        new(2026, 6, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;

    public IssueInvoiceIntegrationTests(PostgresHarness harness)
    {
        _harness = harness;
        _harness.ResetMutableTablesAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task End_to_end_happy_path_persists_invoice_and_uploads_PDF()
    {
        await SeedPaidOrderAsync();

        var blob = new FakeBlobStorageClient();
        var (sut, _) = BuildHandler(blob);

        var result = await sut.Handle(
            new IssueInvoice.Command(OrderId), CancellationToken.None);

        // Commit the UoW manually — in production the
        // UnitOfWorkPipelineBehavior calls SaveChangesAsync; in this
        // direct-dispatch test we own the commit.
        await CommitAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.PdfBlobPath.Should().StartWith($"cz/orders/{OrderId}/");
        result.Value.PdfBlobPath.Should().EndWith(".pdf");

        // Postgres round-trip
        await using var db = _harness.CreateDbContext();
        var persisted = await db.Set<Invoice>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.OrderId == OrderId);

        persisted.Should().NotBeNull();
        persisted!.InvoiceNumber.Should().Be(result.Value.InvoiceNumber);
        persisted.PdfBlobPath.Should().Be(result.Value.PdfBlobPath);
        // The CZ seed's ARES identity, snapshotted onto the row.
        persisted.IssuerName.Should().Be("JVM Yore, s.r.o.");
        persisted.IssuerIco.Should().Be("29633443");
        persisted.IssuerAddress.Should().Be("Příčná 1892/4, Nové Město, 110 00 Praha 1");
        persisted.InvoicingMode.Should().Be(InvoicingMode.None);
        // Settled at issuance — the outbox row this handler runs off is only
        // enqueued by MarkOrderPaid, so the document is a receipt.
        persisted.PaidOn.Should().NotBeNull();
        persisted.PaymentMethod.Should().NotBeNullOrWhiteSpace();
        persisted.AmountWithVatMinor.Should().Be(57_900);
        persisted.Currency.Should().Be("CZK");

        // Blob round-trip
        blob.Store.Should().ContainKey($"{BlobContainer.Invoices}/{result.Value.PdfBlobPath}");
        var stored = blob.Store[$"{BlobContainer.Invoices}/{result.Value.PdfBlobPath}"];
        stored.ContentType.Should().Be("application/pdf");
        stored.Bytes.Should().NotBeEmpty();
        // PDF magic header
        stored.Bytes[0].Should().Be(0x25);
        stored.Bytes[1].Should().Be(0x50);
    }

    [Fact]
    public async Task Idempotent_retry_returns_existing_and_does_not_double_upload()
    {
        await SeedPaidOrderAsync();

        var blob = new FakeBlobStorageClient();
        var (sut1, _) = BuildHandler(blob);

        var first = await sut1.Handle(
            new IssueInvoice.Command(OrderId), CancellationToken.None);
        await CommitAsync();
        first.IsSuccess.Should().BeTrue();
        var firstUploadCount = blob.Store.Count;
        firstUploadCount.Should().Be(1);

        // Second dispatch — webhook re-delivery shape. New handler scope
        // (fresh DbContext) to mirror the production isolation between
        // outbox-processor invocations.
        var (sut2, _) = BuildHandler(blob);

        var second = await sut2.Handle(
            new IssueInvoice.Command(OrderId), CancellationToken.None);
        await CommitAsync();

        second.IsSuccess.Should().BeTrue();
        second.Value!.InvoiceId.Should().Be(first.Value!.InvoiceId);
        second.Value.InvoiceNumber.Should().Be(first.Value.InvoiceNumber);
        second.Value.PdfBlobPath.Should().Be(first.Value.PdfBlobPath);

        blob.Store.Count.Should().Be(1,
            because: "an idempotent retry must not re-upload — the existing " +
                     "invoice's PdfBlobPath short-circuits before render + upload");
    }

    [Fact]
    public async Task Blob_upload_failure_does_not_leave_orphan_blob()
    {
        await SeedPaidOrderAsync();

        // Throwing blob client — handler must translate to Transient
        // InvoiceBlobUploadFailed and the new Invoice row will exist
        // (it was added before the upload), but PdfBlobPath stays null
        // so the next sweep recovers via the partial-issuance path.
        var blob = new ThrowingBlobStorageClient();
        var (sut, _) = BuildHandler(blob);

        var result = await sut.Handle(
            new IssueInvoice.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.InvoiceBlobUploadFailed);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    // === Helpers ===

    private MakablesDbContextScope _scope = default!;

    /// <summary>
    /// Wire up the handler against a single MakablesDbContext that all
    /// the repositories + generators share — same scope semantics as the
    /// production request pipeline.
    /// </summary>
    private (IssueInvoice.Handler Handler, Makables.Infra.Database.MakablesDbContext Db) BuildHandler(IBlobStorageClient blob)
    {
        var clock = new SystemClock();
        // Construct a DbContext with the AuditableSaveChangesInterceptor
        // wired so the new Invoice row's audit columns are populated
        // (mirrors production wiring; the unit tests skip the interceptor
        // because they don't hit Postgres).
        var session = Substitute.For<IUserSessionProvider>();
        session.GetUserId().Returns("test-user");
        var interceptor = new AuditableSaveChangesInterceptor(session, clock);
        var options = new DbContextOptionsBuilder<MakablesDbContext>()
            .UseNpgsql(_harness.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        var db = new MakablesDbContext(options);
        _scope = new MakablesDbContextScope(db);
        var orders = new OrderRepository(db);
        var countries = new CountryConfigurationRepository(db);
        var invoices = new InvoiceRepository(db);
        var numberGen = new InvoiceNumberGenerator(db, clock, countries);
        var renderer = new QuestPdfInvoiceRenderer();
        var ids = new UlidIdGenerator();

        var handler = new IssueInvoice.Handler(
            orders, countries, invoices, numberGen,
            renderer, blob, ids, clock,
            NullLogger<IssueInvoice.Handler>.Instance);

        return (handler, db);
    }

    private async Task CommitAsync()
    {
        await _scope.Db.SaveChangesAsync();
    }

    /// <summary>
    /// Seed a paid order matching the test constants. CZ country
    /// configuration is already in the DB courtesy of the initial seed
    /// migration + the T-0068b AddCountryConfigurationIssuerAndIban
    /// migration (JVM YORE s.r.o. / 00000000 / null / null).
    /// </summary>
    private async Task SeedPaidOrderAsync()
    {
        await using var db = _harness.CreateDbContext();

        var order = Order.Create(
            id: OrderId,
            orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId,
            makerId: MakerId,
            productId: "prod-1",
            contactName: "Anna Nováková",
            contactEmail: "anna@example.cz",
            contactPhone: "+420700000000",
            productPriceAmountMinor: 50_000,
            shippingPriceAmountMinor: 7_900,
            platformFeeAmountMinor: 7_500,
            makerPayoutAmountMinor: 50_400,
            totalAmountMinor: 57_900,
            currency: "CZK",
            vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: CountryCode);
        order.MarkCreated("test-seed", Now);

        // Transition to Paid. Need to first reserve a payment session
        // (PendingPayment requires PaymentProviderRef before MarkAsPaid),
        // actually MarkAsPaid sets it directly. State machine: Created
        // doesn't exist — Order.Create returns PendingPayment.
        var clock = new SystemClock();
        // Override Now via FakeClock not available — SystemClock returns
        // real UtcNow. For seeding we set PaidAt via MarkAsPaid override.
        var transition = order.MarkAsPaid(
            clock,
            paymentProviderRef: "comgate-tx-test",
            paymentMethod: "CARD_CZ",
            paidAtOverride: PaidAt);
        transition.IsSuccess.Should().BeTrue();

        db.Set<Order>().Add(order);
        await db.SaveChangesAsync();
    }

    /// <summary>Tracks the active DbContext for commit hand-off.</summary>
    private sealed class MakablesDbContextScope(Makables.Infra.Database.MakablesDbContext db)
    {
        public Makables.Infra.Database.MakablesDbContext Db => db;
    }

    /// <summary>
    /// IBlobStorageClient that throws on every UploadAsync — exercises
    /// the handler's try/catch translation to Transient
    /// InvoiceBlobUploadFailed.
    /// </summary>
    private sealed class ThrowingBlobStorageClient : IBlobStorageClient
    {
        public Task<BusinessResult> UploadAsync(
            string container, string path, Stream content, string contentType,
            CancellationToken cancellationToken) =>
            throw new IOException("simulated network blip");

        public Task<BusinessResult<BlobDownload>> DownloadAsync(
            string container, string path, CancellationToken cancellationToken) =>
            Task.FromResult(BusinessResult.Failure<BlobDownload>(
                Error.NotFound("blob", BusinessErrorMessage.BlobNotFound)));

        public Task<BusinessResult> DeleteAsync(
            string container, string path, CancellationToken cancellationToken) =>
            Task.FromResult(BusinessResult.Success());

        public Task<BusinessResult<bool>> ExistsAsync(
            string container, string path, CancellationToken cancellationToken) =>
            Task.FromResult(BusinessResult.Success(false));
    }
}
