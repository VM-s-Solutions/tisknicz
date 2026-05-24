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
}
