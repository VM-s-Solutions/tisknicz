using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class Addresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "addresses",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    house_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    city = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    zip = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    country_code_iso = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
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
                    table.PrimaryKey("PK_addresses", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_addresses_pending_geocode",
                table: "addresses",
                column: "id",
                filter: "latitude IS NULL AND is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "addresses");
        }
    }
}
