using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class Makers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "makers",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    user_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    registration_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    vat_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    company_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    legal_form = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    registered_address_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    incorporated_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active_in_registry = table.Column<bool>(type: "boolean", nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    source_registry = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    snapshot_fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    snapshot_is_stale = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_makers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_makers_registered_address_id",
                table: "makers",
                column: "registered_address_id");

            migrationBuilder.CreateIndex(
                name: "ix_makers_registration_number",
                table: "makers",
                column: "registration_number",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_makers_user_id",
                table: "makers",
                column: "user_id",
                unique: true,
                filter: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "makers");
        }
    }
}
