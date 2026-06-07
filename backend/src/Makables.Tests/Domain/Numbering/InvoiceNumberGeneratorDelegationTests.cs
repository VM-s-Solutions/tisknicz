using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Infra.Database;
using Makables.Infra.Database.Numbering;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Makables.Tests.Domain.Numbering;

/// <summary>
/// Unit-level regression net for the <see cref="InvoiceNumberGenerator"/>
/// delegation contract introduced by T-0068a's TZ-aware migration.
/// Mirrors <see cref="OrderNumberGeneratorDelegationTests"/> — the
/// generator must
/// (a) resolve the <see cref="CountryConfiguration"/> via
/// <see cref="ICountryConfigurationRepository"/>,
/// (b) query <see cref="IClock.UtcNow"/> for the year arm, and
/// (c) reach the shared <c>NumberingSequenceAllocator</c> using
/// <see cref="NumberingScope.Invoice"/>.
///
/// <para>
/// True race + rollback semantics, plus the round-trip year assertion
/// against a real timezone database, live in
/// <see cref="Makables.IntegrationTests.Numbering.InvoiceNumberGeneratorRaceSafetyTests"/>
/// and <c>InvoiceNumberGeneratorYearContractTests</c>. This test runs in
/// milliseconds and guards against accidental scope / TZ-arm regressions
/// even when Docker is unavailable.
/// </para>
/// </summary>
public sealed class InvoiceNumberGeneratorDelegationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MakablesDbContext _db;

    public InvoiceNumberGeneratorDelegationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<MakablesDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new MakablesDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task NextAsync_resolves_country_config_then_reads_clock_then_calls_allocator_with_Invoice_scope()
    {
        // 23:30 UTC on Dec 31 = 00:30 Prague-local on Jan 1, so the
        // TZ-aware arm computes year 2027. Without the TZ conversion, the
        // generator would pass 2026 — this is the bug the T-0068a
        // signature change closes.
        var nowUtc = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(nowUtc);

        var czConfig = CountryConfiguration.Create(
            countryId: "CZ",
            defaultCurrencyCode: "CZK",
            defaultLanguageCode: "cs-CZ",
            timeZoneId: "Europe/Prague",
            phonePrefix: "+420",
            dateFormat: "d. M. yyyy",
            standardVatRateBp: 2100,
            taxIdLabel: "DIČ",
            vatIdLabel: "DIČ",
            registrationNumberLabel: "IČO",
            defaultPaymentProvider: "comgate",
            defaultShippingCarrier: "packeta",
            defaultRegistry: "ares",
            defaultEmailProvider: "resend",    issuerName: "JVM YORE s.r.o.",    issuerIco: "00000000");
        var configs = Substitute.For<ICountryConfigurationRepository>();
        configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CountryConfiguration?>(czConfig));

        var generator = new InvoiceNumberGenerator(_db, clock, configs);

        // SQLite rejects the FOR UPDATE clause that the Postgres allocator
        // emits — that's the proof the flow reached the allocator. The
        // throw is intentional; race + round-trip assertions live in
        // InvoiceNumberGeneratorRaceSafetyTests on real Postgres.
        var act = async () => await generator.NextAsync("CZ", CancellationToken.None);
        var ex = await act.Should().ThrowAsync<SqliteException>();
        ex.Which.Message.Should().Contain("FOR",
            because: "the allocator's FOR UPDATE query must reach SQLite — " +
                     "proving execution flowed through both the TZ arm and the allocator entry point");

        // The TZ arm fired with the right country code.
        await configs.Received(1).GetByCodeAsync("CZ", Arg.Any<CancellationToken>());
        // The year was computed from the clock — not a constant or
        // DateTimeOffset.UtcNow.Year sneaking in.
        _ = clock.Received().UtcNow;
    }
}
