using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceIssuerAddressAndSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "issuer_address",
                table: "invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "paid_on",
                table: "invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_method",
                table: "invoices",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "issuer_address",
                table: "country_configuration",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // --- CZ issuer identity: the real ARES record ------------------
            // Closes manual_step "country-config-ico-replace-placeholder-pre-launch"
            // (T-0068b shipped issuer_ico = '00000000' as a placeholder).
            // Source: ARES ekonomicke-subjekty, IČO 29633443, read 2026-08-23.
            // The name is the registry spelling, because that string is what
            // has to match on a legal document. Not VAT-registered
            // (stavZdrojeDph = NEEXISTUJICI), so issuer_dic stays NULL and
            // InvoicingMode stays None.
            migrationBuilder.Sql(@"
                UPDATE country_configuration
                SET issuer_name    = 'JVM Yore, s.r.o.',
                    issuer_ico     = '29633443',
                    issuer_address = 'Příčná 1892/4, Nové Město, 110 00 Praha 1'
                WHERE id = 'CZ';
            ");

            // --- Correct the placeholder on already-issued invoices --------
            // Guarded on the placeholder value, so a row that snapshotted a
            // real IČO is never rewritten. These are pre-launch rows whose
            // snapshot names an IČO that belongs to nobody; leaving them is
            // not 'preserving history', it is keeping a wrong legal record.
            migrationBuilder.Sql(@"
                UPDATE invoices
                SET issuer_name    = 'JVM Yore, s.r.o.',
                    issuer_ico     = '29633443',
                    issuer_address = 'Příčná 1892/4, Nové Město, 110 00 Praha 1'
                WHERE issuer_ico = '00000000';
            ");

            // --- Backfill settlement on already-issued invoices ------------
            // Both existing families were settled before their document
            // existed, and in both the issue date IS the settlement date:
            //   Customer -> issue_date = Order.PaidAt in country-local terms
            //               (IssueInvoice.ComputeIssueDate)
            //   Fee      -> issue_date = the batch claim date, which is when
            //               the fee was netted out of the payout
            //               (PayoutArtifactService).
            // payment_method is left NULL for Customer rows: the provider's
            // method code was never snapshotted, and inventing one would put
            // a claim on a legal record that no source supports. Fee rows get
            // the platform's own constant, which is derivable with certainty.
            migrationBuilder.Sql(@"
                UPDATE invoices
                SET paid_on = issue_date
                WHERE paid_on IS NULL;
            ");
            migrationBuilder.Sql(@"
                UPDATE invoices
                SET payment_method = 'payout-deduction'
                WHERE type = 'Fee' AND payment_method IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "issuer_address",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "paid_on",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "payment_method",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "issuer_address",
                table: "country_configuration");

            // Restore the T-0068b placeholder so up -> down -> up lands on
            // the same values rather than on a half-migrated identity.
            migrationBuilder.Sql(@"
                UPDATE country_configuration
                SET issuer_name = 'JVM YORE s.r.o.',
                    issuer_ico  = '00000000'
                WHERE id = 'CZ';
            ");
            migrationBuilder.Sql(@"
                UPDATE invoices
                SET issuer_name = 'JVM YORE s.r.o.',
                    issuer_ico  = '00000000'
                WHERE issuer_ico = '29633443';
            ");
        }
    }
}
