using FluentAssertions;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;
using Makables.Infra.Database;
using Makables.Infra.Database.Auditing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Makables.Tests.Infra.Database.Auditing;

/// <summary>
/// T-0137 (Q-0028) unit tests for <see cref="AdminReadAuditWriter"/>. The
/// writer commits one <c>admin_audit_log</c> row per privileged admin PII
/// read on a DEDICATED <see cref="MakablesDbContext"/> obtained from an
/// <see cref="IDbContextFactory{TContext}"/>.
///
/// The SUT needs a real factory; we back it with the established in-memory
/// SQLite harness (same shape as <see cref="TestDbHarness"/>): one open
/// connection, the full production model via <c>EnsureCreated()</c>, and a
/// tiny inline <see cref="IDbContextFactory{TContext}"/> that hands out fresh
/// contexts over that same connection — so the writer's own-context commit is
/// exercised against real EF Core behaviour.
/// </summary>
public sealed class AdminReadAuditWriterTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 10, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuditReadAsync_writes_exactly_one_row_with_all_fields()
    {
        using var harness = SqliteFactoryHarness.Create();

        var session = Substitute.For<IUserSessionProvider>();
        session.GetUserId().Returns("user-admin-1");

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);

        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.Next().Returns("audit-id-1");

        var sut = new AdminReadAuditWriter(harness.Factory, session, clock, idGenerator);

        await sut.AuditReadAsync(
            actionCode: "invoice.pdf.download",
            targetEntity: "invoice",
            targetId: "inv-1",
            ipAddress: "1.2.3.4",
            userAgent: "agent/1.0",
            notes: null,
            cancellationToken: CancellationToken.None);

        await using var assertDb = harness.Factory.CreateDbContext();
        var rows = await assertDb.Set<AdminAuditLogEntry>().ToListAsync();

        rows.Should().ContainSingle();
        var row = rows[0];
        row.Id.Should().Be("audit-id-1");
        row.ActionCode.Should().Be("invoice.pdf.download");
        row.TargetEntity.Should().Be("invoice");
        row.TargetId.Should().Be("inv-1");
        row.IpAddress.Should().Be("1.2.3.4");
        row.UserAgent.Should().Be("agent/1.0");
        row.BeforeJson.Should().BeNull();
        row.AfterJson.Should().BeNull();
        row.Notes.Should().BeNull();
        row.AdminUserId.Should().Be("user-admin-1");
        row.CreatedAt.Should().Be(FixedNow);
    }

    [Fact]
    public async Task AuditReadAsync_falls_back_to_system_when_session_user_is_null()
    {
        using var harness = SqliteFactoryHarness.Create();

        var session = Substitute.For<IUserSessionProvider>();
        session.GetUserId().Returns((string?)null);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);

        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.Next().Returns("audit-id-2");

        var sut = new AdminReadAuditWriter(harness.Factory, session, clock, idGenerator);

        await sut.AuditReadAsync(
            actionCode: "order.detail.view",
            targetEntity: "order",
            targetId: "ord-1",
            ipAddress: null,
            userAgent: null,
            notes: null,
            cancellationToken: CancellationToken.None);

        await using var assertDb = harness.Factory.CreateDbContext();
        var row = await assertDb.Set<AdminAuditLogEntry>().SingleAsync();

        row.AdminUserId.Should().Be("system");
        row.IpAddress.Should().BeNull();
        row.UserAgent.Should().BeNull();
    }

    /// <summary>
    /// In-memory SQLite scaffolding that exposes a real
    /// <see cref="IDbContextFactory{TContext}"/> over a single open
    /// connection, mirroring the production own-context commit path. Reuses
    /// the SQLite-over-MakablesDbContext approach from
    /// <see cref="TestDbHarness"/>.
    /// </summary>
    private sealed class SqliteFactoryHarness : IDisposable
    {
        private readonly SqliteConnection _connection;

        public IDbContextFactory<MakablesDbContext> Factory { get; }

        private SqliteFactoryHarness(
            SqliteConnection connection,
            IDbContextFactory<MakablesDbContext> factory)
        {
            _connection = connection;
            Factory = factory;
        }

        public static SqliteFactoryHarness Create()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<MakablesDbContext>()
                .UseSqlite(connection)
                .Options;

            using (var seedDb = new MakablesDbContext(options))
            {
                seedDb.Database.EnsureCreated();
            }

            return new SqliteFactoryHarness(connection, new InlineDbContextFactory(options));
        }

        public void Dispose() => _connection.Dispose();

        /// <summary>
        /// Tiny inline factory returning fresh <see cref="MakablesDbContext"/>
        /// instances over the same open SQLite connection — exactly the seam
        /// the SUT resolves in production.
        /// </summary>
        private sealed class InlineDbContextFactory(DbContextOptions<MakablesDbContext> options)
            : IDbContextFactory<MakablesDbContext>
        {
            public MakablesDbContext CreateDbContext() => new(options);
        }
    }
}
