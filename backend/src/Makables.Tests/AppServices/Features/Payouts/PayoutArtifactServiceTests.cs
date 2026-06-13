using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Payouts;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Numbering;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payouts;
using Makables.Core.Domain.Rendering;
using Makables.Core.Domain.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Payouts;

/// <summary>
/// T-0102b <see cref="PayoutArtifactService"/> contract: per-maker Fee
/// invoice math (PlatformFee sums, None mode ⇒ zero VAT, DUZP = batch
/// local date), re-run skipping complete makers (Received(0) renderer),
/// and the mid-pipeline failure posture (Complete == false, earlier work
/// persisted, CSV not attempted).
/// </summary>
public class PayoutArtifactServiceTests
{
    private static readonly DateTimeOffset BatchCreatedAt = DateTimeOffset.Parse("2026-06-15T08:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IInvoiceNumberGenerator _invoiceNumbers = Substitute.For<IInvoiceNumberGenerator>();
    private readonly IInvoicePdfRenderer _renderer = Substitute.For<IInvoicePdfRenderer>();
    private readonly IBlobStorageClient _blobs = Substitute.For<IBlobStorageClient>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ICountryConfigurationRepository _countries = Substitute.For<ICountryConfigurationRepository>();
    private readonly IPayoutCsvFormatter _csvFormatter = new GenericPayoutCsvFormatter();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly PayoutArtifactService _sut;

    public PayoutArtifactServiceTests()
    {
        _ids.Next().Returns(_ => Guid.NewGuid().ToString("N"));
        _countries.GetByCodeAsync("CZ", Arg.Any<CancellationToken>()).Returns(BuildConfig());
        _invoiceNumbers.NextAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(_ => $"FV-CZ-2026{Random.Shared.Next(1000, 9999)}");
        _renderer.RenderFeeAsync(Arg.Any<Invoice>(), Arg.Any<IReadOnlyList<FeeInvoiceLineItem>>(),
                Arg.Any<CountryConfiguration>(), Arg.Any<CancellationToken>())
            .Returns([0x25, 0x50, 0x44, 0x46]); // "%PDF"
        _blobs.UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success());
        _languageResolver.ResolveForUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns("cs-CZ");

        var urls = Options.Create(new PublicAppUrlsOptions { WebBaseUrl = "https://makables.test" });

        _sut = new PayoutArtifactService(
            _orders, _invoices, _invoiceNumbers, _renderer, _blobs, _makers, _users, _countries,
            _csvFormatter, _outbox, _languageResolver, _ids, urls,
            NullLogger<PayoutArtifactService>.Instance);
    }

    private static CountryConfiguration BuildConfig() => CountryConfiguration.Create(
        countryId: "CZ", defaultCurrencyCode: "CZK", defaultLanguageCode: "cs-CZ",
        timeZoneId: "Europe/Prague", phonePrefix: "+420", dateFormat: "d. M. yyyy",
        standardVatRateBp: 2100, taxIdLabel: "DIČ", vatIdLabel: "DIČ", registrationNumberLabel: "IČO",
        defaultPaymentProvider: "comgate", defaultShippingCarrier: "packeta",
        defaultRegistry: "ares", defaultEmailProvider: "sendgrid",
        issuerName: "JVM YORE s.r.o.", issuerIco: "00000000",
        invoicingMode: InvoicingMode.None);

    private static PayoutBatch BuildBatch(string id = "pb-1")
    {
        var batch = PayoutBatch.Create(id, "VYP-CZ-2026-W25", "CZ", 30000, "CZK", 3, 2, 0, 0, 0);
        batch.MarkCreated("admin-1", BatchCreatedAt);
        return batch;
    }

