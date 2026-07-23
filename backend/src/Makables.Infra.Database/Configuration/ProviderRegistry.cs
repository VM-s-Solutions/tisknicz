using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Email;
using Makables.Core.Domain.Payments;
using Makables.Core.Domain.Registry;
using Makables.Core.Domain.Shipping;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Infra.Database.Configuration;

/// <summary>
/// <see cref="IProviderRegistry"/> impl (T-0108) — the write-time
/// validation seam backing the "must reference a registered keyed service"
/// invariant on <see cref="CountryConfiguration"/> provider codes.
///
/// <para>
/// All four provider kinds are discovered from the keyed
/// <see cref="ServiceDescriptor.ServiceKey"/>s registered for
/// <see cref="IPaymentProvider"/> / <see cref="IShippingCarrier"/>
/// (T-0065 / T-0070) / <see cref="ICompanyRegistry"/> /
/// <see cref="IEmailProvider"/> (T-0124) — the same discovery the webhook
/// integration tests use. Sets are case-insensitive — provider codes are
/// lowercase constants but admin input matches leniently.
/// </para>
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly IReadOnlySet<string> _payment;
    private readonly IReadOnlySet<string> _shipping;
    private readonly IReadOnlySet<string> _registry;
    private readonly IReadOnlySet<string> _email;

    /// <summary>
    /// Built from the composition root's <see cref="IServiceCollection"/> so
    /// the registered keyed-service keys are captured once at startup (the
    /// runtime <c>IServiceProvider</c> cannot enumerate keys). See
    /// <c>AddMakablesInfrastructure</c>.
    /// </summary>
    public ProviderRegistry(IServiceCollection services)
    {
        _payment = DiscoverKeys(services, typeof(IPaymentProvider));
        _shipping = DiscoverKeys(services, typeof(IShippingCarrier));
        _registry = DiscoverKeys(services, typeof(ICompanyRegistry));
        _email = DiscoverKeys(services, typeof(IEmailProvider));
    }

    private static IReadOnlySet<string> DiscoverKeys(IServiceCollection services, Type serviceType)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == serviceType &&
                descriptor.IsKeyedService &&
                descriptor.ServiceKey is string key)
            {
                set.Add(key);
            }
        }
        return set;
    }

    public IReadOnlySet<string> GetRegisteredCodes(ProviderKind kind) => kind switch
    {
        ProviderKind.Payment => _payment,
        ProviderKind.Shipping => _shipping,
        ProviderKind.Registry => _registry,
        ProviderKind.Email => _email,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown provider kind."),
    };
}
