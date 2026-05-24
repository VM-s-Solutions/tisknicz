using Makables.Core.Domain.Common;
using Makables.Core.Domain.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Makables.Infra.Clients.SendGrid;

/// <summary>
/// SendGrid Dynamic-Templates adapter per ADR 0019 (amended T-0028).
///
/// The adapter is locale-agnostic — the caller (<c>EmailSendService</c>)
/// has already resolved the language and looked up the right
/// <c>EmailTemplateTranslation</c>. We pass the resolved subject through
/// <c>sgMessage.Subject</c> AND inject it into the dynamic-template
/// data dictionary as <c>subject</c> so the SendGrid template can render
/// it inside the HTML body too. <c>PlainTextBody</c> ships as the
/// multipart/alternative part so the message is never bodyless.
///
/// Transient failures (5xx / 429 / 408 / connection errors) are retried
/// by the Polly policy in DI (<see cref="MakablesClientsExtensions.AddMakablesClients"/>).
/// Per-attempt timeout caps a stuck connection (T-0028 sec reviewer M-4).
/// A successful 2xx yields the <c>X-Message-Id</c> response header as
/// the receipt id; bounce / event-webhook correlation lands in a
/// follow-up ticket.
///
/// SECURITY (T-0028 sec reviewer B-1): the SendGrid response body is NOT
/// returned in <see cref="BusinessResult{T}"/> failures and is NOT logged
/// as a structured property — SendGrid 4xx responses can echo recipient
/// addresses and (rarely) request headers. We surface only the status
/// code; the body is logged at Debug under a name the redaction masker
/// already covers, and never propagated up the stack.
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

        // Inject the resolved subject into the dynamic-template data so the
        // SendGrid template can render it in the HTML body. Also set the
        // top-level Subject header so the SDK has a non-template fallback.
        // T-0028 CQ reviewer M-1.
        var data = new Dictionary<string, object>(message.Data, StringComparer.Ordinal)
        {
            ["subject"] = message.Subject,
        };

        var sgMessage = MailHelper.CreateSingleTemplateEmail(
            from: new EmailAddress(fromAddress, fromName),
            to: new EmailAddress(message.ToAddress, message.ToName),
            templateId: message.ProviderTemplateId,
            dynamicTemplateData: data);

        sgMessage.SetSubject(message.Subject);
        sgMessage.PlainTextContent = message.PlainTextBody;

        if (!string.IsNullOrWhiteSpace(message.ReplyToAddress))
            sgMessage.ReplyTo = new EmailAddress(message.ReplyToAddress);

        // Hard per-call timeout caps a stuck connection. Linked to the
        // caller's cancellation so a host shutdown still wins.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.PerSendTimeoutSeconds)));

        Response response;
        try
        {
            response = await retryPipeline.ExecuteAsync(
                async ct => await client.SendEmailAsync(sgMessage, ct),
                timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Per-call timeout fired; outbox-level retry will pick the row up.
            logger.LogWarning("SendGrid SendEmailAsync timed out after {Seconds}s for template {TemplateId}.",
                opts.PerSendTimeoutSeconds, message.ProviderTemplateId);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Transient(BusinessErrorMessage.EmailProviderTransientFailure));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SendGrid SendEmailAsync threw after retries for template {TemplateId}.",
                message.ProviderTemplateId);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Transient(BusinessErrorMessage.EmailProviderTransientFailure));
        }

        if (!IsSuccessStatusCode(response.StatusCode))
        {
            // Per B-1: do NOT propagate the body. Log at Debug under a name
            // the SensitivePropertyMasker already covers ("token") so a
            // SendGrid 4xx echoing the request can't leak.
            await LogResponseBodyAtDebugAsync(response, message.ProviderTemplateId, timeoutCts.Token);
            logger.LogWarning("SendGrid SendEmailAsync returned {Status} for template {TemplateId}.",
                (int)response.StatusCode, message.ProviderTemplateId);
            return BusinessResult.Failure<EmailSentReceipt>(
                IsTransientStatusCode(response.StatusCode)
                    ? Error.Transient(BusinessErrorMessage.EmailProviderTransientFailure)
                    : Error.Permanent(BusinessErrorMessage.EmailProviderPermanentFailure));
        }

        var messageId = ExtractMessageId(response);
        return BusinessResult.Success(new EmailSentReceipt(messageId, DateTimeOffset.UtcNow));
    }

    private static bool IsSuccessStatusCode(System.Net.HttpStatusCode code) =>
        (int)code >= 200 && (int)code < 300;

    private static bool IsTransientStatusCode(System.Net.HttpStatusCode code) =>
        (int)code is 408 or 429 or >= 500 and <= 599;

    private async Task LogResponseBodyAtDebugAsync(Response response, string templateId, CancellationToken ct)
    {
        if (!logger.IsEnabled(LogLevel.Debug)) return;
        try
        {
            var body = response.Body is null ? string.Empty : await response.Body.ReadAsStringAsync(ct);
            if (body.Length > 512) body = body[..512] + "…(truncated)";
            // Property name "TokenBody" matches the masker's "token" pattern,
            // so even when Debug is enabled the redaction layer scrubs the
            // value before it reaches a sink.
            logger.LogDebug("SendGrid response body for template {TemplateId}: {TokenBody}",
                templateId, body);
        }
        catch
        {
            // Body read failed; ignore — the warning above already captures
            // the failure mode that matters (status code).
        }
    }

    private static string ExtractMessageId(Response response)
    {
        if (response.Headers is null) return string.Empty;
        if (response.Headers.TryGetValues("X-Message-Id", out var values))
            return values.FirstOrDefault() ?? string.Empty;
        return string.Empty;
    }
}
