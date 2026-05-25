namespace Makables.Core.Domain.Email;

/// <summary>
/// Read access to <see cref="EmailTemplateTranslation"/> rows. T-0028
/// <c>EmailComposer</c> picks the row for the resolved language and
/// falls back through the language chain documented in
/// <c>ILanguageResolver</c> when the exact row is missing.
/// </summary>
public interface IEmailTemplateTranslationRepository
{
    /// <summary>
    /// Returns the translation for (<paramref name="emailTemplateId"/>,
    /// <paramref name="languageCode"/>), or <c>null</c> if none. Active rows only.
    /// </summary>
    Task<EmailTemplateTranslation?> GetAsync(
        string emailTemplateId,
        string languageCode,
        CancellationToken cancellationToken);
}
