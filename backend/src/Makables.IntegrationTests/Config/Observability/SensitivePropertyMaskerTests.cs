using FluentAssertions;
using Makables.Config.Observability;
using Serilog.Events;
using Serilog.Parsing;

namespace Makables.IntegrationTests.Config.Observability;

/// <summary>
/// Pins the redaction patterns in <see cref="SensitivePropertyMasker"/>.
/// Reviewer T-0023 BLOCKER B-2 — the ticket asserted that a bare "token"
/// property would be redacted; it wasn't. These tests now make sure
/// that contract is honored and won't silently regress.
/// </summary>
public class SensitivePropertyMaskerTests
{
    private static LogEvent BuildEvent(string propertyName, string value)
    {
        var template = new MessageTemplateParser().Parse("test {" + propertyName + "}");
        var properties = new List<LogEventProperty> { new(propertyName, new ScalarValue(value)) };
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            messageTemplate: template,
            properties: properties);
    }

    private static string? GetPropertyValue(LogEvent evt, string name)
    {
        if (!evt.Properties.TryGetValue(name, out var prop)) return null;
        return prop is ScalarValue s ? s.Value?.ToString() : prop.ToString();
    }

    private static readonly SensitivePropertyMasker Masker = new();
    private static readonly TestPropertyFactory Factory = new();

    [Theory]
    [InlineData("Password")]
    [InlineData("passwd")]
    [InlineData("Secret")]
    [InlineData("ApiKey")]
    [InlineData("api_key")]
    [InlineData("Token")]              // BLOCKER B-2 fix — bare "token"
    [InlineData("RawToken")]           // BLOCKER B-2 fix — outbox payload property
    [InlineData("raw_token")]
    [InlineData("AccessToken")]
    [InlineData("RefreshToken")]
    [InlineData("TokenHash")]
    [InlineData("token_hash")]
    [InlineData("SigningKey")]
    [InlineData("signing_key")]
    [InlineData("ComgatePayload")]
    [InlineData("comgate_payload")]
    [InlineData("Authorization")]
    public void Redacts_property_names_matching_any_sensitive_pattern(string propertyName)
    {
        var evt = BuildEvent(propertyName, "supersecretvalue");
        Masker.Enrich(evt, Factory);
        GetPropertyValue(evt, propertyName).Should().Be("***");
    }

    [Theory]
    [InlineData("Email")]
    [InlineData("UserId")]
    [InlineData("CountryCode")]
    [InlineData("RequestId")]
    [InlineData("FullName")]
    public void Leaves_non_sensitive_property_names_untouched(string propertyName)
    {
        var evt = BuildEvent(propertyName, "plain-value");
        Masker.Enrich(evt, Factory);
        GetPropertyValue(evt, propertyName).Should().Be("plain-value");
    }

    private sealed class TestPropertyFactory : Serilog.Core.ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }
}
