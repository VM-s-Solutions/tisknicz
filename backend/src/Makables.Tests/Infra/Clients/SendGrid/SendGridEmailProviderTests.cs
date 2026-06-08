using System.Net;
using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Email;
using Makables.Infra.Clients.SendGrid;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Makables.Tests.Infra.Clients.SendGrid;

/// <summary>
/// Pins the SendGrid adapter contract per T-0028 / ADR 0019 (amended).
/// Covers success path (X-Message-Id extraction), permanent failure
/// (4xx non-retryable), transient failure (5xx surfaces as Transient).
/// The Polly retry pipeline is wired with zero retries here so each
/// assertion exercises exactly one provider call.
/// </summary>
public class SendGridEmailProviderTests
{
    private readonly ISendGridClient _client = Substitute.For<ISendGridClient>();
    private readonly SendGridEmailProvider _sut;

    public SendGridEmailProviderTests()
    {
        var opts = Options.Create(new SendGridOptions
        {
            ApiKey = "ignored-in-fake",
            DefaultFromAddress = "no-reply@makables.test",
            DefaultFromName = "Makables",
        });
        // Zero-retry pipeline so each test sees exactly one SendGrid call.
        var pipeline = new ResiliencePipelineBuilder<Response>().Build();
        _sut = new SendGridEmailProvider(_client, opts, pipeline,
            NullLogger<SendGridEmailProvider>.Instance);
    }

    private static EmailMessage CreateMessage(string fromAddress = "", string? fromName = null) =>
        new(
            ProviderTemplateId: "d-test",
            LanguageCode: "cs-CZ",
            ToAddress: "anna@example.cz",
            ToName: null,
            FromAddress: fromAddress,
            FromName: fromName,
            ReplyToAddress: null,
            Subject: "Subject",
            PlainTextBody: "body",
            Data: new Dictionary<string, object> { ["action_url"] = "https://x.test/y" });

    private static Response CreateResponse(HttpStatusCode status, string? messageId = null)
    {
        // SendGrid's Response wraps an HttpResponseMessage. Need to make
        // headers carry X-Message-Id when present.
        var http = new HttpResponseMessage(status);
        if (messageId is not null) http.Headers.TryAddWithoutValidation("X-Message-Id", messageId);
        return new Response(status, responseBody: null, responseHeaders: http.Headers);
    }

    [Fact]
    public void Code_is_sendgrid_matching_CountryConfiguration_DefaultEmailProvider()
    {
        _sut.Code.Should().Be(SendGridEmailProvider.ProviderCode);
        _sut.Code.Should().Be("sendgrid");
    }

