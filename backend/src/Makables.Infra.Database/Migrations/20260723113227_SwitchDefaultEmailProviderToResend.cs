using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// T-0157 — ADR 0019 re-amended to Resend (the 2026-07-04 processor
    /// list names Resend; operator-directed switch). Data-only migration:
    /// existing rows flip from the T-0028-era "sendgrid" to "resend";
    /// fresh databases get "resend" straight from <c>CountrySeed</c>.
    /// The WHERE guard keeps any future per-country override intact.
    /// </summary>
    public partial class SwitchDefaultEmailProviderToResend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE country_configuration SET default_email_provider = 'resend' " +
                "WHERE default_email_provider = 'sendgrid';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE country_configuration SET default_email_provider = 'sendgrid' " +
                "WHERE default_email_provider = 'resend';");
        }
    }
}
