using Makables.Core.Domain.Email;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Repositories;

public sealed class EmailTemplateTranslationRepository(MakablesDbContext db)
    : IEmailTemplateTranslationRepository
{
    public Task<EmailTemplateTranslation?> GetAsync(
        string emailTemplateId,
        string languageCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(emailTemplateId) || string.IsNullOrWhiteSpace(languageCode))
            return Task.FromResult<EmailTemplateTranslation?>(null);

        return db.Set<EmailTemplateTranslation>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.EmailTemplateId == emailTemplateId && t.LanguageCode == languageCode,
                cancellationToken);
    }
}
