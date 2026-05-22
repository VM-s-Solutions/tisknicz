namespace Makables.Core.Domain.Common;

/// <summary>
/// Abstraction over entity id generation. Implementation
/// (<c>UlidIdGenerator</c> in <c>Infra.Common</c>) returns a lexicographically
/// sortable ULID string; tests can inject a deterministic fake.
///
/// Per ADR 0001, <c>Core.Domain</c> takes no third-party dependency on the
/// ULID library directly.
/// </summary>
public interface IIdGenerator
{
    /// <summary>Generate a fresh entity id.</summary>
    string Next();
}
