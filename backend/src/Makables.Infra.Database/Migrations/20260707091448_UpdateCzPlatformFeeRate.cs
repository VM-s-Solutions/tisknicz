using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// K2 (docs/meetings/revize-dev-webu-2026-07-04.md) / §2.2
    /// (docs/meetings/dopady-rozhodnuti-na-platformu.md) data-fix. The CZ
    /// <c>country_configuration</c> seed row (InitialSchema) carried
    /// <c>platform_fee_rate_bp = 1500</c> (15%), contradicting the public
    /// website which already advertises a 7% base commission (with a
    /// separate, not-yet-built loyalty-discounted rate of 3.5%). This
    /// migration corrects only the base rate to 700 bp (7%); the
    /// loyalty/override rate is out of scope here.
    ///
    /// No schema change — the model snapshot is unchanged.
    /// </summary>
    public partial class UpdateCzPlatformFeeRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE country_configuration
                SET platform_fee_rate_bp = 700
                WHERE country_id = 'CZ';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE country_configuration
                SET platform_fee_rate_bp = 1500
                WHERE country_id = 'CZ';
            ");
        }
    }
}
