namespace Makables.Core.Domain.Email;

/// <summary>
/// Read access to <see cref="EmailTemplate"/> rows. The catalog is small
/// (one row per <see cref="EmailTemplateType"/>) so implementations
/// cache in-process for the lifetime of the request.
/// </summary>
public interface IEmailTemplateRepository
{
    /// <summary>
    /// Returns the template for <paramref name="type"/>, or <c>null</c>
    /// if no row has been seeded. Active rows only.
    /// </summary>
    Task<EmailTemplate?> GetByTypeAsync(EmailTemplateType type, CancellationToken cancellationToken);
}
