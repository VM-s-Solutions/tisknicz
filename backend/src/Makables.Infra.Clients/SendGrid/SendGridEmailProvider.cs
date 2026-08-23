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

        // NOTE: <see cref="EmailMessage.HtmlBody"/> is deliberately NOT
        // forwarded. In SendGrid's model the dynamic template owns the HTML
        // and renders it remotely from Data; sending a locally-composed
        // `content` alongside a `template_id` asks the API to honour two
        // competing bodies. The locally-composed part exists for providers
        // that render nothing remotely (Resend, the active adapter per
        // T-0157). Flipping the CZ seed back to SendGrid therefore means
        // authoring the templates in SendGrid's editor — which the
        // `d-placeholder-*` ids in `email_templates` say has never happened.

        if (!string.IsNullOrWhiteSpace(message.ReplyToAddress))
            sgMessage.ReplyTo = new EmailAddress(message.ReplyToAddress);

        // T-0069 locked decision 7: provider only wires bytes; no blob /
        // storage knowledge in the SDK adapter. EmailSendService has
        // already downloaded the PDF into the Attachment record. SendGrid
        // requires base64-encoded content (the SDK's stream-based overload
        // base64s internally; the bytes-based AddAttachment expects an
        // already-encoded string).
        if (message.Attachment is not null)
        {
            sgMessage.AddAttachment(
                filename: message.Attachment.Filename,
                base64Content: Convert.ToBase64String(message.Attachment.Bytes),
                type: message.Attachment.MimeType);
        }

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
            // T-0069 locked decision 4: SendGrid's 30 MB attachment cap
            // typically surfaces as HTTP 413 Payload Too Large or a 4xx
            // with a "too large" / "size" body. Translate to
            // InvoicePdfAttachmentTooLarge Permanent so the outbox stalls
            // for ops investigation — retrying never resolves a fixed cap.
            // Check size-shape BEFORE the generic 4xx → Permanent fallback
            // so the more-specific code wins. The body is read inside the
            // size check ONLY when the carrier message had an attachment;
            // skips the read for vanilla 4xx responses to avoid waste.
            string? bodyForClassification = null;
            if (message.Attachment is not null
                && IsLikelyAttachmentSizeFailure(response.StatusCode))
            {
                bodyForClassification = await TryReadResponseBodyAsync(response, timeoutCts.Token);
                if (response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge
                    || ContainsSizeKeyword(bodyForClassification))
                {
                    logger.LogError(
                        "SendGrid rejected attachment as too large for template {TemplateId} (status {Status}). " +
                        "PDF size {BytesLen} bytes; SendGrid caps at 30 MB.",
                        message.ProviderTemplateId, (int)response.StatusCode,
                        message.Attachment.Bytes.Length);
                    return BusinessResult.Failure<EmailSentReceipt>(
                        Error.Permanent(BusinessErrorMessage.InvoicePdfAttachmentTooLarge));
                }
            }

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

    /// <summary>
    /// True when the response status code is in the range where SendGrid
    /// could plausibly be rejecting the attachment for size reasons.
    /// SendGrid's documented behaviour: 413 Payload Too Large is the
    /// dedicated code, but some gateway hops collapse the chain into a
    /// generic 400. We only enable the body sniff when the carrier
    /// message had an attachment in the first place — vanilla 4xx
    /// responses skip the read to avoid waste.
    /// </summary>
    private static bool IsLikelyAttachmentSizeFailure(System.Net.HttpStatusCode code) =>
        code == System.Net.HttpStatusCode.RequestEntityTooLarge
        || code == System.Net.HttpStatusCode.BadRequest;

    private static bool ContainsSizeKeyword(string? body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        // SendGrid surfaces messages like "Payload Too Large", "exceeds the
        // maximum", "too large". Lowercase + substring lookup is robust
        // enough for the dispatching decision (a misclassification just
        // falls through to the generic Permanent code which already triggers
        // ops attention).
        var lower = body.ToLowerInvariant();
        return lower.Contains("too large")
            || lower.Contains("payload too large")
            || lower.Contains("exceeds")
            || lower.Contains("maximum")
            || lower.Contains(" size");
    }

    private static async Task<string?> TryReadResponseBodyAsync(Response response, CancellationToken ct)
    {
        try
        {
            if (response.Body is null) return null;
            var body = await response.Body.ReadAsStringAsync(ct);
            // Cap the read so a malicious / runaway body can't blow up the
            // outbox processor's memory. The classification keywords are all
            // short; 1 KB is more than enough.
            return body.Length > 1024 ? body[..1024] : body;
        }
        catch
        {
            // Body read failed — fall through to the generic Permanent
            // classification (the same place we'd land for a vanilla 4xx).
            return null;
        }
    }

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
