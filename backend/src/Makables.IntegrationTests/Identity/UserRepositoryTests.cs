using FluentAssertions;
using Makables.Core.Domain.Identity;
using Makables.Infra.Database;
using Makables.Infra.Database.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Makables.IntegrationTests.Identity;

/// <summary>
/// Exercises the soft-delete / IgnoreQueryFilters boundary on
/// <see cref="UserRepository"/>. Reviewer T-0020 MAJOR M-3 — the entity
/// unit tests cannot reach EF Core's query-filter machinery.
/// </summary>
public sealed class UserRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MakablesDbContext _db;
    private readonly UserRepository _repo;

    public UserRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<MakablesDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new MakablesDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new UserRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<User> SeedUserAsync(string email = "anna@example.cz", bool softDeleted = false)
    {
        var user = User.Create(
            id: $"user-{Guid.NewGuid():N}",
            email: email,
            role: UserRole.Customer,
            fullName: "Anna",
            countryCodePrimary: "CZ");
        user.MarkCreated("test-seed", DateTimeOffset.UtcNow);
        if (softDeleted)
        {
            user.MarkDeactivated("test-seed", DateTimeOffset.UtcNow);
        }
        _repo.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task EmailExistsAsync_returns_true_for_active_account()
    {
        await SeedUserAsync(email: "active@example.cz");

        var exists = await _repo.EmailExistsAsync(
            User.NormalizeEmail("active@example.cz"),
            CancellationToken.None);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task EmailExistsAsync_returns_true_for_soft_deleted_account()
    {
        // AC-4 / reviewer M-3: re-registration must be blocked even
        // when the existing row has been soft-deleted, because the
        // application policy is that ops must explicitly purge before
        // the email becomes reusable.
        await SeedUserAsync(email: "old@example.cz", softDeleted: true);

        var exists = await _repo.EmailExistsAsync(
            User.NormalizeEmail("old@example.cz"),
            CancellationToken.None);

        exists.Should().BeTrue("EmailExistsAsync uses IgnoreQueryFilters per ADR 0013");
    }

    [Fact]
    public async Task GetByEmailNormalizedAsync_resolves_soft_deleted_account()
    {
        // Reviewer T-0020 BLOCKER B-1: login / password-reset / lockout
        // need to distinguish "no such email" from "account deactivated"
        // and rely on a deterministic lookup independent of IsActive.
        await SeedUserAsync(email: "ghost@example.cz", softDeleted: true);

        var resolved = await _repo.GetByEmailNormalizedAsync(
            User.NormalizeEmail("ghost@example.cz"),
            CancellationToken.None);

        resolved.Should().NotBeNull();
        resolved!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetByEmailNormalizedAsync_returns_null_when_email_is_unknown()
    {
        var resolved = await _repo.GetByEmailNormalizedAsync(
            User.NormalizeEmail("nobody@example.cz"),
            CancellationToken.None);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task GetByEmailNormalizedAsync_returns_null_for_blank_input()
    {
        (await _repo.GetByEmailNormalizedAsync("", CancellationToken.None)).Should().BeNull();
        (await _repo.GetByEmailNormalizedAsync("  ", CancellationToken.None)).Should().BeNull();
    }
}
