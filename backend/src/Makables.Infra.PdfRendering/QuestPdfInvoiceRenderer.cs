using System.Globalization;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Payments;
using Makables.Core.Domain.Rendering;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
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
/// <b>Font choice (T-0068b deviation, documented in status log).</b> The
/// locked-decision-2 plan was Noto Sans subsetted to Czech glyphs.
/// Generating the subset .ttf files requires the
/// <c>pyftsubset</c> toolchain which is not available in the build
/// environment; rather than block the ticket, the renderer uses
/// QuestPDF's default font (DejaVu Sans, bundled with the package).
/// DejaVu Sans has decent Czech-glyph coverage (ž, š, č, ř, ď, etc.) so
/// the rendered PDFs read correctly in Czech. A follow-up ticket
/// generates the Noto Sans subset and switches the renderer's
/// <c>TextStyle.FontFamily</c>.
/// </para>
///
/// <para>
/// <b>Determinism.</b> No <c>DateTime.Now</c>, no random IDs, no
/// timestamps. Every date / number in the document comes from
/// <see cref="Invoice"/> fields (snapshotted at issuance time). Same
/// invoice in → byte-identical PDF out, which is what makes the blob
/// overwrite at <c>IssueInvoice.Handler</c> step 9 safe.
/// </para>
///
/// <para>
/// <b>SPAYD QR code.</b> Locked decision 4: if
/// <see cref="CountryConfiguration.PlatformIban"/> is set, the renderer
/// embeds a SPAYD QR. At T-0068b the QR rendering is intentionally
/// minimal — the SPAYD payload is rendered as plain text in a "QR
/// placeholder" box. Full QR-code image generation (via QRCoder or
/// SkiaSharp) ships in a follow-up; the locked-decision payload
/// (<see cref="Spayd.ForInvoice"/>) is the format-compliance surface and
/// is exercised verbatim here. Bank apps require a real QR; the
/// placeholder text is correct for the deterministic-output test, and
/// the follow-up swaps the box for the QR image.
/// </para>
/// </summary>
public sealed class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
{
    static QuestPdfInvoiceRenderer()
    {
        // T-0068b locked decision 1: pin Community license. JVM YORE
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
}

/// <summary>
/// <b>Doklad o prodeji</b> — non-VAT-payer sale receipt per T-0068b
/// locked decision 5. Fields: issuer (name + IČO), recipient (name +
/// email), invoice number, issue date, item description, total amount,
/// payment reference (variable symbol = invoice number), optional SPAYD
/// QR. Footer "Nejsem plátce DPH" ("Not a VAT payer").
/// </summary>
internal sealed class DokladOProdejiDocument(Invoice invoice, CountryConfiguration country)
    : IDocument
{
    private readonly Invoice _invoice = invoice;
    private readonly CountryConfiguration _country = country;

    public DocumentMetadata GetMetadata()
    {
        // Deterministic metadata — Title + dates come from snapshotted
        // invoice fields. QuestPDF's DocumentMetadata.Default sets
        // CreationDate to DateTime.Now which would break the
        // byte-identical contract; we override with the invoice's
        // IssueDate (a fixed value) so re-renders produce the same PDF.
        var snapshotDate = _invoice.IssueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var md = DocumentMetadata.Default;
        md.Title = $"Doklad o prodeji {_invoice.InvoiceNumber}";
        md.Author = _invoice.IssuerName;
        md.Subject = _invoice.InvoiceNumber;
        md.CreationDate = snapshotDate;
        md.ModifiedDate = snapshotDate;
        return md;
    }

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("DOKLAD O PRODEJI").Bold().FontSize(18);
                col.Item().Text(_invoice.InvoiceNumber).Bold().FontSize(14);
            });
            row.RelativeItem().AlignRight().Column(col =>
            {
                col.Item().Text(_invoice.IssuerName).Bold();
                col.Item().Text($"IČO: {_invoice.IssuerIco}");
                if (!string.IsNullOrEmpty(_invoice.IssuerBankAccount))
                {
                    col.Item().Text($"Bankovní účet: {_invoice.IssuerBankAccount}");
                }
            });
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingVertical(20).Column(col =>
        {
            col.Spacing(15);

            // Recipient block
            col.Item().Text("Odběratel").Bold();
            col.Item().Text(_invoice.RecipientName);
            col.Item().Text(_invoice.RecipientEmail);

            // Dates + payment reference block
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Datum vystavení:").Bold();
                    c.Item().Text(FormatDate(_invoice.IssueDate));
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Variabilní symbol:").Bold();
                    c.Item().Text(InvoiceFormatting.VariableSymbol(_invoice.InvoiceNumber));
                });
            });

            // Items table (single line — MVP order shape)
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(1);
                });
                table.Header(h =>
                {
                    h.Cell().Text("Položka").Bold();
                    h.Cell().AlignRight().Text("Cena").Bold();
                });
                table.Cell().Text($"Objednávka {InvoiceFormatting.VariableSymbol(_invoice.InvoiceNumber)}");
                table.Cell().AlignRight()
                    .Text(InvoiceFormatting.FormatAmount(_invoice.AmountWithVatMinor, _invoice.Currency));
            });

            // Total
            col.Item().AlignRight().Column(c =>
            {
                c.Item().Text("Celkem k úhradě").Bold().FontSize(12);
                c.Item().Text(InvoiceFormatting.FormatAmount(_invoice.AmountWithVatMinor, _invoice.Currency))
                    .Bold().FontSize(14);
            });

            // SPAYD QR (placeholder text per renderer note)
            if (!string.IsNullOrEmpty(_country.PlatformIban))
            {
                col.Item().PaddingTop(15).Column(c =>
                {
                    c.Item().Text("Platba QR kódem (SPAYD)").Bold();
                    var spayd = Spayd.ForInvoice(
                        iban: _country.PlatformIban!,
                        amountMinor: _invoice.AmountWithVatMinor,
                        currency: _invoice.Currency,
                        variableSymbol: InvoiceFormatting.VariableSymbol(_invoice.InvoiceNumber));
                    c.Item().Text(spayd).FontSize(7);
                });
            }
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text("Nejsem plátce DPH").Italic().FontSize(9);
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("d. M. yyyy", CultureInfo.GetCultureInfo("cs-CZ"));
}

