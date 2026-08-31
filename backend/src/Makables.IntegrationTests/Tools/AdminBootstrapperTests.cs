using FluentAssertions;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Infra.Common.Auth;
using Makables.Infra.Common.Identifiers;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using Makables.Tools.AdminBootstrap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Makables.IntegrationTests.Tools;

/// <summary>
/// Pins the safety guards on the first-admin bootstrap tool, against REAL
/// Postgres.
///
/// <para>
/// The tool exists because <c>Register</c> refuses <see cref="UserRole.Admin"/>
/// and every admin-management use case needs an existing admin, so a fresh
/// database has no reachable path to its own console. It is therefore the one
/// piece of code that can mint a fully privileged account outside the normal
/// authorisation model — which makes its refusals, not its happy path, the part
/// worth pinning.
/// </para>
///
/// <para>
/// These live in the integration suite rather than the unit suite deliberately.
/// The tool is Postgres-specific in three ways a SQLite double cannot express:
/// it parses an Npgsql connection string to confirm the target, it relies on
/// the partial unique index over <c>email_normalized</c>, and it converts
/// SQLSTATE 23505 into a clean refusal. Tested against a double, every one of
/// those would be asserted against a fiction.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class AdminBootstrapperTests(PostgresHarness harness)
{
    private const string GoodPassword = "correct horse battery staple";
    private const string Email = "ops@makables.cz";
    private const string Name = "Ops";

    private string DatabaseName =>
        new NpgsqlConnectionStringBuilder(harness.ConnectionString).Database!;

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-31T09:00:00Z");

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    /// <summary>
    /// Builds the bootstrapper over a context wired like the tool's own — the
    /// audit interceptor included, since it is what stamps <c>created_by</c> on
    /// the row the tool inserts.
    /// </summary>
    private (AdminBootstrapper Sut, MakablesDbContext Db) Build()
    {
        var db = harness.CreateAuditingDbContext("admin-bootstrap");
        var sut = new AdminBootstrapper(
            db,
            new Argon2idPasswordHasher(Microsoft.Extensions.Options.Options.Create(new Argon2idOptions())),
            new UlidIdGenerator(),
            new FixedClock(),
            NullLogger<AdminBootstrapper>.Instance);
        return (sut, db);
    }

    private Task<int> RunAsync(AdminBootstrapper sut, string? password = GoodPassword,
        string? email = Email, string? name = Name, string? confirmDatabase = null)
        => sut.RunAsync(email, name, password, confirmDatabase ?? DatabaseName, CancellationToken.None);

    private async Task SeedUserAsync(string id, string email, UserRole role, bool active = true)
    {
        await using var db = harness.CreateAuditingDbContext("seed");
        var user = User.Create(
            id: id, email: email, role: role, fullName: "Existing",
            countryCodePrimary: "CZ", emailAlreadyConfirmed: true, confirmedAt: Now);
        if (!active) user.MarkDeactivated("admin", Now);
        db.Set<User>().Add(user);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Creates_the_first_admin_when_none_exists()
    {
        await harness.ResetMutableTablesAsync();
        var (sut, db) = Build();
        await using var _ = db;

        var exit = await RunAsync(sut);

        exit.Should().Be(AdminBootstrapper.ExitSuccess);

        await using var verify = harness.CreateDbContext();
        var admin = await verify.Set<User>().SingleAsync(u => u.Role == UserRole.Admin);
        admin.EmailNormalized.Should().Be(Email);
        admin.PasswordHash.Should().NotBeNullOrWhiteSpace("the operator must be able to sign in");
        admin.EmailConfirmedAt.Should().NotBeNull(
            "the confirmation email goes through the outbox, which nothing has drained on a fresh environment");
    }

    /// <summary>
    /// The guard that keeps this a bootstrap rather than a standing back door:
    /// once a console exists, further admins go through it and are audited to a
    /// real actor.
    /// </summary>
    [Fact]
    public async Task Refuses_when_an_active_admin_already_exists()
    {
        await harness.ResetMutableTablesAsync();
        await SeedUserAsync("existing-admin", "boss@makables.cz", UserRole.Admin);
        var (sut, db) = Build();
        await using var _ = db;

        var exit = await RunAsync(sut);

        exit.Should().Be(AdminBootstrapper.ExitRefused);

        await using var verify = harness.CreateDbContext();
        (await verify.Set<User>().CountAsync(u => u.Role == UserRole.Admin)).Should().Be(1);
    }

    /// <summary>
    /// Deliberate counterpart. If every admin is deactivated the platform has no
    /// reachable console — the situation this tool exists to resolve — so
    /// bootstrapping is allowed again. The only in-product path that deactivates
    /// a user is self-service account deletion, so this is a solo admin locking
    /// themselves out, not an escalation route.
    /// </summary>
    [Fact]
    public async Task Allows_bootstrap_when_the_only_admin_is_deactivated()
    {
        await harness.ResetMutableTablesAsync();
        await SeedUserAsync("old-admin", "gone@makables.cz", UserRole.Admin, active: false);
        var (sut, db) = Build();
        await using var _ = db;

        var exit = await RunAsync(sut);

        exit.Should().Be(AdminBootstrapper.ExitSuccess);
    }

    [Fact]
    public async Task Refuses_an_email_that_is_already_registered()
    {
        await harness.ResetMutableTablesAsync();
        await SeedUserAsync("customer-1", Email, UserRole.Customer);
        var (sut, db) = Build();
        await using var _ = db;

        var exit = await RunAsync(sut);

        exit.Should().Be(AdminBootstrapper.ExitRefused);

        await using var verify = harness.CreateDbContext();
        (await verify.Set<User>().AnyAsync(u => u.Role == UserRole.Admin)).Should().BeFalse(
            "silently promoting an existing account is not this tool's job");
    }

    /// <summary>
    /// The check must go through <c>EmailNormalized</c>. Comparing the display
    /// column would miss a different-cased duplicate, and the insert would then
    /// die on the unique index as a raw 23505 instead of a clean refusal.
    /// </summary>
    [Fact]
    public async Task Refuses_an_email_that_differs_only_by_casing()
    {
        await harness.ResetMutableTablesAsync();
        await SeedUserAsync("customer-1", "OPS@Makables.CZ", UserRole.Customer);
        var (sut, db) = Build();
        await using var _ = db;

        var exit = await RunAsync(sut, email: "ops@makables.cz");

        exit.Should().Be(AdminBootstrapper.ExitRefused);
    }

    /// <summary>
    /// A soft-deleted account still owns its address (ADR 0013), and the unique
    /// index is partial on <c>is_active</c> — so without IgnoreQueryFilters the
    /// insert would SUCCEED and permanently block that user's reactivation.
    /// </summary>
    [Fact]
    public async Task Refuses_an_email_held_by_a_soft_deleted_account()
    {
        await harness.ResetMutableTablesAsync();
        await SeedUserAsync("deleted-1", Email, UserRole.Customer, active: false);
        var (sut, db) = Build();
        await using var _ = db;

        var exit = await RunAsync(sut);

        exit.Should().Be(AdminBootstrapper.ExitRefused);
    }

    /// <summary>
    /// "elevenchars" is exactly one below the floor; the companion test below
    /// takes exactly the floor. Together they pin the boundary rather than a
    /// point somewhere near it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("elevenchars")]
    public async Task Refuses_a_password_below_the_floor(string password)
    {
        await harness.ResetMutableTablesAsync();
        var (sut, db) = Build();
        await using var _ = db;

        var exit = await RunAsync(sut, password: password);

        exit.Should().Be(AdminBootstrapper.ExitBadInput);
    }

    [Fact]
    public async Task Accepts_a_password_exactly_at_the_floor()
    {
        await harness.ResetMutableTablesAsync();
        var (sut, db) = Build();
        await using var _ = db;

        var exactly = new string('x', AdminBootstrapper.MinPasswordLength);

        var exit = await RunAsync(sut, password: exactly);

        exit.Should().Be(AdminBootstrapper.ExitSuccess,
            "the floor is inclusive — a rejection here would be an off-by-one");
    }

    [Theory]
    [InlineData(null, Name)]
    [InlineData("not-an-email", Name)]
    [InlineData(Email, null)]
    [InlineData(Email, "  ")]
    public async Task Refuses_missing_or_malformed_identity(string? email, string? name)
    {
        await harness.ResetMutableTablesAsync();
        var (sut, db) = Build();
        await using var _ = db;

        var exit = await sut.RunAsync(email, name, GoodPassword, DatabaseName, CancellationToken.None);

        exit.Should().Be(AdminBootstrapper.ExitBadInput);
    }

    /// <summary>
    /// The target guard. A host-is-localhost check would have been worse than
    /// useless — an SSH tunnel to a private production Postgres IS localhost —
    /// so the operator names the database instead.
    /// </summary>
    [Fact]
    public async Task Refuses_when_the_confirmed_database_does_not_match()
    {
        await harness.ResetMutableTablesAsync();
        var (sut, db) = Build();
        await using var _ = db;

        var exit = await RunAsync(sut, confirmDatabase: "some_other_database");

        exit.Should().Be(AdminBootstrapper.ExitRefused);

        await using var verify = harness.CreateDbContext();
        (await verify.Set<User>().AnyAsync()).Should().BeFalse("nothing may be written to an unconfirmed target");
    }

    [Fact]
    public async Task Refuses_when_no_database_is_confirmed()
    {
        await harness.ResetMutableTablesAsync();
        var (sut, db) = Build();
        await using var _ = db;

        var exit = await sut.RunAsync(Email, Name, GoodPassword, confirmDatabase: null, CancellationToken.None);

        exit.Should().Be(AdminBootstrapper.ExitRefused);
    }

    [Fact]
    public async Task Records_an_audit_entry_for_the_bootstrap()
    {
        await harness.ResetMutableTablesAsync();
        var (sut, db) = Build();
        await using var _ = db;

        await RunAsync(sut);

        await using var verify = harness.CreateDbContext();
        var admin = await verify.Set<User>().SingleAsync(u => u.Role == UserRole.Admin);
        var entry = await verify.Set<AdminAuditLogEntry>().SingleAsync();

        entry.ActionCode.Should().Be("admin.bootstrap");
        entry.TargetId.Should().Be(admin.Id);
        entry.AdminUserId.Should().Be(admin.Id,
            "there is no prior admin to attribute this to, so the entry is self-referential by necessity");
    }
}
