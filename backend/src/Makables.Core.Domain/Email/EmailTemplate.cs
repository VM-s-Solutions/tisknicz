using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Email;

/// <summary>
/// Per-type master record for a transactional email. Carries the
/// language-invariant settings — provider template id, From address /
/// name override, ReplyTo — while the subject + plain-text body live on
/// <see cref="EmailTemplateTranslation"/> per language.
///
/// Modelled per ADR 0019 (amended) §"DB-backed translation". The
/// SendGrid dynamic-template id (<see cref="ProviderTemplateId"/>) is
/// what makes the actual SendGrid API call work; subject + plain body
/// in translations are used for previews and as the plain-text-alternate
/// part of multipart messages.
/// </summary>
public sealed class EmailTemplate : Auditable
{
    public EmailTemplateType Type { get; private set; }

    /// <summary>
    /// SendGrid dynamic-template id (a string starting with <c>d-</c>).
    /// Filled in by the admin UI / config seed once the template is
    /// authored in SendGrid; the row exists before that with a placeholder.
    /// </summary>
    public string ProviderTemplateId { get; private set; } = default!;

    /// <summary>
    /// From address override; <c>null</c> means "use SendGrid options
    /// default" (<see cref="Makables.Infra.Clients.SendGrid.SendGridOptions.DefaultFromAddress"/>).
    /// </summary>
    public string? FromAddress { get; private set; }

    /// <summary>Optional display name on the From header.</summary>
    public string? FromName { get; private set; }

    /// <summary>Optional Reply-To address (e.g. <c>podpora@makables.cz</c>).</summary>
    public string? ReplyToAddress { get; private set; }

    private EmailTemplate() { }

    public static EmailTemplate Create(
        string id,
        EmailTemplateType type,
        string providerTemplateId,
        string countryCode,
        string? fromAddress = null,
        string? fromName = null,
        string? replyToAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerTemplateId);
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCode));

        return new EmailTemplate
        {
            Id = id,
            Type = type,
            ProviderTemplateId = providerTemplateId,
            FromAddress = string.IsNullOrWhiteSpace(fromAddress) ? null : fromAddress,
            FromName = string.IsNullOrWhiteSpace(fromName) ? null : fromName,
            ReplyToAddress = string.IsNullOrWhiteSpace(replyToAddress) ? null : replyToAddress,
            CountryCode = countryCode.ToUpperInvariant(),
        };
    }

    public EmailTemplate UpdateProviderTemplateId(string providerTemplateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerTemplateId);
        ProviderTemplateId = providerTemplateId;
        return this;
    }

    public EmailTemplate UpdateFrom(string? fromAddress, string? fromName, string? replyToAddress)
    {
        FromAddress = string.IsNullOrWhiteSpace(fromAddress) ? null : fromAddress;
        FromName = string.IsNullOrWhiteSpace(fromName) ? null : fromName;
        ReplyToAddress = string.IsNullOrWhiteSpace(replyToAddress) ? null : replyToAddress;
        return this;
    }
}
