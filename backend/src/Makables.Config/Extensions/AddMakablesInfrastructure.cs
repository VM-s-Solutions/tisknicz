using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Email;
using Makables.Core.AppServices.Features.Outbox;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Addresses.Validators;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Email;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Numbering;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Registry;
using Makables.Core.Domain.SeedWork;
using Makables.Infra.Common.Addresses;
using Makables.Infra.Common.Auth;
using Makables.Infra.Common.Identifiers;
using Makables.Infra.Common.Outbox;
using Makables.Infra.Common.Time;
using Makables.Infra.Database;
using Makables.Infra.Database.Addresses;
using Makables.Infra.Database.Auditing;
using Makables.Infra.Database.Interceptors;
using Makables.Infra.Database.Makers;
using Makables.Infra.Database.Numbering;
using Makables.Infra.Database.Outbox;
using Makables.Infra.Database.Registry;
using Makables.Infra.Database.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Config.Extensions;

/// <summary>
/// Registers the Postgres-backed <see cref="MakablesDbContext"/>, its
/// audit interceptor, the unit-of-work alias, repositories (none yet —
/// added in Phase 2+), and the numbering generators.
/// Per ADR 0008 / patterns §A.16. Called by every API host's
/// <c>Program.cs</c> and by Makables.Functions.
/// </summary>
public static class MakablesInfrastructureExtensions
{
    public static IServiceCollection AddMakablesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // === Cross-cutting primitives ===
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, UlidIdGenerator>();

        // === Auth crypto (T-0021) ===
        services.AddOptions<Argon2idOptions>()
            .Bind(configuration.GetSection(Argon2idOptions.SectionName));
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddHostedService<Argon2idStartupBenchmark>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IJwtIssuer, JwtIssuer>();

        // Shared HMAC-signed OAuth state signer (T-0026).
        services.AddSingleton<IOAuthStateSigner, OAuthStateSigner>();

        // Default country for OAuth-created accounts (T-0026 CQ M-1).
        services.AddOptions<Makables.Core.AppServices.Features.Auth.AuthDefaultCountryOptions>()
            .Bind(configuration.GetSection(
                Makables.Core.AppServices.Features.Auth.AuthDefaultCountryOptions.SectionName));

        // === Auth policy (T-0022) ===
        services.AddOptions<LockoutOptions>()
            .Bind(configuration.GetSection(LockoutOptions.SectionName));

        // === EF Core DbContext + interceptor + UoW alias ===
        services.AddScoped<AuditableSaveChangesInterceptor>();

        // Reusable per-call DbContext configurator. Used both for the
        // request-scoped DbContext (the UoW the MediatR pipeline commits)
        // and the IDbContextFactory (used by side-effect adapters that
        // need an isolated DbContext scope — see ICompanyRegistryCacheStore
        // / T-0032 sec reviewer M-1).
        void ConfigureMakablesDbContext(IServiceProvider sp, DbContextOptionsBuilder options)
        {
            var connectionString = configuration.GetConnectionString("Postgres");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                // Reviewer T-0009 MAJOR #1: production appsettings.json ships
                // an empty string for ConnectionStrings:Postgres, which is
                // non-null and would have slipped past a `?? throw` guard.
                // IsNullOrWhiteSpace catches both missing and empty.
                throw new InvalidOperationException(
                    "Connection string 'Postgres' is not configured. " +
                    "Set ConnectionStrings:Postgres in appsettings or environment.");
            }

