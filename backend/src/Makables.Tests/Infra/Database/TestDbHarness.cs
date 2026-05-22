using Makables.Core.Domain.Common;
using Makables.Infra.Database;
using Makables.Infra.Database.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Makables.Tests.Infra.Database;

/// <summary>
/// In-memory SQLite scaffolding for testing MakablesDbContext's interceptor
/// and global query filter wiring against real EF Core behaviour. We use
/// SQLite (not the EF in-memory provider) so global query filters and
/// transactions behave correctly.
///
/// A test entity (<see cref="WidgetEntity"/>) inherits Auditable so the
/// soft-delete filter and audit interceptor have something to act on.
/// Phase 1 has no production entities yet; once they land, those will be
/// the system-under-test.
/// </summary>
internal sealed class TestDbHarness : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDbContext Db { get; }
    public IUserSessionProvider Session { get; }
    public IClock Clock { get; }
    public DateTimeOffset FixedNow { get; }

    private TestDbHarness(
        SqliteConnection connection,
        TestDbContext db,
        IUserSessionProvider session,
        IClock clock,
        DateTimeOffset fixedNow)
    {
        _connection = connection;
        Db = db;
        Session = session;
        Clock = clock;
        FixedNow = fixedNow;
    }

    public static TestDbHarness Create(string? userId = "alice@example.com")
    {
        var fixedNow = DateTimeOffset.Parse("2026-05-22T10:00:00Z");

        var session = Substitute.For<IUserSessionProvider>();
        session.GetUserId().Returns(userId);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(fixedNow);

        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditableSaveChangesInterceptor(session, clock))
            .Options;

        var db = new TestDbContext(options);
        db.Database.EnsureCreated();

        return new TestDbHarness(connection, db, session, clock, fixedNow);
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

/// <summary>
/// Test-only Auditable entity. Lives in the test project so production
/// schema stays clean.
/// </summary>
internal sealed class WidgetEntity : Auditable
{
    public string Name { get; set; } = default!;

    public WidgetEntity() { }

    public WidgetEntity(string id, string countryCode, string name)
    {
        Id = id;
        CountryCode = countryCode;
        Name = name;
    }
}

/// <summary>
/// Test DbContext that inherits the production MakablesDbContext so the
/// query-filter / interceptor wiring is the actual code under test.
/// </summary>
internal sealed class TestDbContext(DbContextOptions options) : MakablesDbContext(CastOptions(options))
{
    public DbSet<WidgetEntity> Widgets => Set<WidgetEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WidgetEntity>(b =>
        {
            b.ToTable("widgets");
            b.HasKey(w => w.Id);
            b.Property(w => w.Id).HasMaxLength(26);
            b.Property(w => w.CountryCode).HasMaxLength(2).IsRequired();
            b.Property(w => w.Name).HasMaxLength(100).IsRequired();
            b.Property(w => w.CreatedBy).HasMaxLength(200);
        });

        base.OnModelCreating(modelBuilder);
    }

    private static DbContextOptions<MakablesDbContext> CastOptions(DbContextOptions options)
    {
        // MakablesDbContext takes DbContextOptions<MakablesDbContext>; we
        // construct DbContextOptions<TestDbContext> in the harness. Both
        // share the same underlying extension dictionary, so we rebuild
        // the typed options pointing the same providers/interceptors at
        // the parent context type.
        var builder = new DbContextOptionsBuilder<MakablesDbContext>();
        foreach (var ext in options.Extensions)
        {
            ((Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsBuilderInfrastructure)builder)
                .AddOrUpdateExtension(ext);
        }
        return builder.Options;
    }
}
