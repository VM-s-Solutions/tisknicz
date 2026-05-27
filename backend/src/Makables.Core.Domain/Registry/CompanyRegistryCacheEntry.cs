namespace Makables.Core.Domain.Registry;

/// <summary>
/// DB-cache row per ADR 0018 §"Caching policy". One row per
/// (<see cref="RegistryCode"/>, <see cref="RegistrationNumber"/>) pair;
/// the payload is the JSON-serialised <see cref="CompanyRecord"/> as it
/// came back from the registry at <see cref="FetchedAt"/>.
///
/// NOT <see cref="Common.Auditable"/>: this is bookkeeping
/// infrastructure (same posture as
/// <see cref="Outbox.OutboxEvent"/>). The background eviction Function
/// (out-of-scope for T-0032; ADR 0018 §"Caching policy" mentions
/// <c>EvictExpiredRegistryCache</c>) deletes truly-expired rows; until
/// then they stay around for the 7-day stale-fallback window.
/// </summary>
public sealed class CompanyRegistryCacheEntry
{
    public string RegistryCode { get; private set; } = default!;
    public string RegistrationNumber { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;
    public DateTimeOffset FetchedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    private CompanyRegistryCacheEntry() { }

    public static CompanyRegistryCacheEntry Create(
        string registryCode,
        string registrationNumber,
        string payloadJson,
        DateTimeOffset fetchedAt,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        if (expiresAt <= fetchedAt)
            throw new ArgumentException("ExpiresAt must be after FetchedAt.", nameof(expiresAt));

        return new CompanyRegistryCacheEntry
        {
            RegistryCode = registryCode,
            RegistrationNumber = registrationNumber,
            PayloadJson = payloadJson,
            FetchedAt = fetchedAt,
            ExpiresAt = expiresAt,
        };
    }

    /// <summary>
    /// Refresh the row in-place with a newly-fetched payload (called on
    /// cache-miss-then-fetch when the row already exists from a prior
    /// stale entry).
    /// </summary>
    public void Refresh(string payloadJson, DateTimeOffset fetchedAt, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        if (expiresAt <= fetchedAt)
            throw new ArgumentException("ExpiresAt must be after FetchedAt.", nameof(expiresAt));
        PayloadJson = payloadJson;
        FetchedAt = fetchedAt;
        ExpiresAt = expiresAt;
    }
}
