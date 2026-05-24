using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Email;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Email;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Numbering;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.SeedWork;
using Makables.Infra.Common.Auth;
using Makables.Infra.Common.Identifiers;
using Makables.Infra.Common.Time;
using Makables.Infra.Database;
using Makables.Infra.Database.Auditing;
using Makables.Infra.Database.Interceptors;
using Makables.Infra.Database.Numbering;
using Makables.Infra.Database.Outbox;
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

        services.AddDbContext<MakablesDbContext>((sp, options) =>
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
        });

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
        services.AddScoped<IAdminAuditLogWriter, AdminAuditLogWriter>();

        return services;
    }
}
