namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for every Phase-2 email-bound auth flow (magic link,
/// email confirmation, password reset). All three flows ship the same
/// four fields to T-0029's <c>ProcessOutboxFunction</c>; the event
/// type discriminates which email template to render.
///
/// The <c>RawToken</c> property name is intentional — the T-0014
/// <see cref="Makables.Config.Observability.SensitivePropertyMasker"/>
/// pattern list includes the bare substring <c>"token"</c>, so any
/// Serilog scope or EF SQL trace capturing this payload
/// property-by-property is redacted. Pinned by
/// <c>SensitivePropertyMaskerTests</c>.
/// </summary>
public sealed record OneTimeTokenOutboxPayload(
    string UserId,
    string Email,
    string RawToken,
    DateTimeOffset ExpiresAt);
