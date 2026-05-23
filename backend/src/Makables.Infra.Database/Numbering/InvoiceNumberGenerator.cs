using Makables.Core.Domain.Common;
using Makables.Core.Domain.Numbering;

namespace Makables.Infra.Database.Numbering;

/// <summary>
/// Postgres-backed gap-free invoice number allocator per ADR 0009. CZ tax
/// law requires gap-free invoice numbering; this guarantee comes from the
/// allocation being inside the surrounding <c>IssueInvoice</c> command's
/// transaction — any failure rolls back without consuming the number.
/// </summary>
public sealed class InvoiceNumberGenerator(MakablesDbContext db, IClock clock)
    : IInvoiceNumberGenerator
{
    public Task<string> NextAsync(string countryCode, int year, CancellationToken cancellationToken) =>
        NumberingSequenceAllocator.AllocateAsync(
            db, clock, countryCode, NumberingScope.Invoice, year, cancellationToken);
}
