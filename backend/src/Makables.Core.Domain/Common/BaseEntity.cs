namespace Makables.Core.Domain.Common;

/// <summary>
/// Root of every entity. Carries a string Id (ULID at launch; assigned by
/// the concrete entity's factory via <see cref="IIdGenerator"/>) and the
/// soft-delete <see cref="IsActive"/> flag. Per ADR 0013 (data scoping +
/// soft delete) and patterns §A.11.
///
/// Note: Per ADR 0001, <c>Core.Domain</c> has no third-party packages —
/// including ULID. Id generation lives behind <see cref="IIdGenerator"/>,
/// implemented in <c>Makables.Infra.Common</c>.
/// </summary>
public abstract class BaseEntity : IEntity<string>
{
    public virtual string Id { get; protected internal set; } = default!;

    public bool IsActive { get; protected internal set; } = true;

    object IEntity.Id => Id;
}