    [Fact]
    public async Task Returns_receipt_with_X_Message_Id_on_2xx()
    {
        _client.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(CreateResponse(HttpStatusCode.Accepted, "sg-abc-123"));

        var result = await _sut.SendAsync(CreateMessage(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderMessageId.Should().Be("sg-abc-123");
    }

    [Fact]
    public async Task Empty_FromAddress_falls_back_to_SendGridOptions_DefaultFromAddress()
    {
        SendGridMessage? captured = null;
        _client.SendEmailAsync(Arg.Do<SendGridMessage>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(CreateResponse(HttpStatusCode.Accepted, "sg-x"));

        await _sut.SendAsync(CreateMessage(fromAddress: ""), CancellationToken.None);

        captured!.From.Email.Should().Be("no-reply@makables.test");
        captured.From.Name.Should().Be("Makables");
    }

    [Fact]
    public async Task Subject_is_forwarded_to_SendGrid_message_AND_data_dictionary()
    {
        // T-0028 CQ reviewer M-1: the resolved subject from
        // EmailTemplateTranslation MUST reach the wire. Without this it
        // silently relied on whatever subject was hard-coded in the SendGrid
        // template, ignoring the DB row.
        SendGridMessage? captured = null;
        _client.SendEmailAsync(Arg.Do<SendGridMessage>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(CreateResponse(HttpStatusCode.Accepted, "sg-x"));

        await _sut.SendAsync(CreateMessage() with { Subject = "Custom subject from DB" },
            CancellationToken.None);

        captured.Should().NotBeNull();
        // SendGrid's SetSubject puts the value on the first Personalization
        // (per-recipient override); the top-level Subject is the template default.
        captured!.Personalizations.Should().HaveCountGreaterThan(0);
        captured.Personalizations[0].Subject.Should().Be("Custom subject from DB");
        // Personalization data dict also carries the subject so the SendGrid
        // template can render it inside the HTML body.
        captured.Personalizations[0].TemplateData.Should().NotBeNull();
    }

    [Fact]
    public async Task Failure_responses_never_carry_the_SendGrid_response_body_in_the_returned_Error()
    {
        // T-0028 sec reviewer B-1: SendGrid 4xx echoes the offending request
        // (recipient address, headers). The body MUST NOT propagate to the
        // BusinessResult — outbox last_error column or admin UI would
        // otherwise persist PII.
        var body = new StringContent("Bad Request: recipient anna@example.cz rejected; Authorization: Bearer leaked");
        var http = new HttpResponseMessage(HttpStatusCode.BadRequest);
        var responseWithBody = new Response(HttpStatusCode.BadRequest, body, http.Headers);
        _client.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(responseWithBody);

        var result = await _sut.SendAsync(CreateMessage(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailProviderPermanentFailure);
        // Error.Details is where the body USED to be propagated; it must
        // now be null so nothing PII flows to outbox / logs / admin UI.
        result.Error.Details.Should().BeNull();
    }

    [Theory]
    [InlineData((int)HttpStatusCode.InternalServerError)]
    [InlineData((int)HttpStatusCode.BadGateway)]
    [InlineData((int)HttpStatusCode.ServiceUnavailable)]
    [InlineData(429)]
    [InlineData(408)]
    public async Task Transient_status_codes_surface_as_Transient_BusinessError(int statusCode)
    {
        _client.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(CreateResponse((HttpStatusCode)statusCode));

        var result = await _sut.SendAsync(CreateMessage(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailProviderTransientFailure);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    [Theory]
    [InlineData((int)HttpStatusCode.BadRequest)]
    [InlineData((int)HttpStatusCode.Unauthorized)]
    [InlineData((int)HttpStatusCode.Forbidden)]
    public async Task Permanent_4xx_surfaces_as_Permanent_BusinessError(int statusCode)
    {
        _client.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(CreateResponse((HttpStatusCode)statusCode));

        var result = await _sut.SendAsync(CreateMessage(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailProviderPermanentFailure);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Fact]
    public async Task Cancellation_during_send_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _client.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns<Response>(_ => throw new OperationCanceledException(cts.Token));

        var act = async () => await _sut.SendAsync(CreateMessage(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // === T-0069 — Attachment wiring + size-error translation. ===

    [Fact]
    public async Task EmailMessage_with_non_null_Attachment_calls_AddAttachment_with_base64_filename_and_mime()
    {
        // T-0069 AC-5: provider only wires bytes per locked decision 7.
        // SendGrid SDK's AddAttachment expects an ALREADY-base64-encoded
        // content string + filename + mime type.
        SendGridMessage? captured = null;
        _client.SendEmailAsync(Arg.Do<SendGridMessage>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(CreateResponse(HttpStatusCode.Accepted, "sg-x"));
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // "%PDF"
        var attachment = new Makables.Core.Domain.Email.Attachment(
            "faktura-M-CZ-20260042.pdf", pdfBytes, "application/pdf");

        await _sut.SendAsync(CreateMessage() with { Attachment = attachment }, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Attachments.Should().HaveCount(1);
        var sgAtt = captured.Attachments[0];
        sgAtt.Filename.Should().Be("faktura-M-CZ-20260042.pdf");
        sgAtt.Type.Should().Be("application/pdf");
        sgAtt.Content.Should().Be(Convert.ToBase64String(pdfBytes));
    }

    [Fact]
    public async Task EmailMessage_with_null_Attachment_does_NOT_call_AddAttachment()
    {
        // T-0069 AC-9 / AC-10: auth-flow + maker emails pass null
        // attachment; SendGrid message has no attachments list populated.
        SendGridMessage? captured = null;
        _client.SendEmailAsync(Arg.Do<SendGridMessage>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(CreateResponse(HttpStatusCode.Accepted, "sg-x"));

        await _sut.SendAsync(CreateMessage(), CancellationToken.None);

        captured.Should().NotBeNull();
        // SendGrid's Attachments is null until AddAttachment lands at least
        // one — either null or empty is acceptable for "no attachment".
        (captured!.Attachments is null || captured.Attachments.Count == 0)
            .Should().BeTrue();
    }

    [Fact]
    public async Task SendGrid_413_response_with_attachment_translates_to_InvoicePdfAttachmentTooLarge_Permanent()
    {
        // T-0069 AC-7: SendGrid 30 MB cap → Permanent + outbox stall. The
        // adapter classifies 413 (or 4xx with "too large" body) when the
        // outgoing message carried an attachment.
        _client.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(CreateResponse(HttpStatusCode.RequestEntityTooLarge));
        var attachment = new Makables.Core.Domain.Email.Attachment(
            "faktura-big.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf");

        var result = await _sut.SendAsync(CreateMessage() with { Attachment = attachment }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.InvoicePdfAttachmentTooLarge);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Fact]
    public async Task SendGrid_400_with_too_large_body_and_attachment_translates_to_InvoicePdfAttachmentTooLarge()
    {
        // Defensive variant: some SendGrid responses collapse to 400 with
        // a "Payload too large" message body. The adapter sniffs the body
        // when an attachment was on the outgoing message.
        var http = new HttpResponseMessage(HttpStatusCode.BadRequest);
        var body = new StringContent("{\"errors\":[{\"message\":\"Payload too large\"}]}");
        var responseWithBody = new Response(HttpStatusCode.BadRequest, body, http.Headers);
        _client.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(responseWithBody);
        var attachment = new Makables.Core.Domain.Email.Attachment(
            "faktura-big.pdf", new byte[] { 0x25 }, "application/pdf");

        var result = await _sut.SendAsync(CreateMessage() with { Attachment = attachment }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.InvoicePdfAttachmentTooLarge);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Fact]
    public async Task SendGrid_413_without_attachment_still_classifies_as_generic_Permanent()
    {
        // Edge: a 413 without an attachment (carrier message had none)
        // skips the size-sniff entirely; the generic 4xx path wins. This
        // pins that the attachment-size code only fires when the failing
        // message actually carried an attachment.
        _client.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(CreateResponse(HttpStatusCode.RequestEntityTooLarge));

        var result = await _sut.SendAsync(CreateMessage(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailProviderPermanentFailure);
    }
}
