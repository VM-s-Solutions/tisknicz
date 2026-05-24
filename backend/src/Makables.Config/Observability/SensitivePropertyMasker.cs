using Serilog.Core;
using Serilog.Events;

namespace Makables.Config.Observability;

/// <summary>
/// Serilog enricher that walks the message-template properties of every
/// emitted event and replaces values whose key looks sensitive (password,
/// token, secret, signing key, raw Comgate payload). Per ADR 0023 §4
/// Logging: <em>sensitive fields redacted at the logger layer</em>.
///
/// Matching is case-insensitive substring against the property name. The
/// replacement is the literal string <c>"***"</c>; the property still
/// exists on the event so downstream consumers don't get tripped up by
/// missing fields, just by the redacted shape.
/// </summary>
public sealed class SensitivePropertyMasker : ILogEventEnricher
{
    private static readonly string[] Patterns =
    [
        "password",
        "passwd",
        "secret",
        "apikey",
        "api_key",
        // `token` (bare substring) catches RawToken, AccessToken,
        // RefreshToken, MagicLinkToken, and the JSON-serialized outbox
        // payloads that carry single-use tokens (T-0023 reviewer B-2).
        // The two specific entries below are kept for clarity but are
        // strictly redundant — left in so a future grep finds them.
        "token",
        "tokenhash",
        "token_hash",
        "refreshtoken",
        "refresh_token",
        "signingkey",
        "signing_key",
        "comgatepayload",
        "comgate_payload",
        "authorization",
    ];

    private const string Mask = "***";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        foreach (var (name, _) in logEvent.Properties)
        {
            if (LooksSensitive(name))
            {
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(name, Mask));
            }
        }
    }

    private static bool LooksSensitive(string propertyName)
    {
        foreach (var pattern in Patterns)
        {
            if (propertyName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
