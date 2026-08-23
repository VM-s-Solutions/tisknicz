using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Payments;
using Makables.Core.Domain.Rendering;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Makables.Infra.PdfRendering;

/// <summary>
/// The chrome every Makables invoice shares: masthead, the two party
/// blocks, the metadata strip, totals, settlement stamp and footer. The
/// three concrete documents differ only in their title, who the recipient
/// is, and what the line-item table looks like — so those are the
/// abstract members and everything else is drawn once, here.
///
/// <para>
/// <b>Settlement is data, not an assumption.</b> The document renders
/// payment instructions (due date, variable symbol, SPAYD) only while
/// <see cref="Invoice.PaidOn"/> is null. When it is set — which is every
/// invoice the platform currently issues, because both families are
/// settled before the document exists — the same space becomes a receipt:
/// an UHRAZENO stamp with the date and channel, and the totals line reads
/// "Celkem" rather than "Celkem k úhradě". Printing "Celkem k úhradě 739
/// Kč" at a customer who had already paid was the reported defect.
/// </para>
///
/// <para>
/// <b>Determinism.</b> No <c>DateTime.Now</c>, no random ids. Every value
/// on the page comes from the snapshotted <see cref="Invoice"/>, so the
/// same invoice re-renders to the same bytes and the blob overwrite at
/// <c>IssueInvoice.Handler</c> stays safe.
/// </para>
/// </summary>
internal abstract class InvoiceDocumentBase(Invoice invoice, CountryConfiguration country) : IDocument
{
    protected Invoice Invoice { get; } = invoice;
    protected CountryConfiguration Country { get; } = country;

    /// <summary>Masthead title, e.g. "DOKLAD O PRODEJI".</summary>
    protected abstract string DocumentTitle { get; }

    /// <summary>PDF metadata title prefix, e.g. "Doklad o prodeji".</summary>
    protected abstract string MetadataTitle { get; }

    /// <summary>Heading over the recipient block — makers get a qualifier.</summary>
    protected virtual string RecipientLabel => "ODBĚRATEL";

    /// <summary>The line-item table. The only structurally different part.</summary>
    protected abstract void ComposeItems(IContainer container);

    /// <summary>Rows above the grand total, e.g. the VAT breakdown.</summary>
    protected virtual void ComposeTotalBreakdown(ColumnDescriptor column) { }

    protected bool IsSettled => Invoice.PaidOn is not null;

    /// <summary>
    /// How the order is named on the line item. The snapshotted
    /// <see cref="Invoice.OrderNumber"/> is what the customer sees
    /// everywhere else; rows issued before that column existed fall back
    /// to the invoice number's numeric tail, which is what those
    /// documents already printed.
    /// </summary>
    protected string OrderReference =>
        Invoice.OrderNumber
        ?? InvoiceFormatting.VariableSymbol(Invoice.InvoiceNumber);

    public DocumentMetadata GetMetadata()
    {
        // DocumentMetadata.Default stamps DateTime.Now, which would break
        // the byte-identical contract; override both dates with a fixed
        // value derived from the invoice.
        var snapshotDate = Invoice.IssueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var md = DocumentMetadata.Default;
        md.Title = $"{MetadataTitle} {Invoice.InvoiceNumber}";
        md.Author = Invoice.IssuerName;
        md.Subject = Invoice.InvoiceNumber;
        md.CreationDate = snapshotDate;
        md.ModifiedDate = snapshotDate;
        return md;
    }

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(42);
            page.DefaultTextStyle(t => t
                .FontSize(9.5f)
                .LineHeight(1.35f)
                .FontColor(InvoiceTheme.InkBody));

