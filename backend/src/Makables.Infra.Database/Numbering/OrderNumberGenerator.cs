using Makables.Core.Domain.Common;
using Makables.Core.Domain.Numbering;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Numbering;

/// <summary>
/// Postgres-backed allocator using <c>SELECT ... FOR UPDATE</c> per ADR 0009.
/// The lock is held for the duration of the surrounding transaction, so
/// concurrent allocators serialize and the increment only commits if the
/// command succeeds. Order numbers are NOT legally gap-free, but the
/// pattern is the same as for invoices to keep one implementation strategy
/// across all numbering scopes.
/// </summary>
public sealed class OrderNumberGenerator(MakablesDbContext db, IClock clock)
    : IOrderNumberGenerator
{
    public Task<string> NextAsync(string countryCode, int year, CancellationToken cancellationToken) =>
        NumberingSequenceAllocator.AllocateAsync(
            db, clock, countryCode, NumberingScope.Order, year, cancellationToken);
}
