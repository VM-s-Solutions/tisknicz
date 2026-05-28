using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class Categories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // === categories table (T-0040 Category entity) ===
            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_categories", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_categories_slug",
                table: "categories",
                column: "slug",
                unique: true,
                filter: "is_active");

            // === maker_categories join (T-0040) ===
            // No EF entity — pure m:n reference table. Composite PK on
            // (maker_id, category_id). Cascade deletes are intentionally
            // OFF: the global soft-delete query filter on Maker /
            // Category drops join rows from queries when either side is
            // deactivated; we don't want a Category deletion to ripple
            // through maker history. The deactivated row remains
            // pointable so admin audit can reconstruct who offered what.
            migrationBuilder.CreateTable(
                name: "maker_categories",
                columns: table => new
                {
                    maker_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maker_categories", x => new { x.maker_id, x.category_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_maker_categories_category_id",
                table: "maker_categories",
                column: "category_id");

            // === Seed: six launch categories per role/category.md ===
            // Done via raw SQL (matches the CountryConfiguration seed
            // pattern in 20260523105147_InitialSchema) so created_by /
            // created_at are stamped explicitly with the deterministic
            // seed value, not by EF's HasData snapshot mechanism.
            var seededAt = new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero);
            var seededAtSql = seededAt.ToString("yyyy-MM-dd HH:mm:sszzz");

            migrationBuilder.Sql($@"
                INSERT INTO categories
                    (id, name, slug, icon, description, sort_order,
                     is_active, country_code, created_by, created_at)
                VALUES
                    ('cat-3d-tisk',         '3D tisk',         '3d-tisk',         NULL, NULL, 10, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('cat-klasicky-tisk',   'Klasický tisk',   'klasicky-tisk',   NULL, NULL, 20, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('cat-potisk-textilu',  'Potisk textilu',  'potisk-textilu',  NULL, NULL, 30, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('cat-laser-cnc',       'Laser & CNC',     'laser-cnc',       NULL, NULL, 40, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('cat-velkoformat',     'Velkoformát',     'velkoformat',     NULL, NULL, 50, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('cat-handmade',        'Handmade',        'handmade',        NULL, NULL, 60, TRUE, 'CZ', 'seed', '{seededAtSql}');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "maker_categories");
            migrationBuilder.DropTable(name: "categories");
        }
    }
}
