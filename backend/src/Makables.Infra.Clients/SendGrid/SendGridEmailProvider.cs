using Makables.Core.Domain.Common;
using Makables.Core.Domain.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Makables.Infra.Clients.SendGrid;

/// <summary>
/// SendGrid Dynamic-Templates adapter per ADR 0019 (amended T-0028).
///
/// The adapter is locale-agnostic â€” the caller (<c>EmailSendService</c>)
/// has already resolved the language and looked up the right
/// <c>EmailTemplateTranslation</c>. We pass <c>Subject</c> + the data
/// dictionary as template-substitutions, and ship <c>PlainTextBody</c>
/// as the plain-text alternate so multipart/alternative is well-formed
/// even when the SendGrid template fails to render HTML for any reason.
///
/// Transient failures (5xx / network) are retried by the Polly policy
/// configured in DI (<see cref="MakablesClientsExtensions.AddMakablesClients"/>).
/// A successful return code yields a <c>X-Message-Id</c> header SendGrid
/// uses to correlate event-webhook callbacks (bounce / complaint â€”
/// follow-up ticket).
/// </summary>
public sealed class SendGridEmailProvider(
    ISendGridClient client,
    IOptions<SendGridOptions> options,
    ResiliencePipeline<Response> retryPipeline,
    ILogger<SendGridEmailProvider> logger) : IEmailProvider
{
    public const string ProviderCode = "sendgrid";

    public string Code => ProviderCode;

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

        var sgMessage = MailHelper.CreateSingleTemplateEmail(
            from: new EmailAddress(fromAddress, fromName),
            to: new EmailAddress(message.ToAddress, message.ToName),
            templateId: message.ProviderTemplateId,
            dynamicTemplateData: message.Data);

        // Plain-text alternate. SendGrid will use this when the recipient
        // mail client falls back from the HTML part, or when the template
        // is misconfigured. Always shipped so the message is never
        // bodyless.
        sgMessage.PlainTextContent = message.PlainTextBody;

        if (!string.IsNullOrWhiteSpace(message.ReplyToAddress))
            sgMessage.ReplyTo = new EmailAddress(message.ReplyToAddress);

        Response response;
        try
        {
            response = await retryPipeline.ExecuteAsync(
                async ct => await client.SendEmailAsync(sgMessage, ct),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SendGrid SendEmailAsync threw after retries for template {TemplateId}.",
                message.ProviderTemplateId);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Transient(BusinessErrorMessage.EmailProviderTransientFailure,
                    ex.Message));
        }

        if (!IsSuccessStatusCode(response.StatusCode))
        {
            var body = await SafeReadBodyAsync(response, cancellationToken);
            logger.LogWarning(
                "SendGrid SendEmailAsync returned {Status}; body={Body}",
                (int)response.StatusCode, body);
            return BusinessResult.Failure<EmailSentReceipt>(
                IsTransientStatusCode(response.StatusCode)
                    ? Error.Transient(BusinessErrorMessage.EmailProviderTransientFailure, body)
                    : Error.Permanent(BusinessErrorMessage.EmailProviderPermanentFailure, body));
        }

        var messageId = ExtractMessageId(response);
        return BusinessResult.Success(new EmailSentReceipt(messageId, DateTimeOffset.UtcNow));
    }

    private static bool IsSuccessStatusCode(System.Net.HttpStatusCode code) =>
        (int)code >= 200 && (int)code < 300;

    private static bool IsTransientStatusCode(System.Net.HttpStatusCode code) =>
        (int)code is 408 or 429 or >= 500 and <= 599;

    private static async Task<string> SafeReadBodyAsync(Response response, CancellationToken ct)
    {
        try
        {
            return await response.Body.ReadAsStringAsync(ct);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractMessageId(Response response)
    {
        if (response.Headers is null) return string.Empty;
        if (response.Headers.TryGetValues("X-Message-Id", out var values))
        {
            foreach (var v in values) return v;
        }
        return string.Empty;
    }
}
