using System.Text.Json;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Email;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Email;

/// <summary>
/// Composes + dispatches one transactional email from a queued outbox
/// row. Owns the end-to-end logic (decode payload → look up template →
/// resolve translation with fallback → assemble <see cref="EmailMessage"/>
/// → call <see cref="IEmailProvider"/>) so T-0029's
/// <c>ProcessOutboxFunction</c> stays a thin scheduler.
///
/// Per ADR 0019 (amended): the outbox is the single chokepoint into
/// emails — no handler ever calls <see cref="IEmailProvider"/> directly.
/// This service is the only consumer.
/// </summary>
public interface IEmailSendService
{
    /// <summary>
    /// Render and send the email described by an outbox row.
    /// </summary>
    /// <param name="outboxEventType">One of <see cref="OutboxEventTypes"/>.</param>
    /// <param name="payloadJson">JSON-encoded <see cref="OneTimeTokenOutboxPayload"/>.</param>
    Task<BusinessResult<EmailSentReceipt>> SendAsync(
        string outboxEventType,
        string payloadJson,
        CancellationToken cancellationToken);
}

public sealed class EmailSendService(
    IEmailTemplateRepository templates,
    IEmailTemplateTranslationRepository translations,
    IEmailProvider provider,
    IOptions<PublicAppUrlsOptions> urls,
    ILogger<EmailSendService> logger) : IEmailSendService
{
    public async Task<BusinessResult<EmailSentReceipt>> SendAsync(
        string outboxEventType,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxEventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        if (!TryMapEventToTemplateType(outboxEventType, out var templateType))
        {
            logger.LogWarning("Unknown outbox event type {EventType}.", outboxEventType);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailEventTypeUnknown,
                    $"No email template is mapped to outbox event '{outboxEventType}'."));
        }

        OneTimeTokenOutboxPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OneTimeTokenOutboxPayload>(payloadJson);
        }
        catch (JsonException ex)
        {
            // T-0028 CQ reviewer N-4: split from MissingFields so T-0029's
            // triage UI can distinguish "decode crashed" from "decode succeeded
            // but a field is blank".
            logger.LogWarning(ex, "Outbox payload JSON malformed for event {EventType}.", outboxEventType);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailPayloadMalformed,
                    "Outbox payload could not be JSON-decoded."));
        }
        if (payload is null
            || string.IsNullOrWhiteSpace(payload.Email)
            || string.IsNullOrWhiteSpace(payload.RawToken)
            || string.IsNullOrWhiteSpace(payload.LanguageCode))
        {
            logger.LogWarning("Outbox payload for {EventType} decoded but is missing required fields.", outboxEventType);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailPayloadMissingFields,
                    "Outbox payload is missing one or more required fields (Email, RawToken, LanguageCode)."));
        }

        var template = await templates.GetByTypeAsync(templateType, cancellationToken);
        if (template is null)
        {
            logger.LogError("EmailTemplate row missing for type {TemplateType}.", templateType);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailTemplateNotFound,
                    $"No EmailTemplate row exists for type '{templateType}'."));
        }

        var translation = await translations.GetAsync(template.Id, payload.LanguageCode, cancellationToken);
        if (translation is null && payload.LanguageCode != LanguageCode.DefaultFallback)
        {
            logger.LogInformation(
                "EmailTemplateTranslation missing for ({TemplateType}, {LanguageCode}); falling back to {Fallback}.",
                templateType, payload.LanguageCode, LanguageCode.DefaultFallback);
            translation = await translations.GetAsync(template.Id, LanguageCode.DefaultFallback, cancellationToken);
        }
        if (translation is null)
        {
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailTemplateTranslationMissing,
                    $"No translation exists for template '{template.Id}' in '{payload.LanguageCode}' or the fallback '{LanguageCode.DefaultFallback}'."));
        }

        var u = urls.Value;
        var actionUrl = BuildActionUrl(u, templateType, payload.RawToken);
        var data = new Dictionary<string, object>
        {
            ["action_url"] = actionUrl,
            ["expires_at"] = payload.ExpiresAt.ToString("u"),
            ["expires_at_unix"] = payload.ExpiresAt.ToUnixTimeSeconds(),
            ["language_code"] = payload.LanguageCode,
        };

        var message = new EmailMessage(
            ProviderTemplateId: template.ProviderTemplateId,
            LanguageCode: translation.LanguageCode,
            ToAddress: payload.Email,
            ToName: null,
            FromAddress: template.FromAddress ?? string.Empty,
            FromName: template.FromName,
            ReplyToAddress: template.ReplyToAddress,
            Subject: translation.Subject,
            PlainTextBody: SubstitutePlainTextPlaceholders(translation.PlainTextBody, data),
            Data: data);

        var result = await provider.SendAsync(message, cancellationToken);
        if (!result.IsSuccess)
        {
            logger.LogWarning(
                "Email send failed via {Provider}: {Code} ({Type})",
                provider.Code, result.Error!.Code, result.Error.Type);
        }
        return result;
    }

    private static bool TryMapEventToTemplateType(string outboxEventType, out EmailTemplateType templateType)
    {
        switch (outboxEventType)
        {
            case OutboxEventTypes.AuthMagicLinkSend:
                templateType = EmailTemplateType.AuthMagicLink;
                return true;
            case OutboxEventTypes.AuthEmailConfirmationSend:
                templateType = EmailTemplateType.AuthEmailConfirmation;
                return true;
            case OutboxEventTypes.AuthPasswordResetSend:
                templateType = EmailTemplateType.AuthPasswordReset;
                return true;
            default:
                templateType = default;
                return false;
        }
    }

    private static string BuildActionUrl(PublicAppUrlsOptions u, EmailTemplateType type, string rawToken)
    {
        var pathTemplate = type switch
        {
            EmailTemplateType.AuthMagicLink         => u.MagicLinkPath,
            EmailTemplateType.AuthEmailConfirmation => u.EmailConfirmationPath,
            EmailTemplateType.AuthPasswordReset     => u.PasswordResetPath,
            _ => throw new InvalidOperationException($"No URL path mapped for {type}."),
        };
        // The PublicAppUrlsOptionsValidator (startup) guarantees pathTemplate
        // contains {token} — so Replace is always a real substitution here.
        // Sec reviewer M-1 closed at the validator layer rather than via a
        // post-replace check.
        var path = pathTemplate.Replace(PublicAppUrlsOptions.TokenPlaceholder, Uri.EscapeDataString(rawToken));
        var basePart = u.WebBaseUrl.TrimEnd('/');
        return path.StartsWith('/') ? basePart + path : $"{basePart}/{path}";
    }

    // SECURITY: plain-text only. Do NOT reuse for HTML bodies — there is no
    // escaping, so any value-containing-{{key}} would produce an XSS-shaped
    // surprise. The current callers feed only URL / timestamp / language tag
    // values; revisit if that changes.
    private static string SubstitutePlainTextPlaceholders(
        string body, IReadOnlyDictionary<string, object> data)
    {
        var result = body;
        foreach (var (key, value) in data)
            result = result.Replace($"{{{{{key}}}}}", value?.ToString() ?? string.Empty);
        return result;
    }
}
