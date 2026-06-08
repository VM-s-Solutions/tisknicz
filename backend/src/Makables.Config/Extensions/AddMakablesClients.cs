using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Email;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Payments;
using Makables.Core.Domain.Registry;
using Makables.Core.Domain.Shipping;
using Makables.Infra.Clients.Ares;
using Makables.Infra.Clients.Comgate;
using Makables.Infra.Clients.Google;
using Makables.Infra.Clients.Mapbox;
using Makables.Infra.Clients.Packeta;
using Makables.Infra.Clients.SendGrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using Polly.Retry;
using SendGrid;

namespace Makables.Config.Extensions;

/// <summary>
/// Registers typed <see cref="HttpClient"/>s for every external adapter
/// (Comgate, Packeta, ARES, SendGrid, Mapbox, Google OAuth) and the
/// keyed implementations of the adapter interfaces. Per ADR 0008 /
/// patterns §A.15 (provider adapter pattern with keyed services).
///
/// Each Phase-2/4 ticket adds its own provider here. Concrete adapters
/// land per their ticket: T-0026 Google OAuth, T-0028 SendGrid,
/// T-0031 Mapbox, T-0032 ARES, T-0065 Comgate, T-0070 Packeta.
/// </summary>
public static class MakablesClientsExtensions
{
    public static IServiceCollection AddMakablesClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // === Google OAuth (T-0026) ===
        services.AddOptions<GoogleOAuthOptions>()
            .Bind(configuration.GetSection(GoogleOAuthOptions.SectionName));
        services.AddHttpClient(GoogleOAuthClient.HttpClientName);
        services.AddScoped<IGoogleOAuthClient, GoogleOAuthClient>();

        // === SendGrid (T-0028) ===
        // ValidateOnStart so a missing/typo'd SendGrid:ApiKey crashes the
        // host at boot, not on the first email send. T-0028 sec reviewer M-3.
        services.AddOptions<SendGridOptions>()
            .Bind(configuration.GetSection(SendGridOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey),
                "SendGrid:ApiKey is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.DefaultFromAddress),
                "SendGrid:DefaultFromAddress is required.")
            .Validate(o => o.RetryCount >= 0 && o.RetryCount <= 10,
                "SendGrid:RetryCount must be 0..10.")
            .Validate(o => o.PerSendTimeoutSeconds is >= 1 and <= 60,
                "SendGrid:PerSendTimeoutSeconds must be 1..60.")
            .ValidateOnStart();

