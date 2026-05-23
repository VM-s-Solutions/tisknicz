namespace Makables.Core.Domain.Numbering;

/// <summary>
/// Hand out the next invoice number per ADR 0009. Format
/// <c>FV-{CC}-{YYYY}{NNNN}</c>. <b>Gap-free</b> by mechanism (allocation
/// happens inside the surrounding <c>IssueInvoice</c> command's transaction;
/// any failure rolls back without consuming a number).
/// </summary>
public interface IInvoiceNumberGenerator
{
    Task<string> NextAsync(string countryCode, int year, CancellationToken cancellationToken);
}