            page.Header().Element(ComposeMasthead);
            page.Content().Element(ComposeBody);
            page.Footer().Element(ComposeFooter);
        });
    }

    // === Masthead ==========================================================

    private void ComposeMasthead(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Row(brand =>
                {
                    brand.ConstantItem(28).AlignMiddle().Height(28).Svg(InvoiceTheme.LogoSvg);
                    brand.ConstantItem(10);
                    brand.AutoItem().AlignMiddle().Column(c =>
                    {
                        c.Item().Text("Makables")
                            .FontSize(17).Bold().FontColor(InvoiceTheme.InkTitle);
                        c.Item().Text("makables.cz")
                            .FontSize(8).FontColor(InvoiceTheme.InkFaint);
                    });
                });

                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text(DocumentTitle)
                        .FontSize(8.5f).Bold().LetterSpacing(0.16f)
                        .FontColor(InvoiceTheme.InkMuted);
                    c.Item().AlignRight().Text(Invoice.InvoiceNumber)
                        .FontSize(16).Bold().FontColor(InvoiceTheme.InkTitle);
                });
            });

            // The one place the primary is spent on structure.
            col.Item().PaddingTop(10).LineHorizontal(1.4f).LineColor(InvoiceTheme.BrandLine);
        });
    }

    // === Body ==============================================================

    private void ComposeBody(IContainer container)
    {
        container.PaddingTop(22).Column(col =>
        {
            col.Spacing(20);
            col.Item().Element(ComposeParties);
            col.Item().Element(ComposeMetaStrip);
            col.Item().Element(ComposeItems);
            col.Item().Element(ComposeSummary);

            if (!IsSettled)
            {
                col.Item().Element(ComposePaymentInstructions);
            }
        });
    }

    private void ComposeParties(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(ComposeIssuerBlock);
            row.ConstantItem(24);
            row.RelativeItem().Element(ComposeRecipientBlock);
        });
    }

    private void ComposeIssuerBlock(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Element(c => MicroHeader(c, "DODAVATEL"));
            col.Item().PaddingTop(6).Text(Invoice.IssuerName)
                .FontSize(11).Bold().FontColor(InvoiceTheme.InkTitle);

            foreach (var line in InvoiceFormatting.AddressLines(Invoice.IssuerAddress))
            {
                col.Item().Text(line).FontColor(InvoiceTheme.InkMuted);
            }

            col.Item().PaddingTop(4).Text($"IČO: {Invoice.IssuerIco}");
            if (!string.IsNullOrEmpty(Invoice.IssuerDic))
            {
                col.Item().Text($"DIČ: {Invoice.IssuerDic}");
            }
            else if (Invoice.InvoicingMode == InvoicingMode.None)
            {
                // Says the same thing as the footer note, but in the block a
                // reader checks when they are looking for a DIČ.
                col.Item().Text("Neplátce DPH").FontColor(InvoiceTheme.InkMuted);
            }

            if (!string.IsNullOrEmpty(Invoice.IssuerBankAccount))
            {
                col.Item().Text($"Bankovní účet: {Invoice.IssuerBankAccount}");
            }
        });
    }

    private void ComposeRecipientBlock(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Element(c => MicroHeader(c, RecipientLabel));
            col.Item().PaddingTop(6).Text(Invoice.RecipientName)
                .FontSize(11).Bold().FontColor(InvoiceTheme.InkTitle);
            col.Item().Text(Invoice.RecipientEmail).FontColor(InvoiceTheme.InkMuted);

            if (!string.IsNullOrEmpty(Invoice.RecipientTaxId))
            {
                col.Item().PaddingTop(4).Text($"IČO: {Invoice.RecipientTaxId}");
            }
            if (!string.IsNullOrEmpty(Invoice.RecipientVatId))
            {
                col.Item().Text($"DIČ: {Invoice.RecipientVatId}");
            }
        });
    }

    /// <summary>
    /// The dates band. Which cells appear is the settled/outstanding
    /// switch: a receipt shows when and how it was paid, an outstanding
    /// invoice shows when and against what reference to pay it.
    /// </summary>
    private void ComposeMetaStrip(IContainer container)
    {
        var cells = new List<(string Label, string Value)>
        {
            ("DATUM VYSTAVENÍ", InvoiceFormatting.FormatDate(Invoice.IssueDate)),
        };

        if (Invoice.TaxableSupplyDate is { } duzp)
        {
            cells.Add(("DUZP", InvoiceFormatting.FormatDate(duzp)));
        }

        if (Invoice.PaidOn is { } paidOn)
        {
            cells.Add(("DATUM ÚHRADY", InvoiceFormatting.FormatDate(paidOn)));
            var method = InvoiceFormatting.PaymentMethodLabel(Invoice.PaymentMethod);
            if (method.Length > 0)
            {
                cells.Add(("ZPŮSOB ÚHRADY", char.ToUpperInvariant(method[0]) + method[1..]));
            }
        }
        else
        {
            cells.Add(("DATUM SPLATNOSTI", InvoiceFormatting.FormatDate(Invoice.DueDate)));
            cells.Add(("VARIABILNÍ SYMBOL", InvoiceFormatting.VariableSymbol(Invoice.InvoiceNumber)));
        }

        container
            .Background(InvoiceTheme.BandFill)
            .Border(1).BorderColor(InvoiceTheme.HairlineSoft)
            .PaddingVertical(11).PaddingHorizontal(14)
            .Row(row =>
            {
                foreach (var (label, value) in cells)
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Element(x => MicroHeader(x, label));
                        c.Item().PaddingTop(3).Text(value)
                            .FontSize(10).SemiBold().FontColor(InvoiceTheme.InkTitle);
                    });
                }
            });
    }

    /// <summary>
    /// Totals, right-aligned in a fixed-width column so the money edge
    /// lines up with the table above it, followed by the settlement stamp.
    /// </summary>
    private void ComposeSummary(IContainer container)
    {
        container.AlignRight().Width(260).Column(col =>
        {
            ComposeTotalBreakdown(col);

            col.Item().PaddingTop(6).LineHorizontal(1).LineColor(InvoiceTheme.Hairline);

            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().AlignLeft().PaddingTop(3).Text(IsSettled ? "Celkem" : "Celkem k úhradě")
                    .FontSize(10.5f).SemiBold().FontColor(InvoiceTheme.InkTitle);
                row.AutoItem().AlignRight().Text(
                        InvoiceFormatting.FormatAmount(Invoice.AmountWithVatMinor, Invoice.Currency))
                    .FontSize(17).Bold().FontColor(InvoiceTheme.InkTitle);
            });

            if (Invoice.PaidOn is { } paidOn)
            {
                col.Item().PaddingTop(12).Element(c => ComposeSettlementStamp(c, paidOn));
            }
        });
    }

    private void ComposeSettlementStamp(IContainer container, DateOnly paidOn)
    {
        var method = InvoiceFormatting.PaymentMethodLabel(Invoice.PaymentMethod);
        var detail = method.Length > 0
            ? $"{InvoiceFormatting.FormatDate(paidOn)} · {method}"
            : InvoiceFormatting.FormatDate(paidOn);

        container
            .Background(InvoiceTheme.TintSuccess)
            .PaddingVertical(9).PaddingHorizontal(14)
            .Column(col =>
            {
                col.Item().Text("UHRAZENO")
                    .FontSize(10).Bold().LetterSpacing(0.14f)
                    .FontColor(InvoiceTheme.OnTintSuccess);
                col.Item().PaddingTop(1).Text($"Uhrazeno {detail}")
                    .FontSize(8.5f).FontColor(InvoiceTheme.OnTintSuccess);
            });
    }

    /// <summary>
    /// Only reachable while <see cref="Invoice.PaidOn"/> is null. Nothing
    /// the platform issues today lands here, which is the point — the
    /// payment instructions exist for an invoice that genuinely is
    /// outstanding, and never sit on a receipt.
    /// </summary>
    private void ComposePaymentInstructions(IContainer container)
    {
        container
            .Border(1).BorderColor(InvoiceTheme.Hairline)
            .Padding(14)
            .Column(col =>
            {
                col.Item().Element(c => MicroHeader(c, "PLATEBNÍ ÚDAJE"));
                col.Item().PaddingTop(6).Text(t =>
                {
                    t.Span("Variabilní symbol: ").FontColor(InvoiceTheme.InkMuted);
                    t.Span(InvoiceFormatting.VariableSymbol(Invoice.InvoiceNumber)).SemiBold();
                });
                col.Item().Text(t =>
                {
                    t.Span("Splatnost: ").FontColor(InvoiceTheme.InkMuted);
                    t.Span(InvoiceFormatting.FormatDate(Invoice.DueDate)).SemiBold();
                });

                if (!string.IsNullOrEmpty(Country.PlatformIban))
                {
                    col.Item().Text(t =>
                    {
                        t.Span("IBAN: ").FontColor(InvoiceTheme.InkMuted);
                        t.Span(Country.PlatformIban!).SemiBold();
                    });

                    // SPAYD payload as text — T-0068b shipped the placeholder
                    // and a real QR image is still a follow-up. Captioned so
                    // it reads as a machine payload rather than a stray
                    // string, and kept on the outstanding branch only, so no
                    // receipt carries it.
                    var spayd = Spayd.ForInvoice(
                        iban: Country.PlatformIban!,
                        amountMinor: Invoice.AmountWithVatMinor,
                        currency: Invoice.Currency,
                        variableSymbol: InvoiceFormatting.VariableSymbol(Invoice.InvoiceNumber));
                    col.Item().PaddingTop(10).Element(c => MicroHeader(c, "PLATEBNÍ ŘETĚZEC (SPAYD)"));
                    col.Item().PaddingTop(2).Text(spayd)
                        .FontSize(6.5f).FontColor(InvoiceTheme.InkFaint);
                }
            });
    }

    private void ComposeFooter(IContainer container)
    {
        var vatNote = Invoice.InvoicingMode == InvoicingMode.None
            ? "Nejsem plátce DPH."
            : "Daňový doklad podle § 29 zákona č. 235/2004 Sb., o DPH.";

        var identity = string.IsNullOrEmpty(Invoice.IssuerAddress)
            ? $"{Invoice.IssuerName}, IČO {Invoice.IssuerIco}"
            : $"{Invoice.IssuerName}, {Invoice.IssuerAddress}, IČO {Invoice.IssuerIco}";

        container.PaddingTop(14).Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(InvoiceTheme.HairlineSoft);
            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(vatNote).FontSize(8).FontColor(InvoiceTheme.InkMuted);
                    c.Item().Text(identity).FontSize(7.5f).FontColor(InvoiceTheme.InkFaint);
                });
                row.AutoItem().AlignBottom().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(InvoiceTheme.InkFaint));
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });
    }

    // === Shared primitives =================================================

    /// <summary>
    /// The iOS-style uppercase micro-header the design language uses for
    /// section labels — muted, letterspaced, never a heavier weight than
    /// the content it labels.
    /// </summary>
    protected static void MicroHeader(IContainer container, string text) =>
        container.Text(text)
            .FontSize(7.5f).SemiBold().LetterSpacing(0.14f)
            .FontColor(InvoiceTheme.InkFaint);

    /// <summary>Table header cell — hairline underline, no fill.</summary>
    protected static IContainer HeaderCell(IContainer container) =>
        container
            .BorderBottom(1).BorderColor(InvoiceTheme.Hairline)
            .PaddingBottom(6);

    /// <summary>Table body cell — hairline row separator.</summary>
    protected static IContainer BodyCell(IContainer container) =>
        container
            .BorderBottom(1).BorderColor(InvoiceTheme.HairlineSoft)
            .PaddingVertical(8);

    protected static void HeaderLabel(IContainer container, string text, bool alignRight = false)
    {
        var cell = HeaderCell(container);
        if (alignRight) cell = cell.AlignRight();
        cell.Text(text)
            .FontSize(7.5f).SemiBold().LetterSpacing(0.12f)
            .FontColor(InvoiceTheme.InkFaint);
    }
}

