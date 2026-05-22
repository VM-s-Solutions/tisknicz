namespace Makables.Core.Domain.Common;

/// <summary>
/// Marker for an entity with a string-typed identity (ULID at launch).
/// Untyped variant kept for assembly-scanning convenience.
/// </summary>
public interface IEntity
{
    object Id { get; }
}

/// <summary>
/// Marker for an entity with a typed identity.
/// </summary>
public interface IEntity<TKey> : IEntity
{
    new TKey Id { get; }
}
