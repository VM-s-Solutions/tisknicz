using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class Orders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    customer_user_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    maker_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    product_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    product_price_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    shipping_price_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    platform_fee_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    maker_payout_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    total_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    vat_rate_bp = table.Column<int>(type: "integer", nullable: false),
                    shipping_method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    zasilkovna_pickup_point_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disputed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payment_provider_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    shipping_carrier_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    auto_deliver_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    customer_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_orders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_created",
                table: "orders",
                columns: new[] { "customer_user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_orders_maker_state_created",
                table: "orders",
                columns: new[] { "maker_id", "state", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_orders_order_number",
                table: "orders",
                column: "order_number",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_orders_payment_provider_ref",
                table: "orders",
                column: "payment_provider_ref",
                unique: true,
                filter: "payment_provider_ref IS NOT NULL AND is_active");

            migrationBuilder.CreateIndex(
                name: "ix_orders_state",
                table: "orders",
                column: "state",
                filter: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orders");
        }
    }
}
