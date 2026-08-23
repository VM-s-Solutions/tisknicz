using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Snapshots <c>Order.OrderNumber</c> onto Customer invoices so the
    /// line item can name the order the way the customer knows it. The
    /// templates previously printed the invoice number's own numeric tail
    /// as "Objednávka {tail}" — a reference that matches no order in the
    /// customer's list. Fee invoices stay NULL: they cover many orders and
    /// carry each number on its own line already.
    /// </summary>
    public partial class InvoiceOrderNumberSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "order_number",
                table: "invoices",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            // Backfill from the order the invoice already points at. Exact,
            // not a guess: order_id is an FK, so every Customer row resolves
            // to precisely one order_number. Fee rows have a NULL order_id
            // and are left NULL by the join.
            migrationBuilder.Sql(@"
                UPDATE invoices i
                SET order_number = o.order_number
                FROM orders o
                WHERE i.order_id = o.id
                  AND i.order_number IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "order_number",
                table: "invoices");
        }
    }
}
