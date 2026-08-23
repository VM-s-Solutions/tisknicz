namespace Makables.Core.Domain.Invoices;

/// <summary>
/// The settlement channels the platform itself originates, for
/// <see cref="Invoice.PaymentMethod"/>. Everything else in that column is
/// a payment provider's own vocabulary passed through verbatim (Comgate
/// returns codes such as <c>CARD_CZ_CSOB_2</c>) — the domain deliberately
/// does not normalise those, because the provider owns the list and a
/// closed enum here would reject a code the provider added yesterday.
/// </summary>
public static class SettlementMethods
{
    /// <summary>
    /// A <see cref="InvoiceType.Fee"/> invoice is never transferred by the
    /// maker: <c>PayoutArtifactService</c> pays out
    /// <c>Order.MakerPayoutAmountMinor</c>, which is already the gross
    /// minus <c>Order.PlatformFeeAmountMinor</c>. The fee invoice
    /// documents that deduction, so it is settled the moment it is issued.
    /// </summary>
    public const string PayoutDeduction = "payout-deduction";
}
