using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Email;
using Makables.Core.Domain.Identity;
using Makables.Infra.Clients.Google;
using Makables.Infra.Clients.Mapbox;
using Makables.Infra.Clients.SendGrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
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

        // Polly v8 ResiliencePipeline<HttpResponseMessage>. Same shape as
        // the SendGrid one: retry on transient HTTP statuses with
        // exponential backoff + jitter, bounded by MapboxOptions.RetryCount.
        services.AddSingleton<ResiliencePipeline<HttpResponseMessage>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MapboxOptions>>().Value;
            return new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = Math.Max(0, opts.RetryCount),
                    Delay = TimeSpan.FromMilliseconds(Math.Max(50, opts.RetryBaseDelayMs)),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .Handle<TaskCanceledException>()
                        .HandleResult(static r =>
                            (int)r.StatusCode is 408 or 429 or >= 500 and <= 599),
                })
                .Build();
        });

        services.AddScoped<IAddressGeocoder, MapboxAddressGeocoder>();

        return services;
    }
}
