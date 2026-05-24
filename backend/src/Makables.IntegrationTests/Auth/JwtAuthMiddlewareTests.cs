using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Makables.Core.Domain.Identity;
using Makables.Infra.Common.Auth;
using Makables.Infra.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Makables.IntegrationTests.Auth;

/// <summary>
/// End-to-end audience-binding tests across the four Web hosts. Per
/// ADR 0012 §JWT structure:
///   - Customer host accepts customer + admin audiences.
///   - Maker host accepts maker + admin audiences.
///   - Admin host accepts admin audience only.
///   - Public host accepts any authenticated audience.
///
/// Each test issues a real JWT via the production <see cref="JwtIssuer"/>
/// against a deterministic test key, then presents it to the host and
/// asserts the framework-authorized response. The protected endpoint
/// is registered inline by <see cref="WithProtectedEcho"/> so we don't
/// depend on Phase-2 controllers that don't exist yet.
/// </summary>
public sealed class JwtAuthMiddlewareTests
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // 32 zero bytes
    private const string TestIssuer = "https://makables.test";

    private static User CreateUser(UserRole role) =>
        User.Create("user-1", "anna@example.cz", role, "Anna", "CZ", "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");

    /// <summary>
    /// Spin up <typeparamref name="TProgram"/> with the production
    /// <c>AddMakablesAuth</c> wiring, swap the DbContext to SQLite, and
    /// graft a single <c>/__test/protected</c> endpoint marked
    /// <c>[Authorize]</c>. Each test instantiates this fresh.
    /// </summary>
    private static WebApplicationFactory<TProgram> Build<TProgram>() where TProgram : class =>
        new WebApplicationFactory<TProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("IntegrationTest");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = "Host=placeholder;Database=ignored",
                    ["Jwt:Issuer"] = TestIssuer,
                    ["Jwt:SigningKeyBase64"] = TestKeyBase64,
                });
            });

            builder.ConfigureServices(services =>
            {
                var dbContextDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<MakablesDbContext>));
                if (dbContextDescriptor is not null) services.Remove(dbContextDescriptor);

                var connection = new SqliteConnection("DataSource=:memory:");
                connection.Open();
                services.AddSingleton(connection);
                services.AddDbContext<MakablesDbContext>(o => o.UseSqlite(connection));
            });

            builder.Configure(app =>
            {
                // Mirror Program.cs ordering for the auth-relevant slice.
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(e =>
                {
                    e.MapGet("/__test/protected",
                        [Authorize] (Microsoft.AspNetCore.Http.HttpContext ctx) =>
                            Results.Ok(new { sub = ctx.User.Identity?.Name ?? string.Empty }));
                });
            });
        });

    private static string IssueToken(UserRole role, string audience)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var token = issuer.Issue(CreateUser(role), audience, DateTimeOffset.UtcNow);
        return token.Token;
    }

    private static async Task<HttpStatusCode> CallProtectedAsync<TProgram>(string token) where TProgram : class
    {
        using var factory = Build<TProgram>();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/__test/protected");
        return response.StatusCode;
    }

    // === Customer host ===

    [Fact]
    public async Task Customer_host_accepts_customer_audience()
    {
        var status = await CallProtectedAsync<Makables.Web.Customer.Program>(
            IssueToken(UserRole.Customer, "customer"));
        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Customer_host_accepts_admin_audience()
    {
        var status = await CallProtectedAsync<Makables.Web.Customer.Program>(
            IssueToken(UserRole.Admin, "admin"));
        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Customer_host_rejects_maker_audience()
    {
        var status = await CallProtectedAsync<Makables.Web.Customer.Program>(
            IssueToken(UserRole.Maker, "maker"));
        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    // === Maker host ===

    [Fact]
    public async Task Maker_host_accepts_maker_audience()
    {
        var status = await CallProtectedAsync<Makables.Web.Maker.Program>(
            IssueToken(UserRole.Maker, "maker"));
        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Maker_host_accepts_admin_audience()
    {
        var status = await CallProtectedAsync<Makables.Web.Maker.Program>(
            IssueToken(UserRole.Admin, "admin"));
        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Maker_host_rejects_customer_audience()
    {
        var status = await CallProtectedAsync<Makables.Web.Maker.Program>(
            IssueToken(UserRole.Customer, "customer"));
        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    // === Admin host ===

    [Fact]
    public async Task Admin_host_accepts_admin_audience()
    {
        var status = await CallProtectedAsync<Makables.Web.Admin.Program>(
            IssueToken(UserRole.Admin, "admin"));
        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_host_rejects_customer_audience()
    {
        var status = await CallProtectedAsync<Makables.Web.Admin.Program>(
            IssueToken(UserRole.Customer, "customer"));
        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_host_rejects_maker_audience()
    {
        var status = await CallProtectedAsync<Makables.Web.Admin.Program>(
            IssueToken(UserRole.Maker, "maker"));
        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    // === Public host ===

    [Theory]
    [InlineData(UserRole.Customer, "customer")]
    [InlineData(UserRole.Maker, "maker")]
    [InlineData(UserRole.Admin, "admin")]
    public async Task Public_host_accepts_any_audience(UserRole role, string audience)
    {
        var status = await CallProtectedAsync<Makables.Web.Public.Program>(IssueToken(role, audience));
        status.Should().Be(HttpStatusCode.OK);
    }

    // === Cross-cutting ===

    [Fact]
    public async Task Customer_host_rejects_token_signed_by_a_different_key()
    {
        // Issue a token under a different signing key.
        var otherKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = otherKey,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var token = issuer.Issue(CreateUser(UserRole.Customer), "customer", DateTimeOffset.UtcNow).Token;

        var status = await CallProtectedAsync<Makables.Web.Customer.Program>(token);
        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Customer_host_rejects_no_token()
    {
        using var factory = Build<Makables.Web.Customer.Program>();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/__test/protected");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
