namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the maker-facing "Faktura provize za výplatní dávku"
/// email, enqueued per maker by the T-0102b <c>PayoutArtifactService</c> at
/// batch creation. The Fee invoice PDF is NOT in the payload — it is looked
/// up from the invoice's <c>PdfBlobPath</c> at SEND time (T-0069 attachment
/// pattern), so a not-yet-rendered invoice returns
/// <c>Transient(InvoiceNotYetRendered)</c> for outbox re-delivery.
///
/// <para>
/// <see cref="InvoiceId"/> is the lookup key for the send-time attachment.
/// <see cref="FeeAmountMinor"/> is the summed platform fee on the maker's
/// claimed orders (the invoice total). PascalCase JSON property names match
/// the order-email payload convention.
/// </para>
/// </summary>
public sealed record PayoutFeeInvoiceMakerEmailPayload(
    string InvoiceId,
    string MakerId,
    string MakerEmail,
    string BatchNumber,
    string InvoiceNumber,
    long FeeAmountMinor,
    string Currency,
    string ActionUrl,
    string LanguageCode);
