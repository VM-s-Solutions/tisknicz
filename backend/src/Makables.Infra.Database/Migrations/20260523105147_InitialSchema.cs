using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "countries",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    iso_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    is_serviced = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_countries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "country_configuration",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    country_id = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    default_currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    default_language_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    phone_prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    date_format = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    zip_format = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    standard_vat_rate_bp = table.Column<int>(type: "integer", nullable: false),
                    reduced_vat_rate_bp = table.Column<int>(type: "integer", nullable: true),
                    invoicing_mode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    platform_fee_rate_bp = table.Column<int>(type: "integer", nullable: false),
                    tax_id_label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tax_id_format = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    vat_id_label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    vat_id_format = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    vat_id_required = table.Column<bool>(type: "boolean", nullable: false),
                    registration_number_label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    registration_number_format = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    registration_number_required = table.Column<bool>(type: "boolean", nullable: false),
                    default_payment_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    default_shipping_carrier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    default_registry = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    default_email_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    legal_requirements_json = table.Column<string>(type: "jsonb", nullable: true),
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
                    table.PrimaryKey("PK_country_configuration", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "numbering_sequence",
                columns: table => new
                {
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_used_value = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_numbering_sequence", x => new { x.country_code, x.scope, x.year });
                });

            migrationBuilder.CreateIndex(
                name: "IX_countries_iso_code",
                table: "countries",
                column: "iso_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_country_configuration_country_id",
                table: "country_configuration",
                column: "country_id",
                unique: true);

            // === Seed: Czech Republic (ADR 0004 / CountrySeed). ===
            // Done via raw SQL rather than HasData so Auditable's CreatedBy /
            // CreatedAt are stamped explicitly with the deterministic seed
            // value, not by EF's HasData snapshot mechanism.
            var seededAt = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero);
            var seededAtSql = seededAt.ToString("yyyy-MM-dd HH:mm:sszzz");

            migrationBuilder.Sql($@"
                INSERT INTO countries
                    (id, country_code, name, iso_code, is_serviced, is_active, created_by, created_at)
                VALUES
                    ('CZ', 'CZ', 'Česká republika', 'CZ', TRUE, TRUE, 'seed', '{seededAtSql}');

                INSERT INTO country_configuration
                    (id, country_id, country_code, default_currency_code, default_language_code,
                     time_zone_id, phone_prefix, date_format, zip_format,
                     standard_vat_rate_bp, reduced_vat_rate_bp, invoicing_mode, platform_fee_rate_bp,
                     tax_id_label, tax_id_format, vat_id_label, vat_id_format, vat_id_required,
                     registration_number_label, registration_number_format, registration_number_required,
                     default_payment_provider, default_shipping_carrier, default_registry, default_email_provider,
                     is_active, created_by, created_at)
                VALUES
                    ('CZ', 'CZ', 'CZ', 'CZK', 'cs-CZ',
                     'Europe/Prague', '+420', 'd. M. yyyy', '^\d{{3}}\s?\d{{2}}$',
                     2100, 1200, 'None', 1500,
                     'DIČ', '^CZ\d{{8,10}}$', 'DIČ', '^CZ\d{{8,10}}$', FALSE,
                     'IČO', '^\d{{8}}$', TRUE,
                     'comgate', 'packeta', 'ares', 'resend',
                     TRUE, 'seed', '{seededAtSql}');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "countries");

            migrationBuilder.DropTable(
                name: "country_configuration");

            migrationBuilder.DropTable(
                name: "numbering_sequence");
        }
    }
}