/// <summary>
/// <b>Doklad o prodeji</b> — the non-VAT-payer sale receipt (T-0068b
/// locked decision 5). One line for the order, no VAT columns, and the
/// "Nejsem plátce DPH" footer note.
/// </summary>
internal sealed class DokladOProdejiDocument(Invoice invoice, CountryConfiguration country)
    : InvoiceDocumentBase(invoice, country)
{
    protected override string DocumentTitle => "DOKLAD O PRODEJI";
    protected override string MetadataTitle => "Doklad o prodeji";

    protected override void ComposeItems(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4);
                c.RelativeColumn(1);
            });

            table.Header(h =>
            {
                h.Cell().Element(x => HeaderLabel(x, "POLOŽKA"));
                h.Cell().Element(x => HeaderLabel(x, "CENA", alignRight: true));
            });

            table.Cell().Element(BodyCell).Text($"Objednávka {OrderReference}");
            table.Cell().Element(BodyCell).AlignRight().Text(
                InvoiceFormatting.FormatAmount(Invoice.AmountWithVatMinor, Invoice.Currency));
        });
    }
}

/// <summary>
/// <b>Daňový doklad</b> — the full § 29 zákona č. 235/2004 Sb. document.
/// Adds the per-line VAT base / rate / total columns and the summary VAT
/// breakdown above the grand total.
/// </summary>
internal sealed class DanovyDokladDocument(Invoice invoice, CountryConfiguration country)
    : InvoiceDocumentBase(invoice, country)
{
    protected override string DocumentTitle => "DAŇOVÝ DOKLAD";
    protected override string MetadataTitle => "Daňový doklad";

    protected override void ComposeItems(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4);
                c.RelativeColumn(1.6f);
                c.RelativeColumn(1.2f);
                c.RelativeColumn(1.6f);
            });

            table.Header(h =>
            {
                h.Cell().Element(x => HeaderLabel(x, "POLOŽKA"));
                h.Cell().Element(x => HeaderLabel(x, "BEZ DPH", alignRight: true));
                h.Cell().Element(x => HeaderLabel(x, "SAZBA", alignRight: true));
                h.Cell().Element(x => HeaderLabel(x, "CELKEM", alignRight: true));
            });

            table.Cell().Element(BodyCell).Text($"Objednávka {OrderReference}");
            table.Cell().Element(BodyCell).AlignRight().Text(
                InvoiceFormatting.FormatAmount(Invoice.AmountWithoutVatMinor, Invoice.Currency));
            table.Cell().Element(BodyCell).AlignRight().Text(
                InvoiceFormatting.FormatVatRate(Invoice.VatRateBp));
            table.Cell().Element(BodyCell).AlignRight().Text(
                InvoiceFormatting.FormatAmount(Invoice.AmountWithVatMinor, Invoice.Currency));
        });
    }

    protected override void ComposeTotalBreakdown(ColumnDescriptor column)
    {
        SummaryRow(column, "Základ daně",
            InvoiceFormatting.FormatAmount(Invoice.AmountWithoutVatMinor, Invoice.Currency));
        SummaryRow(column, $"DPH {InvoiceFormatting.FormatVatRate(Invoice.VatRateBp)}",
            InvoiceFormatting.FormatAmount(Invoice.VatAmountMinor, Invoice.Currency));
    }

    private static void SummaryRow(ColumnDescriptor column, string label, string value)
    {
        column.Item().PaddingBottom(3).Row(row =>
        {
            row.RelativeItem().Text(label).FontColor(InvoiceTheme.InkMuted);
            row.AutoItem().AlignRight().Text(value).SemiBold();
        });
    }
}