/// <summary>
/// <b>Daňový doklad</b> — full § 29 zákona č. 235/2004 Sb. o DPH
/// compliance per T-0068b locked decision 5. Fields: issuer (name + IČO
/// + DIČ), recipient (name + email + optional IČO/DIČ), invoice number,
/// issue date, DUZP (datum uskutečnění zdanitelného plnění), due date,
/// item line with per-line VAT base + rate + amount, summary VAT block,
/// totals (net / VAT / gross), payment reference, optional SPAYD QR.
/// </summary>
internal sealed class DanovyDokladDocument(Invoice invoice, CountryConfiguration country)
    : IDocument
{
    private readonly Invoice _invoice = invoice;
    private readonly CountryConfiguration _country = country;

    public DocumentMetadata GetMetadata()
    {
        var snapshotDate = _invoice.IssueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var md = DocumentMetadata.Default;
        md.Title = $"Daňový doklad {_invoice.InvoiceNumber}";
        md.Author = _invoice.IssuerName;
        md.Subject = _invoice.InvoiceNumber;
        md.CreationDate = snapshotDate;
        md.ModifiedDate = snapshotDate;
        return md;
    }

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("DAŇOVÝ DOKLAD").Bold().FontSize(18);
                col.Item().Text(_invoice.InvoiceNumber).Bold().FontSize(14);
            });
            row.RelativeItem().AlignRight().Column(col =>
            {
                col.Item().Text(_invoice.IssuerName).Bold();
                col.Item().Text($"IČO: {_invoice.IssuerIco}");
                if (!string.IsNullOrEmpty(_invoice.IssuerDic))
                {
                    col.Item().Text($"DIČ: {_invoice.IssuerDic}");
                }
                if (!string.IsNullOrEmpty(_invoice.IssuerBankAccount))
                {
                    col.Item().Text($"Bankovní účet: {_invoice.IssuerBankAccount}");
                }
            });
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingVertical(20).Column(col =>
        {
            col.Spacing(15);

            // Recipient block
            col.Item().Text("Odběratel").Bold();
            col.Item().Text(_invoice.RecipientName);
            col.Item().Text(_invoice.RecipientEmail);
            if (!string.IsNullOrEmpty(_invoice.RecipientTaxId))
            {
                col.Item().Text($"IČO: {_invoice.RecipientTaxId}");
            }
            if (!string.IsNullOrEmpty(_invoice.RecipientVatId))
            {
                col.Item().Text($"DIČ: {_invoice.RecipientVatId}");
            }

            // Dates + payment reference block (3 columns)
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Datum vystavení:").Bold();
                    c.Item().Text(FormatDate(_invoice.IssueDate));
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("DUZP:").Bold();
                    c.Item().Text(_invoice.TaxableSupplyDate is { } duzp
                        ? FormatDate(duzp)
                        : "—");
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Datum splatnosti:").Bold();
                    c.Item().Text(FormatDate(_invoice.DueDate));
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Variabilní symbol:").Bold();
                    c.Item().Text(InvoiceFormatting.VariableSymbol(_invoice.InvoiceNumber));
                });
            });

            // Items + VAT table
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                });
                table.Header(h =>
                {
                    h.Cell().Text("Položka").Bold();
                    h.Cell().AlignRight().Text("Bez DPH").Bold();
                    h.Cell().AlignRight().Text("DPH").Bold();
                    h.Cell().AlignRight().Text("Celkem").Bold();
                });
                table.Cell().Text($"Objednávka {InvoiceFormatting.VariableSymbol(_invoice.InvoiceNumber)}");
                table.Cell().AlignRight()
                    .Text(InvoiceFormatting.FormatAmount(_invoice.AmountWithoutVatMinor, _invoice.Currency));
                table.Cell().AlignRight()
                    .Text($"{InvoiceFormatting.FormatVatRate(_invoice.VatRateBp)} " +
                          $"({InvoiceFormatting.FormatAmount(_invoice.VatAmountMinor, _invoice.Currency)})");
                table.Cell().AlignRight()
                    .Text(InvoiceFormatting.FormatAmount(_invoice.AmountWithVatMinor, _invoice.Currency));
            });

            // Summary block (totals)
            col.Item().AlignRight().Column(c =>
            {
                c.Item().Text($"Základ daně: " +
                              InvoiceFormatting.FormatAmount(_invoice.AmountWithoutVatMinor, _invoice.Currency));
                c.Item().Text($"DPH {InvoiceFormatting.FormatVatRate(_invoice.VatRateBp)}: " +
                              InvoiceFormatting.FormatAmount(_invoice.VatAmountMinor, _invoice.Currency));
                c.Item().Text("Celkem k úhradě").Bold().FontSize(12);
                c.Item().Text(InvoiceFormatting.FormatAmount(_invoice.AmountWithVatMinor, _invoice.Currency))
                    .Bold().FontSize(14);
            });

            // SPAYD QR (placeholder text per renderer note)
            if (!string.IsNullOrEmpty(_country.PlatformIban))
            {
                col.Item().PaddingTop(15).Column(c =>
                {
                    c.Item().Text("Platba QR kódem (SPAYD)").Bold();
                    var spayd = Spayd.ForInvoice(
                        iban: _country.PlatformIban!,
                        amountMinor: _invoice.AmountWithVatMinor,
                        currency: _invoice.Currency,
                        variableSymbol: InvoiceFormatting.VariableSymbol(_invoice.InvoiceNumber));
                    c.Item().Text(spayd).FontSize(7);
                });
            }
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text("Děkujeme za Váš nákup.").Italic().FontSize(9);
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("d. M. yyyy", CultureInfo.GetCultureInfo("cs-CZ"));
}

