using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Email;

/// <summary>
/// Adapter for the transactional email provider per ADR 0019 (amended for
/// SendGrid). Lives in <c>Core.Domain</c> so application services can
/// depend on the interface without referencing the concrete adapter in
/// <c>Infra.Clients</c>.
///
/// The provider is locale-agnostic — the caller resolves the language and
/// picks the template translation; the provider just hands the assembled
/// <see cref="EmailMessage"/> to SendGrid (or AWS SES, etc. — keyed by
/// <see cref="Code"/>, see <c>CountryConfiguration.DefaultEmailProvider</c>).
/// </summary>
public interface IEmailProvider
{
    /// <summary>
    /// Keyed-service code (e.g. <c>"sendgrid"</c>). Matches the value
    /// of <c>CountryConfiguration.DefaultEmailProvider</c> so the
    /// per-country adapter selection runs through one factory.
    /// </summary>
    string Code { get; }

    /// <summary>
    /// Submit <paramref name="message"/> to the upstream provider.
    /// Returns a receipt on success, or a <see cref="BusinessResult{T}"/>
    /// failure with an appropriate <see cref="BusinessErrorMessage"/> code
    /// on a permanent failure (the outbox processor decides whether to retry
    /// based on the error type — transient or permanent).
    /// </summary>
    Task<BusinessResult<EmailSentReceipt>> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken);
}

/// <summary>
/// Self-contained dispatch unit. Carries everything the provider needs
/// to deliver one email — no further DB lookups.
/// </summary>
public sealed record EmailMessage(
    string ProviderTemplateId,
    string LanguageCode,
    string ToAddress,
    string? ToName,
    string FromAddress,
    string? FromName,
    string? ReplyToAddress,
    string Subject,
    string PlainTextBody,
    IReadOnlyDictionary<string, object> Data);

/// <summary>
/// Provider-issued acknowledgement of a successful send. <see cref="ProviderMessageId"/>
/// is the value future bounce / event webhooks will reference.
/// </summary>
public sealed record EmailSentReceipt(string ProviderMessageId, DateTimeOffset SentAt);
