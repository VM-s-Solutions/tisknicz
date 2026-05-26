using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Registry;
using Makables.Core.Domain.Registry.Validators;
using Makables.Core.Domain.SeedWork;
using Makables.Infra.Common.Czech;
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
///   <item><description><b>DB cache.</b> Per ADR 0018 §"Caching policy".
///     If a fresh row exists (<c>expires_at &gt; now</c>) it's served and
///     promoted to in-memory.</description></item>
///   <item><description><b>HTTP fetch from ARES.</b> Polly retries 408 / 429 / 5xx
///     within the overall-timeout wall clock.</description></item>
///   <item><description><b>On HTTP 404 →</b> <see cref="BusinessErrorMessage.CompanyNotFound"/>.</description></item>
///   <item><description><b>On ARES unreachable (Transient) →</b> stale-fallback:
///     a DB row up to <see cref="AresOptions.StaleFallbackDays"/> days
///     past <c>expires_at</c> is returned with
///     <see cref="CompanyRecord.IsStale"/> = <c>true</c>. T-0033
///     <c>RegisterMaker</c> surfaces this so the user sees a "data may
///     be outdated" warning but registration proceeds.</description></item>
/// </list>
///
/// All failures are <see cref="BusinessResult{T}"/> failures; no
/// exceptions cross the boundary. No secret in the URL (ARES is public)
/// so the T-0031 Authorization-header pattern doesn't apply here.
/// </summary>
public sealed class AresCompanyRegistry(
    IHttpClientFactory httpClientFactory,
    ResiliencePipelineRegistry<string> pipelineRegistry,
    ICompanyRegistryCacheRepository cacheRepository,
    IUnitOfWork unitOfWork,
    IMemoryCache memoryCache,
    IClock clock,
    IOptions<AresOptions> options,
    ILogger<AresCompanyRegistry> logger) : ICompanyRegistry
{
    public const string ProviderCode = "ares";
    public const string HttpClientName = "Makables.Infra.Clients.Ares";

    private ResiliencePipeline<HttpResponseMessage> RetryPipeline =>
        pipelineRegistry.GetPipeline<HttpResponseMessage>(HttpClientName);

    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
    };

    public string Code => ProviderCode;

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

        // 2. DB cache.
        var dbEntry = await cacheRepository.GetAsync(ProviderCode, registrationNumber, cancellationToken);
        var now = clock.UtcNow;
        if (dbEntry is not null && dbEntry.ExpiresAt > now)
        {
            var freshFromDb = Deserialize(dbEntry.PayloadJson);
            if (freshFromDb is not null)
            {
                StoreInMemory(memoryKey, freshFromDb, opts);
                return BusinessResult.Success(freshFromDb);
            }
            // Payload corrupt — fall through to HTTP; log + don't block.
            logger.LogWarning(
                "DB cache row for {Registry}/{Ico} could not be deserialised; falling through to HTTP.",
                ProviderCode, registrationNumber);
        }

        // 3. HTTP fetch from ARES.
        var httpResult = await FetchFromAresAsync(registrationNumber, opts, cancellationToken);

        if (httpResult.IsSuccess)
        {
            var record = httpResult.Value!;
            await PersistAsync(dbEntry, record, opts, cancellationToken);
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
            var stale = Deserialize(dbEntry.PayloadJson);
            if (stale is not null)
            {
                logger.LogInformation(
                    "ARES unreachable for {Ico}; serving stale DB cache (fetched {FetchedAt:o}).",
                    registrationNumber, dbEntry.FetchedAt);
                var staleRecord = stale with { IsStale = true };
                // Don't write the stale value to the in-memory cache —
                // we want the next request to actually retry ARES.
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
                return BusinessResult.Failure<CompanyRecord>(
                    Error.NotFound("company"));
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
                payload = await response.Content.ReadFromJsonAsync<AresEkonomickySubjekt>(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ARES response for {Ico} could not be deserialised.", ico);
                return BusinessResult.Failure<CompanyRecord>(
                    Error.Permanent(BusinessErrorMessage.CompanyRegistryPermanent));
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.Ico))
            {
                return BusinessResult.Failure<CompanyRecord>(
                    Error.Permanent(BusinessErrorMessage.CompanyRegistryPermanent));
            }

            try
            {
                return BusinessResult.Success(MapToRecord(payload, clock.UtcNow));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ARES response for {Ico} failed structural mapping.", ico);
                return BusinessResult.Failure<CompanyRecord>(
                    Error.Permanent(BusinessErrorMessage.CompanyRegistryPermanent));
            }
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
        CompanyRegistryCacheEntry? existing,
        CompanyRecord record,
        AresOptions opts,
        CancellationToken cancellationToken)
    {
        var fetchedAt = record.FetchedAt;
        var expiresAt = fetchedAt + TimeSpan.FromHours(Math.Max(1, opts.DbCacheTtlHours));
        var payloadJson = JsonSerializer.Serialize(CachedRecord.From(record), PayloadSerializerOptions);

        if (existing is null)
        {
            cacheRepository.Add(CompanyRegistryCacheEntry.Create(
                registryCode: ProviderCode,
                registrationNumber: record.RegistrationNumber,
                payloadJson: payloadJson,
                fetchedAt: fetchedAt,
                expiresAt: expiresAt));
        }
        else
        {
            existing.Refresh(payloadJson, fetchedAt, expiresAt);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string MemoryKey(string ico) => $"ares:{ico}";

    private static CompanyRecord? Deserialize(string payloadJson)
    {
        try
        {
            var cached = JsonSerializer.Deserialize<CachedRecord>(payloadJson, PayloadSerializerOptions);
            return cached?.ToRecord();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Flat DTO for cache persistence. <see cref="CompanyRecord"/>
    /// embeds a live <see cref="Address"/> aggregate whose
    /// <see cref="Address.Create"/> factory is the only construction
    /// path — System.Text.Json can't round-trip through it. Mapping
    /// to/from this DTO keeps cache rows readable in SQL too.
    /// </summary>
    private sealed record CachedRecord(
        string RegistrationNumber,
        string? VatId,
        string CompanyName,
        string? LegalForm,
        string Street,
        string HouseNumber,
        string City,
        string Zip,
        string? State,
        string CountryCodeIso,
        string AuditCountryCode,
        DateOnly? IncorporatedOn,
        bool IsActiveInRegistry,
        string SourceRegistry,
        DateTimeOffset FetchedAt)
    {
        public static CachedRecord From(CompanyRecord r) => new(
            r.RegistrationNumber,
            r.VatId,
            r.CompanyName,
            r.LegalForm,
            r.RegisteredAddress.Street,
            r.RegisteredAddress.HouseNumber,
            r.RegisteredAddress.City,
            r.RegisteredAddress.Zip,
            r.RegisteredAddress.State,
            r.RegisteredAddress.CountryCodeIso,
            r.RegisteredAddress.CountryCode,
            r.IncorporatedOn,
            r.IsActiveInRegistry,
            r.SourceRegistry,
            r.FetchedAt);

        public CompanyRecord ToRecord() => new(
            RegistrationNumber: RegistrationNumber,
            VatId: VatId,
            CompanyName: CompanyName,
            LegalForm: LegalForm,
            RegisteredAddress: Address.Create(
                id: $"ares-snapshot-{RegistrationNumber}",
                street: Street,
                houseNumber: HouseNumber,
                city: City,
                zip: Zip,
                countryCodeIso: CountryCodeIso,
                auditCountryCode: AuditCountryCode,
                state: State),
            IncorporatedOn: IncorporatedOn,
            IsActiveInRegistry: IsActiveInRegistry,
            SourceRegistry: SourceRegistry,
            FetchedAt: FetchedAt,
            IsStale: false);
    }

    // ---- ARES response shape + mapping ----

    private static CompanyRecord MapToRecord(AresEkonomickySubjekt p, DateTimeOffset now)
    {
        var sidlo = p.Sidlo ?? new AresSidlo(null, null, null, null, null);
        var address = Address.Create(
            id: $"ares-snapshot-{p.Ico}",
            street: NullableToRequired(sidlo.NazevUlice ?? sidlo.NazevObce, "unknown"),
            houseNumber: NullableToRequired(sidlo.CisloDomovni?.ToString(), "0"),
            city: NullableToRequired(sidlo.NazevObce, "unknown"),
            zip: NullableToRequired(sidlo.Psc?.ToString(), "00000"),
            countryCodeIso: "CZ",
            auditCountryCode: "CZ");

        DateOnly? incorporatedOn = null;
        if (!string.IsNullOrWhiteSpace(p.DatumVzniku)
            && DateOnly.TryParse(p.DatumVzniku, out var d))
        {
            incorporatedOn = d;
        }

        return new CompanyRecord(
            RegistrationNumber: p.Ico!,
            VatId: string.IsNullOrWhiteSpace(p.Dic) ? null : p.Dic,
            CompanyName: p.ObchodniJmeno ?? string.Empty,
            LegalForm: CzechLegalForms.Resolve(p.PravniForma),
            RegisteredAddress: address,
            IncorporatedOn: incorporatedOn,
            IsActiveInRegistry: string.IsNullOrWhiteSpace(p.DatumZaniku),
            SourceRegistry: ProviderCode,
            FetchedAt: now,
            IsStale: false);
    }

    /// <summary>
    /// ARES sometimes omits fields that are required-non-null in
    /// <see cref="Address.Create"/>. We substitute a structural placeholder
    /// so the record still constructs; T-0033's RegisterMaker can detect
    /// the placeholder (or simply present the data to the user for
    /// confirmation, which is the ADR 0018 expectation).
    /// </summary>
    private static string NullableToRequired(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    // ARES v3 JSON shape (subset we actually map; tolerant of extra fields).
    private sealed record AresEkonomickySubjekt(
        [property: JsonPropertyName("ico")]            string? Ico,
        [property: JsonPropertyName("obchodniJmeno")]  string? ObchodniJmeno,
        [property: JsonPropertyName("dic")]            string? Dic,
        [property: JsonPropertyName("pravniForma")]    string? PravniForma,
        [property: JsonPropertyName("datumVzniku")]    string? DatumVzniku,
        [property: JsonPropertyName("datumZaniku")]    string? DatumZaniku,
        [property: JsonPropertyName("sidlo")]          AresSidlo? Sidlo);

    private sealed record AresSidlo(
        [property: JsonPropertyName("nazevUlice")]     string? NazevUlice,
        [property: JsonPropertyName("cisloDomovni")]   int? CisloDomovni,
        [property: JsonPropertyName("nazevObce")]      string? NazevObce,
        [property: JsonPropertyName("psc")]            int? Psc,
        [property: JsonPropertyName("nazevStatu")]     string? NazevStatu);
}