    private static Order ClaimedOrder(string id, string makerId, string batchId, long product, long fee)
    {
        var payout = product - fee;
        var o = Order.Create(
            id: id, orderNumber: $"M-CZ-{id}",
            customerUserId: "user-1", makerId: makerId, productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: product, shippingPriceAmountMinor: 0,
            platformFeeAmountMinor: fee, makerPayoutAmountMinor: payout,
            totalAmountMinor: product, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-1", countryCode: "CZ");
        var c = Substitute.For<IClock>();
        c.UtcNow.Returns(BatchCreatedAt.AddDays(-5));
        o.MarkAsPaid(c, $"tx-{id}");
        o.Accept(c);
        o.Ship(c, $"PKT-{id}", 7);
        o.MarkAsDelivered(c, OrderDeliverySource.Auto);
        o.AssignToPayoutBatch(batchId);
        return o;
    }

    private Makables.Core.Domain.Makers.Maker SetupMaker(string id, string company, string bank, string email)
    {
        var address = "addr-1";
        var maker = Makables.Core.Domain.Makers.Maker.Create(
            id: id, userId: $"u-{id}", registrationNumber: "12345678", vatId: null,
            companyName: company, legalForm: "s.r.o.", registeredAddressId: address,
            incorporatedOn: null, isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: BatchCreatedAt, snapshotIsStale: false, countryCode: "CZ");
        maker.UpdateProfile(bio: null, bankAccount: bank, personalPickupEnabled: null, pickupNote: null);
        _makers.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(maker);

        var user = User.Create($"u-{id}", email, UserRole.Maker, company, "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        _users.GetByIdAsync($"u-{id}", Arg.Any<CancellationToken>()).Returns(user);
        return maker;
    }

    // === 1. Fee-invoice math ===

    [Fact]
    public async Task Generates_one_fee_invoice_per_maker_with_summed_fee_and_zero_vat()
    {
        var batch = BuildBatch();
        SetupMaker("maker-A", "Alpha s.r.o.", "111/0100", "a@x.cz");
        SetupMaker("maker-B", "Beta s.r.o.", "222/0800", "b@x.cz");

        var claimed = new List<Order>
        {
            ClaimedOrder("o1", "maker-A", batch.Id, 30000, 10000),
            ClaimedOrder("o2", "maker-A", batch.Id, 15000, 5000),
            ClaimedOrder("o3", "maker-B", batch.Id, 21000, 7000),
        };
        _orders.GetByPayoutBatchIdUnscopedAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(claimed);
        _invoices.GetByPayoutBatchIdAsync(batch.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Invoice>());

        var issued = new List<Invoice>();
        await _invoices.AddAsync(Arg.Do<Invoice>(i => issued.Add(i)), Arg.Any<CancellationToken>());

        var result = await _sut.GenerateAsync(batch, claimedOrders: null, CancellationToken.None);

        result.Complete.Should().BeTrue();
        issued.Should().HaveCount(2);
        issued.Should().AllSatisfy(i =>
        {
            i.Type.Should().Be(InvoiceType.Fee);
            i.PayoutBatchId.Should().Be(batch.Id);
            i.OrderId.Should().BeNull();
            i.VatAmountMinor.Should().Be(0);
            i.InvoicingMode.Should().Be(InvoicingMode.None);
        });
        issued.Single(i => i.MakerId == "maker-A").AmountWithVatMinor.Should().Be(15000); // 10000 + 5000
        issued.Single(i => i.MakerId == "maker-B").AmountWithVatMinor.Should().Be(7000);

        // DUZP = batch creation date in Prague local (2026-06-15).
        issued.First().IssueDate.Should().Be(new DateOnly(2026, 6, 15));

        // One CSV upload + two PDF uploads + two maker emails.
        await _blobs.Received(1).UploadAsync(BlobContainer.Payouts, Arg.Any<string>(),
            Arg.Any<Stream>(), "text/csv", Arg.Any<CancellationToken>());
        _outbox.Received(2).Enqueue(batch.Id, OutboxEventTypes.PayoutFeeInvoiceMakerEmail, Arg.Any<string>());
    }

    // === 2. Re-run skips complete makers ===

    [Fact]
    public async Task Re_run_with_all_artifacts_present_is_a_no_op()
    {
        var batch = BuildBatch();
        batch.AttachCsvBlobPath("payouts/cz/VYP-CZ-2026-W25.csv");
        SetupMaker("maker-A", "Alpha s.r.o.", "111/0100", "a@x.cz");

        var claimed = new List<Order> { ClaimedOrder("o1", "maker-A", batch.Id, 30000, 10000) };
        _orders.GetByPayoutBatchIdUnscopedAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(claimed);

        // Existing Fee invoice WITH a rendered PDF → nothing to do.
        var existing = Invoice.Issue(
            id: "inv-A", invoiceNumber: "FV-CZ-20260001", type: InvoiceType.Fee,
            orderId: null, payoutBatchId: batch.Id, makerId: "maker-A",
            issuerName: "JVM YORE s.r.o.", issuerIco: "00000000", issuerDic: null, issuerBankAccount: null,
            recipientName: "Alpha s.r.o.", recipientEmail: "a@x.cz", recipientTaxId: "12345678", recipientVatId: null,
            issueDate: new DateOnly(2026, 6, 15), taxableSupplyDate: null, dueDate: new DateOnly(2026, 6, 29),
            invoicingMode: InvoicingMode.None, amountWithoutVatMinor: 10000, vatRateBp: 0, vatAmountMinor: 0,
            amountWithVatMinor: 10000, currency: "CZK", countryCode: "CZ");
        existing.AttachPdfBlobPath("cz/payouts/pb-1/FV-CZ-20260001.pdf");
        _invoices.GetByPayoutBatchIdAsync(batch.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Invoice> { existing });

        var result = await _sut.GenerateAsync(batch, claimedOrders: null, CancellationToken.None);

        result.Complete.Should().BeTrue();
        await _renderer.DidNotReceive().RenderFeeAsync(
            Arg.Any<Invoice>(), Arg.Any<IReadOnlyList<FeeInvoiceLineItem>>(),
            Arg.Any<CountryConfiguration>(), Arg.Any<CancellationToken>());
        await _blobs.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _invoices.DidNotReceive().AddAsync(Arg.Any<Invoice>(), Arg.Any<CancellationToken>());
    }

    // === 3. Mid-pipeline failure ===

    [Fact]
    public async Task Render_failure_leaves_complete_false_and_skips_csv()
    {
        var batch = BuildBatch();
        SetupMaker("maker-A", "Alpha s.r.o.", "111/0100", "a@x.cz");

        var claimed = new List<Order> { ClaimedOrder("o1", "maker-A", batch.Id, 30000, 10000) };
        _orders.GetByPayoutBatchIdUnscopedAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(claimed);
        _invoices.GetByPayoutBatchIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(new List<Invoice>());
        _renderer.RenderFeeAsync(Arg.Any<Invoice>(), Arg.Any<IReadOnlyList<FeeInvoiceLineItem>>(),
                Arg.Any<CountryConfiguration>(), Arg.Any<CancellationToken>())
            .Returns<byte[]>(_ => throw new InvalidOperationException("boom"));

        var result = await _sut.GenerateAsync(batch, claimedOrders: null, CancellationToken.None);

        result.Complete.Should().BeFalse();
        // No CSV upload attempted (the maker unit failed before CSV).
        await _blobs.DidNotReceive().UploadAsync(BlobContainer.Payouts, Arg.Any<string>(),
            Arg.Any<Stream>(), "text/csv", Arg.Any<CancellationToken>());
        batch.CsvBlobPath.Should().BeNull();
    }
}
