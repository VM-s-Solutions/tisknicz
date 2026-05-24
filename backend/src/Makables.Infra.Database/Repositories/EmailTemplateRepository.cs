using Makables.Core.Domain.Email;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Repositories;

public sealed class EmailTemplateRepository(MakablesDbContext db) : IEmailTemplateRepository
{
    public Task<EmailTemplate?> GetByTypeAsync(EmailTemplateType type, CancellationToken cancellationToken) =>
        db.Set<EmailTemplate>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Type == type, cancellationToken);
}
