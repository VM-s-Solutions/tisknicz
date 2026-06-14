namespace Makables.Core.Domain.Configuration;

/// <summary>
/// The four provider seams a <see cref="CountryConfiguration"/> selects a
/// default for. Used by <see cref="IProviderRegistry"/> to answer "is this
/// provider code registered?" at write time (T-0108).
/// </summary>
public enum ProviderKind
{
    Payment = 0,
    Shipping = 1,
    Registry = 2,
    Email = 3,
}
