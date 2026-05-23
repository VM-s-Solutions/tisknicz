namespace Makables.Core.Domain.Numbering;

/// <summary>
/// Distinguishes the three sequence families per ADR 0009. Stored as string
/// in the <see cref="NumberingSequence"/> row so a future fourth scope is
/// additive without an enum change rippling through.
/// </summary>
public static class NumberingScope
{
    public const string Order = "order";
    public const string Invoice = "invoice";
    public const string PayoutBatch = "payout_batch";
}
