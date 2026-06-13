using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class PayoutBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payout_batch_id",
                table: "orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payout_batches",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    batch_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    order_count = table.Column<int>(type: "integer", nullable: false),
                    maker_count = table.Column<int>(type: "integer", nullable: false),
                    excluded_partially_refunded_order_count = table.Column<int>(type: "integer", nullable: false),
                    excluded_no_bank_account_order_count = table.Column<int>(type: "integer", nullable: false),
                    excluded_no_bank_account_maker_count = table.Column<int>(type: "integer", nullable: false),
                    csv_blob_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_payout_batches", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_payout_batch_id",
                table: "orders",
                column: "payout_batch_id",
                filter: "payout_batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_payout_batch_id",
                table: "invoices",
                column: "payout_batch_id");

            migrationBuilder.CreateIndex(
                name: "ux_payout_batches_country_batch_number",
                table: "payout_batches",
                columns: new[] { "country_code", "batch_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payout_batches_open_per_country",
                table: "payout_batches",
                column: "country_code",
                unique: true,
                filter: "state = 'Processing' AND is_active");

            migrationBuilder.AddForeignKey(
                name: "FK_invoices_payout_batches_payout_batch_id",
                table: "invoices",
                column: "payout_batch_id",
                principalTable: "payout_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_orders_payout_batches_payout_batch_id",
                table: "orders",
                column: "payout_batch_id",
                principalTable: "payout_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_invoices_payout_batches_payout_batch_id",
                table: "invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_orders_payout_batches_payout_batch_id",
                table: "orders");

            migrationBuilder.DropTable(
                name: "payout_batches");

            migrationBuilder.DropIndex(
                name: "ix_orders_payout_batch_id",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_invoices_payout_batch_id",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "payout_batch_id",
                table: "orders");
        }
    }
}