        // ISendGridClient is registered as singleton — the official SDK
        // is thread-safe and pools its underlying HttpClient internally.
        services.AddSingleton<ISendGridClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SendGridOptions>>().Value;
            return new SendGridClient(opts.ApiKey);
        });

        // Polly v8 ResiliencePipeline<Response>. Retries 5xx / 429 / 408
        // with exponential backoff capped by SendGridOptions.RetryCount.
        // Failed terminal responses surface to the provider which maps
        // to Transient vs Permanent BusinessErrors.
        services.AddSingleton<ResiliencePipeline<Response>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SendGridOptions>>().Value;
            return new ResiliencePipelineBuilder<Response>()
                .AddRetry(new RetryStrategyOptions<Response>
                {
                    MaxRetryAttempts = Math.Max(0, opts.RetryCount),
                    Delay = TimeSpan.FromMilliseconds(Math.Max(50, opts.RetryBaseDelayMs)),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder<Response>()
                        .Handle<HttpRequestException>()
                        .Handle<TaskCanceledException>()
                        .HandleResult(static r =>
                            (int)r.StatusCode is 408 or 429 or >= 500 and <= 599),
                })
                .Build();
        });

        services.AddSingleton<IEmailProvider, SendGridEmailProvider>();

        // === Mapbox (T-0031) ===
        // ValidateOnStart so a missing/typo'd Mapbox:AccessToken crashes
        // the host at boot. The autocomplete proxy is hot-path UX; we want
        // the misconfig caught in deploy logs, not in user-facing 503s.
        services.AddOptions<MapboxOptions>()
            .Bind(configuration.GetSection(MapboxOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.AccessToken),
                "Mapbox:AccessToken is required.")
            // T-0031 sec reviewer MN-2: BaseUrl must be absolute https. A
            // compromised config that pointed Mapbox calls at an
            // attacker-controlled host would leak typed-into-the-form PII
            // + the bearer token. Validator enforces scheme; hostname
            // allow-list deferred to a future hardening ticket.
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var u)
                        && u.Scheme == Uri.UriSchemeHttps,
                "Mapbox:BaseUrl must be an absolute https URI.")
            .Validate(o => o.AutocompleteLimit is >= 1 and <= 10,
                "Mapbox:AutocompleteLimit must be 1..10.")
            .Validate(o => o.RetryCount is >= 0 and <= 5,
                "Mapbox:RetryCount must be 0..5.")
            // T-0031 Copilot review: cap RetryBaseDelayMs so an
            // accidental huge value can't stretch the retry chain past
            // OverallTimeoutSeconds (5s default) and silently turn every
            // autocomplete call into a 5s wait.
            .Validate(o => o.RetryBaseDelayMs is >= 0 and <= 5000,
                "Mapbox:RetryBaseDelayMs must be 0..5000.")
            .Validate(o => o.OverallTimeoutSeconds is >= 1 and <= 30,
                "Mapbox:OverallTimeoutSeconds must be 1..30.")
            .ValidateOnStart();

        services.AddHttpClient(MapboxAddressGeocoder.HttpClientName);

        services.AddScoped<IAddressGeocoder, MapboxAddressGeocoder>();

        // === ARES (T-0032) ===
        // No API key — ARES is a public registry. We still validate the
        // BaseUrl is absolute https (same shape as T-0031 sec MN-2) and
        // crash the host at boot on any misconfig so deploys don't ship
        // a quietly broken registry path.
        services.AddOptions<AresOptions>()
            .Bind(configuration.GetSection(AresOptions.SectionName))
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var u)
                        && u.Scheme == Uri.UriSchemeHttps,
                "Ares:BaseUrl must be an absolute https URI.")
            .Validate(o => o.RetryCount is >= 0 and <= 5,
                "Ares:RetryCount must be 0..5.")
            // T-0032 Copilot review: cap RetryBaseDelayMs like the
            // Mapbox option (T-0031) so an accidental large value can't
            // stretch the retry chain past OverallTimeoutSeconds or
            // effectively disable retries.
            .Validate(o => o.RetryBaseDelayMs is >= 0 and <= 5000,
                "Ares:RetryBaseDelayMs must be 0..5000.")
            .Validate(o => o.OverallTimeoutSeconds is >= 1 and <= 60,
                "Ares:OverallTimeoutSeconds must be 1..60.")
            .Validate(o => o.InMemoryCacheTtlMinutes is >= 1 and <= 1440,
                "Ares:InMemoryCacheTtlMinutes must be 1..1440.")
            .Validate(o => o.DbCacheTtlHours is >= 1 and <= 168,
                "Ares:DbCacheTtlHours must be 1..168.")
            .Validate(o => o.StaleFallbackDays is >= 1 and <= 30,
                "Ares:StaleFallbackDays must be 1..30.")
            .ValidateOnStart();

        services.AddHttpClient(AresCompanyRegistry.HttpClientName);

        services.AddScoped<ICompanyRegistry, AresCompanyRegistry>();

        // === Comgate (T-0065) ===
        // ValidateOnStart so a missing/typo'd Comgate:MerchantId or
        // Comgate:Secret crashes the host at boot rather than on the
        // first /create call. The merchant id and secret are the entire
        // auth surface — there is no header alternative — so a misconfig
        // here turns every payment-session attempt into a 5xx.
        services.AddOptions<ComgateOptions>()
            .Bind(configuration.GetSection(ComgateOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.MerchantId),
                "Comgate:MerchantId is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Secret),
                "Comgate:Secret is required.")
            // BaseUrl must be absolute https (same shape as T-0031 sec MN-2 +
            // T-0032 ARES). A compromised config that pointed Comgate calls
            // at an attacker-controlled host would leak the secret on every
            // create + status call.
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var u)
                        && u.Scheme == Uri.UriSchemeHttps,
                "Comgate:BaseUrl must be an absolute https URI.")
            // T-0066 reviewer M-1: AC-4 requires malformed WebhookAllowedIps
            // entries to be discovered at startup, not only on the first
            // webhook request. Every entry must parse as either a bare
            // IPAddress or a CIDR via System.Net.IPNetwork. Bad entries
            // crash the host at boot — same fail-loud posture as the
            // MerchantId/Secret/BaseUrl guards above.
            .Validate(o =>
            {
                foreach (var entry in o.WebhookAllowedIps)
                {
                    var trimmed = entry?.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                        return false;
                    if (System.Net.IPNetwork.TryParse(trimmed, out _))
                        continue;
                    if (System.Net.IPAddress.TryParse(trimmed, out _))
                        continue;
                    return false;
                }
                return true;
            }, "Comgate:WebhookAllowedIps contains an entry that is not a valid IP address or CIDR range.")
            .ValidateOnStart();

        services.AddHttpClient(ComgatePaymentProvider.HttpClientName);

        // Keyed registration — T-0065 introduces the keyed-services pattern
        // for IPaymentProvider; SendGrid + ARES migration in T-0124 (Q3).
        services.AddKeyedScoped<IPaymentProvider, ComgatePaymentProvider>(
            ComgatePaymentProvider.ProviderCode);
        services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();

        // === Packeta (T-0070) ===
        // ValidateOnStart so missing/typo'd Packeta:ApiKey or
        // Packeta:PublicWidgetKey crashes the host at boot, not on the
        // first ship call. Validator surfaces every problem at once.
        services.AddOptions<PacketaOptions>()
            .Bind(configuration.GetSection(PacketaOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<PacketaOptions>,
            PacketaOptionsValidator>();
        // Single-attempt cap for Packeta REST calls. The Polly pipeline
        // (registered below) wraps this with a 3-attempt retry chain, so
        // the worst-case wall-clock is bounded by 30s × retry budget rather
        // than HttpClient's default 100s — which would let a hung Packeta
        // socket pin a request thread for nearly two minutes before the
        // first retry. 30s mirrors the standard tisknicz external-integration
        // ceiling (Comgate T-0065).
        services.AddHttpClient(PacketaShippingCarrier.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Keyed registration — selection via CountryConfiguration.DefaultShippingCarrier
        // through ShippingCarrierFactory.
        services.AddKeyedScoped<IShippingCarrier, PacketaShippingCarrier>(
            PacketaShippingCarrier.CarrierCode);
        services.AddScoped<IShippingCarrierFactory, ShippingCarrierFactory>();

        // === Shared Polly registry for every HttpResponseMessage-typed
        // pipeline across adapters (T-0032 follow-up to a latent T-0031
        // collision: two adapters registering ResiliencePipeline<HttpResponseMessage>
        // as singleton overwrote each other in the container). Polly v8's
        // ResiliencePipelineRegistry<string> is the canonical multi-pipeline
        // primitive — keyed by adapter name so each adapter resolves its
        // own pipeline.
        services.AddSingleton<ResiliencePipelineRegistry<string>>(sp =>
        {
            var registry = new ResiliencePipelineRegistry<string>();

            var mapboxOpts = sp.GetRequiredService<IOptions<MapboxOptions>>().Value;
            registry.TryAddBuilder<HttpResponseMessage>(
                MapboxAddressGeocoder.HttpClientName,
                (builder, _) => builder.AddRetry(HttpRetryStrategy(
                    mapboxOpts.RetryCount, mapboxOpts.RetryBaseDelayMs)));

            var aresOpts = sp.GetRequiredService<IOptions<AresOptions>>().Value;
            registry.TryAddBuilder<HttpResponseMessage>(
                AresCompanyRegistry.HttpClientName,
                (builder, _) => builder.AddRetry(HttpRetryStrategy(
                    aresOpts.RetryCount, aresOpts.RetryBaseDelayMs)));

            // Comgate (T-0065): fixed 3-attempt retry chain with 200ms
            // exponential jittered backoff. Matches the ticket §Scope.DI
            // numbers; Comgate doesn't have a per-call options surface
            // for retry count today (single global section per Q4), so
            // the constants live inline rather than on ComgateOptions.
            registry.TryAddBuilder<HttpResponseMessage>(
                ComgatePaymentProvider.HttpClientName,
                (builder, _) => builder.AddRetry(HttpRetryStrategy(
                    retryCount: 3, baseDelayMs: 200)));

            // Packeta (T-0070): same 3-attempt + 200ms baseline as Comgate.
            // Packeta has no per-call options surface for retry count.
            registry.TryAddBuilder<HttpResponseMessage>(
                PacketaShippingCarrier.HttpClientName,
                (builder, _) => builder.AddRetry(HttpRetryStrategy(
                    retryCount: 3, baseDelayMs: 200)));

            return registry;
        });

        return services;
    }

    /// <summary>
    /// Shared retry-strategy factory for any <see cref="HttpResponseMessage"/>
    /// pipeline: handles transient HTTP exceptions + 408/429/5xx with
    /// exponential backoff + jitter, bounded by the supplied retry count.
    /// </summary>
    private static RetryStrategyOptions<HttpResponseMessage> HttpRetryStrategy(int retryCount, int baseDelayMs) =>
        new()
        {
            MaxRetryAttempts = Math.Max(0, retryCount),
            Delay = TimeSpan.FromMilliseconds(Math.Max(50, baseDelayMs)),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>()
                .HandleResult(static r =>
                    (int)r.StatusCode is 408 or 429 or >= 500 and <= 599),
        };
}
