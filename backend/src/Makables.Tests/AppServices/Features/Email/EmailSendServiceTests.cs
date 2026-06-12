using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Email;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Email;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Makables.Tests.AppServices.Features.Email;

/// <summary>
/// Pins the orchestration contract T-0029 will depend on. Per T-0028
/// design directive: the service decodes the outbox payload, picks the
/// EmailTemplate + Translation (with one-step fallback to the platform
/// default language), composes an EmailMessage, and dispatches via
/// <see cref="IEmailProvider"/>. Provider failure paths bubble back as
/// <see cref="BusinessResult{T}"/> failures.
/// </summary>
public class EmailSendServiceTests
{
    private readonly IEmailTemplateRepository _templates = Substitute.For<IEmailTemplateRepository>();
    private readonly IEmailTemplateTranslationRepository _translations =
        Substitute.For<IEmailTemplateTranslationRepository>();
    private readonly IEmailProvider _provider = Substitute.For<IEmailProvider>();
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IBlobStorageClient _blobStorage = Substitute.For<IBlobStorageClient>();
    private readonly EmailSendService _sut;

    public EmailSendServiceTests()
    {
        var urls = Options.Create(new PublicAppUrlsOptions
        {
            WebBaseUrl = "https://makables.test",
            MagicLinkPath = "/auth/magic?token={token}",
            EmailConfirmationPath = "/auth/confirm?token={token}",
            PasswordResetPath = "/auth/reset?token={token}",
        });
        // T-0106: admin dispute-digest recipient resolves at send time.
        var emailOptions = Options.Create(new EmailOptions
        {
            AdminNotificationAddress = "ops@makables.test",
        });
        _sut = new EmailSendService(_templates, _translations, _provider,
            _invoices, _blobStorage,
            urls, emailOptions, NullLogger<EmailSendService>.Instance);
    }

    private static EmailTemplate CreateTemplate(EmailTemplateType type) =>
        EmailTemplate.Create(
            id: $"tpl-{type}",
            type: type,
            providerTemplateId: $"d-fake-{type}",
            countryCode: "CZ",
            fromAddress: null);

    private static EmailTemplateTranslation CreateTranslation(string templateId, string lang, string subject, string body) =>
        EmailTemplateTranslation.Create(
            id: $"tr-{templateId}-{lang}",
            emailTemplateId: templateId,
            languageCode: lang,
            subject: subject,
            plainTextBody: body,
            countryCode: "CZ");

    private static string EncodePayload(string lang, string rawToken = "rawtok123") =>
        JsonSerializer.Serialize(new OneTimeTokenOutboxPayload(
            UserId: "user-1",
            Email: "anna@example.cz",
            RawToken: rawToken,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(15),
            LanguageCode: lang));

