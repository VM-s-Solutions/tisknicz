using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdersPayoutUnclaimedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_orders_payout_unclaimed",
                table: "orders",
                columns: new[] { "state", "payout_batch_id" },
                filter: "state = 'Delivered' AND payout_batch_id IS NULL AND is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_payout_unclaimed",
                table: "orders");
        }
    }
}
