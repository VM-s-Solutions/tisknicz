using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Email;

/// <summary>
/// Subject + plain-text body of an <see cref="EmailTemplate"/> in one
/// language. The actual HTML rendering happens inside SendGrid using
/// dynamic-template substitution; this row drives:
/// <list type="bullet">
///   <item><description>The email <c>Subject</c> header (SendGrid uses the value of <see cref="Subject"/> verbatim when sent as a dynamic-template variable; per ADR 0019 we put it on the SendGrid template itself but ship the substring DB-side too for admin previews).</description></item>
///   <item><description>The plain-text alternate body (multipart/alternative; some clients show only this).</description></item>
/// </list>
///
/// Composite uniqueness is enforced on (<see cref="EmailTemplateId"/>,
/// <see cref="LanguageCode"/>). Adding a language is a row insert per
/// template.
/// </summary>
public sealed class EmailTemplateTranslation : Auditable
{
    public string EmailTemplateId { get; private set; } = default!;

    /// <summary>BCP-47 (e.g. <c>"cs-CZ"</c>, <c>"en-US"</c>). See <see cref="LanguageCode"/>.</summary>
    public string LanguageCode { get; private set; } = default!;

    public string Subject { get; private set; } = default!;
    public string PlainTextBody { get; private set; } = default!;

    private EmailTemplateTranslation() { }

    public static EmailTemplateTranslation Create(
        string id,
        string emailTemplateId,
        string languageCode,
        string subject,
        string plainTextBody,
        string countryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(emailTemplateId);
        if (!Common.LanguageCode.IsValid(languageCode))
            throw new ArgumentException($"LanguageCode '{languageCode}' is not a recognised BCP-47 tag.", nameof(languageCode));
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(plainTextBody);
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCode));

        return new EmailTemplateTranslation
        {
            Id = id,
            EmailTemplateId = emailTemplateId,
            LanguageCode = languageCode,
            Subject = subject.Trim(),
            PlainTextBody = plainTextBody,
            CountryCode = countryCode.ToUpperInvariant(),
        };
    }

    public EmailTemplateTranslation Update(string subject, string plainTextBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(plainTextBody);
        Subject = subject.Trim();
        PlainTextBody = plainTextBody;
        return this;
    }
}
