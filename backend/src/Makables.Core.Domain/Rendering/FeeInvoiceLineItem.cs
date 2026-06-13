namespace Makables.Core.Domain.Rendering;

/// <summary>
/// One line on a Fee invoice's PDF: the claimed order's number + the
/// platform fee charged on it (minor units). The <c>Invoice</c> row carries
/// only the summed total, so the renderer takes these line items separately
/// to print "Provize za zprostředkování — obj. {orderNumber}" per order
/// (T-0102b §C.9).
/// </summary>
public sealed record FeeInvoiceLineItem(string OrderNumber, long FeeAmountMinor);
