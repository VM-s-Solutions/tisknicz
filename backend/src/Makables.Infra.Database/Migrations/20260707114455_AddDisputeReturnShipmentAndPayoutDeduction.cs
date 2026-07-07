using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDisputeReturnShipmentAndPayoutDeduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "return_carrier_ref",
                table: "disputes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "return_received_at",
                table: "disputes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_received_by",
                table: "disputes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_tracking_url",
                table: "disputes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payout_deductions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    maker_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    dispute_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<short>(type: "smallint", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    payout_batch_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_deductions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payout_deductions_maker_pending",
                table: "payout_deductions",
                columns: new[] { "maker_id", "payout_batch_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payout_deductions");

            migrationBuilder.DropColumn(
                name: "return_carrier_ref",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "return_received_at",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "return_received_by",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "return_tracking_url",
                table: "disputes");
        }
    }
}
