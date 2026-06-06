using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Email;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Email;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

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
        _sut = new EmailSendService(_templates, _translations, _provider, urls,
            NullLogger<EmailSendService>.Instance);
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
}
