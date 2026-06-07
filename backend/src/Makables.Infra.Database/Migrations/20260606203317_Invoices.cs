using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class Invoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    order_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    payout_batch_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    maker_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    issuer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    issuer_ico = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    issuer_dic = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    issuer_bank_account = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    recipient_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    recipient_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    recipient_tax_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    recipient_vat_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    taxable_supply_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    invoicing_mode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    amount_without_vat_minor = table.Column<long>(type: "bigint", nullable: false),
                    vat_rate_bp = table.Column<int>(type: "integer", nullable: false),
                    vat_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    amount_with_vat_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    pdf_blob_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_invoices", x => x.id);
                    table.ForeignKey(
                        name: "FK_invoices_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_invoice_number",
                table: "invoices",
                column: "invoice_number",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_maker_created",
                table: "invoices",
                columns: new[] { "maker_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_order_id",
                table: "invoices",
                column: "order_id",
                unique: true,
                filter: "order_id IS NOT NULL AND is_active");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_type",
                table: "invoices",
                column: "type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoices");
        }
    }
}
