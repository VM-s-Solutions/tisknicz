using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// T-0103 — add the nullable <c>bank_reference VARCHAR(140)</c> column to
    /// <c>payout_batches</c>: the operator's bank-assigned wire transaction id,
    /// recorded at settlement for the audit trail + the maker-facing
    /// reconciliation surface. Null while Processing.
    /// </summary>
    public partial class AddPayoutBatchBankReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bank_reference",
                table: "payout_batches",
                type: "character varying(140)",
                maxLength: 140,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bank_reference",
                table: "payout_batches");
        }
    }
}
