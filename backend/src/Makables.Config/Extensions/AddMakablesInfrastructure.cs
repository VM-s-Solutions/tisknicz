using Makables.Core.Domain.Common;
using Makables.Core.Domain.Numbering;
using Makables.Core.Domain.SeedWork;
using Makables.Infra.Common.Identifiers;
using Makables.Infra.Common.Time;
using Makables.Infra.Database;
using Makables.Infra.Database.Interceptors;
using Makables.Infra.Database.Numbering;
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

        // === EF Core DbContext + interceptor + UoW alias ===
        services.AddScoped<AuditableSaveChangesInterceptor>();

        services.AddDbContext<MakablesDbContext>((sp, options) =>
        {
            var connectionString =
                configuration.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException(
                    "Connection string 'Postgres' is not configured. " +
                    "Set ConnectionStrings:Postgres in appsettings or environment.");

            options.UseNpgsql(connectionString);
            options.AddInterceptors(sp.GetRequiredService<AuditableSaveChangesInterceptor>());
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MakablesDbContext>());

        // === Numbering ===
        services.AddScoped<IOrderNumberGenerator, OrderNumberGenerator>();
        services.AddScoped<IInvoiceNumberGenerator, InvoiceNumberGenerator>();
        services.AddSingleton<IPayoutBatchNumberGenerator, PayoutBatchNumberGenerator>();

        // Repositories land here as Phase 2+ adds aggregates.

        return services;
    }
}
