using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddProductRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "product_id",
                table: "reviews",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rating_average_bp",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rating_count",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_reviews_product",
                table: "reviews",
                column: "product_id",
                filter: "product_id IS NOT NULL");

            // Backfill 1/2: denormalize the catalog product off the
            // reviewed order (reviews on custom orders keep NULL).
            migrationBuilder.Sql("""
                UPDATE reviews r
                SET product_id = o.product_id
                FROM orders o
                WHERE r.order_id = o.id
                  AND o.product_id IS NOT NULL;
                """);

            // Backfill 2/2: recompute every affected product's aggregate
            // from its ACTIVE review rows — same recompute-from-rows
            // discipline as the runtime SubmitReview path (half-up
            // rounding via ROUND, clamped to 0..50000 bp).
            migrationBuilder.Sql("""
                UPDATE products p
                SET rating_count = agg.cnt,
                    rating_average_bp = LEAST(50000, GREATEST(0, agg.bp))
                FROM (
                    SELECT product_id,
                           COUNT(*) AS cnt,
                           CAST(ROUND(AVG(rating) * 10000) AS int) AS bp
                    FROM reviews
                    WHERE is_active AND product_id IS NOT NULL
                    GROUP BY product_id
                ) agg
                WHERE p.id = agg.product_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reviews_product",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "product_id",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "rating_average_bp",
                table: "products");

            migrationBuilder.DropColumn(
                name: "rating_count",
                table: "products");
        }
    }
}
