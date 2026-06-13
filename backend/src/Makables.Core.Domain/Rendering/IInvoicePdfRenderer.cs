using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;

namespace Makables.Core.Domain.Rendering;

/// <summary>
/// Renders an <see cref="Invoice"/> into a PDF byte array per T-0068b
/// locked decision 3 — invoice-specific interface, NOT a generic
/// <c>IPdfRenderer&lt;T&gt;</c>. The renderer reads
/// <see cref="Invoice.InvoicingMode"/> (snapshotted at issuance time per
/// T-0068a locked decision 2) and branches to the right document
/// template; the optional <paramref name="country"/> supplies the
/// platform IBAN for SPAYD QR rendering (locked decision 4 — renderer
/// skips the QR when <see cref="CountryConfiguration.PlatformIban"/> is
/// null).
///
/// <para>
/// <b>Determinism contract.</b> Same <see cref="Invoice"/> in →
/// byte-identical PDF out. The renderer MUST NOT read <c>DateTime.Now</c>
/// for any footer / "rendered on" text — that data comes from
/// <see cref="Invoice.IssueDate"/>. Determinism is what makes the blob
/// overwrite at <c>IssueInvoice.Handler</c> step 9 safe (a retry writes
/// the same bytes). Renderer breaks this and the
/// idempotent-retry-does-not-double-upload test catches it.
/// </para>
///
/// <para>
/// <b>InvoicingMode coverage at T-0068b.</b>
/// <list type="bullet">
///   <item><see cref="InvoicingMode.None"/> → Doklad o prodeji template.</item>
///   <item><see cref="InvoicingMode.StandardVat"/> → § 29 daňový doklad template.</item>
///   <item><see cref="InvoicingMode.ReverseCharge"/> → throws
///     <see cref="NotImplementedException"/>; caller translates to
///     <see cref="Common.BusinessErrorMessage.InvoicingModeNotImplemented"/>.</item>
///   <item><see cref="InvoicingMode.StrictFiscalReporting"/> → same.</item>
/// </list>
/// </para>
/// </summary>
public interface IInvoicePdfRenderer
{
    /// <summary>
    /// Render the invoice as a PDF. Returns the bytes (typically 50-150
    /// KB); the caller (<c>IssueInvoice.Handler</c>) wraps the bytes in
    /// a stream and uploads via <see cref="Storage.IBlobStorageClient"/>.
    /// </summary>
    /// <exception cref="NotImplementedException">
    /// <see cref="Invoice.InvoicingMode"/> is
    /// <see cref="InvoicingMode.ReverseCharge"/> or
    /// <see cref="InvoicingMode.StrictFiscalReporting"/>.
    /// </exception>
    Task<byte[]> RenderAsync(
        Invoice invoice,
        CountryConfiguration country,
        CancellationToken cancellationToken);

    /// <summary>
    /// Render a Fee invoice (<c>InvoiceType.Fee</c>) per T-0102b §C.9 — the
    /// <c>ProvizniDokladDocument</c> template. The <see cref="Invoice"/> row
    /// carries only the summed fee total, so the per-order
    /// <paramref name="lineItems"/> ("Provize za zprostředkování — obj.
    /// {orderNumber}") are passed separately. Determinism contract holds:
    /// every value comes from the snapshotted invoice + the supplied line
    /// items; no <c>DateTime.Now</c>.
    /// </summary>
    Task<byte[]> RenderFeeAsync(
        Invoice invoice,
        IReadOnlyList<FeeInvoiceLineItem> lineItems,
        CountryConfiguration country,
        CancellationToken cancellationToken);
}
