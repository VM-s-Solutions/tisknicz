using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMakerLegalType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "legal_type",
                table: "makers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_makers_legal_type",
                table: "makers",
                column: "legal_type",
                filter: "is_active AND legal_type IS NOT NULL");

            // Backfill from the stored label. Existing makers were written
            // before the raw ČSÚ code was classified, and the code is not
            // retained — but `legal_form` holds a label this codebase
            // produced itself (CzechLegalForms.Resolve), so the mapping back
            // is exact for every catalogued form. Without this every
            // pre-existing maker would be NULL and therefore invisible to
            // BOTH filter buckets until its next ARES snapshot refresh.
            //
            // Rows left NULL on purpose: an uncatalogued form (stored as the
            // bare numeric code) and the "Anonymized" erasure sentinel —
            // neither should answer the public filter.
            migrationBuilder.Sql("""
                UPDATE makers SET legal_type = 'NaturalPerson'
                WHERE legal_type IS NULL AND legal_form IN (
                    'Fyzická osoba podnikající dle živnostenského zákona',
                    'Zahraniční fyzická osoba');
                """);

            migrationBuilder.Sql("""
                UPDATE makers SET legal_type = 'LegalEntity'
                WHERE legal_type IS NULL AND legal_form IN (
                    'Veřejná obchodní společnost',
                    'Společnost s ručením omezeným',
                    'Nadace',
                    'Akciová společnost',
                    'Družstvo',
                    'Státní podnik',
                    'Organizační složka zahraniční právnické osoby',
                    'Vysoká škola',
                    'Příspěvková organizace',
                    'Spolek');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_makers_legal_type",
                table: "makers");

            migrationBuilder.DropColumn(
                name: "legal_type",
                table: "makers");
        }
    }
}
