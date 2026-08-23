using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Rendering;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Makables.Infra.PdfRendering;

/// <summary>
/// QuestPDF-backed <see cref="IInvoicePdfRenderer"/> per T-0068b locked
/// decisions 1 + 5. Branches on <see cref="Invoice.InvoicingMode"/> to
/// the right Czech-language template:
/// <list type="bullet">
///   <item><see cref="InvoicingMode.None"/> →
///     <see cref="DokladOProdejiDocument"/> (non-VAT-payer sale receipt;
///     footer "Nejsem plátce DPH").</item>
///   <item><see cref="InvoicingMode.StandardVat"/> →
///     <see cref="DanovyDokladDocument"/> (full § 29 daňový doklad with
///     DUZP, VAT rate, base + VAT lines).</item>
///   <item><see cref="InvoicingMode.ReverseCharge"/> +
///     <see cref="InvoicingMode.StrictFiscalReporting"/> →
///     <see cref="NotImplementedException"/>; the caller (<c>IssueInvoice.Handler</c>)
///     catches and translates to
///     <see cref="Makables.Core.Domain.Common.BusinessErrorMessage.InvoicingModeNotImplemented"/>.</item>
/// </list>
///
/// <para>
/// The page itself — masthead, party blocks, metadata band, totals,
/// settlement stamp, footer — lives in <see cref="InvoiceDocumentBase"/>,
/// drawn with the light-theme tokens transcribed into
/// <see cref="InvoiceTheme"/>.
/// </para>
///
/// <para>
/// <b>Font choice (T-0068b deviation, documented in status log).</b> The
/// locked-decision-2 plan was Noto Sans subsetted to Czech glyphs.
/// Generating the subset .ttf files requires the
/// <c>pyftsubset</c> toolchain which is not available in the build
/// environment; rather than block the ticket, the renderer uses
/// QuestPDF's default font, whose Czech-glyph coverage (ž, š, č, ř, ď,
/// etc.) renders correctly. A follow-up ticket generates the Noto Sans
/// subset and switches the documents' <c>TextStyle.FontFamily</c>.
/// </para>
///
/// <para>
/// <b>Determinism.</b> No <c>DateTime.Now</c>, no random IDs, no
/// timestamps. Every date / number in the document comes from
/// <see cref="Invoice"/> fields (snapshotted at issuance time). Same
/// invoice in → byte-identical PDF out, which is what makes the blob
/// overwrite at <c>IssueInvoice.Handler</c> step 9 safe.
/// </para>
/// </summary>
public sealed class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
{
    static QuestPdfInvoiceRenderer()
    {
        // T-0068b locked decision 1: pin Community license. JVM Yore
        // qualifies (Czech s.r.o., revenue < $1M USD, < 10 employees, not
        // state-funded) per ADR 0025. PM revisits on revenue / headcount
        // milestone.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<byte[]> RenderAsync(
        Invoice invoice,
        CountryConfiguration country,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(country);

        IDocument document = invoice.InvoicingMode switch
        {
            InvoicingMode.None => new DokladOProdejiDocument(invoice, country),
            InvoicingMode.StandardVat => new DanovyDokladDocument(invoice, country),
            InvoicingMode.ReverseCharge =>
                throw new NotImplementedException(
                    "InvoicingMode.ReverseCharge is not implemented at T-0068b — " +
                    "caller translates to BusinessErrorMessage.InvoicingModeNotImplemented."),
            InvoicingMode.StrictFiscalReporting =>
                throw new NotImplementedException(
                    "InvoicingMode.StrictFiscalReporting is not implemented at T-0068b — " +
                    "Czech EET was repealed in 2023; mode exists for non-CZ expansion."),
            _ =>
                throw new NotImplementedException(
                    $"Unknown InvoicingMode: {invoice.InvoicingMode}."),
        };

        return Task.FromResult(document.GeneratePdf());
    }

    public Task<byte[]> RenderFeeAsync(
        Invoice invoice,
        IReadOnlyList<FeeInvoiceLineItem> lineItems,
        CountryConfiguration country,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(lineItems);
        ArgumentNullException.ThrowIfNull(country);

        IDocument document = new ProvizniDokladDocument(invoice, lineItems, country);
        return Task.FromResult(document.GeneratePdf());
    }
}