            options.UseNpgsql(connectionString);
            options.AddInterceptors(sp.GetRequiredService<AuditableSaveChangesInterceptor>());
        }

        services.AddDbContext<MakablesDbContext>(ConfigureMakablesDbContext);

        // IDbContextFactory<MakablesDbContext> for adapters that need to
        // run side-effect commits OUTSIDE the request-scoped UoW.
        // T-0032 sec reviewer M-1: the ARES cache store uses this so a
        // cache write can't flush a calling command's tracked-but-uncommitted
        // aggregates. Lifetime is Scoped (not the default Singleton)
        // because AddDbContext already registers DbContextOptions<T> as
        // Scoped; mixing a singleton factory with scoped options trips
        // ValidateScopes on host build (T-0032 CI fix — the explicit
        // lifetime was missing on the T-0032 branch and only landed in
        // T-0033, so the T-0032 PR alone crashed at boot during NSwag CI).
        services.AddDbContextFactory<MakablesDbContext>(
            ConfigureMakablesDbContext,
            lifetime: ServiceLifetime.Scoped);

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MakablesDbContext>());

        // === Numbering ===
        services.AddScoped<IOrderNumberGenerator, OrderNumberGenerator>();
        services.AddScoped<IInvoiceNumberGenerator, InvoiceNumberGenerator>();
        services.AddSingleton<IPayoutBatchNumberGenerator, PayoutBatchNumberGenerator>();

        // === Repositories (Phase 2+ adds more) ===
        services.AddScoped<ICountryConfigurationRepository, CountryConfigurationRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ILoginAttemptBucketRepository, LoginAttemptBucketRepository>();
        services.AddScoped<IOneTimeTokenRepository, OneTimeTokenRepository>();

        // === Makers (T-0033) ===
        services.AddScoped<IMakerRepository, MakerRepository>();

        // === Addresses (T-0030) ===
        services.AddScoped<IAddressRepository, AddressRepository>();
        // Scoped because it depends on ICountryConfigurationRepository
        // (which holds the per-request DbContext). The regex cache is a
        // static ConcurrentDictionary on the type, so cache state survives
        // across scopes anyway.
        services.AddScoped<IAddressFormatValidator, ConfigurationDrivenAddressFormatValidator>();

        // === Company registry cache (T-0032) ===
        // ICompanyRegistryCacheStore uses IDbContextFactory<MakablesDbContext>
        // so cache reads/writes never touch the request-scoped UoW
        // (T-0032 sec reviewer M-1). Scoped lifetime is fine — the
        // factory itself is the singleton, the store just holds a reference.
        services.AddScoped<ICompanyRegistryCacheStore, CompanyRegistryCacheStore>();
        // IMemoryCache backs the in-process hot layer used by
        // AresCompanyRegistry. Singleton + thread-safe; entries TTL out
        // via MemoryCacheEntryOptions set per-call.
        services.AddMemoryCache();

        // === Email templates + send pipeline (T-0028) ===
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
        services.AddScoped<IEmailTemplateTranslationRepository, EmailTemplateTranslationRepository>();
        services.AddScoped<ILanguageResolver, LanguageResolver>();
        // Validated on start so a misconfigured WebBaseUrl (e.g. "javascript:")
        // or a path template missing the {token} placeholder crashes the host
        // at boot, not on the first email send. T-0028 sec reviewer B-2 / M-1.
        services.AddOptions<PublicAppUrlsOptions>()
            .Bind(configuration.GetSection(PublicAppUrlsOptions.SectionName))
            .Validate(o =>
            {
                var (ok, _) = PublicAppUrlsOptionsValidator.Validate(o);
                return ok;
            }, "PublicAppUrls is misconfigured. WebBaseUrl must be absolute https (or http on loopback for dev) " +
               "and every path template must start with '/' and contain the literal '{token}' placeholder.")
            .ValidateOnStart();
        services.AddScoped<IEmailSendService, EmailSendService>();

        // === Outbox + Admin audit log ===
        services.AddScoped<IOutbox, OutboxWriter>();
        services.AddScoped<IOutboxConsumerRepository, OutboxConsumerRepository>();
        services.AddScoped<IAdminAuditLogWriter, AdminAuditLogWriter>();

        // === T-0029 outbox queue publisher (used by Makables.Functions
        // ProcessOutboxFunction). The Web hosts don't strictly need this
        // — only the Functions host enqueues — but registering it here
        // keeps the DI surface uniform across hosts. Singleton because
        // QueueClient is thread-safe and pools connections internally.
        // ValidateOnStart so a typo'd OutboxQueues:ConnectionString or queue
        // name crashes the host at boot rather than silently inside a timer
        // tick 30 s later. T-0029 sec reviewer M-4 / CQ m-3.
        services.AddOptions<OutboxQueueOptions>()
            .Bind(configuration.GetSection(OutboxQueueOptions.SectionName))
            .Validate(o =>
            {
                var (ok, _) = OutboxQueueOptionsValidator.Validate(o);
                return ok;
            }, "OutboxQueues is misconfigured. ConnectionString + SendEmailQueueName are required " +
               "and HandoffParkMinutes must be 1..360.")
            .ValidateOnStart();
        services.AddSingleton<IOutboxQueuePublisher, StorageQueueOutboxPublisher>();
        services.AddOptions<OutboxDispatcherOptions>()
            .Bind(configuration.GetSection(OutboxDispatcherOptions.SectionName))
            .Validate(o => o.HandoffParkMinutes is >= 1 and <= 360,
                "OutboxDispatcher:HandoffParkMinutes must be 1..360.")
            .ValidateOnStart();
        services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();
        services.AddScoped<ISendEmailHandler, SendEmailHandler>();

        return services;
    }
}
