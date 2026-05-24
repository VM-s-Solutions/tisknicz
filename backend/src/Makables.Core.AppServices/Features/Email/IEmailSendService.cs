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
/// row. Owns the end-to-end logic (decode payload â†’ look up template â†’
/// resolve translation with fallback â†’ assemble <see cref="EmailMessage"/>
/// â†’ call <see cref="IEmailProvider"/>) so T-0029's
/// <c>ProcessOutboxFunction</c> stays a thin scheduler.
///
/// Per ADR 0019 (amended): the outbox is the single chokepoint into
/// emails â€” no handler ever calls <see cref="IEmailProvider"/> directly.
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

        // 1) Outbox event â†’ EmailTemplateType.
        if (!TryMapEventToTemplateType(outboxEventType, out var templateType))
        {
            logger.LogWarning("Unknown outbox event type {EventType}.", outboxEventType);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailEventTypeUnknown,
                    $"No email template is mapped to outbox event '{outboxEventType}'."));
        }

        // 2) Decode payload.
        OneTimeTokenOutboxPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OneTimeTokenOutboxPayload>(payloadJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Outbox payload JSON malformed for event {EventType}.", outboxEventType);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailPayloadInvalid,
                    "Outbox payload could not be decoded."));
        }
        if (payload is null
            || string.IsNullOrWhiteSpace(payload.Email)
            || string.IsNullOrWhiteSpace(payload.RawToken)
            || string.IsNullOrWhiteSpace(payload.LanguageCode))
        {
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailPayloadInvalid,
                    "Outbox payload is missing required fields."));
        }

        // 3) Template lookup.
        var template = await templates.GetByTypeAsync(templateType, cancellationToken);
        if (template is null)
        {
            logger.LogError("EmailTemplate row missing for type {TemplateType}.", templateType);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailTemplateNotFound,
                    $"No EmailTemplate row exists for type '{templateType}'."));
        }

        // 4) Translation lookup with one-step fallback to the platform default.
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

        // 5) Compose message.
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

        // 6) Dispatch.
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
        var path = pathTemplate.Replace("{token}", Uri.EscapeDataString(rawToken));
        var basePart = u.WebBaseUrl.TrimEnd('/');
        return path.StartsWith('/') ? basePart + path : $"{basePart}/{path}";
    }

    private static string SubstitutePlainTextPlaceholders(
        string body, IReadOnlyDictionary<string, object> data)
    {
        // The plain-text body is for the multipart/alternative part. We
        // substitute {{key}} placeholders directly so the plain-text part
        // is rendered without depending on SendGrid's HTML template engine.
        var result = body;
        foreach (var (key, value) in data)
            result = result.Replace($"{{{{{key}}}}}", value?.ToString() ?? string.Empty);
        return result;
    }
}
