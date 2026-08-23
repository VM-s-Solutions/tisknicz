using FluentAssertions;
using Makables.Core.AppServices.Features.Email;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Email;
using Makables.Core.Domain.Outbox;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.IntegrationTests.Outbox;

/// <summary>
/// End-to-end composition of the three no-reply auth emails, against the
/// REAL seeded copy in <c>email_template_translations</c> — the rows the
/// migrations put there, not a fixture.
///
/// <para>
/// This is the test that keeps two halves of the same change honest. The
/// copy lives in a migration; the Czech-locale timestamp and the HTML
/// shell live in code. Either half can be edited without the other
/// noticing: re-add a " UTC" suffix to the DB body, or stop putting the
/// action URL on its own line, and the unit tests all stay green while
/// the actual email regresses. Here the two meet.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthEmailCompositionIntegrationTests : IAsyncLifetime
{
    /// <summary>18:30Z in August is 20:30 in Prague — the DST case.</summary>
    private static readonly DateTimeOffset Expiry = new(2026, 8, 24, 18, 30, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;
    private CapturingEmailProvider _provider = default!;

    public AuthEmailCompositionIntegrationTests(PostgresHarness harness) => _harness = harness;

    /// <summary>Captures the assembled message instead of calling Resend.</summary>
    private sealed class CapturingEmailProvider : IEmailProvider
    {
        public EmailMessage? Last { get; private set; }

        public string Code => "capturing";

        public Task<BusinessResult<EmailSentReceipt>> SendAsync(
            EmailMessage message, CancellationToken cancellationToken)
        {
            Last = message;
            return Task.FromResult(BusinessResult.Success(
                new EmailSentReceipt("captured", Expiry)));
        }
    }

    public Task InitializeAsync()
    {
        // No table reset: this suite reads the migration-seeded email
        // template rows, which ResetMutableTablesAsync deliberately leaves
        // alone, and writes nothing.
        _provider = new CapturingEmailProvider();
        _factory = new WebApplicationFactory<Makables.Web.Customer.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("IntegrationTest");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Postgres"] = _harness.ConnectionString,
                        ["Jwt:Issuer"] = "https://makables.test",
                        ["Jwt:SigningKeyBase64"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                        ["SendGrid:ApiKey"] = "SG.integration-test-stub",
                        ["SendGrid:DefaultFromAddress"] = "no-reply@makables.test",
                        ["Resend:ApiKey"] = "re_integration_test_stub",
                        ["Resend:DefaultFromAddress"] = "no-reply@makables.test",
                        ["PublicAppUrls:WebBaseUrl"] = "https://makables.test",
                        ["Mapbox:AccessToken"] = "pk.integration-test-stub",
                        ["Ares:BaseUrl"] = "https://ares.integration-test.local",
                        ["Comgate:MerchantId"] = "12345",
                        ["Comgate:Secret"] = "integration-test-secret",
                        ["Comgate:BaseUrl"] = "https://payments.comgate.test",
                        ["Packeta:ApiKey"] = "integration-test-packeta-key",
                        ["Packeta:PublicWidgetKey"] = "integration-test-packeta-public-key",
                        ["Packeta:BaseUrl"] = "https://api.packeta.test",
                        ["Packeta:WidgetScriptUrl"] = "https://widget.packeta.test/v6/library.js",
                        ["Packeta:SenderLabel"] = "makables-test",
                        ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                        ["Cors:AllowedOrigins:customer:0"] = "https://customer.makables.test",
                    });
                });
                builder.ConfigureServices(services =>
                {
                    var dbContextDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<MakablesDbContext>));
                    if (dbContextDescriptor is not null) services.Remove(dbContextDescriptor);
                    services.AddDbContext<MakablesDbContext>(o => o.UseNpgsql(_harness.ConnectionString));

                    foreach (var d in services.Where(d => d.ServiceType == typeof(IEmailProvider) && d.ServiceKey is null).ToList())
                        services.Remove(d);
                    services.AddSingleton<IEmailProvider>(_provider);
                });
            });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private async Task<EmailMessage> ComposeAsync(string outboxEventType, string languageCode = LanguageCode.CsCZ)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new OneTimeTokenOutboxPayload(
            UserId: "user-compose-1",
            Email: "anna@example.cz",
            RawToken: "RawTok123",
            ExpiresAt: Expiry,
            LanguageCode: languageCode));

        using var scope = _factory.Services.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<IEmailSendService>();

        var result = await sut.SendAsync(outboxEventType, payload, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            because: $"the seeded template + translation for '{outboxEventType}' must resolve");
        return _provider.Last!;
    }

    // === Registration (e-mail confirmation) ===

    [Fact]
    public async Task Registration_email_ships_a_branded_html_part_with_a_working_button()
    {
        var message = await ComposeAsync(OutboxEventTypes.AuthEmailConfirmationSend);

        message.Subject.Should().Be("Potvrďte svůj e-mail");
        message.HtmlBody.Should().NotBeNullOrWhiteSpace();
        message.HtmlBody.Should().StartWith("<!DOCTYPE html>").And.EndWith("</html>");
        message.HtmlBody.Should().Contain(">Potvrďte svůj e-mail</h1>");
        message.HtmlBody.Should().Contain(">Potvrdit e-mail</a>");
        message.HtmlBody.Should().Contain("href=\"https://makables.test/verify?token=RawTok123\"");
        message.HtmlBody.Should().Contain("neodpovídejte");
        message.HtmlBody.Should().NotContain("{{");
    }

    // === Password reset ===

    [Fact]
    public async Task Password_reset_email_ships_a_branded_html_part_with_a_working_button()
    {
        var message = await ComposeAsync(OutboxEventTypes.AuthPasswordResetSend);

        message.Subject.Should().Be("Obnovení hesla");
        message.HtmlBody.Should().Contain(">Obnovení hesla</h1>");
        message.HtmlBody.Should().Contain(">Nastavit nové heslo</a>");
        message.HtmlBody.Should().Contain("href=\"https://makables.test/reset?token=RawTok123\"");
        message.HtmlBody.Should().Contain("vaše stávající heslo zůstává v platnosti");
    }

    [Fact]
    public async Task Magic_link_email_ships_a_branded_html_part_with_a_working_button()
    {
        var message = await ComposeAsync(OutboxEventTypes.AuthMagicLinkSend);

        message.HtmlBody.Should().Contain(">Přihlásit se</a>");
        message.HtmlBody.Should().Contain("href=\"https://makables.test/magic?token=RawTok123\"");
    }

    // === The date ===

    [Theory]
    [InlineData(OutboxEventTypes.AuthEmailConfirmationSend)]
    [InlineData(OutboxEventTypes.AuthPasswordResetSend)]
    [InlineData(OutboxEventTypes.AuthMagicLinkSend)]
    public async Task Expiry_reads_as_a_Czech_date_in_Prague_time_in_both_parts(string outboxEventType)
    {
        var message = await ComposeAsync(outboxEventType);

        // 18:30Z → 20:30 CEST, "d. M. yyyy" from CountryConfiguration.
        message.PlainTextBody.Should().Contain("24. 8. 2026, 20:30");
        message.HtmlBody.Should().Contain("24. 8. 2026, 20:30");
    }

    [Theory]
    [InlineData(OutboxEventTypes.AuthEmailConfirmationSend)]
    [InlineData(OutboxEventTypes.AuthPasswordResetSend)]
    [InlineData(OutboxEventTypes.AuthMagicLinkSend)]
    public async Task No_seeded_body_still_claims_UTC_or_prints_an_ISO_timestamp(string outboxEventType)
    {
        var message = await ComposeAsync(outboxEventType);

        // The rendered value is Prague wall-clock now; a leftover " UTC"
        // in the DB copy would make the email actively lie.
        message.PlainTextBody.Should().NotContain("UTC");
        message.PlainTextBody.Should().NotContain("2026-08-24");
        message.HtmlBody.Should().NotContain("UTC");
    }

    // === Both parts, one message ===

    [Fact]
    public async Task Plain_text_alternative_survives_alongside_the_html()
    {
        var message = await ComposeAsync(OutboxEventTypes.AuthPasswordResetSend);

        // multipart/alternative: a text-only reader must still get the link.
        message.PlainTextBody.Should().Contain("https://makables.test/reset?token=RawTok123");
        message.PlainTextBody.Should().NotContain("<");
        message.PlainTextBody.Should().NotContain("{{");
    }

    [Fact]
    public async Task English_recipients_get_the_English_translation_and_chrome()
    {
        var message = await ComposeAsync(OutboxEventTypes.AuthPasswordResetSend, LanguageCode.EnUS);

        message.Subject.Should().Be("Password reset");
        message.HtmlBody.Should().Contain("<html lang=\"en\"");
        message.HtmlBody.Should().Contain(">Set a new password</a>");
    }
}
