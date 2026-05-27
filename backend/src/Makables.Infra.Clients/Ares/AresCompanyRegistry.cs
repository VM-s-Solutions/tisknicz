using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Registry;
using Makables.Core.Domain.Registry.Validators;
using Makables.Infra.Clients.Ares.Caching;
using Makables.Infra.Clients.Ares.Mapping;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace Makables.Infra.Clients.Ares;

/// <summary>
/// ARES (Czech state company registry) adapter per ADR 0018. Two-layer
/// cache + stale-fallback flow:
///
/// <list type="number">
///   <item><description><b>Format check.</b> IČO is validated by
///     <see cref="CzechIcoValidator"/> before any I/O — invalid format
///     returns <see cref="BusinessErrorMessage.IcoFormatInvalid"/>
///     without consuming the ARES rate-limit budget.</description></item>
///   <item><description><b>In-memory cache.</b> Hot path. TTL =
///     <see cref="AresOptions.InMemoryCacheTtlMinutes"/>.</description></item>
///   <item><description><b>DB cache.</b> Via <see cref="ICompanyRegistryCacheStore"/>
///     — uses a dedicated <see cref="DbContext"/> per call so the
///     adapter's cache write CANNOT flush a calling command's
///     tracked-but-uncommitted aggregates (T-0032 sec reviewer M-1).</description></item>
///   <item><description><b>HTTP fetch from ARES.</b> Polly retries
///     408 / 429 / 5xx within the overall-timeout wall clock.</description></item>
///   <item><description><b>On HTTP 404 →</b> <see cref="BusinessErrorMessage.CompanyNotFound"/>.</description></item>
///   <item><description><b>On ARES unreachable (Transient) →</b>
///     stale-fallback: a DB row up to
///     <see cref="AresOptions.StaleFallbackDays"/> days past
///     <c>expires_at</c> is returned with
///     <see cref="CompanyRecord.IsStale"/> = <c>true</c>. T-0033
///     <c>RegisterMaker</c> surfaces this so the user sees a "data may
///     be outdated" warning but registration proceeds.</description></item>
/// </list>
///
/// All failures are <see cref="BusinessResult{T}"/> failures; no
/// exceptions cross the boundary. No secret in the URL (ARES is public)
/// so the T-0031 Authorization-header pattern doesn't apply here.
///
/// PII note: the IČO itself is appended to log entries below. For a
/// sole-proprietor (OSVČ — fyzická osoba) the IČO ties to a natural
/// person under GDPR Art. 4(1); when the OTel pipeline gains a PII
/// redaction policy (ADR 0023 follow-up) IČO log fields should route
/// through it.
/// </summary>
public sealed class AresCompanyRegistry(
    IHttpClientFactory httpClientFactory,
    ResiliencePipelineRegistry<string> pipelineRegistry,
    ICompanyRegistryCacheStore cacheStore,
    IMemoryCache memoryCache,
    IClock clock,
    IOptions<AresOptions> options,
    ILogger<AresCompanyRegistry> logger) : ICompanyRegistry
{
    public const string ProviderCode = "ares";
    public const string HttpClientName = "Makables.Infra.Clients.Ares";

    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
    };

    public string Code => ProviderCode;

    private ResiliencePipeline<HttpResponseMessage> RetryPipeline =>
        pipelineRegistry.GetPipeline<HttpResponseMessage>(HttpClientName);

    public async Task<BusinessResult<CompanyRecord>> LookupByRegistrationNumberAsync(
        string registrationNumber,
        CancellationToken cancellationToken)
    {
        if (!CzechIcoValidator.IsValid(registrationNumber))
            return BusinessResult.Failure<CompanyRecord>(
                Error.Validation(nameof(registrationNumber), BusinessErrorMessage.IcoFormatInvalid));

        var opts = options.Value;
        var memoryKey = MemoryKey(registrationNumber);

        // 1. In-memory cache (the hot path).
        if (memoryCache.TryGetValue(memoryKey, out CompanyRecord? cached) && cached is not null)
        {
            return BusinessResult.Success(cached);
        }

        // 2. DB cache (via the dedicated store — independent DbContext scope).
        var dbEntry = await cacheStore.GetAsync(ProviderCode, registrationNumber, cancellationToken);
        var now = clock.UtcNow;
        if (dbEntry is not null && dbEntry.ExpiresAt > now)
        {
            var freshFromDb = Deserialize(dbEntry.PayloadJson, registrationNumber);
            if (freshFromDb is not null)
            {
                StoreInMemory(memoryKey, freshFromDb, opts);
                return BusinessResult.Success(freshFromDb);
            }
            // Payload corrupt — distinct LogWarning per T-0032 sec
            // reviewer Mn-1 so DB tampering vs schema-drift can be
            // distinguished from generic JSON failures elsewhere.
            logger.LogWarning(
                "DB cache row for {Registry}/{Ico} could not be reconstructed (corrupt payload or post-deserialise validation failure); falling through to HTTP.",
                ProviderCode, registrationNumber);
        }

        // 3. HTTP fetch from ARES.
        var httpResult = await FetchFromAresAsync(registrationNumber, opts, cancellationToken);

        if (httpResult.IsSuccess)
        {
            var record = httpResult.Value!;
            await PersistAsync(record, opts, cancellationToken);
            StoreInMemory(memoryKey, record, opts);
            return BusinessResult.Success(record);
        }

        // 4. Failure-path stale fallback. Only when the failure is Transient
        // (5xx / 429 / timeout) — a 404 is final and a Permanent parse
        // failure shouldn't be papered over with stale data.
        if (httpResult.Error!.Type == ErrorType.Transient
            && dbEntry is not null
            && dbEntry.FetchedAt > now - TimeSpan.FromDays(Math.Max(1, opts.StaleFallbackDays)))
        {
            var stale = Deserialize(dbEntry.PayloadJson, registrationNumber);
            if (stale is not null)
            {
                logger.LogInformation(
                    "ARES unreachable for {Ico}; serving stale DB cache (fetched {FetchedAt:o}).",
                    registrationNumber, dbEntry.FetchedAt);
                var staleRecord = stale with { IsStale = true };
                // Don't write the stale value to the in-memory cache —
                // the next request must retry ARES.
                return BusinessResult.Success(staleRecord);
            }
        }

        return BusinessResult.Failure<CompanyRecord>(httpResult.Error!);
    }

    // ---- HTTP ----

    private async Task<BusinessResult<CompanyRecord>> FetchFromAresAsync(
        string ico, AresOptions opts, CancellationToken cancellationToken)
    {
        var url = $"{opts.BaseUrl.TrimEnd('/')}/ekonomicke-subjekty-v-be/rest/ekonomicke-subjekty/{Uri.EscapeDataString(ico)}";
        var http = httpClientFactory.CreateClient(HttpClientName);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.OverallTimeoutSeconds)));

        HttpResponseMessage response;
        try
        {
            response = await RetryPipeline.ExecuteAsync(
                async ct => await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct),
                timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("ARES lookup for {Ico} timed out after {Seconds}s.", ico, opts.OverallTimeoutSeconds);
            return BusinessResult.Failure<CompanyRecord>(
                Error.Transient(BusinessErrorMessage.CompanyRegistryTransient));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ARES lookup for {Ico} threw after retries.", ico);
            return BusinessResult.Failure<CompanyRecord>(
                Error.Transient(BusinessErrorMessage.CompanyRegistryTransient));
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return BusinessResult.Failure<CompanyRecord>(Error.NotFound("company"));
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var isTransient = status is 408 or 429 or >= 500 and <= 599;
                logger.LogWarning("ARES lookup for {Ico} returned {Status}.", ico, status);
                return BusinessResult.Failure<CompanyRecord>(isTransient
                    ? Error.Transient(BusinessErrorMessage.CompanyRegistryTransient)
                    : Error.Permanent(BusinessErrorMessage.CompanyRegistryPermanent));
            }

            AresEkonomickySubjekt? payload;
            try
            {
                // T-0032 Copilot review: deserialise under the linked
                // timeoutCts so a stalled body stream can't push the
                // overall call past OverallTimeoutSeconds. The HTTP
                // send above already uses timeoutCts.Token; without
                // matching it here the body read becomes an unbounded
                // tail on top of the timeout budget.
                payload = await response.Content.ReadFromJsonAsync<AresEkonomickySubjekt>(timeoutCts.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ARES response for {Ico} could not be deserialised.", ico);
                return BusinessResult.Failure<CompanyRecord>(
                    Error.Permanent(BusinessErrorMessage.CompanyRegistryPermanent));
            }

            if (payload is null)
            {
                return BusinessResult.Failure<CompanyRecord>(
                    Error.Permanent(BusinessErrorMessage.CompanyRegistryPermanent));
            }

            var record = AresResponseMapper.TryMap(payload, clock.UtcNow, out var mapFailure);
            if (record is null)
            {
                logger.LogWarning(
                    "ARES response for {Ico} failed structural mapping: {MapFailure}.",
                    ico, mapFailure);
                return BusinessResult.Failure<CompanyRecord>(
                    Error.Permanent(BusinessErrorMessage.CompanyRegistryPermanent));
            }

            return BusinessResult.Success(record);
        }
    }

    // ---- cache ----

    private void StoreInMemory(string memoryKey, CompanyRecord record, AresOptions opts)
    {
        memoryCache.Set(memoryKey, record, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow =
                TimeSpan.FromMinutes(Math.Max(1, opts.InMemoryCacheTtlMinutes)),
        });
    }

    private async Task PersistAsync(
        CompanyRecord record,
        AresOptions opts,
        CancellationToken cancellationToken)
    {
        var fetchedAt = record.FetchedAt;
        var expiresAt = fetchedAt + TimeSpan.FromHours(Math.Max(1, opts.DbCacheTtlHours));
        var payloadJson = JsonSerializer.Serialize(
            CachedCompanyRecord.From(record), PayloadSerializerOptions);

        await cacheStore.UpsertAsync(
            ProviderCode,
            record.RegistrationNumber,
            payloadJson,
            fetchedAt,
            expiresAt,
            cancellationToken);
    }

    private static string MemoryKey(string ico) => $"ares:{ico}";

    /// <summary>
    /// Deserialize a cached row's payload back to a live
    /// <see cref="CompanyRecord"/>. Returns null on either JSON
    /// failure or post-deserialise <see cref="Address.Create"/>
    /// validation failure — both surface to the caller as a fall-through
    /// to HTTP, but the caller's LogWarning explicitly disambiguates.
    ///
    /// <para>
    /// T-0032 Copilot review: the <paramref name="ico"/> parameter
    /// now sanity-checks the cached payload against the requested IČO.
    /// A mismatch (cache corruption or accidental row tampering) is
    /// treated as "no cache hit" so the caller falls through to a fresh
    /// fetch rather than serving a different company's snapshot.
    /// </para>
    /// </summary>
    private static CompanyRecord? Deserialize(string payloadJson, string ico)
    {
        try
        {
            var cached = JsonSerializer.Deserialize<CachedCompanyRecord>(payloadJson, PayloadSerializerOptions);
            var record = cached?.ToRecord();
            if (record is null) return null;
            if (!string.Equals(record.RegistrationNumber, ico, StringComparison.Ordinal))
            {
                // Defence-in-depth: don't serve a different company's
                // snapshot if a row's IČO has drifted from its primary key.
                return null;
            }
            return record;
        }
        catch
        {
            return null;
        }
    }
}