/// <summary>
/// <b>Faktura — provize za zprostředkování</b> (T-0102b §C.9): the
/// platform-fee invoice raised on a maker's payout batch, one line per
/// claimed order. Always settled at issuance — the payout the maker
/// receives is already net of the fee — so it renders as a receipt with
/// the "srážkou z vyplacené částky" channel.
/// </summary>
internal sealed class ProvizniDokladDocument(
    Invoice invoice,
    IReadOnlyList<FeeInvoiceLineItem> lineItems,
    CountryConfiguration country)
    : InvoiceDocumentBase(invoice, country)
{
    private readonly IReadOnlyList<FeeInvoiceLineItem> _lineItems = lineItems;

    protected override string DocumentTitle => "FAKTURA — PROVIZE";
    protected override string MetadataTitle => "Faktura — provize";
    protected override string RecipientLabel => "ODBĚRATEL (VÝROBCE)";

    protected override void ComposeItems(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4);
                c.RelativeColumn(1);
            });

            table.Header(h =>
            {
                h.Cell().Element(x => HeaderLabel(x, "POLOŽKA"));
                h.Cell().Element(x => HeaderLabel(x, "PROVIZE", alignRight: true));
            });

            foreach (var item in _lineItems)
            {
                table.Cell().Element(BodyCell).Text(
                    $"Provize za zprostředkování — obj. {item.OrderNumber}");
                table.Cell().Element(BodyCell).AlignRight().Text(
                    InvoiceFormatting.FormatAmount(item.FeeAmountMinor, Invoice.Currency));
            }
        });
    }
}
