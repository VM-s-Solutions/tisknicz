using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Payments;
using Makables.Core.Domain.Shipping;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Infra.Database.Configuration;

/// <summary>
/// <see cref="IProviderRegistry"/> impl (T-0108) — the write-time
/// validation seam backing the "must reference a registered keyed service"
/// invariant on <see cref="CountryConfiguration"/> provider codes.
///
/// <para>
/// Payment + shipping codes are discovered from the keyed
/// <see cref="ServiceDescriptor.ServiceKey"/>s registered for
/// <see cref="IPaymentProvider"/> / <see cref="IShippingCarrier"/> (T-0065 /
/// T-0070) — the same discovery the webhook integration tests use. Registry
/// + email fall back to a static known-codes set until they are keyed
/// (T-0124). Sets are case-insensitive — provider codes are lowercase
/// constants but admin input matches leniently.
/// </para>
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly IReadOnlySet<string> _payment;
    private readonly IReadOnlySet<string> _shipping;

    // TODO(T-0124): replace these static fallbacks with a keyed-container
    // probe once IEmailProvider + ICompanyRegistry are keyed-registered.
    private static readonly IReadOnlySet<string> RegistryCodes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ares" };

    private static readonly IReadOnlySet<string> EmailCodes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sendgrid" };

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
        ProviderKind.Registry => RegistryCodes,
        ProviderKind.Email => EmailCodes,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown provider kind."),
    };
}
