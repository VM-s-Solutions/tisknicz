using Makables.Core.Domain.Common;

namespace Makables.Infra.Common.Identifiers;

/// <summary>
/// Production <see cref="IIdGenerator"/> backed by ULID. Registered as a
/// singleton; thread-safe.
/// </summary>
public sealed class UlidIdGenerator : IIdGenerator
{
    public string Next() => Ulid.NewUlid().ToString();
}
