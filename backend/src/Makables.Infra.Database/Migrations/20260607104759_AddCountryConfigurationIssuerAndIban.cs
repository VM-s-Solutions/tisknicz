using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryConfigurationIssuerAndIban : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // T-0068b locked decisions 4 + 8: add the four columns the
            // IssueInvoice handler snapshots onto every new Invoice row
            // (issuer_name, issuer_ico, issuer_dic) + the SPAYD QR IBAN
            // (platform_iban).
            //
            // For NOT NULL columns we add a temporary empty-string DEFAULT
            // so the migration can land on a database that already has the
            // CZ seed row from InitialSchema. The DEFAULT is dropped at the
            // end of the migration after the CZ row is patched with real
            // values — keeps the column shape clean for the admin UI
            // (T-0108) which doesn't want a "" fallback to confuse the
            // form-required check.
            migrationBuilder.AddColumn<string>(
                name: "issuer_name",
                table: "country_configuration",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "issuer_ico",
                table: "country_configuration",
                type: "character(8)",
                fixedLength: true,
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "issuer_dic",
                table: "country_configuration",
                type: "character varying(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "platform_iban",
                table: "country_configuration",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            // CZ seed per T-0068b locked decision 8 + user direction:
            // issuer_name = 'JVM YORE s.r.o.';
            // issuer_ico = '00000000' (placeholder — manual_step
            // "country-config-ico-replace-placeholder-pre-launch"
            // tracks the pre-prod-launch update);
            // issuer_dic = NULL (not VAT-registered at MVP per T-0068a
            // locked decision 2);
            // platform_iban = NULL (bank-account decision still open;
            // renderer skips SPAYD QR when null per locked decision 4).
            //
            // Idempotent: running the migration twice (down + up again)
            // lands the same values.
            migrationBuilder.Sql(@"
                UPDATE country_configuration
                SET issuer_name = 'JVM YORE s.r.o.',
                    issuer_ico  = '00000000',
                    issuer_dic  = NULL,
                    platform_iban = NULL
                WHERE id = 'CZ';
            ");

            // Drop the temp DEFAULTs now that every existing row has a
            // real value. New rows the admin UI inserts (T-0108) must
            // supply issuer_name + issuer_ico explicitly, which is the
            // right shape: the platform's legal-entity identity isn't a
            // sensible default.
            migrationBuilder.Sql(@"
                ALTER TABLE country_configuration ALTER COLUMN issuer_name DROP DEFAULT;
                ALTER TABLE country_configuration ALTER COLUMN issuer_ico  DROP DEFAULT;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "issuer_dic",
                table: "country_configuration");

            migrationBuilder.DropColumn(
                name: "issuer_ico",
                table: "country_configuration");

            migrationBuilder.DropColumn(
                name: "issuer_name",
                table: "country_configuration");

            migrationBuilder.DropColumn(
                name: "platform_iban",
                table: "country_configuration");
        }
    }
}