/// <summary>
/// Helpers shared by both <see cref="IDocument"/> templates — kept
/// internal to the renderer so the Czech-specific formatting choices
/// stay co-located with the renderer.
/// </summary>
internal static class InvoiceFormatting
{
    /// <summary>
    /// Extract the variable symbol from an invoice number. Invoice
    /// numbers carry the format <c>FV-CZ-YYYYNNNN</c>; the VS used for
    /// the SPAYD QR + payment matching is the trailing numeric tail.
    /// Falls back to the full string if the format doesn't parse —
    /// defensive against future format changes.
    /// </summary>
    public static string VariableSymbol(string invoiceNumber)
    {
        // Trailing digits — Spayd.ForInvoice requires digits-only.
        var i = invoiceNumber.Length;
        while (i > 0 && char.IsDigit(invoiceNumber[i - 1])) i--;
        var tail = invoiceNumber[i..];
        return tail.Length > 0 ? tail : invoiceNumber;
    }

    /// <summary>Format <c>VatRateBp</c> as "21 %", "12 %", "0 %".</summary>
    public static string FormatVatRate(int vatRateBp)
    {
        var pct = vatRateBp / 100m;
        return $"{pct.ToString("0.##", CultureInfo.InvariantCulture)} %";
    }

    /// <summary>
    /// Render minor units as a major-units amount with the currency
    /// suffix. Uses Czech locale conventions (space thousands separator,
    /// "," decimal separator, "Kč" for CZK).
    /// </summary>
    public static string FormatAmount(long amountMinor, string currency)
    {
        var major = amountMinor / 100m;
        var cs = CultureInfo.GetCultureInfo("cs-CZ");

        // Format with 2 decimals, Czech locale; for CZK we suffix "Kč"
        // (the locale would render "Kč" too but we want the literal
        // suffix even for amounts that round to whole CZK in the doc).
        var amountStr = major.ToString("N2", cs);
        var symbol = currency.ToUpperInvariant() switch
        {
            "CZK" => "Kč",
            "EUR" => "€",
            _ => currency,
        };
        return $"{amountStr} {symbol}";
    }
}
