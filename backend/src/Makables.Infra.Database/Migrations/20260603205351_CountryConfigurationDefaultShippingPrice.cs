using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class CountryConfigurationDefaultShippingPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "default_shipping_price_minor",
                table: "country_configuration",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // T-0061 Q3: CZ seed value 7900 minor (79 CZK) = midpoint of
            // the 69–89 CZK Zásilkovna rate per PROJEKT-VIZE.md:87. The
            // column default keeps the migration safe to run on a fresh
            // DB (the seed row is inserted in InitialSchema with the
            // implicit default), and this UPDATE applies the real value
            // on an already-seeded DB. Both code paths converge on
            // CZ.default_shipping_price_minor = 7900. Idempotent: running
            // the migration twice (down + up again) lands the same value.
            migrationBuilder.Sql(@"
                UPDATE country_configuration
                SET default_shipping_price_minor = 7900
                WHERE id = 'CZ';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_shipping_price_minor",
                table: "country_configuration");
        }
    }
}
