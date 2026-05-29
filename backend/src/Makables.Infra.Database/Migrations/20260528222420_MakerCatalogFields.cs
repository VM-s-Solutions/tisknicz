using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class MakerCatalogFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "rating_average_bp",
                table: "makers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rating_count",
                table: "makers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The "" default is only safe because the makers table is
            // empty in every environment at this migration (no maker seed
            // data). If makers existed, the unique ix_makers_slug below
            // would reject multiple ""-slug rows — a backfill step would
            // be required first. T-0043.
            migrationBuilder.AddColumn<string>(
                name: "slug",
                table: "makers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "total_orders",
                table: "makers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_makers_catalog_sort",
                table: "makers",
                columns: new[] { "rating_average_bp", "total_orders" },
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_makers_slug",
                table: "makers",
                column: "slug",
                unique: true,
                filter: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_makers_catalog_sort",
                table: "makers");

            migrationBuilder.DropIndex(
                name: "ix_makers_slug",
                table: "makers");

            migrationBuilder.DropColumn(
                name: "rating_average_bp",
                table: "makers");

            migrationBuilder.DropColumn(
                name: "rating_count",
                table: "makers");

            migrationBuilder.DropColumn(
                name: "slug",
                table: "makers");

            migrationBuilder.DropColumn(
                name: "total_orders",
                table: "makers");
        }
    }
}
