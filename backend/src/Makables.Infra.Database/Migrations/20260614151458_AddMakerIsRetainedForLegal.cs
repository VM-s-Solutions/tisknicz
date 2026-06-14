using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMakerIsRetainedForLegal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reviews_users_customer_user_id",
                table: "reviews");

            migrationBuilder.AddColumn<bool>(
                name: "is_retained_for_legal",
                table: "makers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_retained_for_legal",
                table: "makers");

            migrationBuilder.AddForeignKey(
                name: "FK_reviews_users_customer_user_id",
                table: "reviews",
                column: "customer_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
