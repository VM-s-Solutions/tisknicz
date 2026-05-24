using Makables.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Repositories;

public sealed class LoginAttemptBucketRepository(MakablesDbContext db) : ILoginAttemptBucketRepository
{
    public Task<LoginAttemptBucket?> GetAsync(string emailNormalized, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(emailNormalized))
            return Task.FromResult<LoginAttemptBucket?>(null);

        // LoginAttemptBucket inherits BaseEntity (not Auditable) so the
        // DbContext does NOT install a soft-delete query filter for it
        // (see MakablesDbContext.ApplySoftDeleteQueryFilters). Lookup is
        // a plain index probe on the email-id primary key.
        return db.Set<LoginAttemptBucket>()
            .FirstOrDefaultAsync(b => b.Id == emailNormalized, cancellationToken);
    }

    public void Add(LoginAttemptBucket bucket) => db.Set<LoginAttemptBucket>().Add(bucket);
}