    [Fact]
    public async Task Happy_path_composes_message_with_action_url_and_dispatches()
    {
        var tpl = CreateTemplate(EmailTemplateType.AuthMagicLink);
        var tr = CreateTranslation(tpl.Id, LanguageCode.CsCZ, "Přihlášení", "Klikněte: {{action_url}}");
        _templates.GetByTypeAsync(EmailTemplateType.AuthMagicLink, Arg.Any<CancellationToken>()).Returns(tpl);
        _translations.GetAsync(tpl.Id, LanguageCode.CsCZ, Arg.Any<CancellationToken>()).Returns(tr);
        _provider.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new EmailSentReceipt("sg-msg-1", DateTimeOffset.UtcNow)));

        var result = await _sut.SendAsync(
            OutboxEventTypes.AuthMagicLinkSend,
            EncodePayload(LanguageCode.CsCZ, "rawTok!special"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderMessageId.Should().Be("sg-msg-1");

        await _provider.Received(1).SendAsync(Arg.Is<EmailMessage>(m =>
            m.ProviderTemplateId == "d-fake-AuthMagicLink"
            && m.ToAddress == "anna@example.cz"
            && m.LanguageCode == LanguageCode.CsCZ
            && m.Data.ContainsKey("action_url")
            && ((string)m.Data["action_url"]).StartsWith("https://makables.test/auth/magic?token=")
            && m.PlainTextBody.Contains("https://makables.test/auth/magic?token=")
            && !m.PlainTextBody.Contains("{{action_url}}")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Token_in_action_url_is_URL_escaped()
    {
        var tpl = CreateTemplate(EmailTemplateType.AuthMagicLink);
        var tr = CreateTranslation(tpl.Id, LanguageCode.CsCZ, "S", "B");
        _templates.GetByTypeAsync(Arg.Any<EmailTemplateType>(), Arg.Any<CancellationToken>()).Returns(tpl);
        _translations.GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(tr);
        _provider.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new EmailSentReceipt("x", DateTimeOffset.UtcNow)));

        await _sut.SendAsync(OutboxEventTypes.AuthMagicLinkSend,
            EncodePayload(LanguageCode.CsCZ, "raw token with spaces"),
            CancellationToken.None);

        await _provider.Received().SendAsync(Arg.Is<EmailMessage>(m =>
            ((string)m.Data["action_url"]).Contains("raw%20token%20with%20spaces")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Falls_back_to_platform_default_language_when_requested_translation_missing()
    {
        var tpl = CreateTemplate(EmailTemplateType.AuthEmailConfirmation);
        var trEn = CreateTranslation(tpl.Id, LanguageCode.CsCZ, "Potvrďte", "Body");
        _templates.GetByTypeAsync(Arg.Any<EmailTemplateType>(), Arg.Any<CancellationToken>()).Returns(tpl);
        _translations.GetAsync(tpl.Id, "de-DE", Arg.Any<CancellationToken>())
            .Returns((EmailTemplateTranslation?)null);
        _translations.GetAsync(tpl.Id, LanguageCode.DefaultFallback, Arg.Any<CancellationToken>())
            .Returns(trEn);
        _provider.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new EmailSentReceipt("x", DateTimeOffset.UtcNow)));

        var result = await _sut.SendAsync(OutboxEventTypes.AuthEmailConfirmationSend,
            EncodePayload("de-DE"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _provider.Received().SendAsync(Arg.Is<EmailMessage>(m =>
            m.LanguageCode == LanguageCode.DefaultFallback),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_translationMissing_when_neither_requested_nor_fallback_exists()
    {
        var tpl = CreateTemplate(EmailTemplateType.AuthPasswordReset);
        _templates.GetByTypeAsync(Arg.Any<EmailTemplateType>(), Arg.Any<CancellationToken>()).Returns(tpl);
        _translations.GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((EmailTemplateTranslation?)null);

        var result = await _sut.SendAsync(OutboxEventTypes.AuthPasswordResetSend,
            EncodePayload("de-DE"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailTemplateTranslationMissing);
        await _provider.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_templateNotFound_when_no_EmailTemplate_row()
    {
        _templates.GetByTypeAsync(Arg.Any<EmailTemplateType>(), Arg.Any<CancellationToken>())
            .Returns((EmailTemplate?)null);

        var result = await _sut.SendAsync(OutboxEventTypes.AuthMagicLinkSend,
            EncodePayload(LanguageCode.CsCZ), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailTemplateNotFound);
    }

    [Fact]
    public async Task Returns_eventTypeUnknown_for_unmapped_outbox_event()
    {
        var result = await _sut.SendAsync("order.shipped.send",
            EncodePayload(LanguageCode.CsCZ), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailEventTypeUnknown);
    }

    [Fact]
    public async Task Returns_payloadMalformed_for_malformed_json()
    {
        var result = await _sut.SendAsync(OutboxEventTypes.AuthMagicLinkSend,
            "{not valid json", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailPayloadMalformed);
    }

    [Fact]
    public async Task Returns_payloadMissingFields_for_payload_decoded_but_blank()
    {
        var bad = JsonSerializer.Serialize(new OneTimeTokenOutboxPayload(
            UserId: "user-1", Email: "", RawToken: "tok", ExpiresAt: DateTimeOffset.UtcNow,
            LanguageCode: LanguageCode.CsCZ));

        var result = await _sut.SendAsync(OutboxEventTypes.AuthMagicLinkSend, bad, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailPayloadMissingFields);
    }

    // === T-0067 — Order email branches. ===

    private const string TestOrderId = "ord-1";
    private const string TestOrderNumber = "M-CZ-20260042";
    private const string TestInvoicePdfPath = "cz/orders/ord-1/FV-CZ-20260042.pdf";
    private static readonly byte[] TestPdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2d, 0x31, 0x2e, 0x37 };

    /// <summary>
    /// Build a rendered (PDF-attached) invoice for the order-paid happy
    /// path. <see cref="Invoice.AttachPdfBlobPath"/> is called so the
    /// EmailSendService passes the "invoice rendered" gate.
    /// </summary>
    private static Invoice CreateRenderedInvoice(string orderId = TestOrderId, string blobPath = TestInvoicePdfPath)
    {
        var inv = Invoice.Issue(
            id: "inv-1",
            invoiceNumber: "FV-CZ-20260042",
            type: InvoiceType.Customer,
            orderId: orderId,
            payoutBatchId: null,
            makerId: "maker-1",
            issuerName: "JVM YORE s.r.o.",
            issuerIco: "00000000",
            issuerDic: null,
            issuerBankAccount: null,
            recipientName: "Anna Nováková",
            recipientEmail: "anna@example.cz",
            recipientTaxId: null,
            recipientVatId: null,
            issueDate: new DateOnly(2026, 6, 7),
            taxableSupplyDate: null,
            dueDate: new DateOnly(2026, 6, 21),
            invoicingMode: Makables.Core.Domain.Configuration.InvoicingMode.None,
            amountWithoutVatMinor: 57_900,
            vatRateBp: 0,
            vatAmountMinor: 0,
            amountWithVatMinor: 57_900,
            currency: "CZK",
            countryCode: "CZ");
        inv.AttachPdfBlobPath(blobPath);
        return inv;
    }

    private static Order CreateOrderForEmail(string orderNumber = TestOrderNumber)
    {
        return Order.Create(
            id: TestOrderId,
            orderNumber: orderNumber,
            customerUserId: "user-1",
            makerId: "maker-1",
            productId: "prod-1",
            contactName: "Anna Nováková",
            contactEmail: "anna@example.cz",
            contactPhone: "+420 777 123 456",
            productPriceAmountMinor: 50_000,
            shippingPriceAmountMinor: 7_900,
            platformFeeAmountMinor: 7_500,
            makerPayoutAmountMinor: 50_400,
            totalAmountMinor: 57_900,
            currency: "CZK",
            vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: "CZ");
    }

    /// <summary>
    /// Pre-stub the new T-0069 deps (Invoice + Order + blob download)
    /// so the existing OrderPaidCustomerEmail tests stay green. Returns
    /// the seeded invoice + order so individual tests can assert on
    /// specific values when they want to.
    /// </summary>
    private Invoice ArrangeInvoiceReady(string orderNumber = TestOrderNumber)
    {
        // T-0069 reviewer DC1/ID5 fold: OrderNumber is pre-baked into
        // OrderPaidCustomerEmailPayload by MarkOrderPaid.Handler (T-0067).
        // EmailSendService never needs to load the Order — the orderNumber
        // parameter is the value the test injects into the payload, returned
        // for assertion convenience.
        _ = orderNumber;
        var invoice = CreateRenderedInvoice();
        _invoices.GetByOrderIdAsync(TestOrderId, Arg.Any<CancellationToken>()).Returns(invoice);
        _blobStorage.DownloadAsync(BlobContainer.Invoices, TestInvoicePdfPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new BlobDownload(
                Content: new MemoryStream(TestPdfBytes, writable: false),
                ContentType: "application/pdf",
                ContentLength: TestPdfBytes.Length,
                ETag: null)));
        return invoice;
    }

    private static string EncodeCustomerPayload(
        string lang = LanguageCode.CsCZ,
        string actionUrl = "https://makables.test/objednavka/ord-1") =>
        JsonSerializer.Serialize(new OrderPaidCustomerEmailPayload(
            OrderId: "ord-1",
            OrderNumber: "M-CZ-20260042",
            Email: "anna@example.cz",
            ContactName: "Anna",
            TotalAmountMinor: 57900,
            Currency: "CZK",
            LanguageCode: lang,
            ActionUrl: actionUrl));

    private static string EncodeMakerPayload(
        string lang = LanguageCode.CsCZ,
        string actionUrl = "https://makables.test/dashboard/maker/objednavky/ord-1") =>
        JsonSerializer.Serialize(new OrderPlacedMakerEmailPayload(
            OrderId: "ord-1",
            OrderNumber: "M-CZ-20260042",
            MakerId: "maker-1",
            MakerEmail: "maker@example.cz",
            TotalAmountMinor: 57900,
            Currency: "CZK",
            LanguageCode: lang,
            ActionUrl: actionUrl));

    [Fact]
    public async Task SendAsync_with_OrderPaidCustomerEmail_routes_to_OrderEmail_branch()
    {
        ArrangeInvoiceReady();
        var tpl = CreateTemplate(EmailTemplateType.OrderPaidCustomer);
        var tr = CreateTranslation(tpl.Id, LanguageCode.CsCZ,
            "Děkujeme za objednávku", "Pre-baked URL: {{action_url}}");
        _templates.GetByTypeAsync(EmailTemplateType.OrderPaidCustomer, Arg.Any<CancellationToken>())
            .Returns(tpl);
        _translations.GetAsync(tpl.Id, LanguageCode.CsCZ, Arg.Any<CancellationToken>())
            .Returns(tr);
        _provider.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new EmailSentReceipt("sg-msg-customer", DateTimeOffset.UtcNow)));

        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderPaidCustomerEmail,
            EncodeCustomerPayload(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderMessageId.Should().Be("sg-msg-customer");

        // The pre-baked URL is passed verbatim — no further substitution
        // (Q4: do NOT touch BuildActionUrl for the order branch).
        await _provider.Received(1).SendAsync(Arg.Is<EmailMessage>(m =>
            m.ProviderTemplateId == "d-fake-OrderPaidCustomer"
            && m.ToAddress == "anna@example.cz"
            && m.ToName == "Anna"
            && m.LanguageCode == LanguageCode.CsCZ
            && m.Data.ContainsKey("action_url")
            && ((string)m.Data["action_url"]) == "https://makables.test/objednavka/ord-1"
            && m.PlainTextBody.Contains("https://makables.test/objednavka/ord-1")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_with_OrderPaidCustomerEmail_substitutes_order_number_in_subject()
    {
        // T-0067 reviewer B-1: SendGrid does NOT substitute the subject from
        // dynamicTemplateData; if the producer doesn't inline the substitution
        // the customer's inbox shows the raw "#{{order_number}}" literal.
        // Pin the contract so a future template-engine change can't silently
        // regress.
        ArrangeInvoiceReady();
        var tpl = CreateTemplate(EmailTemplateType.OrderPaidCustomer);
        var tr = CreateTranslation(tpl.Id, LanguageCode.CsCZ,
            "Děkujeme za objednávku #{{order_number}}",
            "Hi {{contact_name}}");
        _templates.GetByTypeAsync(EmailTemplateType.OrderPaidCustomer, Arg.Any<CancellationToken>())
            .Returns(tpl);
        _translations.GetAsync(tpl.Id, LanguageCode.CsCZ, Arg.Any<CancellationToken>())
            .Returns(tr);
        _provider.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new EmailSentReceipt("sg-msg-customer", DateTimeOffset.UtcNow)));

        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderPaidCustomerEmail,
            EncodeCustomerPayload(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _provider.Received(1).SendAsync(Arg.Is<EmailMessage>(m =>
            m.Subject == "Děkujeme za objednávku #M-CZ-20260042"
            && m.PlainTextBody == "Hi Anna"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_with_OrderPlacedMakerEmail_routes_to_OrderEmail_branch()
    {
        var tpl = CreateTemplate(EmailTemplateType.OrderPlacedMaker);
        var tr = CreateTranslation(tpl.Id, LanguageCode.CsCZ, "Nová objednávka", "B");
        _templates.GetByTypeAsync(EmailTemplateType.OrderPlacedMaker, Arg.Any<CancellationToken>())
            .Returns(tpl);
        _translations.GetAsync(tpl.Id, LanguageCode.CsCZ, Arg.Any<CancellationToken>())
            .Returns(tr);
        _provider.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new EmailSentReceipt("sg-msg-maker", DateTimeOffset.UtcNow)));

        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderPlacedMakerEmail,
            EncodeMakerPayload(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _provider.Received(1).SendAsync(Arg.Is<EmailMessage>(m =>
            m.ProviderTemplateId == "d-fake-OrderPlacedMaker"
            && m.ToAddress == "maker@example.cz"
            && ((string)m.Data["action_url"]) == "https://makables.test/dashboard/maker/objednavky/ord-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_with_malformed_order_payload_returns_OrderEmailPayloadMalformed_Permanent()
    {
        // T-0067 — distinct error code so T-0029's triage UI separates
        // auth-flow payload bugs from order-flow payload bugs.
        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderPaidCustomerEmail,
            "{not valid json",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderEmailPayloadMalformed);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Fact]
    public async Task SendAsync_with_unknown_event_type_still_returns_EmailEventTypeUnknown()
    {
        // Auth-flow code path unchanged. Pin that adding the order
        // branches did not regress the default arm.
        var result = await _sut.SendAsync("future.unmapped.send",
            EncodePayload(LanguageCode.CsCZ), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailEventTypeUnknown);
    }

    [Fact]
    public async Task SendAsync_with_OrderPaidCustomerEmail_falls_back_to_default_language()
    {
        // Mirror the auth-flow translation-fallback behaviour for the
        // order branch — same two-step lookup (requested → fallback).
        ArrangeInvoiceReady();
        var tpl = CreateTemplate(EmailTemplateType.OrderPaidCustomer);
        var trDefault = CreateTranslation(tpl.Id, LanguageCode.DefaultFallback, "Subject", "Body");
        _templates.GetByTypeAsync(EmailTemplateType.OrderPaidCustomer, Arg.Any<CancellationToken>())
            .Returns(tpl);
        _translations.GetAsync(tpl.Id, "de-DE", Arg.Any<CancellationToken>())
            .Returns((EmailTemplateTranslation?)null);
        _translations.GetAsync(tpl.Id, LanguageCode.DefaultFallback, Arg.Any<CancellationToken>())
            .Returns(trDefault);
        _provider.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new EmailSentReceipt("x", DateTimeOffset.UtcNow)));

        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderPaidCustomerEmail,
            EncodeCustomerPayload(lang: "de-DE"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _provider.Received().SendAsync(Arg.Is<EmailMessage>(m =>
            m.LanguageCode == LanguageCode.DefaultFallback),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Provider_failure_bubbles_back_unchanged()
    {
        var tpl = CreateTemplate(EmailTemplateType.AuthMagicLink);
        var tr = CreateTranslation(tpl.Id, LanguageCode.CsCZ, "S", "B");
        _templates.GetByTypeAsync(Arg.Any<EmailTemplateType>(), Arg.Any<CancellationToken>()).Returns(tpl);
        _translations.GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(tr);
        _provider.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(
            BusinessResult.Failure<EmailSentReceipt>(
                Error.Transient(BusinessErrorMessage.EmailProviderTransientFailure, "503")));

        var result = await _sut.SendAsync(OutboxEventTypes.AuthMagicLinkSend,
            EncodePayload(LanguageCode.CsCZ), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailProviderTransientFailure);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    // === T-0069 — Invoice PDF attachment on the customer order-paid branch. ===

    private void ArrangeOrderPaidTemplate()
    {
        var tpl = CreateTemplate(EmailTemplateType.OrderPaidCustomer);
        var tr = CreateTranslation(tpl.Id, LanguageCode.CsCZ, "Subject", "Body");
        _templates.GetByTypeAsync(EmailTemplateType.OrderPaidCustomer, Arg.Any<CancellationToken>()).Returns(tpl);
        _translations.GetAsync(tpl.Id, LanguageCode.CsCZ, Arg.Any<CancellationToken>()).Returns(tr);
        _provider.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new EmailSentReceipt("sg-msg-69", DateTimeOffset.UtcNow)));
    }

    [Fact]
    public async Task OrderPaidCustomerEmail_with_invoice_not_yet_rendered_returns_Transient_InvoiceNotYetRendered()
    {
        // T-0069 AC-4: race-loser path. The invoice.generate queue lost the
        // FIFO race; EmailSendService finds no Invoice row yet and returns
        // Transient so the outbox retries.
        ArrangeOrderPaidTemplate();
        _invoices.GetByOrderIdAsync(TestOrderId, Arg.Any<CancellationToken>()).Returns((Invoice?)null);

        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderPaidCustomerEmail,
            EncodeCustomerPayload(),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.InvoiceNotYetRendered);
        result.Error.Type.Should().Be(ErrorType.Transient);
        await _provider.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderPaidCustomerEmail_with_invoice_PdfBlobPath_null_returns_Transient_InvoiceNotYetRendered()
    {
        // Defensive variant of AC-4: the Invoice row exists but the render
        // step hasn't committed PdfBlobPath yet (mid-flight). Same outcome
        // — Transient → retry → second attempt succeeds.
        ArrangeOrderPaidTemplate();
        var halfBaked = Invoice.Issue(
            id: "inv-1", invoiceNumber: "FV-CZ-20260042",
            type: InvoiceType.Customer, orderId: TestOrderId, payoutBatchId: null,
            makerId: "maker-1", issuerName: "JVM YORE", issuerIco: "00000000",
            issuerDic: null, issuerBankAccount: null,
            recipientName: "Anna", recipientEmail: "anna@example.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: new DateOnly(2026, 6, 7), taxableSupplyDate: null,
            dueDate: new DateOnly(2026, 6, 21),
            invoicingMode: Makables.Core.Domain.Configuration.InvoicingMode.None,
            amountWithoutVatMinor: 57_900, vatRateBp: 0, vatAmountMinor: 0,
            amountWithVatMinor: 57_900, currency: "CZK", countryCode: "CZ");
        // NB: NO AttachPdfBlobPath call — PdfBlobPath is null.
        _invoices.GetByOrderIdAsync(TestOrderId, Arg.Any<CancellationToken>()).Returns(halfBaked);

        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderPaidCustomerEmail,
            EncodeCustomerPayload(),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.InvoiceNotYetRendered);
        result.Error.Type.Should().Be(ErrorType.Transient);
        await _blobStorage.DidNotReceive().DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderPaidCustomerEmail_happy_path_attaches_PDF_to_EmailMessage()
    {
        // T-0069 AC-5: invoice rendered + blob download succeeds →
        // EmailMessage.Attachment populated; SendGrid provider receives it.
        ArrangeOrderPaidTemplate();
        ArrangeInvoiceReady();

        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderPaidCustomerEmail,
            EncodeCustomerPayload(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _provider.Received(1).SendAsync(Arg.Is<EmailMessage>(m =>
            m.Attachment != null
            && m.Attachment.MimeType == "application/pdf"
            && m.Attachment.Bytes.Length == TestPdfBytes.Length
            && m.Attachment.Filename.EndsWith(".pdf")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderPaidCustomerEmail_blob_download_throws_returns_Permanent_InvoicePdfAttachmentDownloadFailed()
    {
        // T-0069 AC-8: blob download throws → Permanent (data-integrity
        // issue worth ops attention).
        ArrangeOrderPaidTemplate();
        var invoice = CreateRenderedInvoice();
        _invoices.GetByOrderIdAsync(TestOrderId, Arg.Any<CancellationToken>()).Returns(invoice);
        _blobStorage.DownloadAsync(BlobContainer.Invoices, TestInvoicePdfPath, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("blob missing"));

        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderPaidCustomerEmail,
            EncodeCustomerPayload(),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.InvoicePdfAttachmentDownloadFailed);
        result.Error.Type.Should().Be(ErrorType.Permanent);
        await _provider.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderPaidCustomerEmail_filename_is_faktura_when_LanguageCode_is_cs_CZ()
    {
        // T-0069 AC-6: cs-CZ → "faktura-{OrderNumber}.pdf".
        ArrangeOrderPaidTemplate();
        ArrangeInvoiceReady();

        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderPaidCustomerEmail,
            EncodeCustomerPayload(lang: LanguageCode.CsCZ),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _provider.Received(1).SendAsync(Arg.Is<EmailMessage>(m =>
            m.Attachment != null && m.Attachment.Filename == $"faktura-{TestOrderNumber}.pdf"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderPaidCustomerEmail_filename_is_invoice_when_LanguageCode_is_en_US()
    {
        // T-0069 AC-6: en-US → "invoice-{OrderNumber}.pdf". The PDF body
        // is still Czech-only at MVP; only the filename gets the locale
        // label per locked decision 6.
        ArrangeOrderPaidTemplate();
        // The English-locale lookup needs the same translation fallback
        // path that the existing fallback test exercises — the requested
        // en-US translation is null, default cs-CZ wins.
        var tpl = CreateTemplate(EmailTemplateType.OrderPaidCustomer);
        var trDefault = CreateTranslation(tpl.Id, LanguageCode.DefaultFallback, "Subject", "Body");
        _templates.GetByTypeAsync(EmailTemplateType.OrderPaidCustomer, Arg.Any<CancellationToken>()).Returns(tpl);
        _translations.GetAsync(tpl.Id, "en-US", Arg.Any<CancellationToken>())
            .Returns((EmailTemplateTranslation?)null);
        _translations.GetAsync(tpl.Id, LanguageCode.DefaultFallback, Arg.Any<CancellationToken>())
            .Returns(trDefault);
        ArrangeInvoiceReady();

        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderPaidCustomerEmail,
            EncodeCustomerPayload(lang: "en-US"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _provider.Received(1).SendAsync(Arg.Is<EmailMessage>(m =>
            m.Attachment != null && m.Attachment.Filename == $"invoice-{TestOrderNumber}.pdf"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderPlacedMakerEmail_does_NOT_carry_an_attachment()
    {
        // T-0069 AC-10: the maker email is regression-pinned to never
        // populate Attachment — only the customer email carries the PDF.
        // Belt-and-braces: also pin that the Invoice/Order/blob deps are
        // NEVER touched on the maker branch (no perf regression / no
        // accidental blob download).
        var tpl = CreateTemplate(EmailTemplateType.OrderPlacedMaker);
        var tr = CreateTranslation(tpl.Id, LanguageCode.CsCZ, "Nová", "Body");
        _templates.GetByTypeAsync(EmailTemplateType.OrderPlacedMaker, Arg.Any<CancellationToken>()).Returns(tpl);
        _translations.GetAsync(tpl.Id, LanguageCode.CsCZ, Arg.Any<CancellationToken>()).Returns(tr);
        _provider.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new EmailSentReceipt("sg-maker-69", DateTimeOffset.UtcNow)));

        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderPlacedMakerEmail,
            EncodeMakerPayload(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _provider.Received(1).SendAsync(Arg.Is<EmailMessage>(m => m.Attachment == null),
            Arg.Any<CancellationToken>());
        await _invoices.DidNotReceive().GetByOrderIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _blobStorage.DidNotReceive().DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // === T-0076 — OrderDeliveredCustomerEmail branch. ===

    private static string EncodeDeliveredCustomerPayload(
        string lang = LanguageCode.CsCZ,
        string actionUrl = "https://makables.test/objednavka/ord-1") =>
        JsonSerializer.Serialize(new OrderDeliveredCustomerEmailPayload(
            OrderId: "ord-1",
            OrderNumber: TestOrderNumber,
            Email: "anna@example.cz",
            ContactName: "Anna",
            LanguageCode: lang,
            ActionUrl: actionUrl));

    [Fact]
    public async Task SendAsync_with_OrderDeliveredCustomerEmail_loads_template_and_sends()
    {
        // T-0076 AC-9: the new EmailTemplateType.OrderDeliveredCustomer
        // branch loads the template, applies the substitutions, and calls
        // SendGrid with the pre-baked action URL verbatim.
        var tpl = CreateTemplate(EmailTemplateType.OrderDeliveredCustomer);
        var tr = CreateTranslation(tpl.Id, LanguageCode.CsCZ,
            "Doručeno #{{order_number}}", "URL: {{action_url}}");
        _templates.GetByTypeAsync(EmailTemplateType.OrderDeliveredCustomer, Arg.Any<CancellationToken>())
            .Returns(tpl);
        _translations.GetAsync(tpl.Id, LanguageCode.CsCZ, Arg.Any<CancellationToken>())
            .Returns(tr);
        _provider.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new EmailSentReceipt("sg-delivered", DateTimeOffset.UtcNow)));

        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderDeliveredCustomerEmail,
            EncodeDeliveredCustomerPayload(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderMessageId.Should().Be("sg-delivered");
        await _provider.Received(1).SendAsync(Arg.Is<EmailMessage>(m =>
            m.ProviderTemplateId == "d-fake-OrderDeliveredCustomer"
            && m.ToAddress == "anna@example.cz"
            && m.ToName == "Anna"
            && m.LanguageCode == LanguageCode.CsCZ
            && m.Data.ContainsKey("action_url")
            && ((string)m.Data["action_url"]) == "https://makables.test/objednavka/ord-1"
            && m.Subject == $"Doručeno #{TestOrderNumber}"
            && m.PlainTextBody.Contains("https://makables.test/objednavka/ord-1")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderDeliveredCustomerEmail_does_NOT_carry_an_attachment()
    {
        // T-0076: no PDF attachment — the invoice rode the OrderPaidCustomer
        // email per T-0069 locked decision 10. Regression-pin so a future
        // refactor doesn't accidentally route through DispatchOrderEmail
        // with an Attachment populated.
        var tpl = CreateTemplate(EmailTemplateType.OrderDeliveredCustomer);
        var tr = CreateTranslation(tpl.Id, LanguageCode.CsCZ, "Doručeno", "Body");
        _templates.GetByTypeAsync(EmailTemplateType.OrderDeliveredCustomer, Arg.Any<CancellationToken>()).Returns(tpl);
        _translations.GetAsync(tpl.Id, LanguageCode.CsCZ, Arg.Any<CancellationToken>()).Returns(tr);
        _provider.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new EmailSentReceipt("sg-delivered", DateTimeOffset.UtcNow)));

        var result = await _sut.SendAsync(
            OutboxEventTypes.OrderDeliveredCustomerEmail,
            EncodeDeliveredCustomerPayload(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _provider.Received(1).SendAsync(Arg.Is<EmailMessage>(m => m.Attachment == null),
            Arg.Any<CancellationToken>());
        // Belt-and-braces: the delivered branch never touches Invoice
        // or blob storage (no perf regression / no spurious blob download).
        await _invoices.DidNotReceive().GetByOrderIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _blobStorage.DidNotReceive().DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthMagicLinkSend_does_NOT_carry_an_attachment()
    {
        // T-0069 AC-9: auth-flow regression pin — Attachment stays null
        // and none of the new deps are touched.
        var tpl = CreateTemplate(EmailTemplateType.AuthMagicLink);
        var tr = CreateTranslation(tpl.Id, LanguageCode.CsCZ, "S", "B");
        _templates.GetByTypeAsync(Arg.Any<EmailTemplateType>(), Arg.Any<CancellationToken>()).Returns(tpl);
        _translations.GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(tr);
        _provider.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new EmailSentReceipt("sg-auth-69", DateTimeOffset.UtcNow)));

        var result = await _sut.SendAsync(
            OutboxEventTypes.AuthMagicLinkSend,
            EncodePayload(LanguageCode.CsCZ),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _provider.Received(1).SendAsync(Arg.Is<EmailMessage>(m => m.Attachment == null),
            Arg.Any<CancellationToken>());
        await _invoices.DidNotReceive().GetByOrderIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _blobStorage.DidNotReceive().DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
