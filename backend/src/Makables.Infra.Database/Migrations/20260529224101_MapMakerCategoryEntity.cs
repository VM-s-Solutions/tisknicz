using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// T-0043 Copilot review: the snapshot-sync migration for the new
    /// <c>MakerCategory</c> mapping. The <c>maker_categories</c> table
    /// itself was created by the T-0040 Categories migration; T-0043
    /// added a mapped domain type (<see cref="Makables.Core.Domain.Makers.MakerCategory"/>)
    /// so the catalog category-filter could query it via LINQ.
    ///
    /// <para>
    /// Without this migration, <c>MakablesDbContextModelSnapshot</c>
    /// wouldn't know about <c>MakerCategory</c>, and the next
    /// <c>dotnet ef migrations add</c> would try to re-create the
    /// existing table. The <c>Up</c>/<c>Down</c> bodies are intentionally
    /// empty — the table is already there. What matters is the
    /// accompanying <c>.Designer.cs</c> snapshot, which captures the
    /// mapped entity.
    /// </para>
    /// </summary>
    public partial class MapMakerCategoryEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — the maker_categories table was
            // created by 20260527211229_Categories. This migration exists
            // solely to update the model snapshot.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see Up. The T-0040 Categories
            // migration owns the table's Down.
        }
    }
}
