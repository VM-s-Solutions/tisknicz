namespace Makables.Core.Domain.Numbering;

/// <summary>
/// Hand out the next order number per ADR 0009. Format
/// <c>M-{CC}-{YYYY}{NNNN}</c>. Not legally gap-free (orders that fail to
/// pay leave gaps); concurrent safety via <c>FOR UPDATE</c> in the
/// generator impl.
/// </summary>
public interface IOrderNumberGenerator
{
    Task<string> NextAsync(string countryCode, int year, CancellationToken cancellationToken);
}
