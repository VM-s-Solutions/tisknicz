namespace Makables.Core.Domain.Numbering;

/// <summary>
/// Derive the payout-batch number for a given country + date per ADR 0009.
/// Format <c>VYP-{CC}-{YYYY}-W{ww}</c> where <c>ww</c> is the ISO 8601 week
/// number. Does NOT consult the <see cref="NumberingSequence"/> table —
/// uniqueness is enforced by the <c>(country_code, batch_number)</c> unique
/// index on the payout batch table.
/// </summary>
public interface IPayoutBatchNumberGenerator
{
    string For(string countryCode, DateOnly batchDate);
}
