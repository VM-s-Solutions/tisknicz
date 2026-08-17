using FluentAssertions;
using Makables.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Makables.IntegrationTests.Database;

/// <summary>
/// T-0123 — assertions over the schema the migration pipeline actually
/// produces on Postgres.
///
/// <para>
/// T-0062 shipped <see cref="PostgresHarness"/>, which calls
/// <c>MigrateAsync</c> at fixture start; that proves the migrations *run*.
/// It does not prove they produce the schema the code assumes. Everything
/// below is a rule the codebase relies on but nothing verified — until a
/// production 500 did. T-0160 is the precedent: a raw upsert passed every
/// unit test because SQLite degrades <c>jsonb</c> to TEXT, then 500'd on the
/// first live ARES lookup.
/// </para>
///
/// <para>
/// These are invariants, not a schema snapshot. A new table is expected to
/// satisfy them; a new table that does not is the finding. That is why the
/// money / timestamp / audit checks enumerate the live catalog rather than
/// listing tables by name — a snapshot test would go stale and get muted,
/// while an invariant test gets *more* valuable as the schema grows.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MigrationPipelineTests(PostgresHarness harness)
{
    /// <summary>
    /// Tables outside the domain model whose columns are not ours to shape.
    /// </summary>
    private static readonly string[] NonDomainTables = ["__EFMigrationsHistory"];

    // ---- the journal itself ----

    [Fact]
    public async Task Every_migration_in_the_assembly_is_applied_and_none_are_pending()
    {
        await using var db = harness.CreateDbContext();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var all = db.Database.GetMigrations().ToList();

        all.Should().NotBeEmpty("the assembly must carry migrations");
        applied.Should().BeEquivalentTo(all,
            "MigrateAsync must apply every migration the assembly declares");
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Model_matches_the_last_migration_no_undeclared_model_drift()
    {
        // The failure this catches: an entity/configuration change committed
        // without `dotnet ef migrations add`. The model then differs from every
        // deployed database, and the first symptom is a runtime column-not-found
        // — on the environment where it is most expensive.
        await using var db = harness.CreateDbContext();

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot;
        snapshot.Should().NotBeNull("a model snapshot must be committed alongside the migrations");

        var snapshotModel = snapshot!.Model is IMutableModel mutable
            ? mutable.FinalizeModel()
            : snapshot.Model;
        snapshotModel = db.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshotModel, designTime: true, validationLogger: null);

        var differences = db.GetService<IMigrationsModelDiffer>().GetDifferences(
            snapshotModel.GetRelationalModel(),
            db.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        differences.Should().BeEmpty(
            "the committed model snapshot must match the current model — run `dotnet ef migrations add`");
    }

    // ---- money (CLAUDE.md §2.3 / ADR 0003) ----

    [Fact]
    public async Task Every_minor_unit_money_column_is_bigint()
    {
        var columns = await QueryAsync(
            """
            SELECT table_name, column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name LIKE '%_minor'
            """,
            r => (Table: r.GetString(0), Column: r.GetString(1), Type: r.GetString(2)));

        columns.Should().NotBeEmpty("the schema stores money");
        columns.Where(c => c.Type != "bigint")
            .Should().BeEmpty("money is long minor units — never numeric, never double");
    }

    [Fact]
    public async Task Every_money_column_has_a_currency_companion_on_the_same_table()
    {
        // Money is (amount_minor, currency). A minor-unit column on a table
        // with no currency column anywhere is an amount whose currency is
        // implicit — the exact shape ADR 0003 exists to prevent.
        var moneyTables = await QueryAsync(
            """
            SELECT DISTINCT table_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name LIKE '%_minor'
            """,
            r => r.GetString(0));

        var currencyTables = await QueryAsync(
            """
            SELECT DISTINCT table_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name LIKE '%currency%'
            """,
            r => r.GetString(0));

        moneyTables.Except(currencyTables).Should().BeEmpty(
            "every table holding minor units must also carry the currency");
    }

    [Fact]
    public async Task Every_currency_column_is_a_three_character_code()
    {
        var columns = await QueryAsync(
            """
            SELECT table_name, column_name, character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name LIKE '%currency%'
            """,
            r => (Table: r.GetString(0), Column: r.GetString(1),
                  MaxLength: r.IsDBNull(2) ? (int?)null : r.GetInt32(2)));

        columns.Should().NotBeEmpty();
        columns.Where(c => c.MaxLength != 3)
            .Should().BeEmpty("ISO 4217 codes are exactly 3 characters");
    }

    // ---- time ----

    [Fact]
    public async Task Every_timestamp_column_is_timestamptz()
    {
        // `timestamp without time zone` silently drops the offset. Everything
        // in the domain is DateTimeOffset from IClock.UtcNow; a naive column
        // would make ordering and expiry comparisons wrong by the server's
        // local offset — and correct on a UTC dev box, so it would ship.
        var naive = await QueryAsync(
            $"""
             SELECT table_name, column_name
             FROM information_schema.columns
             WHERE table_schema = 'public'
               AND data_type = 'timestamp without time zone'
               AND table_name <> ALL({NonDomainTables.AsSqlArrayLiteral()})
             """,
            r => $"{r.GetString(0)}.{r.GetString(1)}");

        naive.Should().BeEmpty("every timestamp is timestamptz");

        // Guards the guard: an all-empty result would also come from a
        // mistyped catalog query, and this assertion would then pass forever
        // while proving nothing.
        var aware = await QueryAsync(
            """
            SELECT table_name FROM information_schema.columns
            WHERE table_schema = 'public' AND data_type = 'timestamp with time zone'
            """,
            r => r.GetString(0));
        aware.Should().NotBeEmpty("the catalog query itself must be able to see timestamp columns");
    }

    // ---- audit / soft delete (CLAUDE.md §2.7) ----

    [Theory]
    [InlineData("orders")]
    [InlineData("makers")]
    [InlineData("products")]
    [InlineData("users")]
    [InlineData("invoices")]
    [InlineData("payout_batches")]
    [InlineData("disputes")]
    [InlineData("categories")]
    [InlineData("addresses")]
    [InlineData("reviews")]
    public async Task Auditable_tables_carry_the_full_audit_column_set(string table)
    {
        var columns = await ColumnNamesAsync(table);

        columns.Should().Contain(
            ["country_code", "is_active", "created_by", "created_at", "updated_by", "updated_at"],
            $"{table} is an Auditable aggregate table");
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("makers")]
    [InlineData("products")]
    [InlineData("users")]
    public async Task Soft_delete_flag_is_not_null_so_the_query_filter_can_never_skip_a_row(string table)
    {
        var nullable = await QueryAsync(
            $"""
             SELECT is_nullable FROM information_schema.columns
             WHERE table_schema = 'public' AND table_name = '{table}' AND column_name = 'is_active'
             """,
            r => r.GetString(0));

        nullable.Should().ContainSingle().Which.Should().Be("NO");
    }

    // ---- indexes the read paths depend on ----

    [Theory]
    // Catalog + profile reads (T-0043 / T-0044).
    [InlineData("ix_makers_catalog_sort")]
    [InlineData("ix_makers_slug")]
    [InlineData("ix_makers_registration_number")]
    // Order lists, both audiences (T-0086a / T-0087a).
    [InlineData("ix_orders_customer_created")]
    [InlineData("ix_orders_maker_state_created")]
    [InlineData("ix_orders_order_number")]
    // Payment webhook lookup by provider ref (ADR 0016 idempotency).
    [InlineData("ix_orders_payment_provider_ref")]
    // Outbox sweep (ADR 0020) — the hottest recurring query in the system.
    [InlineData("ix_outbox_event_due")]
    // Category slug uniqueness (T-0040).
    [InlineData("ix_categories_slug")]
    public async Task Index_exists(string indexName)
    {
        var found = await QueryAsync(
            $"SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND indexname = '{indexName}'",
            r => r.GetString(0));

        found.Should().ContainSingle($"{indexName} backs a hot read path");
    }

    [Theory]
    // A partial index whose filter is dropped still answers queries — slower,
    // and silently: the plan changes, nothing errors. These three are the ones
    // whose filters carry correctness weight, not just size.
    [InlineData("ix_categories_slug", "is_active")]
    [InlineData("ix_outbox_event_due", "processed_at IS NULL")]
    [InlineData("ix_orders_payment_provider_ref", "payment_provider_ref IS NOT NULL")]
    public async Task Partial_index_keeps_its_filter(string indexName, string filterFragment)
    {
        var definitions = await QueryAsync(
            $"SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND indexname = '{indexName}'",
            r => r.GetString(0));

        definitions.Should().ContainSingle();
        definitions[0].Should().Contain("WHERE");
        definitions[0].Replace("(", "").Replace(")", "")
            .Should().ContainEquivalentOf(filterFragment);
    }

    [Fact]
    public async Task Company_registry_cache_payload_is_jsonb_not_text()
    {
        // The T-0160 bug in column form: SQLite degrades jsonb to TEXT, so the
        // raw upsert's untyped text parameter passed every unit test and threw
        // 42804 on the first live lookup.
        var type = await QueryAsync(
            """
            SELECT data_type FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'company_registry_cache' AND column_name = 'payload'
            """,
            r => r.GetString(0));

        type.Should().ContainSingle().Which.Should().Be("jsonb");
    }

    [Fact]
    public async Task Numbering_sequence_table_exists_for_the_FOR_UPDATE_generators()
    {
        var columns = await ColumnNamesAsync("numbering_sequence");

        columns.Should().NotBeEmpty(
            "ADR 0009's order/invoice/payout number generators lock a row in this table");
    }

    // ---- helpers ----

    private async Task<List<string>> ColumnNamesAsync(string table) =>
        await QueryAsync(
            $"""
             SELECT column_name FROM information_schema.columns
             WHERE table_schema = 'public' AND table_name = '{table}'
             """,
            r => r.GetString(0));

    private async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> read)
    {
        await using var connection = new NpgsqlConnection(harness.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(read(reader));
        }
        return rows;
    }
}

internal static class SqlArrayLiteralExtensions
{
    /// <summary>
    /// Render a string array as a Postgres array literal. Only ever called
    /// with compile-time constants in this file.
    /// </summary>
    public static string AsSqlArrayLiteral(this string[] values) =>
        $"ARRAY[{string.Join(", ", values.Select(v => $"'{v}'"))}]";
}
