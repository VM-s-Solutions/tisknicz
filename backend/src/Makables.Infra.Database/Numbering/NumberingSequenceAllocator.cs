using Makables.Core.Domain.Common;
using Makables.Core.Domain.Numbering;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Numbering;

/// <summary>
/// Shared allocator backing <see cref="OrderNumberGenerator"/> and
/// <see cref="InvoiceNumberGenerator"/>. Postgres-specific:
/// <c>SELECT ... FOR UPDATE</c> locks the row for the duration of the
/// surrounding transaction, serializing concurrent allocators.
///
/// The unit-of-work pipeline behavior commits the transaction; if the
/// surrounding command fails, the increment rolls back. This is how the
/// invoice number sequence stays gap-free per ADR 0009.
///
/// When no row exists yet for the <c>(country, scope, year)</c> triple,
/// the allocator creates it with <c>LastUsedValue=0</c> via standard EF
/// add semantics, then immediately increments to 1. The
/// CREATE-then-INCREMENT happens in a single transaction; race on first
/// allocation is resolved by Postgres returning a unique-key violation on
/// the loser, which the caller can retry. We treat first-allocation as an
/// uncontended case in practice (the row exists for the lifetime of the
/// country × scope × year — typically forever once seeded).
/// </summary>
internal static class NumberingSequenceAllocator
{
    public static async Task<string> AllocateAsync(
        MakablesDbContext db,
        IClock clock,
        string countryCode,
        string scope,
        int year,
        CancellationToken cancellationToken)
    {
        // The lock must be held inside the surrounding transaction. If the
        // caller has no transaction, EF Core in the Npgsql provider runs
        // each command in an implicit transaction; the FOR UPDATE would
        // release immediately. The pipeline behavior wraps commands in a
        // transaction; we trust the convention here.
        var normalizedCountry = countryCode.ToUpperInvariant();

        var row = await db.Set<NumberingSequence>()
            .FromSqlInterpolated($@"
                SELECT * FROM numbering_sequence
                WHERE country_code = {normalizedCountry}
                  AND scope = {scope}
                  AND year = {year}
                FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            row = NumberingSequence.StartAt(normalizedCountry, scope, year, clock.UtcNow);
            db.Set<NumberingSequence>().Add(row);
        }

        var formatted = row.Increment(clock.UtcNow);
        return formatted;
    }
}
