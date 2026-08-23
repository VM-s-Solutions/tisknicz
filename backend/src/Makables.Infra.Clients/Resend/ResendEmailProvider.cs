using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace Makables.Infra.Clients.Resend;

/// <summary>
/// Resend transactional-email adapter per ADR 0019 (re-amended to Resend,
/// T-0157). <c>POST {BaseUrl}/emails</c> with a Bearer API key.
///
/// The adapter is locale-agnostic — the caller (<c>EmailSendService</c>)
/// has already resolved the language, looked up the
/// <c>EmailTemplateTranslation</c>, and substituted every placeholder
/// locally (<c>SubstitutePlainTextPlaceholders</c>), so
/// <see cref="EmailMessage.Subject"/> + <see cref="EmailMessage.PlainTextBody"/>
/// arrive fully rendered. Unlike SendGrid there is no remote
/// dynamic-template rendering: <see cref="EmailMessage.ProviderTemplateId"/>
/// and <see cref="EmailMessage.Data"/> are ignored. The email ships as
/// <c>multipart/alternative</c> — the rendered plain text plus, when the
/// caller composed one, the <see cref="EmailMessage.HtmlBody"/> produced
/// by <c>EmailHtmlLayout</c>. The DB still stores no HTML; the layout is
/// derived from the plain-text translation at send time.
///
/// Failure taxonomy mirrors the SendGrid adapter: 5xx / 429 / 408 /
/// transport errors → Transient (the outbox retry policy re-delivers);
/// other 4xx → Permanent. SECURITY (T-0028 sec reviewer B-1 carried
/// over): the response body is never propagated in failures and never
/// logged as a structured property — only the status code surfaces.
/// </summary>
public sealed class ResendEmailProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ResendOptions> options,
    ResiliencePipelineRegistry<string> pipelineRegistry,
    ILogger<ResendEmailProvider> logger) : IEmailProvider
{
    public const string ProviderCode = "resend";
    public const string HttpClientName = "Makables.Infra.Clients.Resend";

    public string Code => ProviderCode;

    private ResiliencePipeline<HttpResponseMessage> RetryPipeline =>
        pipelineRegistry.GetPipeline<HttpResponseMessage>(HttpClientName);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<BusinessResult<EmailSentReceipt>> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var opts = options.Value;
        var fromAddress = string.IsNullOrWhiteSpace(message.FromAddress)
            ? opts.DefaultFromAddress
            : message.FromAddress;
        var fromName = message.FromName ?? opts.DefaultFromName;

        var payload = new ResendSendRequest(
            From: string.IsNullOrWhiteSpace(fromName) ? fromAddress : $"{fromName} <{fromAddress}>",
            To: [message.ToAddress],
            Subject: message.Subject,
            Text: message.PlainTextBody,
            // Sending both parts makes this a multipart/alternative: the
            // client picks the HTML, and a text-only reader still gets the
            // full message. Null when the caller composed no HTML — the
            // WhenWritingNull policy then omits the field entirely.
            Html: string.IsNullOrWhiteSpace(message.HtmlBody) ? null : message.HtmlBody,
            ReplyTo: string.IsNullOrWhiteSpace(message.ReplyToAddress) ? null : message.ReplyToAddress,
            Attachments: message.Attachment is null
                ? null
                :
                [
                    new ResendAttachment(
                        Filename: message.Attachment.Filename,
                        Content: Convert.ToBase64String(message.Attachment.Bytes)),
                ]);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.PerSendTimeoutSeconds)));

        HttpResponseMessage response;
        try
        {
            response = await RetryPipeline.ExecuteAsync(
                async ct =>
                {
                    var client = httpClientFactory.CreateClient(HttpClientName);
                    using var request = new HttpRequestMessage(
                        HttpMethod.Post,
                        $"{opts.BaseUrl.TrimEnd('/')}/emails");
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.ApiKey);
                    request.Content = JsonContent.Create(payload, options: SerializerOptions);
                    return await client.SendAsync(request, ct);
                },
                timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Resend send timed out after {Seconds}s (template {TemplateId}).",
                opts.PerSendTimeoutSeconds, message.ProviderTemplateId);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Transient(BusinessErrorMessage.EmailProviderTransientFailure));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Resend send threw after retries (template {TemplateId}).",
                message.ProviderTemplateId);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Transient(BusinessErrorMessage.EmailProviderTransientFailure));
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                string? providerId = null;
                try
                {
                    var body = await response.Content.ReadFromJsonAsync<ResendSendResponse>(
                        cancellationToken: cancellationToken);
                    providerId = body?.Id;
                }
                catch (JsonException)
                {
                    // 2xx with an unexpected body — the send went through;
                    // fall back to a synthetic receipt id below.
                }

                return BusinessResult.Success(new EmailSentReceipt(
                    ProviderMessageId: string.IsNullOrWhiteSpace(providerId)
                        ? $"resend:unparsed:{Guid.NewGuid():N}"
                        : providerId,
                    SentAt: DateTimeOffset.UtcNow));
            }

            // Body deliberately not read into the failure path (can echo
            // recipient PII). Status code only.
            var transient = (int)response.StatusCode is 408 or 429 or >= 500 and <= 599;
            logger.LogWarning(
                "Resend send failed with {StatusCode} (template {TemplateId}, transient={Transient}).",
                (int)response.StatusCode, message.ProviderTemplateId, transient);

            return BusinessResult.Failure<EmailSentReceipt>(transient
                ? Error.Transient(BusinessErrorMessage.EmailProviderTransientFailure)
                : Error.Permanent(BusinessErrorMessage.EmailProviderPermanentFailure));
        }
    }

    /// <summary>Wire shape of <c>POST /emails</c> — snake_case per Resend's API.</summary>
    internal sealed record ResendSendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] IReadOnlyList<string> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("html")] string? Html,
        [property: JsonPropertyName("reply_to")] string? ReplyTo,
        [property: JsonPropertyName("attachments")] IReadOnlyList<ResendAttachment>? Attachments);

    internal sealed record ResendAttachment(
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("content")] string Content);

    internal sealed record ResendSendResponse(
        [property: JsonPropertyName("id")] string? Id);
}
